using System.Collections.Concurrent;
using Nordmann.LUI.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Nordmann.LUI.Querying;

/// <summary>
/// The registry as the catalog uses it (ADR-0072): a copy that was made when the catalog was opened, so that a later call on the
/// <see cref="LuiPayloadRegistry"/> the application holds changes nothing. A context cannot list its types, so a type is resolved
/// through the contexts when an index needs it — <b>once per type</b>: the answer, or the failure, is kept, and a failure is thrown
/// as a new exception on every use. There is no silent precedence: an explicit registration wins over the contexts, and two contexts
/// that both know a type are an error that names both — the order of the registrations never decides.
/// </summary>
internal sealed class FrozenPayloadRegistry
{
    private readonly Dictionary<Type, PayloadCodec> _serializers;
    private readonly (JsonSerializerContext Context, JsonSerializerOptions Options)[] _contexts;
    private readonly ConcurrentDictionary<Type, Lazy<Resolution>> _resolutions = new();
    private int _lookups;

    /// <summary>Copies what was registered.</summary>
    /// <param name="serializers">The explicit registrations, by payload type.</param>
    /// <param name="contexts">The JSON contexts, in the order of the registrations.</param>
    internal FrozenPayloadRegistry(Dictionary<Type, PayloadCodec> serializers, IEnumerable<JsonSerializerContext> contexts)
    {
        _serializers = serializers;
        _contexts = [.. contexts.Select(context => (context, LuiPayloadSerializer.StrictOptions(context)))];
    }

    /// <summary>How many types were looked up in the registrations — the seam of the tests: a type is looked up once, whatever asks for it.</summary>
    internal int Lookups => Volatile.Read(ref _lookups);

    /// <summary>The codec of a payload type.</summary>
    /// <typeparam name="TPayload">The payload type.</typeparam>
    /// <param name="indexName">The index (definition) that needs it, for the message of an error.</param>
    /// <returns>The codec: the serializer, the stable name of the type and the format.</returns>
    /// <exception cref="LuiConfigurationException">No registration knows the type, or two contexts do.</exception>
    internal PayloadCodec<TPayload> Resolve<TPayload>(string indexName)
    {
        if (!_resolutions.TryGetValue(typeof(TPayload), out var resolution))
        {
            resolution = _resolutions.GetOrAdd(
                typeof(TPayload),
                static (_, registry) => new Lazy<Resolution>(() => registry.Lookup<TPayload>()),
                this);
        }

        var found = resolution.Value;
        return found.Codec is PayloadCodec<TPayload> codec
            ? codec
            : throw new LuiConfigurationException($"The index definition '{indexName}': {found.Failure}");
    }

    private Resolution Lookup<TPayload>()
    {
        _ = Interlocked.Increment(ref _lookups);
        var type = typeof(TPayload);
        if (PayloadTypeName.NotASingleType(type) is { } reason)
        {
            return new Resolution(null, reason);
        }

        if (_serializers.TryGetValue(type, out var registered))
        {
            return new Resolution(registered, null);
        }

        List<(JsonSerializerContext Context, JsonTypeInfo<TPayload> Info)> known = [];
        foreach (var (context, options) in _contexts)
        {
            try
            {
                if (LuiPayloadSerializer.TypeInfoOf<TPayload>(options) is { } info)
                {
                    known.Add((context, info));
                }
            }
            catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException or ThreadAbortException))
            {
                // metadata that cannot be built is a failure of the configuration, kept like every other: a new exception on every use
                return new Resolution(null, $"the JSON context '{context.GetType().FullName}' failed to describe the payload type '{type.FullName}': {exception.Message}");
            }
        }

        switch (known.Count)
        {
            case 0:
                return new Resolution(
                    null,
                    $"the payload type '{type.FullName}' is not registered. List it in a JSON context that you pass to "
                    + $"LuiPayloadRegistry.Use(context) ([JsonSerializable(typeof({type.Name}))]), or register a serializer for it with "
                    + $"Use<{type.Name}>(serializer): both are set in LuiCatalogOptions.Payloads.");
            case 1:
                try
                {
                    return new Resolution(new PayloadCodec<TPayload>(LuiPayloadSerializer.Typed(known[0].Info)), null);
                }
                catch (LuiConfigurationException exception)
                {
                    return new Resolution(null, exception.Message);
                }

            default:
                return new Resolution(
                    null,
                    $"the payload type '{type.FullName}' is known to {known.Count} JSON contexts that are registered in LuiCatalogOptions.Payloads: "
                    + $"{string.Join(", ", known.Select(pair => $"'{pair.Context.GetType().FullName}'"))}. "
                    + $"The order of the Use calls never decides: take the type out of all but one context, or register its serializer with Use<{type.Name}>(serializer), "
                    + "which wins over the contexts.");
        }
    }

    /// <summary>What a lookup found: the codec, or why there is none — kept per type, the exception is made anew at every use.</summary>
    private sealed record Resolution(PayloadCodec? Codec, string? Failure);
}
