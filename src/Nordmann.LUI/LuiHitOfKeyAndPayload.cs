#pragma warning disable MA0048 // the generic form of a name that has a non-generic file: <Name>OfKeyAndPayload.cs (ADR-0073)
namespace Nordmann.LUI;

/// <summary>
/// One hit of a search through a handle with a payload (ADR-0073). It is a record — two hits with the same values are equal —
/// with named, initialised properties and no constructor with parameters: a property that comes later breaks no caller. It does
/// not derive from <see cref="LuiHit{TKey}"/>. The hit carries its <see cref="Payload"/> (ADR-0074).
/// </summary>
/// <typeparam name="TKey">The type of the keys of the index.</typeparam>
/// <typeparam name="TPayload">The type of the payload of the index.</typeparam>
public sealed record LuiHit<TKey, TPayload>
    where TKey : notnull
    where TPayload : notnull
{
    /// <summary>The key of the entry.</summary>
    public required TKey Key { get; init; }

    /// <summary>The text of the entry — for a linked index, read from the table of the application.</summary>
    public required string Text { get; init; }

    /// <summary>
    /// The payload of the entry, in the type of the application. It is set when the query returns and never changes: the payloads
    /// of all the hits are read with the hits (an embedded index) or built by the map (a linked one) before <c>Search</c> or
    /// <c>Execute</c> returns, so reading it does no I/O, calls no serializer and no map, throws nothing, and works after the catalog
    /// is disposed. A payload that cannot be read fails the query with a <see cref="LuiPayloadException"/>, not the access to the hit.
    /// </summary>
    public required TPayload Payload { get; init; }

    /// <summary>The weight of the entry.</summary>
    public int Weight { get; init; }

    /// <summary>The priority of the entry.</summary>
    public LuiPriority Priority { get; init; }

    /// <summary>How near the text was to the query, 0 to 1.</summary>
    public double Similarity { get; init; }

    /// <summary>Whether the query matched at the beginning of a word.</summary>
    public bool AtWordStart { get; init; }

    /// <summary>The hit of the index below the handle, with a typed key.</summary>
    internal static LuiHit<TKey, TPayload> From(RawHit hit, string indexName) =>
        new()
        {
            Key = KeyConversion.FromText<TKey>(hit.Key, indexName),
            Text = hit.Text,
            Weight = hit.Weight,
            Priority = hit.Priority,
            Similarity = hit.Similarity,
            AtWordStart = hit.AtWordStart,
            Payload = hit.RequirePayload<TPayload>(),
        };
}
#pragma warning restore MA0048
