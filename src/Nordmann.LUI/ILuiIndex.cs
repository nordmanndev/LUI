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
    /// Adds an entry, or replaces the one with the same key. Inside
    /// <paramref name="transaction"/> the entry and the application's own
    /// rows change atomically (§12.4).
    /// </summary>
    /// <param name="key">The key of the application.</param>
    /// <param name="text">What is searched and shown; not empty.</param>
    /// <param name="weight">The popularity, 0 … 536,870,911.</param>
    /// <param name="payload">Sent back with the hit; stored only if the index has a serialiser.</param>
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
