namespace Nordmann.LUI;

/// <summary>
/// One entry whose payload cannot be read (ADR-0074): the key of the entry and why. It is what <c>CheckPayloads()</c> lists, so
/// that the application can write the entry again. A record — two of them with the same values are equal.
/// </summary>
/// <typeparam name="TKey">The type of the keys of the index.</typeparam>
public sealed record LuiUnreadablePayload<TKey>
    where TKey : notnull
{
    /// <summary>The key of the entry.</summary>
    public required TKey Key { get; init; }

    /// <summary>
    /// Why the payload cannot be read — what a query that met the entry would say: the index, the key, the type and the reason the
    /// serializer gave, or that no payload is stored.
    /// </summary>
    public required string Message { get; init; }
}
