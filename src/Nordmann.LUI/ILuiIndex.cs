namespace Nordmann.LUI;

/// <summary>
/// An index, as it is read: a searchable stock of entries with its own name and kind
/// (LUI-Spezifikation §2). It only reads; what keeps an index in order — <c>Check</c>, the typo dictionary's warm-up and
/// rebuild — belongs to the interfaces that write (review of 2026-11-04, B.3). Every index has this interface, whatever its content
/// source; an index whose content the application writes through LUI is also an
/// <see cref="ILuiEmbeddedIndex{TKey, TPayload}"/>. An index is a singleton and safe
/// to use from many threads.
/// </summary>
/// <typeparam name="TKey">The key of the application: <see cref="long"/> or <see cref="string"/>.</typeparam>
/// <typeparam name="TPayload">The payload that comes back with a hit; <see cref="LuiNoPayload"/> when there is none.</typeparam>
public interface ILuiIndex<TKey, TPayload>
{
    /// <summary>The name of the index.</summary>
    string Name { get; }

    /// <summary>How the index is searched.</summary>
    LuiIndexKind Kind { get; }

    /// <summary>
    /// Whether a query of this instance corrects a typo unless it says otherwise (<c>TypoTolerance()</c> of the builder,
    /// overridden by <c>Query(text).TypoTolerance(…)</c>). A property of the instance, not of the stored index.
    /// </summary>
    bool TypoTolerance { get; }

    /// <summary>
    /// Where the typo dictionary of the index stands (ADR-0021): not built, being built, current, or stale after a sync
    /// that changed or removed entries — a value for a health check, and the question before a rebuild. The dictionary
    /// belongs to the index of the catalog, so every instance of the index reports the same.
    /// </summary>
    LuiTypoDictionaryState TypoDictionaryState { get; }

    /// <summary>The number of entries.</summary>
    /// <returns>The count.</returns>
    long Count();

    /// <summary>Starts a query (fluent); <c>Execute()</c> runs it.</summary>
    /// <param name="text">What the user typed.</param>
    /// <returns>The query.</returns>
    LuiQuery<TKey, TPayload> Query(string text);

    /// <summary>Runs a query — with the default options, or with options that come from a file or a form.</summary>
    /// <param name="text">What the user typed.</param>
    /// <param name="options">The options; <see langword="null"/> takes the defaults.</param>
    /// <param name="cancellationToken">Checked while the candidates are read.</param>
    /// <returns>The answer.</returns>
    LuiResult<TKey, TPayload> Search(string text, LuiQueryOptions? options = null, CancellationToken cancellationToken = default);
}
