namespace Nordmann.LUI.Schema;

/// <summary>
/// The table of the application a <c>Linked</c> index searches, and its columns (LUI-Spezifikation §7.3,
/// ADR-0015). Part of the definition: a link to another table or another column is another index.
/// <para>
/// The columns of the payload are part of it too (ADR-0033): a linked index with a payload can only be opened
/// when the columns it reads are stored with it.
/// </para>
/// </summary>
/// <param name="Table">The table of the application.</param>
/// <param name="Key">Its <c>INTEGER PRIMARY KEY</c>: the key of the hits and the document id of the index.</param>
/// <param name="Text">The column that is searched and shown.</param>
/// <param name="Weight">The column of the popularity; <see langword="null"/> for none (every weight 0).</param>
/// <param name="Priority">The column of the priority (0 … 3); <see langword="null"/> for none (every entry normal).</param>
internal sealed record LinkDefinition(string Table, string Key, string Text, string? Weight, string? Priority)
{
    /// <summary>
    /// The columns the payload is built from, in the order they are read; <see langword="null"/> when the index has no
    /// payload. The definition that comes out of <c>lui_meta</c> holds a list of its own, and it is the same list of
    /// columns as the one that was asked for.
    /// </summary>
    public IReadOnlyList<string>? PayloadColumns { get; init; }

    /// <summary>
    /// Two links are the same when their tables and their columns are the same — the payload columns in the same
    /// order. A list is compared by its content here: the definition that was read out of <c>lui_meta</c> holds
    /// another list than the one that was asked for, and it is still the same index.
    /// </summary>
    /// <param name="other">The other link.</param>
    /// <returns><see langword="true"/> when both are the same link.</returns>
    public bool Equals(LinkDefinition? other) =>
        other is not null
        && string.Equals(Table, other.Table, StringComparison.Ordinal)
        && string.Equals(Key, other.Key, StringComparison.Ordinal)
        && string.Equals(Text, other.Text, StringComparison.Ordinal)
        && string.Equals(Weight, other.Weight, StringComparison.Ordinal)
        && string.Equals(Priority, other.Priority, StringComparison.Ordinal)
        && SameColumns(PayloadColumns, other.PayloadColumns);

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(Table, Key, Text, Weight, Priority, PayloadColumns?.Count ?? -1);

    private static bool SameColumns(IReadOnlyList<string>? left, IReadOnlyList<string>? right) =>
        left is null || right is null
            ? left is null && right is null
            : left.SequenceEqual(right, StringComparer.Ordinal);
}
