using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using Nordmann.LUI.Storage;

namespace Nordmann.LUI.Indexes;

/// <summary>
/// The stored definitions of the catalog, in memory (ADR-0075): every row of <c>lui_meta</c>, read once when the catalog is
/// opened and again when the file's schema changed. <b>Definitions only</b> — what changes with the data (the content, the
/// typo dictionary, a sync lease) is read from the file when it is asked for.
/// <para>
/// The whole state is one <see cref="Snapshot"/> that is replaced as a whole, so a reader never sees half of a reload and needs
/// no lock: the definitions and the <b>SQLite schema counter</b> they were read at are one object. (The counter is what
/// <c>PRAGMA schema_version</c> reads; it is not the LUI format version of <c>lui_meta.schema_version</c>.)
/// </para>
/// </summary>
internal sealed class StoredIndexes(Database database)
{
    private Snapshot _current = new(long.MinValue, new Dictionary<string, IndexStoreEntry?>(StringComparer.Ordinal));
    private int _reloads;

    /// <summary>The database the definitions are read from.</summary>
    internal Database Database { get; } = database;

    /// <summary>How many times the definitions were read from <c>lui_meta</c>, the first read at the open included — the seam of the tests.</summary>
    internal int Reloads => Volatile.Read(ref _reloads);

    /// <summary>What the catalog knows now.</summary>
    internal Snapshot Current => Volatile.Read(ref _current);

    /// <summary>Reads the definitions from the file — always — on a connection of its own.</summary>
    /// <returns>What the file holds now.</returns>
    internal Snapshot Reload()
    {
        using var scope = Database.Begin();
        using var connection = Database.OpenReadConnection();
        return Reload(connection);
    }

    /// <summary>
    /// Reads the definitions again when the schema counter of the file is not the one they were read at (a harmless reload
    /// when the change was a table of the application).
    /// </summary>
    /// <returns><see langword="true"/> when the definitions were read again.</returns>
    internal bool ReloadIfChanged()
    {
        using var scope = Database.Begin();
        using var connection = Database.OpenReadConnection();
        if (Database.ReadSchemaCounter(connection) == Current.Counter)
        {
            return false;
        }

        _ = Reload(connection);
        return true;
    }

    /// <summary>
    /// The stored index of a name, from memory. A name that is not known reloads the definitions <b>once</b>; when it is
    /// still missing, the name is remembered — it costs one statement, the counter, to ask again — until the schema counter
    /// changes (the next snapshot starts with no missing name).
    /// </summary>
    /// <param name="name">The name of the index.</param>
    /// <param name="counter">The schema counter the answer is valid for.</param>
    /// <returns>What <c>lui_meta</c> holds, or <see langword="null"/> when there is no such index.</returns>
    /// <exception cref="LuiDefinitionMismatchException">The row of the index cannot be read: another kind than this version knows.</exception>
    internal IndexStoreEntry? Find(string name, out long counter)
    {
        var snapshot = Current;
        if (!snapshot.Entries.ContainsKey(name))
        {
            if (snapshot.Missing.ContainsKey(name))
            {
                if (!ReloadIfChanged())
                {
                    counter = snapshot.Counter;
                    return null;
                }

                snapshot = Current;
            }
            else
            {
                snapshot = Reload();
            }
        }

        counter = snapshot.Counter;
        if (!snapshot.Entries.TryGetValue(name, out var entry))
        {
            _ = snapshot.Missing.TryAdd(name, 0);
            return null;
        }

        // a row this version cannot read says what is wrong with it when it is read again
        return entry ?? IndexStore.ReadIndex(Database, name);
    }

    /// <summary>
    /// Reads the counter and the definitions in one snapshot of the file: a deferred transaction pins it, so the definitions
    /// are those of the counter — and a change that comes in between is another counter at the next operation.
    /// </summary>
    private Snapshot Reload(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction(deferred: true);
        var counter = Database.ReadSchemaCounter(connection, transaction);
        var entries = IndexStore.ReadAll(connection, transaction);

        var snapshot = new Snapshot(counter, entries);
        Volatile.Write(ref _current, snapshot);
        _ = Interlocked.Increment(ref _reloads);
        return snapshot;
    }

    /// <summary>The definitions of the file at one schema counter, and the names that were asked for and are not there.</summary>
    /// <param name="Counter">The SQLite schema counter the definitions were read at.</param>
    /// <param name="Entries">The stored indexes by name; <see langword="null"/> for a row that cannot be read.</param>
    internal sealed record Snapshot(long Counter, IReadOnlyDictionary<string, IndexStoreEntry?> Entries)
    {
        /// <summary>The names that were asked for and are not in the file — until the counter changes.</summary>
        internal ConcurrentDictionary<string, byte> Missing { get; } = new(StringComparer.Ordinal);
    }
}
