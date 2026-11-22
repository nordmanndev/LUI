namespace Nordmann.LUI.Querying;

/// <summary>The serializer of the old calls (<see cref="LuiCatalogOptions.PayloadSerializer"/>, until #268): one for the catalog, it writes the type a payload has.</summary>
/// <param name="serializer">The serializer of the catalog.</param>
internal sealed class LegacyPayloadSerialization(ILuiPayloadSerializer serializer) : PayloadSerialization
{
    internal override byte[] Serialize(object payload, string indexName) => serializer.Serialize(payload);

    internal override T Deserialize<T>(ReadOnlySpan<byte> bytes, string indexName) => serializer.Deserialize<T>(bytes);
}
