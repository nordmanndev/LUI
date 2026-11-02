using System.Buffers;
using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Nordmann.LUI.Indexing;
using Nordmann.LUI.Normalization;
using Nordmann.LUI.Payloads;
using Nordmann.LUI.Similarity;

namespace Nordmann.LUI.Querying;

/// <summary>
/// Runs a query against an <c>Owned</c> single-word index (LUI-Spezifikation
/// §8, §9): the candidates out of SQLite in the order of the rowid, the
/// ranking in memory, key and payload of the hits in one more statement.
/// </summary>
internal sealed class QueryRunner<TKey, TPayload>
{
    /// <summary>Normalised texts up to this length are handled on the stack.</summary>
    private const int StackTextLength = 256;

    private readonly LuiCatalog _catalog;
    private readonly string _content;
    private readonly IndexKind _kind;
    private readonly NormalizationOptions _normalization;
    private readonly IPayloadSerializer<TPayload>? _payload;
    private readonly ITextSimilarity _defaultSimilarity;
    private readonly string _prefix1Sql;
    private readonly string _prefix2Sql;
    private readonly string _candidatesSql;
    private readonly string?[] _hitsSql = new string?[LuiQueryOptions.MaxResultsLimit + 1];

    internal QueryRunner(
        LuiCatalog catalog,
        string indexName,
        IndexDefinition definition,
        IPayloadSerializer<TPayload>? payload,
        ITextSimilarity defaultSimilarity)
    {
        _catalog = catalog;
        _content = OwnedSchema.Content(indexName);
        _kind = definition.Kind;
        _normalization = new NormalizationOptions { GermanTransliteration = definition.GermanTransliteration };
        _payload = payload;
        _defaultSimilarity = defaultSimilarity;

        // the prefix path (§8.1) and the candidate query (§8.2)
        _prefix1Sql = PrefixSql(_content, 1);
        _prefix2Sql = PrefixSql(_content, 2);
        _candidatesSql = CandidateSql(OwnedSchema.Fts(indexName), _content);
    }

    /// <summary>The statement of the prefix path: the best entries whose text begins with one or two characters.</summary>
    internal static string PrefixSql(string content, int length) =>
        $"SELECT s_rowid, text FROM {content} WHERE s_p{length} = $q ORDER BY s_rowid LIMIT $n;";

    /// <summary>
    /// The statement that reads the candidates of an input of three or more characters.
    /// The text is fetched by <c>doc_id</c>, the low 32 bits of the rowid: one seek in the table
    /// itself, where a join on <c>s_rowid</c> would first seek in an index (ADR-0006).
    /// </summary>
    internal static string CandidateSql(string fts, string content) =>
        $"SELECT f.rowid, c.text FROM {fts} f JOIN {content} c ON c.doc_id = (f.rowid & {RowId.MaxDocId}) "
        + $"WHERE {fts} MATCH $expr ORDER BY f.rowid LIMIT $n;";

    internal LuiResult<TKey, TPayload> Execute(string input, LuiQueryOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(options);
        Validate(options);

        var started = Stopwatch.GetTimestamp();
        using var scope = _catalog.Begin();

        var query = PreparedQuery.Create(input, _normalization, _kind);
        if (query.IsEmpty)
        {
            return new LuiResult<TKey, TPayload>(input, [], 0, false, null, Timings(options, started, started, started, started));
        }

        using var connection = _catalog.OpenConnection();

        // One snapshot for the candidates and for the hits. A deferred transaction pins the WAL snapshot
        // at its first read; without it a writer can commit between the two statements, and a freed
        // doc_id that is handed to a new entry would give a hit the text of the old one and the key and
        // payload of the new one (review B2).
        using var snapshot = connection.BeginTransaction(deferred: true);

        var similarity = options.Similarity ?? _defaultSimilarity;
        var candidates = ReadCandidates(connection, snapshot, query, options, cancellationToken, out var count, out var truncated);
        var read = Stopwatch.GetTimestamp();

        // A one- or two-character input carries no similarity worth ranking by: the
        // candidates all begin with it, and "the most popular first" is the answer (Anhang B).
        var order = query.IsPrefixPath ? RankingOrder.WeightOnly : options.Rank;
        var ranked = Ranker.Rank(
            candidates, count, query.Search, order, options.Priorities, similarity, _normalization, options.MaxResults, cancellationToken);
        var rankedAt = Stopwatch.GetTimestamp();

        var hits = ReadHits(connection, snapshot, ranked, query.Search, similarity, order);
        var done = Stopwatch.GetTimestamp();

        return new LuiResult<TKey, TPayload>(
            input, hits, count, truncated, null, Timings(options, started, read, rankedAt, done));
    }

    private static LuiTimings? Timings(LuiQueryOptions options, long started, long read, long ranked, long done) =>
        options.TrackTimings
            ? new LuiTimings(
                Stopwatch.GetElapsedTime(started, read),
                Stopwatch.GetElapsedTime(read, ranked),
                Stopwatch.GetElapsedTime(ranked, done),
                Stopwatch.GetElapsedTime(started, done))
            : null;

    private static void Validate(LuiQueryOptions options)
    {
        if (options.MaxResults is < 1 or > LuiQueryOptions.MaxResultsLimit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), options.MaxResults, $"MaxResults is 1 … {LuiQueryOptions.MaxResultsLimit}.");
        }

        if (options.Candidates is < 1 or > LuiQueryOptions.CandidatesLimit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), options.Candidates, $"Candidates is 1 … {LuiQueryOptions.CandidatesLimit}.");
        }

        if (!Enum.IsDefined(options.Rank))
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.Rank, "Unknown ranking order.");
        }

        if (!Enum.IsDefined(options.Priorities))
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.Priorities, "Unknown priority ranking.");
        }
    }

    private Scored[] ReadCandidates(
        SqliteConnection connection,
        SqliteTransaction snapshot,
        PreparedQuery query,
        LuiQueryOptions options,
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
            var start = StartOf(text, query.Compact, seen, out var duplicate);
            if (duplicate)
            {
                continue;
            }

            if (count == candidates.Length)
            {
                Array.Resize(ref candidates, Math.Min(limit, candidates.Length * 2));
            }

            candidates[count++] = new Scored { RowId = reader.GetInt64(0), Text = text, WordStart = start };
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
    private int StartOf(string text, string compactQuery, HashSet<string>? seen, out bool duplicate)
    {
        duplicate = false;

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

            if (seen is not null && !seen.Add(new string(searchForm)))
            {
                duplicate = true;
                return -1;
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

    /// <summary>Key and payload of the hits — and only of the hits (E7), in one statement.</summary>
    private List<LuiHit<TKey, TPayload>> ReadHits(
        SqliteConnection connection,
        SqliteTransaction snapshot,
        Scored[] ranked,
        string query,
        ITextSimilarity similarity,
        RankingOrder order)
    {
        var hits = new List<LuiHit<TKey, TPayload>>(ranked.Length);
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
        var payloads = new TPayload?[ranked.Length];

        ReadKeysAndPayloads(command, slot, keys, payloads);

        for (var i = 0; i < ranked.Length; i++)
        {
            // an entry that was removed between the candidates and the hits is not a hit
            if (keys[i] is not { } key)
            {
                continue;
            }

            ref var entry = ref ranked[i];
            var measured = order == RankingOrder.WeightOnly
                ? Ranker.Measure(entry, query, similarity, _normalization)
                : entry.Similarity;

            hits.Add(new LuiHit<TKey, TPayload>(
                KeyCodec<TKey>.FromText(key),
                entry.Text,
                payloads[i],
                RowId.WeightOf(entry.RowId),
                RowId.PriorityOf(entry.RowId),
                measured,
                entry.WordStart >= 0,
                null));
        }

        return hits;
    }

    private void ReadKeysAndPayloads(
        SqliteCommand command,
        Dictionary<long, int> slot,
        string?[] keys,
        TPayload?[] payloads)
    {
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var i = slot[reader.GetInt64(0)];
            keys[i] = reader.GetString(1);

            if (_payload is not null && !reader.IsDBNull(2))
            {
                payloads[i] = _payload.Deserialize(reader.GetFieldValue<byte[]>(2));
            }
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

        var names = string.Join(',', Enumerable.Range(0, count).Select(ParameterName));
        sql = $"SELECT doc_id, key, payload FROM {_content} WHERE doc_id IN ({names});";
        _hitsSql[count] = sql;
        return sql;
    }

    private static string ParameterName(int index) => $"$d{index}";
}
