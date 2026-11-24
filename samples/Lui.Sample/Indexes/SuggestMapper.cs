using System.Diagnostics;
using Nordmann.LUI.Sample.Api;

namespace Nordmann.LUI.Sample.Indexes;

/// <summary>
/// The one place where a <see cref="LuiResult{TKey}"/> becomes the response of the page. The handle of an index with a payload
/// type hands out the payload in that type; the page gets it as JSON.
/// </summary>
internal static class SuggestMapper
{
    internal static SuggestResponse Run(
        LuiIndex<long> index,
        string text,
        LuiQueryOptions options,
        CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        var result = index.Search(text, options, cancellationToken);
        clock.Stop();
        return Map(index.Name, index.TypoDictionaryState, result.Query, result.CorrectedQuery, result.CorrectionSkipped, result.Truncated, result.CandidatesConsidered, result.Timings, clock.Elapsed,
            result.Hits.Select(hit => Dto(hit.Key, hit.Text, hit.Weight, hit.Priority, hit.Similarity, hit.AtWordStart, payload: null)));
    }

    internal static SuggestResponse Run<TPayload>(
        LuiIndex<long, TPayload> index,
        string text,
        LuiQueryOptions options,
        CancellationToken cancellationToken)
        where TPayload : notnull
    {
        var clock = Stopwatch.StartNew();
        var result = index.Search(text, options, cancellationToken);
        clock.Stop();
        return Map(index.Name, index.TypoDictionaryState, result.Query, result.CorrectedQuery, result.CorrectionSkipped, result.Truncated, result.CandidatesConsidered, result.Timings, clock.Elapsed,
            result.Hits.Select(hit => Dto(hit.Key, hit.Text, hit.Weight, hit.Priority, hit.Similarity, hit.AtWordStart, hit.Payload)));
    }

    private static HitDto Dto(long key, string text, int weight, LuiPriority priority, double similarity, bool atWordStart, object? payload) =>
        new(key, text, weight, priority.ToString(), similarity, atWordStart, payload);

    private static SuggestResponse Map(
        string name,
        LuiTypoDictionaryState dictionary,
        string query,
        string? corrected,
        bool correctionSkipped,
        bool truncated,
        int candidates,
        LuiTimings? timings,
        TimeSpan elapsed,
        IEnumerable<HitDto> hits) =>
        new(
            name,
            query,
            corrected,
            correctionSkipped,
            truncated,
            candidates,
            timings is { } t
                ? new TimingsDto(t.CandidateRead.TotalMilliseconds, t.Correction.TotalMilliseconds, t.Ranking.TotalMilliseconds, t.Payload.TotalMilliseconds, t.Total.TotalMilliseconds)
                : null,
            elapsed.TotalMilliseconds,
            dictionary.ToString(),
            [.. hits]);
}
