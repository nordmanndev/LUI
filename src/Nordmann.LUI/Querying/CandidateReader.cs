using System.Buffers;
using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Nordmann.LUI.Normalization;
using Nordmann.LUI.Schema;

namespace Nordmann.LUI.Querying;

/// <summary>
/// The first stage of a query (LUI-Spezifikation §8.1, §8.2): the candidates of one input out of SQLite, in
/// the order of the rowid, each with its word start (§9.1). The statements come from the content source.
/// </summary>
internal sealed class CandidateReader
{
    /// <summary>Normalised texts up to this length are handled on the stack.</summary>
    private const int StackTextLength = 256;

    private readonly NormalizationOptions _normalization;
    private readonly string _prefix1Sql;
    private readonly string _prefix2Sql;
    private readonly string _candidatesSql;

    internal CandidateReader(ContentSource source)
    {
        _normalization = source.Normalization;
        _prefix1Sql = source.PrefixSql(1);
        _prefix2Sql = source.PrefixSql(2);
        _candidatesSql = source.CandidateSql;
    }

    /// <summary>
    /// The candidates of <paramref name="query"/>: at most <c>Candidates</c>, with <c>DistinctText</c> one per
    /// normalised text. <paramref name="truncated"/> says whether there were more.
    /// </summary>
    internal Scored[] Read(
        SqliteConnection connection,
        SqliteTransaction snapshot,
        PreparedQuery query,
        LuiQueryOptions options,
        byte variant,
        CancellationToken cancellationToken,
        out int count,
        out bool truncated)
    {
        using var command = connection.CreateCommand();
        command.Transaction = snapshot;

        // one more than asked for tells whether there were more candidates (Truncated)
        var limit = options.Candidates + 1;
        if (query.IsPrefixPath)
        {
            command.CommandText = query.PrefixLength == 1 ? _prefix1Sql : _prefix2Sql;
            command.Parameters.AddWithValue("$q", query.PrefixKey);
        }
        else
        {
            command.CommandText = _candidatesSql;
            command.Parameters.AddWithValue("$expr", query.Expression);
        }

        command.Parameters.AddWithValue("$n", limit);

        var candidates = new Scored[Math.Min(limit, 512)];

        // one entry per text within this input; across the inputs it is decided after the union
        var seen = options.DistinctText ? new HashSet<string>(StringComparer.Ordinal) : null;
        var rows = 0;
        count = 0;

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if ((rows++ & 63) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (rows > options.Candidates)
            {
                break;
            }

            var text = reader.GetString(1);
            var start = StartOf(text, query.Compact, seen, out var duplicate, out var key);
            if (duplicate)
            {
                continue;
            }

            if (count == candidates.Length)
            {
                Array.Resize(ref candidates, Math.Min(limit, candidates.Length * 2));
            }

            candidates[count++] = new Scored { RowId = reader.GetInt64(0), Text = text, Key = key, WordStart = start, Variant = variant };
        }

        // the LIMIT stops the statement one row after the budget
        truncated = rows > options.Candidates;
        return candidates;
    }

    /// <summary>
    /// Normalises a text on the stack, finds the word start and — for
    /// <c>DistinctText</c> — remembers the text. The candidates arrive in the
    /// order of the rowid, so the first one of a text is the best one (§9.2).
    /// </summary>
    private int StartOf(string text, string compactQuery, HashSet<string>? seen, out bool duplicate, out string? key)
    {
        duplicate = false;
        key = null;

        char[]? rented = null;
        var buffer = text.Length * 3 <= StackTextLength
            ? stackalloc char[StackTextLength]
            : rented = ArrayPool<char>.Shared.Rent(text.Length * 3);

        try
        {
            if (!Normalizer.TryWriteSearchForm(text, buffer, out var written, _normalization))
            {
                // the transliteration at most doubles a text (ä → ae, ß → ss); the buffer holds three times
                throw new UnreachableException("The search form of a text is longer than three times the text.");
            }

            var searchForm = buffer[..written];

            if (seen is not null)
            {
                key = new string(searchForm);
                if (!seen.Add(key))
                {
                    duplicate = true;
                    return -1;
                }
            }

            return WordStart.Find(searchForm, compactQuery);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<char>.Shared.Return(rented);
            }
        }
    }
}
