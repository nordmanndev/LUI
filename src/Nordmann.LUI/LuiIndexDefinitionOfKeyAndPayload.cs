#pragma warning disable MA0048 // the generic form of a name that has a non-generic file: <Name>OfKeyAndPayload.cs (ADR-0073)
namespace Nordmann.LUI;

/// <summary>
/// The definition of an <c>Embedded</c> index <b>with a payload</b> (ADR-0073): the application declares the type of the
/// payload once, writes typed payloads and gets typed handles. Declared once, as a <c>static readonly</c> field:
/// <code>
/// public static readonly LuiIndexDefinition&lt;long, RecipePayload&gt; Recipes =
///     new("recipes", index =&gt; index.MultiWord().TypoTolerance());
/// </code>
/// <c>catalog.Create(SearchIndexes.Recipes)</c> and <c>catalog.Index(SearchIndexes.Recipes)</c> give a
/// <see cref="LuiEmbeddedIndex{TKey, TPayload}"/>, both types inferred. The payload is written with the
/// <see cref="LuiCatalogOptions.PayloadSerializer"/> of the catalog; a write on a catalog without one throws a
/// <see cref="LuiConfigurationException"/>. The type of the payload is not part of the stored definition yet.
/// </summary>
/// <typeparam name="TKey">The type of the keys — <see cref="long"/>, <see cref="int"/>, <see cref="string"/> or <see cref="Guid"/> (ADR-0073).</typeparam>
/// <typeparam name="TPayload">The type of the payload: what every entry of the index carries.</typeparam>
public sealed class LuiIndexDefinition<TKey, TPayload> : LuiIndexDefinitionBase
    where TKey : notnull
    where TPayload : notnull
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
            () => Describe(name, LuiIndexSource.Embedded, typeof(TKey), kind => (index ?? throw new LuiConfigurationException("The description of the index is null.")).Invoke(kind).Describe()),
            typeof(TPayload))
    {
    }
}
#pragma warning restore MA0048
