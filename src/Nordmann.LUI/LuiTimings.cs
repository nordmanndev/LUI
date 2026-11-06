namespace Nordmann.LUI;

/// <summary>Where the time of a query went; only measured with <see cref="LuiQueryOptions.TrackTimings"/>.</summary>
public sealed record LuiTimings
{
    /// <summary>
    /// Reading the candidates of the input out of SQLite — a duration, as the other properties are. The budget of candidates
    /// is <see cref="LuiQueryOptions.Candidates"/>; how many were read is <see cref="LuiResult.CandidatesConsidered"/>.
    /// </summary>
    public TimeSpan CandidateRead { get; init; }

    /// <summary>
    /// The typo correction: building the dictionary on first use, the lookups, and the candidates of the
    /// corrections; zero when nothing was corrected.
    /// </summary>
    public TimeSpan Correction { get; init; }

    /// <summary>Normalising, comparing and sorting the candidates.</summary>
    public TimeSpan Ranking { get; init; }

    /// <summary>Reading key and payload of the hits.</summary>
    public TimeSpan Payload { get; init; }

    /// <summary>The whole query, the correction included.</summary>
    public TimeSpan Total { get; init; }
}
