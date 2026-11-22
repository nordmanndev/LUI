using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Nordmann.LUI;

/// <summary>The serialisers of the payload that LUI brings (ADR-0035, ADR-0072).</summary>
public static class LuiPayloadSerializer
{
    /// <summary>The format of the strict JSON serializer: stored with an embedded index and compared when it is opened (ADR-0072).</summary>
    internal const string JsonFormat = "json-strict-v1";

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

    /// <summary>
    /// The strict JSON serializer of <b>one payload type</b> — what <see cref="LuiPayloadRegistry.Use(JsonSerializerContext)"/> gives
    /// the types of a context — with the format <c>json-strict-v1</c> (ADR-0072). It reads exactly as <see cref="Json(JsonSerializerContext)"/>
    /// does: strictly, with the type descriptions generated for the context (safe for trimming and AOT).
    /// </summary>
    /// <typeparam name="TPayload">The payload type; it is listed in the context (<c>[JsonSerializable(typeof(TagChip))]</c>).</typeparam>
    /// <param name="context">The generated context of the application.</param>
    /// <returns>The serializer.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    /// <exception cref="LuiConfigurationException">The context does not know <typeparamref name="TPayload"/>.</exception>
    public static ILuiPayloadSerializer<TPayload> Json<TPayload>(JsonSerializerContext context)
        where TPayload : notnull
    {
        ArgumentNullException.ThrowIfNull(context);
        return Typed(TypeInfoOf<TPayload>(StrictOptions(context)) ?? throw new LuiConfigurationException(
            $"The payload type '{typeof(TPayload).FullName}' is not in the JSON context '{context.GetType().FullName}': add "
            + $"[JsonSerializable(typeof({typeof(TPayload).Name}))] to it."));
    }

    /// <summary>The settings of the context, made strict; the context stays the resolver, so the descriptions remain the generated ones.</summary>
    /// <param name="context">The generated context of the application.</param>
    /// <returns>New options; their type descriptions are generated again for them, once per type.</returns>
    internal static JsonSerializerOptions StrictOptions(JsonSerializerContext context) =>
        new(context.Options)
        {
            TypeInfoResolver = context,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectRequiredConstructorParameters = true,
        };

    /// <summary>The generated description of a type in strict options, or <see langword="null"/> when the context does not know it.</summary>
    internal static JsonTypeInfo<TPayload>? TypeInfoOf<TPayload>(JsonSerializerOptions options) =>
        options.TryGetTypeInfo(typeof(TPayload), out var typeInfo) ? typeInfo as JsonTypeInfo<TPayload> : null;

    /// <summary>The strict JSON serializer over a description that the strict options of a context made.</summary>
    internal static ILuiPayloadSerializer<TPayload> Typed<TPayload>(JsonTypeInfo<TPayload> typeInfo) => new JsonPayloadSerializer<TPayload>(typeInfo);

    private sealed class JsonPayloadSerializer<TPayload>(JsonTypeInfo<TPayload> typeInfo) : ILuiPayloadSerializer<TPayload>
    {
        public string Format => JsonFormat;

        public byte[] Serialize(TPayload payload) => JsonSerializer.SerializeToUtf8Bytes(payload, typeInfo);

        public TPayload Deserialize(ReadOnlySpan<byte> bytes) =>
            JsonSerializer.Deserialize(bytes, typeInfo) ?? throw new JsonException("The stored payload is the JSON value null.");
    }

    private sealed class JsonPayloadSerializer(JsonSerializerContext context) : ILuiPayloadSerializer
    {
        // the settings of the context, made strict; the context stays the resolver, so the descriptions remain the
        // generated ones (AOT) — they are generated again for these options, once per type
        private readonly JsonSerializerOptions _options = StrictOptions(context);

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
