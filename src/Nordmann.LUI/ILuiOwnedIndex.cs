using Microsoft.Data.Sqlite;

namespace Nordmann.LUI;

/// <summary>
/// An index whose content lives in a table of the library and is written through
/// it (<c>Owned</c>, LUI-Spezifikation §5, §12.4). Inside a transaction of the
/// application, its rows and the entries change atomically.
/// </summary>
/// <typeparam name="TKey">The key of the application: <see cref="long"/> or <see cref="string"/>.</typeparam>
/// <typeparam name="TPayload">The payload that comes back with a hit; <see cref="LuiNoPayload"/> when there is none.</typeparam>
public interface ILuiOwnedIndex<TKey, TPayload> : ILuiIndex<TKey, TPayload>
{
    /// <summary>
    /// Adds an entry, or <b>replaces the whole entry</b> with the same key — text, weight, priority
    /// <i>and payload</i>: an entry without a payload clears the payload that was stored. To change only the
    /// weight or the priority use <see cref="UpdateWeight"/> and <see cref="UpdatePriority"/>. Writing an entry
    /// that is identical to the stored one changes nothing in the search index.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <param name="transaction">The transaction of the application, or <see langword="null"/> for one of its own.</param>
    /// <exception cref="InvalidOperationException">The entry has a payload, and the index stores none.</exception>
    void Upsert(LuiEntry<TKey, TPayload> entry, SqliteTransaction? transaction = null);

    /// <summary>
    /// Adds an entry without a payload, or replaces the whole entry with the same key — the short form of
    /// <see cref="Upsert(LuiEntry{TKey, TPayload}, SqliteTransaction?)"/>; a payload that was stored is cleared.
    /// </summary>
    /// <param name="key">The key of the application.</param>
    /// <param name="text">What is searched and shown; not empty.</param>
    /// <param name="weight">The popularity, 0 … 536,870,911.</param>
    /// <param name="priority">The class of the entry.</param>
    /// <param name="transaction">The transaction of the application, or <see langword="null"/> for one of its own.</param>
    /// <exception cref="ArgumentOutOfRangeException">The weight or the priority is out of range.</exception>
    void Upsert(
        TKey key,
        string text,
        int weight = 0,
        LuiPriority priority = LuiPriority.Normal,
        SqliteTransaction? transaction = null);

    /// <summary>
    /// Upserts many entries in one transaction — the way to load an index: a commit per entry costs far more
    /// than the entry. Without <paramref name="transaction"/> LUI opens one of its own and commits it at the
    /// end: all entries are written, or — on an error or a cancellation — none. With the transaction of the
    /// application the entries are part of it, and the application commits or rolls back.
    /// </summary>
    /// <param name="entries">The entries; a key that comes twice is written twice, and the last one stays.</param>
    /// <param name="transaction">The transaction of the application, or <see langword="null"/> for one of its own.</param>
    /// <param name="cancellationToken">Checked between the entries.</param>
    /// <returns>The number of entries written.</returns>
    /// <exception cref="OperationCanceledException">The token was cancelled; LUI's own transaction is rolled back.</exception>
    int UpsertMany(
        IEnumerable<LuiEntry<TKey, TPayload>> entries,
        SqliteTransaction? transaction = null,
        CancellationToken cancellationToken = default);

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
    bool UpdatePriority(TKey key, LuiPriority priority, SqliteTransaction? transaction = null);

    /// <summary>Removes an entry.</summary>
    /// <param name="key">The key of the application.</param>
    /// <param name="transaction">The transaction of the application, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the entry existed.</returns>
    bool Remove(TKey key, SqliteTransaction? transaction = null);
}
