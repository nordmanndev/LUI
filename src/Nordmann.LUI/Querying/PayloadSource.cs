using System.Text.Json;

namespace Nordmann.LUI.Querying;

/// <summary>
/// Where the payload of a hit comes from (ADR-0035): one per index, shared by all its hits. An embedded index hands out
/// the stored bytes and reads them with the serialiser of the catalog; a linked index hands out what its map built.
/// </summary>
/// <param name="indexName">The index, for the message of an error.</param>
/// <param name="serializer">The serialiser of the catalog; <see langword="null"/> when the payload is already built.</param>
/// <param name="serialized">Whether the payload is bytes that the serialiser reads.</param>
internal sealed class PayloadSource(string indexName, ILuiPayloadSerializer? serializer, bool serialized)
{
    /// <summary>The payloads of an embedded index: bytes, read with the serialiser of the catalog.</summary>
    internal static PayloadSource Serialized(string indexName, ILuiPayloadSerializer? serializer) => new(indexName, serializer, serialized: true);

    /// <summary>The payloads of a linked index: built by its map.</summary>
    internal static PayloadSource Built(string indexName) => new(indexName, serializer: null, serialized: false);

    /// <summary>Reads a stored payload as <typeparamref name="T"/>.</summary>
    internal T Read<T>(object stored, LuiKey key)
    {
        if (!serialized)
        {
            return stored is T built
                ? built
                : throw new LuiPayloadException(
                    indexName, key, typeof(T), $"the map of the linked index built a '{stored.GetType().FullName}'.");
        }

        if (serializer is null)
        {
            throw new LuiPayloadException(
                indexName, key, typeof(T), "the catalog has no serialiser — set LuiCatalogOptions.PayloadSerializer.");
        }

        try
        {
            return serializer.Deserialize<T>((byte[])stored);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidCastException or FormatException)
        {
            throw new LuiPayloadException(indexName, key, typeof(T), exception.Message, exception);
        }
    }
}
