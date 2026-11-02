namespace Nordmann.LUI;

/// <summary>Where the time of a query went; only measured with <c>TrackTimings</c>.</summary>
/// <param name="Candidates">Reading the candidates out of SQLite.</param>
/// <param name="Ranking">Normalising, comparing and sorting the candidates.</param>
/// <param name="Payload">Reading key and payload of the hits.</param>
/// <param name="Total">The whole query.</param>
public sealed record LuiTimings(TimeSpan Candidates, TimeSpan Ranking, TimeSpan Payload, TimeSpan Total);
