using System.Buffers;
using Microsoft.Data.Sqlite;
using Nordmann.LUI.Normalization;
using Nordmann.LUI.Schema;

namespace Nordmann.LUI.Querying;

/// <summary>
/// The last stage of a query (ADR-0008, ADR-0036): key and payload of the hits — and only of the
/// hits — in one statement, in the snapshot of the candidates.
/// </summary>
internal sealed class HitReader(ContentSource source, PayloadReader payload)
{
    private readonly LuiKeyType _keyType = KeyCodec.TypeOf(source.Definition.KeyType);
    private readonly NormalizationOptions _normalization = source.Normalization;
    private readonly string?[] _hitsSql = new string?[LuiQueryOptions.MaxResultsLimit + 1];
    private readonly string?[] _hitsWithTextSql = new string?[LuiQueryOptions.MaxResultsLimit + 1];

    internal List<LuiHit> Read(
        SqliteConnection connection,
        SqliteTransaction snapshot,
        Scored[] ranked,
        IReadOnlyList<string> queries,
        ILuiSimilarity similarity,
        LuiRankingOrder order,
        bool textFromHits,
        bool wordStartFromHits,
        string compactQuery)
    {
        var hits = new List<LuiHit>(ranked.Length);
        if (ranked.Length == 0)
        {
            return hits;
        }

        using var command = HitsCommand(connection, snapshot, ranked, textFromHits, out var slot);

        var keys = new string?[ranked.Length];
        var payloads = new object?[ranked.Length];
        var texts = textFromHits ? new string?[ranked.Length] : null;

        ReadKeysAndPayloads(command, slot, keys, payloads, texts);

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
                if (texts is not null)
                {
                    // the candidates carried the rowid alone (issue #173): the text comes with the hit
                    entry.Text = texts[i]!;
                }

                // the word start was not made for the candidates (issue #174): the hit makes it, from the one search form it
                // makes anyway for its similarity
                var measured = textFromHits || wordStartFromHits
                    ? CompleteWordStart(ref entry, order, queries, similarity, compactQuery, ref buffer)
                    : order == LuiRankingOrder.WeightOnly
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

    /// <summary>
    /// The word start of a hit whose candidate did not get one — for the twenty hits, instead of for the five hundred
    /// candidates (issues #173, #174) — and its similarity when the ranking left it to the hit: both from one search form.
    /// </summary>
    private double CompleteWordStart(
        ref Scored entry,
        LuiRankingOrder order,
        IReadOnlyList<string> queries,
        ILuiSimilarity similarity,
        string compactQuery,
        ref char[] buffer)
    {
        var form = Ranker.SearchForm(entry.Text, _normalization, ref buffer);
        entry.WordStart = WordStart.Find(form, compactQuery);

        return order == LuiRankingOrder.WeightOnly
            ? Ranker.MeasureForm(entry, form, queries, similarity)
            : entry.Similarity;
    }

    private LuiHit Hit(in Scored entry, string key, object? stored, double similarity) =>
        new()
        {
            Key = KeyCodec.FromText(key, _keyType, source.IndexName),
            Text = entry.Text,
            StoredPayload = stored,
            PayloadSource = payload.Source,
            Weight = RowId.WeightOf(entry.RowId),
            Priority = RowId.PriorityOf(entry.RowId),
            Similarity = similarity,
            AtWordStart = entry.WordStart >= 0,
        };

    private SqliteCommand HitsCommand(SqliteConnection connection, SqliteTransaction snapshot, Scored[] ranked, bool textFromHits, out Dictionary<long, int> slot)
    {
        var command = connection.CreateCommand();
        command.Transaction = snapshot;
        command.CommandText = textFromHits ? HitsWithTextSql(ranked.Length) : HitsSql(ranked.Length);

        slot = new Dictionary<long, int>(ranked.Length);
        for (var i = 0; i < ranked.Length; i++)
        {
            var docId = RowId.DocIdOf(ranked[i].RowId);
            slot[docId] = i;
            command.Parameters.AddWithValue(ParameterName(i), docId);
        }

        // Prepare() states the intent and costs nothing: measured, with and without it the query takes the same (ADR-0067)
        command.Prepare();
        return command;
    }

    private static string ParameterName(int index) => $"$d{index}";

    private void ReadKeysAndPayloads(
        SqliteCommand command,
        Dictionary<long, int> slot,
        string?[] keys,
        object?[] payloads,
        string?[]? texts)
    {
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var i = slot[reader.GetInt64(0)];
            keys[i] = reader.GetString(1);
            payloads[i] = payload.Read(reader);
            if (texts is not null)
            {
                texts[i] = reader.GetString(reader.FieldCount - 1);
            }
        }
    }

    private string HitsWithTextSql(int count)
    {
        var sql = _hitsWithTextSql[count];
        if (sql is not null)
        {
            return sql;
        }

        sql = source.HitsWithTextSql(string.Join(',', Enumerable.Range(0, count).Select(ParameterName)), payload.Columns);
        _hitsWithTextSql[count] = sql;
        return sql;
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
