namespace Nordmann.LUI;

/// <summary>
/// What <c>CheckPayloads()</c> found (ADR-0074): the entries of an embedded index whose payload cannot be read with the serializer
/// of the payload type. What <b>cannot be read</b> is the serializer's verdict — the strict JSON of LUI refuses a payload of an old
/// shape, a tolerant serializer may read it. The report is a class with reference equality: it holds a list.
/// </summary>
/// <typeparam name="TKey">The type of the keys of the index.</typeparam>
public sealed class LuiPayloadCheck<TKey>
    where TKey : notnull
{
    /// <summary>The entries whose payload cannot be read, in the order of the rows of the index (the order they were first written); empty when the index is healthy.</summary>
    public required IReadOnlyList<LuiUnreadablePayload<TKey>> Unreadable { get; init; }

    /// <summary>Whether every stored payload can be read — and a query of the index fails for no payload.</summary>
    public bool IsHealthy => Unreadable.Count == 0;
}
