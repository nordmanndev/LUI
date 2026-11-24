#pragma warning disable MA0048 // the generic form of a name that has a non-generic file: <Name>OfKey.cs (ADR-0073)
namespace Nordmann.LUI;

/// <summary>
/// One hit of a search through a typed handle (ADR-0073). It is a record — two hits with the same values are equal — with
/// named, initialised properties and no constructor with parameters: a property that comes later breaks no caller.
/// </summary>
/// <typeparam name="TKey">The type of the keys of the index.</typeparam>
public sealed record LuiHit<TKey>
    where TKey : notnull
{
    /// <summary>The key of the entry.</summary>
    public required TKey Key { get; init; }

    /// <summary>The text of the entry — for a linked index, read from the table of the application.</summary>
    public required string Text { get; init; }

    /// <summary>The weight of the entry.</summary>
    public int Weight { get; init; }

    /// <summary>The priority of the entry.</summary>
    public LuiPriority Priority { get; init; }

    /// <summary>How near the text was to the query, 0 to 1.</summary>
    public double Similarity { get; init; }

    /// <summary>Whether the query matched at the beginning of a word.</summary>
    public bool AtWordStart { get; init; }

    /// <summary>The hit of the index below the handle, with a typed key.</summary>
    internal static LuiHit<TKey> From(RawHit hit, string indexName) =>
        new()
        {
            Key = KeyConversion.FromText<TKey>(hit.Key, indexName),
            Text = hit.Text,
            Weight = hit.Weight,
            Priority = hit.Priority,
            Similarity = hit.Similarity,
            AtWordStart = hit.AtWordStart,
        };
}
#pragma warning restore MA0048
