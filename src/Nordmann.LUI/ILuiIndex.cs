namespace Nordmann.LUI;

/// <summary>
/// An index, as it is read: a searchable stock of entries with its own name and kind
/// (docs/api.md). It only reads; what keeps an index in order — <c>Check</c>, the typo dictionary's warm-up and
/// rebuild — belongs to the interfaces that write (review of 2026-11-04, B.3). Every index has this interface, whatever its content
/// source; an index whose content the application writes through LUI is also an
/// <see cref="ILuiEmbeddedIndex"/>. An index is a singleton and safe
/// to use from many threads.
/// </summary>
public interface ILuiIndex
{
    /// <summary>The name of the index.</summary>
    string Name { get; }

    /// <summary>How the index is searched.</summary>
    LuiIndexKind Kind { get; }

    /// <summary>
    /// What the keys of the index are: numbers or texts (ADR-0035). A key of the other kind is refused on the write; a
    /// <c>Linked</c> index has numbers — the INTEGER PRIMARY KEY of its table.
    /// </summary>
    LuiKeyKind KeyKind { get; }

    /// <summary>
    /// How many errors a query of this instance forgives (ADR-0012): <see cref="LuiTypoDistance.None"/> corrects nothing.
    /// The distance belongs to the index — it is part of the stored definition, because the dictionary of the word
    /// beginnings is built with it. A query may switch the correction on or off
    /// (<c>Query(text).TypoTolerance(…)</c>), but it cannot change the distance.
    /// </summary>
    LuiTypoDistance TypoDistance { get; }

    /// <summary>
    /// Where the typo dictionary of the index stands (ADR-0025): not built, being built, or built (and then it only grows).
    /// A value for a health check. The dictionary belongs to the index of the catalog, so every instance of the index
    /// reports the same — and another index has a dictionary of its own: tags are never corrected towards names.
    /// </summary>
    LuiTypoDictionaryState TypoDictionaryState { get; }

    /// <summary>The number of entries.</summary>
    /// <returns>The count.</returns>
    long Count();

    /// <summary>Starts a query (fluent); <c>Execute()</c> runs it.</summary>
    /// <param name="text">What the user typed.</param>
    /// <returns>The query.</returns>
    LuiQuery Query(string text);

    /// <summary>Runs a query — with the default options, or with options that come from a file or a form.</summary>
    /// <param name="text">What the user typed.</param>
    /// <param name="options">The options; <see langword="null"/> takes the defaults.</param>
    /// <param name="cancellationToken">Checked while the candidates are read.</param>
    /// <returns>The answer.</returns>
    LuiResult Search(string text, LuiQueryOptions? options = null, CancellationToken cancellationToken = default);
}
