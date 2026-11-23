#pragma warning disable MA0048 // the generic form of a name that has a non-generic file: <Name>OfKeyAndPayload.cs (ADR-0073)
using Nordmann.LUI.Schema;

namespace Nordmann.LUI;

/// <summary>
/// The definition of a <c>Linked</c> index <b>with a payload</b> (ADR-0073): the map that builds the payload from the
/// row is part of the definition. Declared once, as a <c>static readonly</c> field:
/// <code>
/// public static readonly LuiLinkedIndexDefinition&lt;long, ChefView&gt; Chefs =
///     new("chefs", index =&gt; index.MultiWord()
///         .LinkTo(table: "chefs", key: "id", text: "name")
///         .WithPayload(["name", "city"], row =&gt; new ChefView(row.GetString("name"), row.GetString("city")))
///         .TypoTolerance());
/// </code>
/// The function has to end in <see cref="LuiLinkedIndexBuilder{TPayload}"/> with the <b>same</b> payload type — a builder
/// without <c>WithPayload</c>, or one that maps to another type, does not compile. The columns of the payload are stored and
/// compared; the map is code, runtime only, and each definition uses its own. <c>catalog.Create(definition)</c> creates the
/// index or opens it and checks the table of the application; it never syncs and never installs triggers.
/// </summary>
/// <typeparam name="TKey">The type of the keys — <see cref="long"/> or <see cref="int"/>: the <c>INTEGER PRIMARY KEY</c> of the table (text keys come with #267).</typeparam>
/// <typeparam name="TPayload">The type the map builds from a row of the table.</typeparam>
public sealed class LuiLinkedIndexDefinition<TKey, TPayload> : LuiIndexDefinitionBase
    where TKey : notnull
    where TPayload : notnull
{
    /// <summary>
    /// Declares the index. Nothing is validated and nothing runs here: the name is checked and the builder function is
    /// run once, on the first use by a catalog (<see cref="LuiConfigurationException"/> if the definition is not valid).
    /// </summary>
    /// <param name="name">The name of the index: <c>^[a-z][a-z0-9_]{0,40}$</c>.</param>
    /// <param name="index">The description: the kind, <c>LinkTo(…)</c> and <c>WithPayload(…)</c>, and optionally the transliteration and the typo tolerance.</param>
    public LuiLinkedIndexDefinition(string name, Func<LuiIndexKindBuilder, LuiLinkedIndexBuilder<TPayload>> index)
        : base(
            name,
            LuiIndexSource.Linked,
            typeof(TKey),
            () => Describe(name, LuiIndexSource.Linked, typeof(TKey), kind => (index ?? throw new LuiConfigurationException("The description of the index is null.")).Invoke(kind).Describe()),
            typeof(TPayload))
    {
    }
}
#pragma warning restore MA0048
