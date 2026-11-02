namespace Nordmann.LUI;

/// <summary>The answer to a query (LUI-Spezifikation §8.3).</summary>
/// <typeparam name="TKey">The key of the application.</typeparam>
/// <typeparam name="TPayload">The payload that comes back with a hit.</typeparam>
/// <param name="Query">The input, as it was given.</param>
/// <param name="Hits">The best entries, best first; at most <c>Take</c>.</param>
/// <param name="CandidatesConsidered">How many candidates were ranked; at most <c>Candidates</c>.</param>
/// <param name="Truncated">Whether there were more candidates than <c>Candidates</c>.</param>
/// <param name="CorrectedQuery">Reserved for the typo tolerance; <see langword="null"/> until it is built.</param>
/// <param name="Timings">Only set with <c>TrackTimings</c>.</param>
public sealed record LuiResult<TKey, TPayload>(
    string Query,
    IReadOnlyList<LuiHit<TKey, TPayload>> Hits,
    int CandidatesConsidered,
    bool Truncated,
    string? CorrectedQuery,
    LuiTimings? Timings);
