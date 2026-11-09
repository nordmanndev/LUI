namespace Nordmann.LUI.Unit.Tests.Support;

/// <summary>
/// The payload of the unit tests. It has its own context here on purpose:
/// the unit layer must not depend on the test infrastructure that opens
/// databases.
/// </summary>
/// <param name="Slug">The slug of the chip.</param>
/// <param name="Color">Its colour, when it has one.</param>
public sealed record Chip(string Slug, string? Color = null);
