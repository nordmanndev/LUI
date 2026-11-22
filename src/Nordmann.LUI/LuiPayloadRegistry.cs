using System.Text.Json.Serialization;
using Nordmann.LUI.Querying;

namespace Nordmann.LUI;

/// <summary>
/// Maps payload types to the serializers that write and read them (ADR-0072). The serializer is bound to the <b>payload type</b>,
/// not to the catalog: one catalog can hold an index of JSON payloads next to an index of MessagePack payloads, and each index
/// is read with the serializer of its own type. Set it in <see cref="LuiCatalogOptions.Payloads"/>:
/// <code>
/// new LuiCatalogOptions
/// {
///     Payloads = new LuiPayloadRegistry()
///         .Use(AppJsonContext.Default)                    // every type of the context, as strict JSON
///         .Use&lt;Attachment&gt;(new MessagePackSerializer())   // this type, in another format
/// }
/// </code>
/// <para>
/// <b>There is no silent precedence.</b> A <see cref="Use{TPayload}"/> registration wins over the contexts — it is an explicit
/// choice —, registering the same type twice with it throws a <see cref="LuiConfigurationException"/> at once, and two contexts
/// that both know a type an index needs throw one when the index is created or opened, naming both: the order of the
/// <c>Use</c> calls never decides. A type that nothing knows is refused at the same moment, with a message that names the type and
/// how to register it. There is <b>no catch-all</b> serializer.
/// </para>
/// <para>
/// <b>The catalog works on a copy.</b> The registry is frozen when the catalog is opened; calls on it afterwards change nothing for
/// that catalog (a catalog opened later sees them). Registration is safe from many threads; the lookup of a type happens once
/// per catalog and the answer — also a refusal — is kept.
/// </para>
/// </summary>
public sealed class LuiPayloadRegistry
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Type, PayloadCodec> _serializers = [];
    private readonly List<JsonSerializerContext> _contexts = [];

    /// <summary>
    /// Registers a source-generated JSON context: every payload type it lists is read and written as strict JSON with the format
    /// <c>json-strict-v1</c> (<see cref="LuiPayloadSerializer.Json{TPayload}"/>). A context cannot list its types, so a type is
    /// looked up in the contexts when an index needs it. The same context registered twice is one registration.
    /// </summary>
    /// <param name="context">The generated context of the application.</param>
    /// <returns>This registry, to chain the registrations.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    public LuiPayloadRegistry Use(JsonSerializerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        lock (_gate)
        {
            if (!_contexts.Contains(context))
            {
                _contexts.Add(context);
            }
        }

        return this;
    }

    /// <summary>
    /// Registers a serializer for <b>exactly one</b> payload type — the way to use any other format (MessagePack, protocol buffers,
    /// reflection-based <c>JsonSerializerOptions</c>, a format of the application). It wins over every context that knows the type.
    /// </summary>
    /// <typeparam name="TPayload">The payload type.</typeparam>
    /// <param name="serializer">The serializer; its <see cref="ILuiPayloadSerializer{TPayload}.Format"/> is read now.</param>
    /// <returns>This registry, to chain the registrations.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="serializer"/> is <see langword="null"/>.</exception>
    /// <exception cref="LuiConfigurationException">
    /// The type is registered with <c>Use</c> already, the serializer has no <see cref="ILuiPayloadSerializer{TPayload}.Format"/>, or the
    /// <see cref="LuiPayloadNameAttribute"/> of the type gives no name.
    /// </exception>
    public LuiPayloadRegistry Use<TPayload>(ILuiPayloadSerializer<TPayload> serializer)
        where TPayload : notnull
    {
        ArgumentNullException.ThrowIfNull(serializer);
        var codec = new PayloadCodec<TPayload>(serializer);
        lock (_gate)
        {
            if (!_serializers.TryAdd(typeof(TPayload), codec))
            {
                throw new LuiConfigurationException(
                    $"The payload type '{typeof(TPayload).FullName}' is registered twice with Use<{typeof(TPayload).Name}>: a type has one serializer. "
                    + "Remove one of the registrations.");
            }
        }

        return this;
    }

    /// <summary>The copy the catalog works on: what is registered now, and nothing that is registered later.</summary>
    /// <returns>The frozen registry.</returns>
    internal FrozenPayloadRegistry Freeze()
    {
        lock (_gate)
        {
            return new FrozenPayloadRegistry(new Dictionary<Type, PayloadCodec>(_serializers), _contexts);
        }
    }
}
