using System.Buffers;
using Microsoft.Data.Sqlite;
using Nordmann.LUI.Normalization;
using Nordmann.LUI.Schema;

namespace Nordmann.LUI.Querying;

/// <summary>
/// The last stage of a query (LUI-Spezifikation §8.3, E7): key and payload of the hits — and only of the
/// hits — in one statement, in the snapshot of the candidates.
/// </summary>
internal sealed class HitReader(ContentSource source, PayloadReader payload)
{
    private readonly LuiKeyKind _keyKind = KeyCodec.KindOf(source.Definition.KeyType);
    private readonly NormalizationOptions _normalization = source.Normalization;
    private readonly string?[] _hitsSql = new string?[LuiQueryOptions.MaxResultsLimit + 1];

    internal List<LuiHit> Read(
        SqliteConnection connection,
        SqliteTransaction snapshot,
        Scored[] ranked,
        IReadOnlyList<string> queries,
        ILuiSimilarity similarity,
        LuiRankingOrder order)
    {
        var hits = new List<LuiHit>(ranked.Length);
        if (ranked.Length == 0)
        {
            return hits;
        }

        using var command = connection.CreateCommand();
        command.Transaction = snapshot;
        command.CommandText = HitsSql(ranked.Length);

        var slot = new Dictionary<long, int>(ranked.Length);
        for (var i = 0; i < ranked.Length; i++)
        {
            var docId = RowId.DocIdOf(ranked[i].RowId);
            slot[docId] = i;
            command.Parameters.AddWithValue(ParameterName(i), docId);
        }

        var keys = new string?[ranked.Length];
        var payloads = new object?[ranked.Length];

        ReadKeysAndPayloads(command, slot, keys, payloads);

        // the measure of a hit whose rank was the weight alone: the ranking did not measure it (issue #30)
        var buffer = ArrayPool<char>.Shared.Rent(256);

        try
        {
            for (var i = 0; i < ranked.Length; i++)
            {
                // an entry that was removed between the candidates and the hits is not a hit
                if (keys[i] is not { } key)
                {
                    continue;
                }

                ref var entry = ref ranked[i];
                var measured = order == LuiRankingOrder.WeightOnly
                    ? Ranker.Measure(entry, queries, similarity, _normalization, ref buffer)
                    : entry.Similarity;

                hits.Add(Hit(entry, key, payloads[i], measured));
            }
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }

        return hits;
    }

    private LuiHit Hit(in Scored entry, string key, object? stored, double similarity) =>
        new()
        {
            Key = KeyCodec.FromText(key, _keyKind),
            Text = entry.Text,
            StoredPayload = stored,
            PayloadSource = payload.Source,
            Weight = RowId.WeightOf(entry.RowId),
            Priority = RowId.PriorityOf(entry.RowId),
            Similarity = similarity,
            AtWordStart = entry.WordStart >= 0,
        };

    private static string ParameterName(int index) => $"$d{index}";

    private void ReadKeysAndPayloads(
        SqliteCommand command,
        Dictionary<long, int> slot,
        string?[] keys,
        object?[] payloads)
    {
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var i = slot[reader.GetInt64(0)];
            keys[i] = reader.GetString(1);
            payloads[i] = payload.Read(reader);
        }
    }

    private string HitsSql(int count)
    {
        // Lock-free on purpose: a reference is written atomically, the string is immutable, and two
        // threads that build the same statement at once build the same text.
        var sql = _hitsSql[count];
        if (sql is not null)
        {
            return sql;
        }

        sql = source.HitsSql(string.Join(',', Enumerable.Range(0, count).Select(ParameterName)), payload.Columns);
        _hitsSql[count] = sql;
        return sql;
    }
}
