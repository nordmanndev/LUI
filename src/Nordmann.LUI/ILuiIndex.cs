namespace Nordmann.LUI;

/// <summary>
/// An index, as it is read: a searchable stock of entries with its own name and kind
/// (LUI-Spezifikation §2). Every index has this interface, whatever its content
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

    /// <summary>
    /// Checks the index (§12.6, §14): the stored definition is the one of this instance, the search index passes FTS5's
    /// integrity check, and what keeps it in step is in place — the triggers of an <c>Embedded</c> index; for a
    /// <c>Linked</c> one, the application's table still fits and the entries agree with its rows. Nothing is changed; the
    /// integrity check reads the whole search index under SQLite's write lock (about 2 s for a million entries); the
    /// comparison of a linked index with its table runs after it, in a snapshot of its own, without the lock. For a linked
    /// index with triggers, the rows they noted count until <c>ApplyPending()</c> — a health check between two of them
    /// asks <c>Pending()</c>.
    /// </summary>
    /// <returns>What was found; <see cref="LuiIntegrityReport.IsHealthy"/> when nothing.</returns>
    LuiIntegrityReport Check();

    /// <summary>
    /// Builds the typo dictionary now, if it is not built yet — so that the first typo does not pay for it
    /// (about 5 seconds and 140 MB for 685,000 words). Meant for the start of an application; it builds the
    /// dictionary even when this instance has the typo tolerance off. Nothing happens if it exists.
    /// </summary>
    /// <param name="cancellationToken">Checked while the entries are read, and while it waits for a build that runs.</param>
    void WarmUpTypoDictionary(CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds the typo dictionary anew from the entries (§10.1). The dictionary is built on first use and
    /// grows with every upsert through LUI: only new word beginnings, so the frequencies of the old ones stay as
    /// they were; a removal does not shrink it, because SymSpell cannot forget — a correction towards a removed
    /// word finds nothing. A rebuild gives the sums of the weights as they are now and forgets the removed
    /// words; it also takes in what was written past the library. It reads every entry (seconds for a large
    /// index); writes that arrive meanwhile are not lost, and queries use the old dictionary until the new one is
    /// ready.
    /// </summary>
    /// <param name="cancellationToken">Checked while the entries are read, and while it waits for a build that runs.</param>
    void RebuildTypoDictionary(CancellationToken cancellationToken = default);
}
