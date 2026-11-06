namespace Nordmann.LUI.Unit.Tests;

/// <summary>A payload with the slug of a chip and a rank its constructor cannot do without.</summary>
/// <param name="Slug">The slug.</param>
/// <param name="Rank">The rank.</param>
public sealed record Ranked(string Slug, int Rank);
