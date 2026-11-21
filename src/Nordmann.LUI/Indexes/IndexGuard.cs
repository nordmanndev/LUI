using Microsoft.Data.Sqlite;
using Nordmann.LUI.Schema;
using Nordmann.LUI.Storage;

namespace Nordmann.LUI.Indexes;

/// <summary>
/// What every operation of a handle asks before its first statement (ADR-0075): is the definition this runtime was built
/// from still the one in the file? The mechanism is SQLite's schema counter — <c>PRAGMA schema_version</c> — read on the
/// connection of the operation and compared with the value the guard last validated at: one statement, no table read. When
/// it differs, the guard looks at the definitions of the catalog (reloaded once, whoever comes first): a definition that is
/// gone is a <see cref="LuiIndexNotFoundException"/>, one that is another a <see cref="LuiDefinitionMismatchException"/>, and
/// the guard keeps the failure — every later operation of the handle throws it again, and the catalog builds a new runtime for
/// the next open call. A schema change of the application's own tables only causes a harmless reload.
/// <para>
/// An index that was dropped and created again <b>with the same definition</b> is not a stale definition: the handle goes on
/// with the new index (its typo dictionary is that of the process — <c>RebuildTypoDictionary()</c> brings the words of the
/// new content). The counter is the SQLite schema counter, not the LUI format version of <c>lui_meta.schema_version</c>.
/// </para>
/// </summary>
internal sealed class IndexGuard : IConnectionGuard
{
    private readonly StoredIndexes _stored;
    private readonly string _name;
    private readonly IndexDefinition _expected;
    private long _validatedAt;
    private Failure? _failure;

    /// <param name="stored">The definitions of the catalog.</param>
    /// <param name="name">The name of the index.</param>
    /// <param name="expected">The stored definition the runtime was built from.</param>
    /// <param name="counter">The schema counter that definition was read at.</param>
    internal IndexGuard(StoredIndexes stored, string name, IndexDefinition expected, long counter)
    {
        _stored = stored;
        _name = name;
        _expected = expected;
        _validatedAt = counter;
    }

    /// <summary>Whether the guard has failed: the index is dropped or another one — the runtime is of no use any more.</summary>
    internal bool HasFailed => Volatile.Read(ref _failure) is not null;

    /// <summary>The catalog dropped the index itself: every handle of this runtime is stale from now on.</summary>
    internal void Dropped() => _ = Interlocked.CompareExchange(ref _failure, new Failure(Stored: null), comparand: null);

    /// <inheritdoc />
    public void Check(SqliteConnection connection, SqliteTransaction? transaction = null)
    {
        ThrowIfFailed();

        var counter = Database.ReadSchemaCounter(connection, transaction);
        if (counter != Volatile.Read(ref _validatedAt))
        {
            Revalidate(counter);
        }
    }

    /// <summary>
    /// The same check for an operation that opens no connection of its own at its start — a sync, a check, a rebuild, the typo
    /// dictionary: it takes a read connection for the one statement.
    /// </summary>
    internal void Check()
    {
        ThrowIfFailed();

        using var scope = _stored.Database.Begin();
        using var connection = _stored.Database.OpenReadConnection();
        Check(connection);
    }

    private void Revalidate(long counter)
    {
        var snapshot = _stored.Current;
        if (snapshot.Counter != counter)
        {
            snapshot = _stored.Reload();
        }

        if (!snapshot.Entries.TryGetValue(_name, out var entry))
        {
            throw Fail(new Failure(Stored: null));
        }

        // a row this version cannot read says what is wrong with it when it is read again
        entry ??= IndexStore.ReadIndex(_stored.Database, _name);
        if (entry is null)
        {
            throw Fail(new Failure(Stored: null));
        }

        if (entry.Definition != _expected)
        {
            throw Fail(new Failure(entry.Definition));
        }

        // the same declaration, written by another version: not a failure of the handle, but an index it must not use
        entry.EnsureReadable(_name);

        // validated at the counter of the connection of the operation: an application's transaction that changed the schema
        // itself has a counter of its own, and does not reload at every operation
        Volatile.Write(ref _validatedAt, counter);
    }

    private Exception Fail(Failure failure)
    {
        _ = Interlocked.CompareExchange(ref _failure, failure, comparand: null);
        return Volatile.Read(ref _failure)!.ToException(_name, _expected);
    }

    private void ThrowIfFailed()
    {
        if (Volatile.Read(ref _failure) is { } failure)
        {
            throw failure.ToException(_name, _expected);
        }
    }

    /// <summary>Why the runtime is of no use: the index is gone (<paramref name="Stored"/> is <see langword="null"/>) or another one.</summary>
    private sealed record Failure(IndexDefinition? Stored)
    {
        /// <summary>A new exception on every use: one object thrown from several threads would have its stack trace overwritten.</summary>
        internal Exception ToException(string name, IndexDefinition expected) =>
            Stored is null
                ? new LuiIndexNotFoundException(name)
                : IndexStore.Mismatch(name, Stored, expected);
    }
}
