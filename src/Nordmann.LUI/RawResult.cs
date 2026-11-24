namespace Nordmann.LUI;

/// <summary>The answer of a query as the handle receives it (ADR-0073): the hits are <see cref="RawHit"/>s, the rest is what <see cref="LuiResult{TKey}"/> carries.</summary>
internal sealed record RawResult
{
    /// <summary>The input that was searched.</summary>
    internal required string Query { get; init; }

    /// <summary>The hits, best first.</summary>
    internal required IReadOnlyList<RawHit> Hits { get; init; }

    /// <summary>How many candidates were ranked.</summary>
    internal int CandidatesConsidered { get; init; }

    /// <summary>Whether more candidates were there than were ranked.</summary>
    internal bool Truncated { get; init; }

    /// <summary>What was searched when the typo tolerance corrected the input.</summary>
    internal string? CorrectedQuery { get; init; }

    /// <summary>Whether a correction was wanted and not searched.</summary>
    internal bool CorrectionSkipped { get; init; }

    /// <summary>Where the time went; only set with <see cref="LuiQueryOptions.TrackTimings"/>.</summary>
    internal LuiTimings? Timings { get; init; }
}
