using Nordmann.LUI.Querying;
using Nordmann.LUI.Schema;

namespace Nordmann.LUI;

/// <summary>
/// What every index definition says (ADR-0073): the name, where the content lives, and the types of the key and of the
/// payload. The application declares an index <b>once</b>, as a definition — typically a <c>static readonly</c> field —
/// and every call site uses that object: <c>catalog.Create(definition)</c> creates the index or opens it,
/// <c>catalog.Index(definition)</c> opens it and gives the typed handle.
/// <para>
/// A definition is immutable, has reference equality, does no I/O and knows no catalog: any number of catalogs may share
/// it. <see cref="Name"/> is stored at once; its validation and the builder function run <b>once, lazily</b>, on first
/// use by a catalog — also when several threads use the definition for the first time at once — and the result, or its
/// failure (every use throws a <see cref="LuiConfigurationException"/> with the same message and cause), is kept. A definition
/// that threw in a static initializer would surface as a <see cref="TypeInitializationException"/> of the class that holds it.
/// </para>
/// <para>
/// No other assembly can derive from this class: the four kinds of definition — <see cref="LuiIndexDefinition{TKey}"/>,
/// <see cref="LuiLinkedIndexDefinition{TKey}"/> and their payload variants <see cref="LuiIndexDefinition{TKey, TPayload}"/> and
/// <see cref="LuiLinkedIndexDefinition{TKey, TPayload}"/> — are the whole family; they derive from nothing else and from each
/// other not at all, so the overloads of <c>Create</c> and <c>Index</c> rely on it. A variable typed <see cref="LuiIndexDefinitionBase"/> passed to <c>Create</c> binds to
/// <c>Create(params LuiIndexDefinitionBase[])</c>, which returns nothing.
/// </para>
/// </summary>
public abstract class LuiIndexDefinitionBase
{
    private readonly Lazy<(ResolvedIndex? Index, LuiConfigurationException? Failure)> _resolved;

    private protected LuiIndexDefinitionBase(string? name, LuiIndexSource source, Type keyType, Func<ResolvedIndex> resolve, Type? payloadType = null)
    {
        PayloadType = payloadType;
        Name = name ?? string.Empty;
        Source = source;
        KeyType = keyType;
        _resolved = new Lazy<(ResolvedIndex?, LuiConfigurationException?)>(
            () =>
            {
                try
                {
                    var resolved = resolve();
                    return payloadType is not null && PayloadTypeName.NotASingleType(payloadType) is { } reason
                        ? throw new LuiConfigurationException($"The index definition '{name}' is not valid: {reason}")
                        : (resolved, null);
                }
                catch (LuiConfigurationException exception)
                {
                    return (null, exception);
                }
            },
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>The name of the index, as it was given. It is validated on first use: <c>^[a-z][a-z0-9_]{0,40}$</c>.</summary>
    public string Name { get; }

    /// <summary>Where the content of the index lives: <see cref="LuiIndexSource.Embedded"/> or <see cref="LuiIndexSource.Linked"/>.</summary>
    public LuiIndexSource Source { get; }

    /// <summary>The type of the keys: the first type argument of the definition: <see cref="long"/>, <see cref="int"/>, <see cref="string"/> or <see cref="Guid"/> (a linked definition: <see cref="long"/> or <see cref="int"/>).</summary>
    public Type KeyType { get; }

    /// <summary>The type of the payload: the second type argument of a definition with a payload; <see langword="null"/> for an index without payload.</summary>
    public Type? PayloadType { get; }

    /// <summary>The definition in one line: <c>tags (embedded, long)</c>.</summary>
    /// <returns>The name, the source, the key type and — if there is one — the payload type.</returns>
    public override string ToString() =>
        PayloadType is null
            ? $"{Name} ({SourceText(Source)}, {KeyTypes.Display(KeyType)})"
            : $"{Name} ({SourceText(Source)}, {KeyTypes.Display(KeyType)}, {PayloadType.Name})";

    /// <summary>
    /// The serializer that the registry of a catalog holds for the payload type of an <b>embedded</b> definition with a payload
    /// (ADR-0072) — the one place that knows the type argument, so the catalog can ask a definition typed as the base class. Nothing for
    /// every other definition: an index without a payload has no serializer, and a linked one builds its payload by its map.
    /// </summary>
    /// <param name="registry">The registry of the catalog, frozen when it was opened.</param>
    /// <param name="indexName">The validated name of the index, for the message of an error.</param>
    /// <returns>The codec; <see langword="null"/> when the definition has no payload that a registry serializes.</returns>
    /// <exception cref="LuiConfigurationException">The registry does not know the type, or two contexts do.</exception>
    internal virtual PayloadCodec? ResolveCodec(FrozenPayloadRegistry registry, string indexName) => null;

    /// <summary>The definition after its first use: the validated name, the description and the key type. Runs once.</summary>
    /// <returns>What the catalog creates from.</returns>
    /// <exception cref="LuiConfigurationException">The definition is not valid: a new exception on every use, with the same
    /// message and the same cause — the failure is kept, not the exception object (one object thrown from several threads
    /// would have its stack trace overwritten).</exception>
    internal ResolvedIndex Resolve()
    {
        var (index, failure) = _resolved.Value;
        return index ?? throw new LuiConfigurationException(failure!.Message, failure.InnerException ?? failure);
    }

    /// <summary>
    /// Validates the name and the key type and runs the builder function — what a definition does on first use. A step
    /// the application chose wrongly comes out as a <see cref="LuiConfigurationException"/> with the refusal as its cause — also a
    /// <see cref="LuiConfigurationException"/> of the builders or of the builder function: the message always names the
    /// definition, so that <c>Create(a, b, c)</c> says which one failed. Whatever the builder function threw, or
    /// a null it returned.
    /// </summary>
    /// <param name="name">The name as it was given.</param>
    /// <param name="source">The source of the definition.</param>
    /// <param name="keyType">The type argument of the definition.</param>
    /// <param name="describe">Runs the builder function and returns the description.</param>
    /// <returns>The resolved definition.</returns>
    private protected static ResolvedIndex Describe(
        string? name,
        LuiIndexSource source,
        Type keyType,
        Func<LuiIndexKindBuilder, IndexDescription> describe)
    {
        try
        {
            var validated = Identifier.ValidateIndexName(name!, nameof(name));
            var storedKey = KeyTypes.Validate(keyType, source);
            return new ResolvedIndex(validated, describe(new LuiIndexKindBuilder(oldPath: false)), storedKey);
        }
        catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException or ThreadAbortException))
        {
            throw new LuiConfigurationException(
                $"The index definition '{name}' is not valid: {exception.Message}",
                exception);
        }
    }

    private static string SourceText(LuiIndexSource source) =>
        source == LuiIndexSource.Embedded ? "embedded" : "linked";
}
