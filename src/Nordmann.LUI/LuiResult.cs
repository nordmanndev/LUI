namespace Nordmann.LUI;

/// <summary>The answer to a query (LUI-Spezifikation §8.3).</summary>
public sealed record LuiResult
{
    /// <summary>The input, as it was given.</summary>
    public required string Query { get; init; }

    /// <summary>The best entries, best first; at most <see cref="LuiQueryOptions.MaxResults"/>.</summary>
    public required IReadOnlyList<LuiHit> Hits { get; init; }

    /// <summary>How many candidates were ranked; at most <see cref="LuiQueryOptions.Candidates"/>.</summary>
    public int CandidatesConsidered { get; init; }

    /// <summary>
    /// Whether there were more candidates than <see cref="LuiQueryOptions.Candidates"/>. For a <c>Linked</c> index it is
    /// also set when the read stopped at a second budget of rows whose text changed and is not applied yet — then the
    /// answer can be empty although matching rows exist: the index is behind, and <c>Pending()</c> / <c>ApplyPending()</c>
    /// are the way out (ADR-0017).
    /// </summary>
    public bool Truncated { get; init; }

    /// <summary>
    /// What was searched when the typo tolerance corrected the input (in search form, e.g. <c>apfelkuchen</c>
    /// for <c>apflekuchen</c>); <see langword="null"/> when nothing was corrected.
    /// </summary>
    public string? CorrectedQuery { get; init; }

    /// <summary>
    /// Whether a correction was wanted and not searched (ADR-0021): the typo dictionary was being built — after the start
    /// of an application, or while another query builds it —, or its build failed. The query did not wait for the build
    /// and did not fail: it answered with what the input found. Ask again once
    /// <see cref="ILuiIndex.TypoDictionaryState"/> is not <see cref="LuiTypoDictionaryState.Building"/>;
    /// after a failed build, the next query that needs a correction tries again.
    /// </summary>
    public bool CorrectionSkipped { get; init; }

    /// <summary>Where the time went; only set with <see cref="LuiQueryOptions.TrackTimings"/>.</summary>
    public LuiTimings? Timings { get; init; }
}
