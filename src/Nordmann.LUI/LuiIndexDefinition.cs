using Nordmann.LUI.Schema;

namespace Nordmann.LUI;

/// <summary>
/// The definition of an <c>Embedded</c> index (ADR-0073): the content lives in a table of the library, and the
/// application writes through the handle. Declared once, as a <c>static readonly</c> field:
/// <code>
/// public static readonly LuiIndexDefinition&lt;long&gt; Tags = new("tags", index =&gt; index.SingleWord());
/// </code>
/// The handle comes from the catalog: <c>catalog.Create(SearchIndexes.Tags)</c> creates the index or opens it (and
/// compares the stored definition), <c>catalog.Index(SearchIndexes.Tags)</c> opens one that is there.
/// </summary>
/// <typeparam name="TKey">The type of the keys — <see cref="long"/>, <see cref="int"/>, <see cref="string"/> or <see cref="Guid"/> (ADR-0073).</typeparam>
public sealed class LuiIndexDefinition<TKey> : LuiIndexDefinitionBase
    where TKey : notnull
{
    /// <summary>
    /// Declares the index. Nothing is validated and nothing runs here: the name is checked and the builder function is
    /// run once, on the first use by a catalog (<see cref="LuiConfigurationException"/> if the definition is not valid).
    /// </summary>
    /// <param name="name">The name of the index: <c>^[a-z][a-z0-9_]{0,40}$</c>.</param>
    /// <param name="index">The description: the kind, and optionally the transliteration and the typo tolerance.</param>
    public LuiIndexDefinition(string name, Func<LuiIndexKindBuilder, LuiEmbeddedIndexBuilder> index)
        : base(
            name,
            LuiIndexSource.Embedded,
            typeof(TKey),
            () => Describe(name, LuiIndexSource.Embedded, typeof(TKey), kind => (index ?? throw new LuiConfigurationException("The description of the index is null.")).Invoke(kind).Describe()))
    {
    }

    /// <summary>The definition the old calls of the catalog build: the name is validated, the description is made.</summary>
    /// <param name="name">The validated name.</param>
    /// <param name="description">What the builder chain described.</param>
    internal LuiIndexDefinition(string name, IndexDescription description)
        : base(
            name,
            LuiIndexSource.Embedded,
            typeof(TKey),
            () => new ResolvedIndex(name, description, KeyTypes.Validate(typeof(TKey), LuiIndexSource.Embedded)))
    {
    }
}
