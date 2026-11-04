namespace Nordmann.LUI.Sample.Api;

/// <summary>An index as the index panel shows it.</summary>
internal sealed record IndexInfo(
    string Name,
    string Kind,
    string Storage,
    bool TypoTolerance,
    string TypoDictionaryState,
    long Count,
    string Payload,
    string Description,
    long? Pending,
    string? Triggers);
