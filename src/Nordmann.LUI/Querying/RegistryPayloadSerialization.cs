namespace Nordmann.LUI.Querying;

/// <summary>
/// The registry of the catalog on the read path of an index that was opened by name (ADR-0072, until #268): the caller names the type
/// with <see cref="LuiHit.GetPayload{T}"/> and the registry resolves it. There is no untyped payload: an entry of an index
/// without a declared payload type cannot carry one.
/// </summary>
/// <param name="registry">The registry of the catalog, frozen when the catalog was opened.</param>
internal sealed class RegistryPayloadSerialization(FrozenPayloadRegistry registry) : PayloadSerialization
{
    internal override byte[] Serialize(object payload, string indexName) =>
        throw new LuiConfigurationException(
            $"The index '{indexName}' has no declared payload type, so its entries carry no payload: with LuiCatalogOptions.Payloads there is "
            + "no untyped payload. Declare the type with a LuiIndexDefinition<TKey, TPayload> (and register it) and write through its handle.");

    internal override T Deserialize<T>(ReadOnlySpan<byte> bytes, string indexName) =>
        registry.Resolve<T>(indexName).Serializer.Deserialize(bytes);
}
