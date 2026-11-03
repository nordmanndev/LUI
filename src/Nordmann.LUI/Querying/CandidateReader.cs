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
    private readonly IndexKindRules _rules;
    private readonly bool _verify;
    private readonly string _prefix1Sql;
    private readonly string _prefix2Sql;
    private readonly string _candidatesSql;

    internal CandidateReader(ContentSource source)
    {
        _normalization = source.Normalization;
        _rules = source.Rules;
        _verify = source.TextCanChangeUnderTheIndex;
        _prefix1Sql = source.PrefixSql(1);
        _prefix2Sql = source.PrefixSql(2);
        _candidatesSql = source.CandidateSql;
    }

    /// <summary>
    /// The candidates of <paramref name="query"/>: at most <c>Candidates</c>, with <c>DistinctText</c> one per
    /// normalised text. <paramref name="truncated"/> says whether there were more.
    /// <para>
    /// A row whose text changed under the index and no longer matches (<c>Linked</c>, a row not refreshed yet) is not a
    /// candidate and does not use up the budget: the read goes on past it — at most another <c>Candidates</c> such rows,
    /// then the answer is truncated (review of pull request 16, F1). Without such rows nothing changes: the read stops
    /// one row after the budget, as before.
    /// </para>
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
        // one more than asked for tells whether there were more candidates (Truncated)
        var limit = options.Candidates + 1;
        using var command = Command(connection, snapshot, query, options);

        var candidates = new Scored[Math.Min(limit, 512)];

        // one entry per text within this input; across the inputs it is decided after the union
        var seen = options.DistinctText ? new HashSet<string>(StringComparer.Ordinal) : null;
        var rows = 0;
        var stale = 0;
        var staleBudgetSpent = false;
        count = 0;

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (((rows++ + stale) & 63) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (rows > options.Candidates)
            {
                break;
            }

            var text = reader.GetString(1);
            var start = StartOf(text, query, seen, out var duplicate, out var outdated, out var key);
            if (outdated)
            {
                // not a candidate: it does not count against the budget
                rows--;
                if (++stale > options.Candidates)
                {
                    staleBudgetSpent = true;
                    break;
                }

                continue;
            }

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

        // the LIMIT stops the statement one row after the budget; a spent budget of outdated rows may hide more
        truncated = rows > options.Candidates || staleBudgetSpent;
        return candidates;
    }

    /// <summary>
    /// The statement of the candidates: the prefix path or FTS5. Its limit is one row after the budget — and, for a source
    /// whose texts can change under the index, another budget of rows that no longer match.
    /// </summary>
    private SqliteCommand Command(SqliteConnection connection, SqliteTransaction snapshot, PreparedQuery query, LuiQueryOptions options)
    {
        var command = connection.CreateCommand();
        command.Transaction = snapshot;
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

        var limit = options.Candidates + 1;
        command.Parameters.AddWithValue("$n", _verify ? limit + options.Candidates : limit);
        return command;
    }

    /// <summary>
    /// Normalises a text on the stack, finds the word start and — for
    /// <c>DistinctText</c> — remembers the text. The candidates arrive in the
    /// order of the rowid, so the first one of a text is the best one (§9.2).
    /// </summary>
    private int StartOf(string text, PreparedQuery query, HashSet<string>? seen, out bool duplicate, out bool outdated, out string? key)
    {
        duplicate = false;
        outdated = false;
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

            // a text that changed under the index no longer matches what was searched: not a hit (review of PR 16, S1)
            if (_verify && !_rules.StillMatches(searchForm, query.IsPrefixPath, query.PrefixKey, query.Search, query.Compact, query.Words))
            {
                outdated = true;
                return -1;
            }

            if (seen is not null)
            {
                key = new string(searchForm);
                if (!seen.Add(key))
                {
                    duplicate = true;
                    return -1;
                }
            }

            return WordStart.Find(searchForm, query.Compact);
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
