using System.Diagnostics;
using Nordmann.LUI.Sample.Api;

namespace Nordmann.LUI.Sample.Indexes;

/// <summary>
/// The one place where a <see cref="LuiResult"/> becomes the response of the page. The hit knows no payload type
/// (ADR-0035): the index of the sample that asks says how its payload is read — the type stands where it is used.
/// </summary>
internal static class SuggestMapper
{
    internal static SuggestResponse Run(
        ILuiIndex index,
        string text,
        LuiQueryOptions options,
        Func<LuiHit, object?> payload,
        CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        var result = index.Search(text, options, cancellationToken);
        clock.Stop();
        return Map(index, result, payload, clock.Elapsed);
    }

    private static SuggestResponse Map(ILuiIndex index, LuiResult result, Func<LuiHit, object?> payload, TimeSpan elapsed) =>
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
                hit.Key.ToInt64(),
                hit.Text,
                hit.Weight,
                hit.Priority.ToString(),
                hit.Similarity,
                hit.AtWordStart,
                payload(hit)))]);
}
