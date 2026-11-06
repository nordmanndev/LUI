namespace Nordmann.LUI.Testing;

/// <summary>A payload of another shape than <see cref="Chip"/> — what a caller asks for when it mistakes the index.</summary>
/// <param name="Title">The title.</param>
/// <param name="Minutes">The minutes.</param>
public sealed record RecipeCard(string Title, int Minutes);
