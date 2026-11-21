using Nordmann.LUI.Schema;

namespace Nordmann.LUI;

/// <summary>
/// The definition of a <c>Linked</c> index (ADR-0073): the content lives in a table of the application, the index reads
/// it and keeps only its search form. The description names the table:
/// <code>
/// public static readonly LuiLinkedIndexDefinition&lt;long&gt; Articles =
///     new("articles", index =&gt; index.MultiWord().LinkTo(table: "articles", key: "id", text: "title").TypoTolerance());
/// </code>
/// <c>catalog.Create(definition)</c> creates LUI's tables and checks the table of the application; it never syncs and
/// never installs triggers. <c>catalog.Index(definition)</c> opens an index that is there.
/// </summary>
/// <typeparam name="TKey">The type of the keys — <see cref="long"/> in this version: the <c>INTEGER PRIMARY KEY</c> of the table.</typeparam>
public sealed class LuiLinkedIndexDefinition<TKey> : LuiIndexDefinitionBase
    where TKey : notnull
{
    /// <summary>
    /// Declares the index. Nothing is validated and nothing runs here: the name is checked and the builder function is
    /// run once, on the first use by a catalog (<see cref="LuiConfigurationException"/> if the definition is not valid).
    /// </summary>
    /// <param name="name">The name of the index: <c>^[a-z][a-z0-9_]{0,40}$</c>.</param>
    /// <param name="index">The description: the kind and <c>LinkTo(…)</c>, and optionally the transliteration and the typo tolerance.</param>
    public LuiLinkedIndexDefinition(string name, Func<LuiIndexKindBuilder, LuiLinkedIndexBuilder> index)
        : base(
            name,
            LuiIndexSource.Linked,
            typeof(TKey),
            () => Describe(name, LuiIndexSource.Linked, typeof(TKey), kind => (index ?? throw new LuiConfigurationException("The description of the index is null.")).Invoke(kind).Describe()))
    {
    }

    /// <summary>The definition the old calls of the catalog build: the name is validated, the description is made.</summary>
    /// <param name="name">The validated name.</param>
    /// <param name="description">What the builder chain described.</param>
    internal LuiLinkedIndexDefinition(string name, IndexDescription description)
        : base(
            name,
            LuiIndexSource.Linked,
            typeof(TKey),
            () => new ResolvedIndex(name, description, KeyTypes.Validate(typeof(TKey), LuiIndexSource.Linked, oldPath: true)))
    {
    }
}
