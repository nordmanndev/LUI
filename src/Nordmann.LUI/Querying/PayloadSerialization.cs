namespace Nordmann.LUI.Querying;

/// <summary>
/// How the payloads of an index become bytes and back (ADR-0035, ADR-0072). There are four ways, and the catalog picks one per index:
/// the <b>codec</b> of a declared payload type (the typed path: the serializer the registry holds for the type), the
/// <b>registry</b> of the catalog for the read path of an index that was opened by name (it resolves the type the caller asks
/// for), the <b>serializer of the old path</b> (<see cref="LuiCatalogOptions.PayloadSerializer"/>, until #268), and none.
/// </summary>
internal abstract class PayloadSerialization
{
    /// <summary>The catalog has no serializer of any kind: an entry that carries a payload is refused.</summary>
    internal static PayloadSerialization None { get; } = new NoSerialization();

    /// <summary>Whether a stored payload can be read at all — <see langword="false"/> when the catalog has no serializer.</summary>
    internal virtual bool CanRead => true;

    /// <summary>Serializes the payload of an entry.</summary>
    /// <param name="payload">The payload, never <see langword="null"/>.</param>
    /// <param name="indexName">The index, for the message of an error.</param>
    /// <returns>The bytes that are stored.</returns>
    /// <exception cref="LuiConfigurationException">The index has no declared payload type and the catalog has a registry.</exception>
    /// <exception cref="InvalidOperationException">The catalog has no serializer.</exception>
    internal abstract byte[] Serialize(object payload, string indexName);

    /// <summary>Reads a stored payload as the type the caller asks for.</summary>
    /// <typeparam name="T">The type the caller asks for.</typeparam>
    /// <param name="bytes">The stored bytes.</param>
    /// <param name="indexName">The index, for the message of an error.</param>
    /// <returns>The payload.</returns>
    /// <exception cref="LuiConfigurationException">The registry of the catalog does not know <typeparamref name="T"/>.</exception>
    internal abstract T Deserialize<T>(ReadOnlySpan<byte> bytes, string indexName);

    private sealed class NoSerialization : PayloadSerialization
    {
        internal override bool CanRead => false;

        internal override byte[] Serialize(object payload, string indexName) =>
            throw new InvalidOperationException(
                $"The entry of the index '{indexName}' has a payload, and the catalog has no serialiser: declare the payload type with a "
                + "LuiIndexDefinition<TKey, TPayload> and register it in LuiCatalogOptions.Payloads — or, for an index of the old calls, "
                + "set LuiCatalogOptions.PayloadSerializer (LuiPayloadSerializer.Json(context)).");

        internal override T Deserialize<T>(ReadOnlySpan<byte> bytes, string indexName) =>
            throw new InvalidOperationException($"The catalog has no payload serializer: the payload of the index '{indexName}' cannot be read.");
    }
}
