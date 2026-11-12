namespace Nordmann.LUI.Sample.Api;

/// <summary>
/// An index as the index panel shows it. <see cref="Error"/> is what a broken index says: one index that cannot be read
/// must not empty the panel of the others — a panel without cards looks like data that was lost.
/// </summary>
internal sealed record IndexInfo(
    string Name,
    string Kind,
    string Storage,
    string TypoDistance,
    string TypoDictionaryState,
    long Count,
    string Payload,
    string Description,
    long? Pending,
    string? Triggers,
    string? Source = null,
    long? Capacity = null,
    string? Error = null);
