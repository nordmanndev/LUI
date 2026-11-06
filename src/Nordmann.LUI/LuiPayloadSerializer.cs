using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Nordmann.LUI;

/// <summary>The serialisers of the payload that LUI brings (ADR-0035).</summary>
public static class LuiPayloadSerializer
{
    /// <summary>
    /// A payload as JSON, with the source-generated type descriptions of the application — safe for trimming and AOT.
    /// Every payload type is listed in the context (<c>[JsonSerializable(typeof(TagChip))]</c>); a type that is not
    /// there is refused when it is written or read, and the message names it.
    /// </summary>
    /// <param name="context">The generated context of the application.</param>
    /// <returns>The serialiser.</returns>
    public static ILuiPayloadSerializer Json(JsonSerializerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new JsonPayloadSerializer(context);
    }

    private sealed class JsonPayloadSerializer(JsonSerializerContext context) : ILuiPayloadSerializer
    {
        public byte[] Serialize(object payload)
        {
            ArgumentNullException.ThrowIfNull(payload);
            return JsonSerializer.SerializeToUtf8Bytes(payload, TypeInfo(payload.GetType()));
        }

        public T Deserialize<T>(ReadOnlySpan<byte> bytes) =>
            JsonSerializer.Deserialize(bytes, (JsonTypeInfo<T>)TypeInfo(typeof(T)))
            ?? throw new JsonException("The stored payload is the JSON value null.");

        private JsonTypeInfo TypeInfo(Type type) =>
            context.GetTypeInfo(type)
            ?? throw new NotSupportedException(
                $"The payload type '{type.FullName}' is not in the JSON context '{context.GetType().FullName}': add "
                + $"[JsonSerializable(typeof({type.Name}))] to it.");
    }
}
