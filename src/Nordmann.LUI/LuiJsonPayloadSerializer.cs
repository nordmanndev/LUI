using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Nordmann.LUI;

/// <summary>
/// Serialises a payload as UTF-8 JSON with a source-generated
/// <see cref="JsonTypeInfo{T}"/>: no reflection, so it is safe for trimming and AOT.
/// </summary>
/// <typeparam name="TPayload">The payload type.</typeparam>
public sealed class LuiJsonPayloadSerializer<TPayload> : ILuiPayloadSerializer<TPayload>
{
    private readonly JsonTypeInfo<TPayload> _typeInfo;

    /// <summary>Creates the serialiser.</summary>
    /// <param name="typeInfo">The generated type information of the payload.</param>
    public LuiJsonPayloadSerializer(JsonTypeInfo<TPayload> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        _typeInfo = typeInfo;
    }

    /// <inheritdoc />
    public byte[] Serialize(TPayload payload) => JsonSerializer.SerializeToUtf8Bytes(payload, _typeInfo);

    /// <inheritdoc />
    public TPayload Deserialize(ReadOnlySpan<byte> bytes) =>
        JsonSerializer.Deserialize(bytes, _typeInfo)
        ?? throw new InvalidOperationException("The stored payload is the JSON value null.");
}
