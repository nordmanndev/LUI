namespace Nordmann.LUI.Unit.Tests;

/// <summary>A payload of another shape than <see cref="Chip"/>: none of its members is a member of a chip.</summary>
/// <param name="Title">The title.</param>
/// <param name="Minutes">The minutes.</param>
public sealed record Recipe(string Title, int Minutes);
