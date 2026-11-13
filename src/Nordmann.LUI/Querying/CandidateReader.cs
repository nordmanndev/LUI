using System.Buffers;
using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Nordmann.LUI.Normalization;
using Nordmann.LUI.Schema;

namespace Nordmann.LUI.Querying;

/// <summary>
/// The first stage of a query (ADR-0008, ADR-0008): the candidates of one input out of SQLite, in
/// the order of the rowid, each with its word start (ADR-0008). The statements come from the content source.
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
    /// The candidates of <paramref name="query"/>: at most <c>Candidates</c>.
    /// <paramref name="truncated"/> says whether there were more.
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
        using var command = Command(connection, snapshot, query, limit, _verify ? limit + options.Candidates : limit);

        // the buffer of the candidates is reused between the queries of a thread (issue #30)
        var candidates = ScoredBuffer.Rent(Math.Min(limit, 512));

        var read = 0;
        var rows = 0;
        var stale = 0;
        truncated = false;
        count = 0;

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if ((read++ & 63) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            // a row past the budget of a source whose texts cannot change under the index is one more: no need to read it
            if (!_verify && rows == options.Candidates)
            {
                truncated = true;
                break;
            }

            var text = reader.GetString(1);
            var start = StartOf(text, query, out var outdated);

            // an outdated row is not a candidate and does not count against the budget — up to a budget of them; and only
            // a row that is a candidate past the budget makes the answer truncated (review of main after #23, finding 4)
            if (outdated ? ++stale > options.Candidates : rows++ == options.Candidates)
            {
                truncated = true;
                break;
            }

            if (outdated)
            {
                continue;
            }

            if (count == candidates.Length)
            {
                Array.Resize(ref candidates, Math.Min(limit, candidates.Length * 2));
            }

            candidates[count++] = new Scored { RowId = reader.GetInt64(0), Text = text, WordStart = start, Variant = variant };
        }

        return candidates;
    }

    /// <summary>
    /// The statement of the candidates: the prefix path or FTS5. Its limit is one row after the budget — and, for a source
    /// whose texts can change under the index, another budget of rows that no longer match.
    /// </summary>
    private SqliteCommand Command(SqliteConnection connection, SqliteTransaction snapshot, PreparedQuery query, int budgetLimit, int statementLimit)
    {
        Debug.Assert(statementLimit >= budgetLimit, "the statement reads at least one row past the budget");
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

        command.Parameters.AddWithValue("$n", statementLimit);
        return command;
    }

    /// <summary>
    /// Normalises a text on the stack and finds the word start.
    /// </summary>
    private int StartOf(string text, PreparedQuery query, out bool outdated)
    {
        outdated = false;

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
