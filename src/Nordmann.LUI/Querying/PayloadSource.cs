namespace Nordmann.LUI.Querying;

/// <summary>
/// Where the payload of a hit comes from (ADR-0035): one per index, shared by all its hits. An embedded index hands out
/// the stored bytes and reads them with the serialisation of the index — the serializer of its payload type, the registry of the
/// catalog, or the serialiser of the old calls (ADR-0072); a linked index hands out what its map built.
/// </summary>
/// <param name="indexName">The index, for the message of an error.</param>
/// <param name="serialization">How the stored bytes are read; <see langword="null"/> when the payload is already built.</param>
/// <param name="serialized">Whether the payload is bytes that the serialisation reads.</param>
internal sealed class PayloadSource(string indexName, PayloadSerialization? serialization, bool serialized)
{
    /// <summary>The payloads of an embedded index: bytes, read with the serialisation of the index.</summary>
    internal static PayloadSource Serialized(string indexName, PayloadSerialization serialization) => new(indexName, serialization, serialized: true);

    /// <summary>The payloads of a linked index: built by its map.</summary>
    internal static PayloadSource Built(string indexName) => new(indexName, serialization: null, serialized: false);

    /// <summary>
    /// The payload of a hit of an index with a payload type (ADR-0074): it has to be there, and it has to be readable. An entry whose
    /// payload is not stored, and a map that returned <see langword="null"/>, are as much a failure as bytes that cannot be read.
    /// </summary>
    internal T Require<T>(object? stored, string key) =>
        stored is null
            ? throw new LuiPayloadException(
                indexName,
                key,
                typeof(T),
                serialized ? "the entry has no stored payload." : "the map of the linked index returned null.")
            : Read<T>(stored, key);

    /// <summary>Reads a stored payload as <typeparamref name="T"/>.</summary>
    internal T Read<T>(object stored, string key)
    {
        if (!serialized)
        {
            return stored is T built
                ? built
                : throw new LuiPayloadException(
                    indexName, key, typeof(T), $"the map of the linked index built a '{stored.GetType().FullName}'.");
        }

        if (serialization is not { CanRead: true })
        {
            throw new LuiPayloadException(
                indexName, key, typeof(T), "the catalog has no serialiser — set LuiCatalogOptions.Payloads.");
        }

        try
        {
            return serialization.Deserialize<T>((byte[])stored, indexName);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or LuiConfigurationException))
        {
            // whatever the serialiser throws — LUI's own or one of the application — the caller gets one exception
            // that names the index, the key and the type (review of PR 60, I1). A type that the registry does not know is no payload
            // that cannot be read but a configuration that is wrong (ADR-0072): it stays a LuiConfigurationException
            throw new LuiPayloadException(indexName, key, typeof(T), exception.Message, exception);
        }
    }
}
