namespace Nordmann.LUI;

/// <summary>
/// The first step of describing an index (docs/api.md): its kind. The kind is fixed when the index is
/// created and decides how it is searched. What follows is an <c>Embedded</c> index with numbers as keys — the
/// default —, or, with <c>LinkTo(…)</c>, an index over a table of the application.
/// <para>
/// The builder is the parameter of an index definition, <c>new LuiIndexDefinition&lt;long&gt;("tags", index =&gt; index.SingleWord())</c>
/// (ADR-0073): it is the description of an index, not the way into it. <b>It holds no catalog and no name</b> — it describes, the catalog creates.
/// </para>
/// </summary>
public sealed class LuiIndexKindBuilder
{
    /// <summary>Starts a description.</summary>
    internal LuiIndexKindBuilder()
    {
    }

    /// <summary>
    /// A single-word index: tags, words, compounds, hashtags. The input is searched as a whole. The default
    /// similarity is Jaro-Winkler.
    /// </summary>
    /// <returns>The builder of the index.</returns>
#pragma warning disable CA1822 // instance members on purpose: the description starts with `index.SingleWord()`, the parameter of a definition
    public LuiEmbeddedIndexBuilder SingleWord() => new(LuiIndexKind.SingleWord);

    /// <summary>
    /// A multi-word index: names and titles. The input is split into words that are ANDed, and it is also
    /// searched compactly, so the order of the words and the spaces do not matter (<c>kuchen apfel</c> and
    /// <c>apfelkuch</c> both find „Apfel Kuchen"). The default similarity is N-gram Dice.
    /// </summary>
    /// <returns>The builder of the index.</returns>
    public LuiEmbeddedIndexBuilder MultiWord() => new(LuiIndexKind.MultiWord);
#pragma warning restore CA1822
}
