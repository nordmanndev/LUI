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
/// No other assembly can derive from this class: the two kinds of definition, <see cref="LuiIndexDefinition{TKey}"/> and
/// <see cref="LuiLinkedIndexDefinition{TKey}"/>, are the whole family — the overloads of <c>Create</c> and <c>Index</c>
/// rely on it. A variable typed <see cref="LuiIndexDefinitionBase"/> passed to <c>Create</c> binds to
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
                    return (resolve(), null);
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

    /// <summary>The type of the keys: the first type argument of the definition (<see cref="long"/> in this version).</summary>
    public Type KeyType { get; }

    /// <summary>The type of the payload; <see langword="null"/> for an index without payload — every definition of this version.</summary>
    public Type? PayloadType { get; }

    /// <summary>The definition in one line: <c>tags (embedded, long)</c>.</summary>
    /// <returns>The name, the source, the key type and — if there is one — the payload type.</returns>
    public override string ToString() =>
        PayloadType is null
            ? $"{Name} ({SourceText(Source)}, {KeyTypes.Display(KeyType)})"
            : $"{Name} ({SourceText(Source)}, {KeyTypes.Display(KeyType)}, {PayloadType.Name})";

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
    /// the application chose wrongly comes out as a <see cref="LuiConfigurationException"/> with the refusal as its cause — whatever the builder function threw, or
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
            var storedKey = KeyTypes.Validate(keyType, source, oldPath: false);
            return new ResolvedIndex(validated, describe(new LuiIndexKindBuilder(oldPath: false)), storedKey);
        }
        catch (LuiConfigurationException)
        {
            throw;
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
