namespace Nordmann.LUI;

/// <summary>The answer to a query (LUI-Spezifikation §8.3).</summary>
/// <typeparam name="TKey">The key of the application.</typeparam>
/// <typeparam name="TPayload">The payload that comes back with a hit.</typeparam>
public sealed record LuiResult<TKey, TPayload>
{
    /// <summary>The input, as it was given.</summary>
    public required string Query { get; init; }

    /// <summary>The best entries, best first; at most <see cref="LuiQueryOptions.MaxResults"/>.</summary>
    public required IReadOnlyList<LuiHit<TKey, TPayload>> Hits { get; init; }

    /// <summary>How many candidates were ranked; at most <see cref="LuiQueryOptions.Candidates"/>.</summary>
    public int CandidatesConsidered { get; init; }

    /// <summary>Whether there were more candidates than <see cref="LuiQueryOptions.Candidates"/>.</summary>
    public bool Truncated { get; init; }

    /// <summary>
    /// What was searched when the typo tolerance corrected the input (in search form, e.g. <c>apfelkuchen</c>
    /// for <c>apflekuchen</c>); <see langword="null"/> when nothing was corrected.
    /// </summary>
    public string? CorrectedQuery { get; init; }

    /// <summary>Where the time went; only set with <see cref="LuiQueryOptions.TrackTimings"/>.</summary>
    public LuiTimings? Timings { get; init; }
}
