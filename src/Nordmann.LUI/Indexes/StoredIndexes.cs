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
    private readonly Lock _swap = new();
    private Snapshot _current = new(long.MinValue, new Dictionary<string, IndexStoreEntry?>(StringComparer.Ordinal));
    private int _reloads;

    /// <summary>The database the definitions are read from.</summary>
    internal Database Database { get; } = database;

    /// <summary>How many times the definitions were read from <c>lui_meta</c>, the first read at the open included — the seam of the tests.</summary>
    internal int Reloads => Volatile.Read(ref _reloads);

    /// <summary>A seam of the tests (ADR-0075): runs before the counter of the file is compared — where another thread may overtake.</summary>
    internal Action? BeforeCounterCheck { get; set; }

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
        BeforeCounterCheck?.Invoke();
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
    /// Whether a decision that was made on <paramref name="judged"/> has to be made again: the snapshot of the catalog is of another
    /// counter now, or the file's counter is not the one of the snapshot and the definitions were read again. <b>False only when
    /// the file has not moved from the counter of the snapshot</b> — only then may the caller throw from it.
    /// </summary>
    /// <param name="judged">The snapshot the caller decided on.</param>
    /// <returns><see langword="true"/> when the caller has to look at <see cref="Current"/> again.</returns>
    internal bool Refresh(Snapshot judged)
    {
        // a snapshot at another counter is a snapshot of another state of the file; one at the same counter holds the same definitions
        // — the file has to be asked whether it moved, and only a move is a reason to decide again: the loops of the callers end
        // when the file stops changing, not when the threads stop reloading
        if (Current.Counter != judged.Counter)
        {
            return true;
        }

        return ReloadIfChanged() || Current.Counter != judged.Counter;
    }

    /// <summary>
    /// The stored index of a name, from memory. A name that is not known reloads the definitions <b>once</b>; when it is
    /// still missing, the name is remembered — it costs one statement, the counter, to ask again — until the schema counter
    /// changes (the next snapshot starts with no missing name).
    /// </summary>
    /// <param name="name">The name of the index.</param>
    /// <param name="snapshot">The snapshot the answer was read from: the one a caller may judge, and ask <see cref="Refresh"/> about.</param>
    /// <returns>What <c>lui_meta</c> holds, or <see langword="null"/> when there is no such index.</returns>
    /// <exception cref="LuiDefinitionMismatchException">The row of the index cannot be read: another kind than this version knows.</exception>
    internal IndexStoreEntry? Find(string name, out Snapshot snapshot)
    {
        snapshot = Current;
        var looked = false;
        while (!snapshot.Entries.ContainsKey(name))
        {
            if (looked || snapshot.Missing.ContainsKey(name))
            {
                // remembered as missing, or a snapshot that was just read again: it says "missing" only when the counter of the file is
                // still its counter and nobody has replaced it — otherwise the decision is made again on the current one
                if (!Refresh(snapshot))
                {
                    _ = snapshot.Missing.TryAdd(name, 0);
                    return null;
                }

                snapshot = Current;
                looked = true;
            }
            else
            {
                // not known: the file is read, and what it says is the answer
                snapshot = Reload();
                looked = true;
            }
        }

        // a row this version cannot read says what is wrong with it when it is read again
        return snapshot.Entries[name] ?? IndexStore.ReadIndex(Database, name);
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

        var read = new Snapshot(counter, entries);
        _ = Interlocked.Increment(ref _reloads);

        // a snapshot that is older than the one that is there does not replace it, and the caller gets the newest — a decision is
        // never made from an older snapshot than the file the caller has seen
        lock (_swap)
        {
            if (read.Counter >= _current.Counter)
            {
                Volatile.Write(ref _current, read);
            }

            return _current;
        }
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
