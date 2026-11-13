using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Nordmann.LUI.Schema;
using Nordmann.LUI.Storage;
using Nordmann.LUI.Typos;

namespace Nordmann.LUI.Querying;

/// <summary>
/// Runs a query (ADR-0008, ADR-0008) as a pipeline: the candidates out of SQLite in the order of the
/// rowid (<see cref="CandidateReader"/>), the typo correction when it is wanted (<see cref="TypoCorrection"/>),
/// the ranking in memory (<see cref="Ranker"/>), key and payload of the hits in one more statement
/// (<see cref="HitReader"/>) — all in one snapshot. Where the content lives is the content
/// source's business, how the input is searched the kind's (ADR-0014).
/// </summary>
internal sealed class QueryRunner
{
    private readonly Database _database;
    private readonly ContentSource _source;
    private readonly ILuiSimilarity _defaultSimilarity;
    private readonly LuiTypoDistance _typoDefault;
    private readonly CandidateReader _candidates;
    private readonly TypoCorrection _correction;
    private readonly HitReader _hits;

    internal QueryRunner(
        Database database,
        ContentSource source,
        PayloadReader payload,
        ILuiSimilarity defaultSimilarity,
        TypoDictionary typos,
        LuiTypoDistance typoDefault)
    {
        _database = database;
        _source = source;
        _defaultSimilarity = defaultSimilarity;
        _typoDefault = typoDefault;
        _candidates = new CandidateReader(source);
        _correction = new TypoCorrection(typos, _candidates, source);
        _hits = new HitReader(source, payload);
    }

    /// <summary>How many errors a query of this instance forgives unless its options say otherwise.</summary>
    internal LuiTypoDistance TypoDistance => _typoDefault;

    internal LuiResult Execute(string input, LuiQueryOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(options);

        var started = Stopwatch.GetTimestamp();
        using var scope = _database.Begin();

        var query = PreparedQuery.Create(input, _source.Normalization, _source.Rules);
        if (query.IsEmpty)
        {
            return new LuiResult { Query = input, Hits = [], Timings = Timings(options, started, started, started, started, started) };
        }

        using var connection = _database.OpenConnection();

        // One snapshot for the candidates and for the hits. A deferred transaction pins the WAL snapshot
        // at its first read; without it a writer can commit between the two statements, and a freed
        // doc_id that is handed to a new entry would give a hit the text of the old one and the key and
        // payload of the new one (review B2).
        using var snapshot = connection.BeginTransaction(deferred: true);

        var similarity = options.Similarity ?? _defaultSimilarity;

        // the buffers of the candidates are reused between the queries of a thread (issue #30)
        var candidates = _candidates.Read(connection, snapshot, query, options, variant: 0, cancellationToken, out var count, out var truncated);
        var original = candidates;
        Scored[]? ranked = null;

        try
        {
            var read = Stopwatch.GetTimestamp();

            (candidates, count, truncated, var queries, var corrected, var skipped, var ran) =
                Correct(connection, snapshot, query, options, candidates, count, truncated, cancellationToken);
            var corrections = ran ? Stopwatch.GetTimestamp() : read;

            // A one- or two-character input carries no similarity worth ranking by: the
            // candidates all begin with it, and "the most popular first" is the answer (ADR-0028).
            var order = query.IsPrefixPath ? LuiRankingOrder.WeightOnly : options.Rank;
            ranked = Ranker.Rank(
                candidates, count, queries, order, options.PriorityMode, similarity, _source.Normalization, options.MaxResults, cancellationToken);
            var rankedAt = Stopwatch.GetTimestamp();

            var hits = _hits.Read(connection, snapshot, ranked, queries, similarity, order);
            var done = Stopwatch.GetTimestamp();

            return Result(input, hits, count, truncated, corrected, skipped, Timings(options, started, read, corrections, rankedAt, done));
        }
        finally
        {
            ReturnBuffers(original, candidates, ranked);
        }
    }

    /// <summary>
    /// Gives the buffers of the candidates back to the thread (issue #30).
    /// </summary>
    private static void ReturnBuffers(Scored[] original, Scored[] current, Scored[]? ranked)
    {
        ScoredBuffer.Return(original);
        if (!ReferenceEquals(current, original))
        {
            ScoredBuffer.Return(current);
        }

        if (ranked is not null)
        {
            ScoredBuffer.Return(ranked);
        }
    }

    /// <summary>
    /// The result of a query: the hits and what the query did.
    /// </summary>
    private static LuiResult Result(
        string input,
        IReadOnlyList<LuiHit> hits,
        int count,
        bool truncated,
        string? corrected,
        bool skipped,
        LuiTimings? timings) =>
        new()
        {
            Query = input,
            Hits = hits,
            CandidatesConsidered = count,
            Truncated = truncated,
            CorrectedQuery = corrected,
            CorrectionSkipped = skipped,
            Timings = timings,
        };

    /// <summary>
    /// The typo correction, when it is wanted (ADR-0012): the candidates of the corrections united with those of the input.
    /// While another build of the dictionary runs, or when the build fails, the query does not wait for it and does not
    /// fail: it answers with what the input found, and says so (ADR-0021).
    /// </summary>
    private (Scored[] Candidates, int Count, bool Truncated, IReadOnlyList<string> Queries, string? Corrected, bool Skipped, bool Ran) Correct(
        SqliteConnection connection,
        SqliteTransaction snapshot,
        PreparedQuery query,
        LuiQueryOptions options,
        Scored[] candidates,
        int count,
        bool truncated,
        CancellationToken cancellationToken)
    {
        if (!(options.TypoTolerance ?? (_typoDefault != LuiTypoDistance.None)) || query.IsPrefixPath || !TypoCorrection.IsWanted(candidates, count))
        {
            return (candidates, count, truncated, [query.Search], null, false, false);
        }

        if (!_correction.IsReady())
        {
            return (candidates, count, truncated, [query.Search], null, true, false);
        }

        var (united, total, cut, queries, corrected) =
            _correction.Apply(connection, snapshot, query, options, candidates, count, truncated, cancellationToken);
        return (united, total, cut, queries, corrected, false, true);
    }

    private static LuiTimings? Timings(
        LuiQueryOptions options, long started, long read, long corrected, long ranked, long done) =>
        options.TrackTimings
            ? new LuiTimings
            {
                CandidateRead = Stopwatch.GetElapsedTime(started, read),
                Correction = Stopwatch.GetElapsedTime(read, corrected),
                Ranking = Stopwatch.GetElapsedTime(corrected, ranked),
                Payload = Stopwatch.GetElapsedTime(ranked, done),
                Total = Stopwatch.GetElapsedTime(started, done),
            }
            : null;
}
