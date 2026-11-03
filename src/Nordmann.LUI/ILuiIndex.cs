using Microsoft.Data.Sqlite;

namespace Nordmann.LUI;

/// <summary>
/// An index: a searchable stock of entries with its own name, kind and
/// content source (LUI-Spezifikation §2). An index is a singleton and safe to
/// use from many threads.
/// </summary>
/// <typeparam name="TKey">The key of the application: <see cref="long"/> or <see cref="string"/>.</typeparam>
/// <typeparam name="TPayload">The payload that comes back with a hit.</typeparam>
public interface ILuiIndex<TKey, TPayload>
{
    /// <summary>The name of the index.</summary>
    string Name { get; }

    /// <summary>How the index is searched.</summary>
    IndexKind Kind { get; }

    /// <summary>
    /// Adds an entry, or <b>replaces the whole entry</b> with the same key — text,
    /// weight, priority <i>and payload</i>: an upsert without a payload clears the
    /// payload that was stored. To change only the weight or the priority use
    /// <see cref="UpdateWeight"/> and <see cref="UpdatePriority"/>. Writing an entry
    /// that is identical to the stored one changes nothing in the search index.
    /// Inside <paramref name="transaction"/> the entry and the application's own
    /// rows change atomically (§12.4).
    /// </summary>
    /// <param name="key">The key of the application.</param>
    /// <param name="text">What is searched and shown; not empty.</param>
    /// <param name="weight">The popularity, 0 … 536,870,911.</param>
    /// <param name="payload">
    /// Sent back with the hit; stored only if the index has a serialiser. <see langword="null"/>
    /// — and, for a value type, its default — means "no payload".
    /// </param>
    /// <param name="priority">The class of the entry.</param>
    /// <param name="transaction">The transaction of the application, or <see langword="null"/> for one of its own.</param>
    /// <exception cref="ArgumentOutOfRangeException">The weight or the priority is out of range.</exception>
    void Upsert(
        TKey key,
        string text,
        int weight,
        TPayload? payload = default,
        Priority priority = Priority.Normal,
        SqliteTransaction? transaction = null);

    /// <summary>Changes the weight of an entry.</summary>
    /// <param name="key">The key of the application.</param>
    /// <param name="weight">The popularity, 0 … 536,870,911.</param>
    /// <param name="transaction">The transaction of the application, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the entry exists.</returns>
    bool UpdateWeight(TKey key, int weight, SqliteTransaction? transaction = null);

    /// <summary>Changes the priority of an entry.</summary>
    /// <param name="key">The key of the application.</param>
    /// <param name="priority">The class of the entry.</param>
    /// <param name="transaction">The transaction of the application, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the entry exists.</returns>
    bool UpdatePriority(TKey key, Priority priority, SqliteTransaction? transaction = null);

    /// <summary>Removes an entry.</summary>
    /// <param name="key">The key of the application.</param>
    /// <param name="transaction">The transaction of the application, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the entry existed.</returns>
    bool Remove(TKey key, SqliteTransaction? transaction = null);

    /// <summary>
    /// Builds the typo dictionary now, if it is not built yet — so that the first typo does not pay for it
    /// (about 5 seconds and 140 MB for 685,000 words). Meant for the start of an application; it builds the
    /// dictionary even when this instance has the typo tolerance off. Nothing happens if it exists.
    /// </summary>
    /// <param name="cancellationToken">Checked while the entries are read.</param>
    void WarmUpTypoDictionary(CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds the typo dictionary anew from the entries (§10.1). The dictionary is built on first use and
    /// grows with <see cref="Upsert"/>: only new word beginnings, so the frequencies of the old ones stay as they
    /// were; <see cref="Remove"/> does not shrink it, because SymSpell cannot forget — a correction towards a
    /// removed word finds nothing. A rebuild gives the sums of the weights as they are now and forgets the removed
    /// words; it also takes in what was written past the library. It reads every entry (seconds for a large
    /// index); writes that arrive meanwhile are not lost, and queries use the old dictionary until the new one is
    /// ready.
    /// </summary>
    /// <param name="cancellationToken">Checked while the entries are read.</param>
    void RebuildTypoDictionary(CancellationToken cancellationToken = default);

    /// <summary>The number of entries.</summary>
    /// <returns>The count.</returns>
    long Count();

    /// <summary>Starts a query (fluent).</summary>
    /// <param name="text">What the user typed.</param>
    /// <returns>The query; <c>Execute()</c> runs it.</returns>
    LuiQuery<TKey, TPayload> Query(string text);

    /// <summary>Runs a query with options that come from a file or a form.</summary>
    /// <param name="text">What the user typed.</param>
    /// <param name="options">The options.</param>
    /// <param name="cancellationToken">Checked while the candidates are read.</param>
    /// <returns>The answer.</returns>
    LuiResult<TKey, TPayload> Query(string text, LuiQueryOptions options, CancellationToken cancellationToken = default);
}
