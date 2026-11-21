namespace Nordmann.LUI;

/// <summary>
/// The first step of describing an index (docs/api.md): its kind. The kind is fixed when the index is
/// created and decides how it is searched. What follows is an <c>Embedded</c> index with numbers as keys — the
/// default —, or, with <c>LinkTo(…)</c>, an index over a table of the application.
/// <para>
/// The builder is the parameter of an index definition, <c>new LuiIndexDefinition&lt;long&gt;("tags", index =&gt; index.SingleWord())</c>
/// (ADR-0073), and of the old <c>LuiCatalog.Create(name, index =&gt; …)</c>: it is the description of an index, not the way into
/// it. <b>It holds no catalog and no name</b> — it describes, the catalog creates.
/// </para>
/// </summary>
public sealed class LuiIndexKindBuilder
{
    private readonly bool _oldPath;

    /// <summary>Starts a description.</summary>
    /// <param name="oldPath"><see langword="true"/> for the old calls of the catalog (<c>WithKey</c> and <c>WithPayload</c> are theirs); a definition says no.</param>
    internal LuiIndexKindBuilder(bool oldPath) => _oldPath = oldPath;

    /// <summary>
    /// A single-word index: tags, words, compounds, hashtags. The input is searched as a whole. The default
    /// similarity is Jaro-Winkler.
    /// </summary>
    /// <returns>The builder of the index.</returns>
    public LuiEmbeddedIndexBuilder SingleWord() => new(LuiIndexKind.SingleWord, _oldPath);

    /// <summary>
    /// A multi-word index: names and titles. The input is split into words that are ANDed, and it is also
    /// searched compactly, so the order of the words and the spaces do not matter (<c>kuchen apfel</c> and
    /// <c>apfelkuch</c> both find „Apfel Kuchen"). The default similarity is N-gram Dice.
    /// </summary>
    /// <returns>The builder of the index.</returns>
    public LuiEmbeddedIndexBuilder MultiWord() => new(LuiIndexKind.MultiWord, _oldPath);
}
