namespace Nordmann.LUI;

/// <summary>
/// The first step of describing an index (LUI-Spezifikation §12.2): its kind. The kind is fixed when the index is
/// created and decides how it is searched. What follows is an <c>Owned</c> index with a <see cref="long"/> key — the
/// default —, or, with <c>LinkTo(…)</c>, an index over a table of the application.
/// </summary>
public sealed class LuiIndexBuilder
{
    private readonly LuiCatalog _catalog;
    private readonly string _name;

    internal LuiIndexBuilder(LuiCatalog catalog, string name)
    {
        _catalog = catalog;
        _name = name;
    }

    /// <summary>
    /// A single-word index: tags, words, compounds, hashtags. The input is searched as a whole. The default
    /// similarity is Jaro-Winkler.
    /// </summary>
    /// <returns>The builder of the index.</returns>
    public LuiOwnedIndexBuilder<long, LuiNoPayload> SingleWord() => new(_catalog, _name, LuiIndexKind.SingleWord);

    /// <summary>
    /// A multi-word index: names and titles. The input is split into words that are ANDed, and it is also
    /// searched compactly, so the order of the words and the spaces do not matter (<c>kuchen apfel</c> and
    /// <c>apfelkuch</c> both find „Apfel Kuchen"). The default similarity is N-gram Dice.
    /// </summary>
    /// <returns>The builder of the index.</returns>
    public LuiOwnedIndexBuilder<long, LuiNoPayload> MultiWord() => new(_catalog, _name, LuiIndexKind.MultiWord);
}
