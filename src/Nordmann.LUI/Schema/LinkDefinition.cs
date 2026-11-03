namespace Nordmann.LUI.Schema;

/// <summary>
/// The table of the application a <c>Linked</c> index searches, and its columns (LUI-Spezifikation §7.3,
/// ADR-0015). Part of the definition: a link to another table or another column is another index.
/// </summary>
/// <param name="Table">The table of the application.</param>
/// <param name="Key">Its <c>INTEGER PRIMARY KEY</c>: the key of the hits and the document id of the index.</param>
/// <param name="Text">The column that is searched and shown.</param>
/// <param name="Weight">The column of the popularity; <see langword="null"/> for none (every weight 0).</param>
/// <param name="Priority">The column of the priority (0 … 3); <see langword="null"/> for none (every entry normal).</param>
internal sealed record LinkDefinition(string Table, string Key, string Text, string? Weight, string? Priority);
