namespace Nordmann.LUI.Sample.Api;

/// <summary>What a fill of a bulk index is doing — the index panel polls it while the index is loaded (ADR-0029).</summary>
internal sealed record FillStatus(
    string Index,
    string State,
    int Requested,
    long Read,
    long Added,
    long Changed,
    long Unchanged,
    long Removed,
    int Batches,
    double ElapsedMs,
    string? Note,
    string? Error);
