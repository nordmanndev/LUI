namespace Nordmann.LUI.Sample.Data;

/// <summary>
/// What a hit of <c>recipes</c> carries besides its text: the category the page groups by, the time the page shows
/// as meta, and an emoji. Stored as JSON next to the entry (an embedded index with <c>WithPayload</c>, ADR-0013).
/// </summary>
internal sealed record RecipePayload(string Category, int Minutes, string Emoji);
