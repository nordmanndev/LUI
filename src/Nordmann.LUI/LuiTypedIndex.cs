namespace Nordmann.LUI;

/// <summary>
/// A typed view of an index (issue #59, ADR-0044): the index as the application sees it, with the payload in the type of
/// the application. The view is a view — it opens nothing, writes nothing, and holds no index of its own.
/// </summary>
public sealed class LuiTypedIndex<TPayload>
{
    private readonly ILuiIndex _index;

    internal LuiTypedIndex(ILuiIndex index) => _index = index;

    /// <summary>The name of the index.</summary>
    public string Name => _index.Name;

    /// <summary>How the index is searched.</summary>
    public LuiIndexKind Kind => _index.Kind;

    /// <summary>What the keys of the index are: numbers or texts (ADR-0035).</summary>
    public LuiKeyKind KeyKind => _index.KeyKind;

    /// <summary>
    /// How many errors a query of this instance forgives (§10): <see cref="LuiTypoDistance.None"/> corrects nothing.
    /// The distance belongs to the index — a query may switch the correction on or off, but it cannot widen it.
    /// </summary>
    public LuiTypoDistance TypoDistance => _index.TypoDistance;

    /// <summary>Where the typo dictionary of the index stands (ADR-0025).</summary>
    public LuiTypoDictionaryState TypoDictionaryState => _index.TypoDictionaryState;

    /// <summary>The number of entries.</summary>
    /// <returns>The count.</returns>
    public long Count() => _index.Count();

    /// <summary>
    /// Runs a query and gives the answer in the type of the application: the hits, and with them the payload in
    /// <typeparamref name="TPayload"/>.
    /// </summary>
    /// <param name="text">What the user typed.</param>
    /// <param name="options">The options; <see langword="null"/> takes the defaults.</param>
    /// <param name="cancellationToken">Checked while the candidates are read.</param>
    /// <returns>The answer.</returns>
    public LuiTypedResult<TPayload> Search(string text, LuiQueryOptions? options = null, CancellationToken cancellationToken = default) =>
        _index.Search(text, options, cancellationToken).As<TPayload>();
}
