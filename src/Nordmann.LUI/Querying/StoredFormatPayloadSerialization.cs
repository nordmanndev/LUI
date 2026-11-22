namespace Nordmann.LUI.Querying;

/// <summary>
/// The serialisation of an index that was opened by name and <b>stores a payload type and a format</b> (ADR-0072): it reads as the catalog
/// reads, and it never writes. A payload of such an index is written through <c>catalog.Index(definition)</c>, with the serializer the
/// fingerprint names — the serializer of the old calls, or the registry by runtime type, must not put bytes of another format into it.
/// </summary>
/// <param name="inner">How the catalog reads.</param>
/// <param name="typeName">The stored name of the payload type.</param>
/// <param name="format">The stored format.</param>
internal sealed class StoredFormatPayloadSerialization(PayloadSerialization inner, string typeName, string format) : PayloadSerialization
{
    internal override bool CanRead => inner.CanRead;

    internal override byte[] Serialize(object payload, string indexName) =>
        throw new LuiConfigurationException(
            $"The index '{indexName}' stores its payloads as '{typeName}' in the format '{format}': write them through catalog.Index(definition) "
            + "with a LuiIndexDefinition<TKey, TPayload>, which uses the registered serializer of that format. An index that was opened by name "
            + "writes no payload into it.");

    internal override T Deserialize<T>(ReadOnlySpan<byte> bytes, string indexName) => inner.Deserialize<T>(bytes, indexName);
}
