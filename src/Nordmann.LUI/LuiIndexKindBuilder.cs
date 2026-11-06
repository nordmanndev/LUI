namespace Nordmann.LUI;

/// <summary>
/// The first step of describing an index (LUI-Spezifikation §12.2): its kind. The kind is fixed when the index is
/// created and decides how it is searched. What follows is an <c>Embedded</c> index with numbers as keys — the
/// default —, or, with <c>LinkTo(…)</c>, an index over a table of the application.
/// <para>
/// The builder is reached through <c>LuiCatalog.Create(name, index =&gt; …)</c>: it is the description of an index, not the
/// way into it (ADR-0033).
/// </para>
/// </summary>
public sealed class LuiIndexKindBuilder
{
    private readonly LuiCatalog _catalog;
    private readonly string _name;

    internal LuiIndexKindBuilder(LuiCatalog catalog, string name)
    {
        _catalog = catalog;
        _name = name;
    }

    /// <summary>
    /// A single-word index: tags, words, compounds, hashtags. The input is searched as a whole. The default
    /// similarity is Jaro-Winkler.
    /// </summary>
    /// <returns>The builder of the index.</returns>
    public LuiEmbeddedIndexBuilder SingleWord() => new(_catalog, _name, LuiIndexKind.SingleWord);

    /// <summary>
    /// A multi-word index: names and titles. The input is split into words that are ANDed, and it is also
    /// searched compactly, so the order of the words and the spaces do not matter (<c>kuchen apfel</c> and
    /// <c>apfelkuch</c> both find „Apfel Kuchen"). The default similarity is N-gram Dice.
    /// </summary>
    /// <returns>The builder of the index.</returns>
    public LuiEmbeddedIndexBuilder MultiWord() => new(_catalog, _name, LuiIndexKind.MultiWord);
}
