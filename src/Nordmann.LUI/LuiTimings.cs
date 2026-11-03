namespace Nordmann.LUI;

/// <summary>Where the time of a query went; only measured with <c>TrackTimings</c>.</summary>
/// <param name="Candidates">Reading the candidates of the input out of SQLite.</param>
/// <param name="Ranking">Normalising, comparing and sorting the candidates.</param>
/// <param name="Payload">Reading key and payload of the hits.</param>
/// <param name="Total">The whole query, the correction included.</param>
/// <param name="Correction">The typo correction: building the dictionary on first use, the lookups, and the candidates of the corrections; zero when nothing was corrected.</param>
public sealed record LuiTimings(TimeSpan Candidates, TimeSpan Ranking, TimeSpan Payload, TimeSpan Total, TimeSpan Correction);
