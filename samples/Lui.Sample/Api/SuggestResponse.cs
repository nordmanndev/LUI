namespace Nordmann.LUI.Sample.Api;

/// <summary>
/// A <see cref="LuiResult"/> as the page gets it. <c>Results</c> is the property the flexdatalist input
/// reads; the rest — the envelope — is what the panels show and flexdatalist would drop.
/// </summary>
internal sealed record SuggestResponse(
    string Index,
    string Query,
    string? CorrectedQuery,
    bool CorrectionSkipped,
    bool Truncated,
    int CandidatesConsidered,
    TimingsDto? Timings,
    double ServerMs,
    string TypoDictionaryState,
    IReadOnlyList<HitDto> Results);
