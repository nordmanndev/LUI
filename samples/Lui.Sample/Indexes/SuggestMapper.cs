using System.Diagnostics;
using Nordmann.LUI.Sample.Api;

namespace Nordmann.LUI.Sample.Indexes;

/// <summary>The one place where a <see cref="LuiResult{TKey, TPayload}"/> becomes the response of the page.</summary>
internal static class SuggestMapper
{
    internal static SuggestResponse Run<TPayload>(
        ILuiIndex<long, TPayload> index,
        string text,
        LuiQueryOptions options,
        CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        var result = index.Search(text, options, cancellationToken);
        clock.Stop();
        return Map(index, result, clock.Elapsed);
    }

    private static SuggestResponse Map<TPayload>(ILuiIndex<long, TPayload> index, LuiResult<long, TPayload> result, TimeSpan elapsed) =>
        new(
            index.Name,
            result.Query,
            result.CorrectedQuery,
            result.CorrectionSkipped,
            result.Truncated,
            result.CandidatesConsidered,
            result.Timings is { } t
                ? new TimingsDto(t.CandidateRead.TotalMilliseconds, t.Correction.TotalMilliseconds, t.Ranking.TotalMilliseconds, t.Payload.TotalMilliseconds, t.Total.TotalMilliseconds)
                : null,
            elapsed.TotalMilliseconds,
            index.TypoDictionaryState.ToString(),
            [.. result.Hits.Select(hit => new HitDto(
                hit.Key,
                hit.Text,
                hit.Weight,
                hit.Priority.ToString(),
                hit.Similarity,
                hit.AtWordStart,
                typeof(TPayload) == typeof(LuiNoPayload) ? null : hit.Payload))]);
}
