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
/// <b>The check and the work are one step.</b> The check is made on the connection and inside the transaction that the work
/// uses: a write holds the write lock from the check to its commit, so no other process can drop or create the index in
/// between; a read pins its snapshot at the check. What cannot share one transaction checks inside each of its transactions.
/// </para>
/// <para>
/// An index that was dropped and created again <b>with the same definition</b> is not a stale definition: the handle goes on with
/// the new index (its typo dictionary is that of the process — <c>RebuildTypoDictionary()</c> brings the words of the new
/// content). But a handle that an operation met <i>between</i> the drop and the create has failed for good: the failure is
/// kept — take a handle per request. The counter is the SQLite schema counter, not the LUI format version of
/// <c>lui_meta.schema_version</c>.
/// </para>
/// </summary>
internal sealed class IndexGuard : IConnectionGuard
{
    private readonly StoredIndexes _stored;
    private readonly IndexDefinition _expected;
    private long _validatedAt;
    private InTransaction? _validatedInTransaction;
    private Failure? _failure;

    /// <param name="stored">The definitions of the catalog.</param>
    /// <param name="name">The name of the index.</param>
    /// <param name="expected">The stored definition the runtime was built from.</param>
    /// <param name="counter">The schema counter that definition was read at.</param>
    internal IndexGuard(StoredIndexes stored, string name, IndexDefinition expected, long counter)
    {
        _stored = stored;
        Name = name;
        _expected = expected;
        _validatedAt = counter;
    }

    /// <summary>The name of the index the guard is for.</summary>
    internal string Name { get; }

    /// <summary>Whether the guard has failed: the index is dropped or another one — the runtime is of no use any more.</summary>
    internal bool HasFailed => Volatile.Read(ref _failure) is not null;

    /// <summary>The catalog dropped the index itself: every handle of this runtime is stale from now on.</summary>
    internal void Dropped() => _ = Interlocked.CompareExchange(ref _failure, new Failure(), comparand: null);

    /// <inheritdoc />
    public void Check(SqliteConnection connection, SqliteTransaction? transaction = null) =>
        Check(connection, transaction, application: false);

    /// <summary>
    /// The check on the connection of the operation. A connection of the application is a different matter: its transaction may
    /// have changed the schema itself, and may be rolled back — its counter belongs to that transaction and is never published to
    /// the other handles.
    /// </summary>
    /// <param name="connection">The connection of the operation.</param>
    /// <param name="transaction">Its transaction, when it has one.</param>
    /// <param name="application">Whether the connection and the transaction are the application's.</param>
    internal void Check(SqliteConnection connection, SqliteTransaction? transaction, bool application)
    {
        ThrowIfFailed();

        var counter = Database.ReadSchemaCounter(connection, transaction);
        _stored.Database.AfterSchemaCheck?.Invoke();
        if (counter == Volatile.Read(ref _validatedAt) || (application && IsValidatedIn(transaction, counter)))
        {
            return;
        }

        Revalidate(connection, transaction, counter, application);
    }

    /// <summary>
    /// The same check for an operation that opens no connection of its own at its start — the typo dictionary: it takes a read
    /// connection for the one statement. The operations that write or check inside transactions of their own check inside them.
    /// </summary>
    internal void Check()
    {
        ThrowIfFailed();

        using var scope = _stored.Database.Begin();
        using var connection = _stored.Database.OpenReadConnection();
        Check(connection);
    }

    /// <summary>
    /// An operation failed with "no such table": the index may have been dropped under it. Looks again — the exception it throws
    /// is the proper one; when the index is fine, the caller throws what it caught.
    /// </summary>
    /// <param name="exception">What the operation threw.</param>
    internal void Explain(SqliteException exception)
    {
        if (exception is { SqliteErrorCode: 1 } && exception.Message.Contains("no such table", StringComparison.Ordinal))
        {
            Check();
        }
    }

    /// <summary>Throws the kept failure, when there is one — no I/O.</summary>
    internal void ThrowIfFailed()
    {
        if (Volatile.Read(ref _failure) is { } failure)
        {
            throw failure.ToException(Name, _expected);
        }
    }

    private bool IsValidatedIn(SqliteTransaction? transaction, long counter) =>
        Volatile.Read(ref _validatedInTransaction) is { } validated
        && validated.Counter == counter
        && validated.Transaction.TryGetTarget(out var known)
        && ReferenceEquals(known, transaction);

    private void Revalidate(SqliteConnection connection, SqliteTransaction? transaction, long counter, bool application)
    {
        // the definitions of the catalog are brought up to date for the open calls — but the guard judges the row as the snapshot
        // of the operation sees it: a read that pinned its snapshot between a drop and a create must end in the exception of that
        // snapshot, not in a bare "no such table" after the newest definitions said "fine"
        if (_stored.Current.Counter != counter)
        {
            _ = _stored.Reload();
        }

        // a row this version cannot read says what is wrong with it
        var entry = IndexStore.ReadIndex(connection, transaction, Name);
        if (entry is null)
        {
            throw Fail(new Failure());
        }

        if (entry.Definition != _expected)
        {
            throw Fail(new Failure(Stored: entry.Definition));
        }

        // the same declaration, written by another version: not a failure of the handle's definition, but an index it must not use
        try
        {
            entry.EnsureReadable(Name);
        }
        catch (LuiDefinitionMismatchException exception)
        {
            throw Fail(new Failure(Found: exception.Stored, Wanted: exception.Requested));
        }

        // validated at the counter of the connection of the operation: a counter that was read inside a transaction of the
        // application is that transaction's — it may be rolled back — and is kept apart from the one of the file
        if (application && transaction is not null)
        {
            Volatile.Write(ref _validatedInTransaction, new InTransaction(new WeakReference<SqliteTransaction>(transaction), counter));
        }
        else if (application)
        {
            // a connection of the application without a transaction: nothing to key the counter by, and nothing is kept
        }
        else
        {
            Volatile.Write(ref _validatedAt, counter);
        }
    }

    private Exception Fail(Failure failure)
    {
        _ = Interlocked.CompareExchange(ref _failure, failure, comparand: null);
        return Volatile.Read(ref _failure)!.ToException(Name, _expected);
    }

    /// <summary>A counter that was validated inside one transaction of the application — and in no other.</summary>
    private sealed record InTransaction(WeakReference<SqliteTransaction> Transaction, long Counter);

    /// <summary>
    /// Why the runtime is of no use, as a record: the index is gone (nothing is set), another definition is stored
    /// (<paramref name="Stored"/>), or the index was written in another LUI format, with another key type or folding
    /// (<paramref name="Found"/> and <paramref name="Wanted"/>).
    /// </summary>
    private sealed record Failure(IndexDefinition? Stored = null, string? Found = null, string? Wanted = null)
    {
        /// <summary>A new exception on every use: one object thrown from several threads would have its stack trace overwritten.</summary>
        internal Exception ToException(string name, IndexDefinition expected) =>
            Found is not null
                ? IndexStoreEntry.Mismatch(name, Found, Wanted!)
                : Stored is null
                    ? new LuiIndexNotFoundException(name)
                    : IndexStore.Mismatch(name, Stored, expected);
    }
}
