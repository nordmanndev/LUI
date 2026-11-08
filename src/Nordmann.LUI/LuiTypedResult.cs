namespace Nordmann.LUI;

/// <summary>
/// The answer of a typed view (issue #59, ADR-0044): the answer of the query, with the payload in the type of the
/// application. It says what <see cref="LuiResult"/> says; <see cref="LuiResult.As{TPayload}"/> makes one.
/// </summary>
public sealed record LuiTypedResult<TPayload>
{
    /// <summary>The input, as it was given.</summary>
    public required string Query { get; init; }

    /// <summary>The best entries, best first; at most <see cref="LuiQueryOptions.MaxResults"/>.</summary>
    public required IReadOnlyList<LuiTypedHit<TPayload>> Hits { get; init; }

    /// <summary>How many candidates were ranked; at most <see cref="LuiQueryOptions.Candidates"/>.</summary>
    public int CandidatesConsidered { get; init; }

    /// <summary>Whether there were more candidates than <see cref="LuiQueryOptions.Candidates"/>.</summary>
    public bool Truncated { get; init; }

    /// <summary>
    /// What was searched when the typo tolerance corrected the input (in search form); <see langword="null"/> when
    /// nothing was corrected.
    /// </summary>
    public string? CorrectedQuery { get; init; }

    /// <summary>
    /// Whether a correction was wanted and not searched (ADR-0021, ADR-0042): the typo dictionary is not built, it is
    /// being built, or its build failed. The query did not wait and did not fail.
    /// </summary>
    public bool CorrectionSkipped { get; init; }

    /// <summary>Where the time went; only set with <see cref="LuiQueryOptions.TrackTimings"/>.</summary>
    public LuiTimings? Timings { get; init; }
}
