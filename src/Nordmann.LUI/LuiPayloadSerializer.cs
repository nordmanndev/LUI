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
}
