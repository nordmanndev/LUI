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
    /// <para>
    /// It reads <b>strictly</b> (review of PR 60, I1): the file names no type, so the bytes are the only witness of what
    /// was written. A member in the bytes that the type asked for does not have, or a parameter of its constructor that
    /// the bytes do not have, is a <see cref="JsonException"/> — not an object filled with defaults that looks like data.
    /// A type that has every member of the bytes and optional ones besides is read. The settings of the context
    /// otherwise stay as the application chose them.
    /// </para>
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
        // the settings of the context, made strict; the context stays the resolver, so the descriptions remain the
        // generated ones (AOT) — they are generated again for these options, once per type
        private readonly JsonSerializerOptions _options = new(context.Options)
        {
            TypeInfoResolver = context,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectRequiredConstructorParameters = true,
        };

        public byte[] Serialize(object payload)
        {
            ArgumentNullException.ThrowIfNull(payload);
            return JsonSerializer.SerializeToUtf8Bytes(payload, TypeInfo(payload.GetType()));
        }

        public T Deserialize<T>(ReadOnlySpan<byte> bytes) =>
            JsonSerializer.Deserialize(bytes, (JsonTypeInfo<T>)TypeInfo(typeof(T)))
            ?? throw new JsonException("The stored payload is the JSON value null.");

        private JsonTypeInfo TypeInfo(Type type) =>
            (_options.TryGetTypeInfo(type, out var typeInfo) ? typeInfo : null)
            ?? throw new NotSupportedException(
                $"The payload type '{type.FullName}' is not in the JSON context '{context.GetType().FullName}': add "
                + $"[JsonSerializable(typeof({type.Name}))] to it.");
    }
}
