namespace Nordmann.LUI.Sample.Api;

/// <summary>One hit as the page gets it: everything <see cref="LuiHit"/> has, the payload as JSON.</summary>
internal sealed record HitDto(
    long Key,
    string Text,
    int Weight,
    string Priority,
    double Similarity,
    bool AtWordStart,
    object? Payload);
