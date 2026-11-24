using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using Nordmann.LUI.Querying;
using Nordmann.LUI.Schema;
using Nordmann.LUI.Storage;
using Nordmann.LUI.Typos;
using Nordmann.LUI.Writing;

namespace Nordmann.LUI.Indexes;

/// <summary>
/// An index whose content lives in a table of the library (
/// ADR-0014, ADR-0015). The writes are plain statements: the generated columns compute the
/// forms, the triggers keep the search index in step (ADR-0015).
/// <para>
/// Whether it may write is decided by the file, not by the index (ADR-0033): an index of a catalog that was
/// opened read-only refuses every write with a <see cref="LuiReadOnlyException"/>.
/// </para>
/// </summary>
#pragma warning disable CA1001 // see TypoDictionary: the lock of the dictionary needs no disposal, and an index lives as long as its catalog
internal sealed class EmbeddedIndex : IMaintainedIndex
#pragma warning restore CA1001
{
    private static readonly LuiQueryOptions DefaultOptions = new();
    private static readonly LuiSyncOptions DefaultSyncOptions = new();

    private readonly Database _database;
    private readonly PayloadSerialization _payload;
    private readonly QueryRunner _runner;
    /// <summary><c>SQLITE_CONSTRAINT_CHECK</c>.</summary>
    private const int SqliteCheckConstraintFailed = 275;

    private readonly EmbeddedSource _source;
    private readonly TypoDictionary _typos;
    private readonly EmbeddedSync _sync;
    private readonly EmbeddedCheck _check;

    // asks, on the connection of every operation, whether the definition of the index is still the one in the file
    // (ADR-0075); null for an index that is built without a definition (the maintenance by name)
    private readonly IndexGuard? _guard;

    // The statements of the application's own connections, one set per connection.
    private readonly ConditionalWeakTable<SqliteConnection, WriteStatements> _applicationStatements = [];

    internal EmbeddedIndex(
        Database database,
        TypoDictionaries typoDictionaries,
        EmbeddedSource source,
        PayloadSerialization payload,
        ILuiSimilarity similarity,
        LuiTypoDistance typoDistance,
        IndexGuard? guard = null,
        PayloadReader? hitPayload = null)
    {
        _database = database;
        _guard = guard;
        _payload = payload;
        _source = source;
        Name = source.IndexName;
        Kind = source.Rules.Kind;
        _typos = typoDictionaries.For(Name, source.Normalization, () => TypoSource.Read(database, source.TextsSql), typoDistance, source.Definition.ToJson());
        _runner = new QueryRunner(
            database, source, hitPayload ?? new SerializedPayload(Name, payload), similarity, _typos, typoDistance, guard);
        _sync = new EmbeddedSync(database, source, PayloadBytes, _typos, guard);
        _check = new EmbeddedCheck(database, source, PayloadBytes, guard);
    }

    public string Name { get; }

    public LuiIndexKind Kind { get; }

    public LuiTypoDistance TypoDistance => _runner.TypoDistance;

    public LuiTypoDictionaryState TypoDictionaryState => _typos.State;

    public void Upsert(RawEntry entry, SqliteTransaction? transaction = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _database.RefuseTheTransactionOfTheApplication("writing an entry", Name, transaction);
        var keyText = entry.Key;
        var bytes = PayloadBytes(entry.Payload);

        using var scope = _database.Begin();
        try
        {
            if (transaction is null)
            {
                using var own = BeginOwnWrite();
                OwnStatements(own.Connection).Upsert(keyText, entry.Text, entry.Weight, (int)entry.Priority, bytes, own.Transaction);
                own.Commit();
            }
            else
            {
                var statements = StatementsFor(transaction);
                CheckIn(transaction);
                statements.Upsert(keyText, entry.Text, entry.Weight, (int)entry.Priority, bytes, transaction);
            }
        }
        catch (SqliteException exception) when (IsDocIdExhausted(exception))
        {
            throw DocIdsExhausted(exception);
        }
        catch (SqliteException exception) when (Database.IsReadOnlyRefusal(exception))
        {
            throw _database.ReadOnlyRefusal("writing an entry", Name, exception);
        }

        // the new word beginnings join the typo dictionary, if it is built (ADR-0012)
        _typos.Add(entry.Text, entry.Weight);
    }

    public int UpsertMany(
        IEnumerable<RawEntry> entries,
        SqliteTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _database.RefuseTheTransactionOfTheApplication("writing entries", Name, transaction);

        using var scope = _database.Begin();
        var written = new List<(string Text, int Weight)>();
        try
        {
            if (transaction is null)
            {
                WriteOwnBatch(entries, written, cancellationToken);
            }
            else
            {
                var statements = StatementsFor(transaction);
                CheckIn(transaction);
                WriteBatch(statements, entries, transaction, written, cancellationToken);
            }
        }
        catch (SqliteException exception) when (IsDocIdExhausted(exception))
        {
            throw DocIdsExhausted(exception);
        }
        catch (SqliteException exception) when (Database.IsReadOnlyRefusal(exception))
        {
            throw _database.ReadOnlyRefusal("writing entries", Name, exception);
        }

        // once, after the writes: the dictionary takes its write lock one time for the whole batch
        _typos.AddRange(written);
        return written.Count;
    }

    /// <summary>
    /// A batch in a transaction of LUI's own, on a connection from the pool: the lock of the writes is held for the whole
    /// batch, so no single write of another index slips into the transaction (there is one writer per file
    /// anyway, ADR-0015, ADR-0046).
    /// </summary>
    private void WriteOwnBatch(
        IEnumerable<RawEntry> entries,
        List<(string Text, int Weight)> written,
        CancellationToken cancellationToken)
    {
        lock (_database.WriterGate)
        {
            using var connection = _database.OpenConnection();
            using var transaction = connection.BeginTransaction(deferred: false);
            _guard?.Check(connection, transaction);
            WriteBatch(OwnStatements(connection), entries, transaction, written, cancellationToken);
            transaction.Commit();
        }
    }

    private void WriteBatch(
        WriteStatements statements,
        IEnumerable<RawEntry> entries,
        SqliteTransaction transaction,
        List<(string Text, int Weight)> written,
        CancellationToken cancellationToken)
    {
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(entry, nameof(entries));

            var keyText = entry.Key;
            statements.Upsert(keyText, entry.Text, entry.Weight, (int)entry.Priority, PayloadBytes(entry.Payload), transaction);
            written.Add((entry.Text, entry.Weight));
        }
    }

    /// <summary>
    /// The stored form of a payload (ADR-0035): <see langword="null"/> is "no payload" and is stored as NULL; any other
    /// value is a payload — a <c>0</c> as well — and is written by the serialiser of the catalog, with the type it has.
    /// </summary>
    private byte[]? PayloadBytes(object? payload)
    {
        if (payload is null)
        {
            return null;
        }

        // the payload is written by the serialisation of the index: the serializer of its declared payload type —
        // the one of the catalog, with the type the payload has (ADR-0035, ADR-0072)
        return _payload.Serialize(payload, Name);
    }

    private InvalidOperationException DocIdsExhausted(SqliteException exception) =>
        new(
            $"The index '{Name}' has used up its document ids (0 … {RowId.MaxDocId:N0}); a new entry gets the "
            + "highest id plus one. Export the entries, drop the index (LuiCatalog.DropIndex), create it again and "
            + "load them: the ids start from 1 again.",
            exception);

    private static bool IsDocIdExhausted(SqliteException exception) =>
        exception.SqliteExtendedErrorCode == SqliteCheckConstraintFailed
        && exception.Message.Contains("doc_id", StringComparison.Ordinal);

    public LuiSyncReport Sync(
        IEnumerable<RawEntry> entries,
        LuiSyncOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        using var scope = _database.Begin();
        try
        {
            return _sync.Run(entries, options ?? DefaultSyncOptions, cancellationToken);
        }
        catch (SqliteException exception) when (IsDocIdExhausted(exception))
        {
            throw DocIdsExhausted(exception);
        }
        catch (SqliteException exception) when (Database.IsReadOnlyRefusal(exception))
        {
            throw _database.ReadOnlyRefusal("syncing an index", Name, exception);
        }
    }

    public LuiIntegrityReport Check(IEnumerable<RawEntry> entries, CancellationToken cancellationToken = default)
    {
        try
        {
            return _check.Against(entries, cancellationToken);
        }
        catch (SqliteException exception) when (Database.IsReadOnlyRefusal(exception))
        {
            throw _database.ReadOnlyRefusal("checking an index", Name, exception);
        }
    }

    public LuiIntegrityReport Check()
    {
        try
        {
            return _check.Itself();
        }
        catch (SqliteException exception) when (Database.IsReadOnlyRefusal(exception))
        {
            throw _database.ReadOnlyRefusal("checking an index", Name, exception);
        }
    }

    /// <summary>What reads the stored payloads of this index: the serializer of its payload type (ADR-0074).</summary>
    internal PayloadSource Payloads => PayloadSource.Serialized(Name, _payload);

    /// <summary>Hands every stored key and payload to <paramref name="visit"/> — the handle reads them in its types (ADR-0074).</summary>
    /// <param name="visit">What the handle does with a key and a stored payload.</param>
    /// <param name="cancellationToken">Checked between the entries.</param>
    internal void ReadPayloads(Action<string, byte[]?> visit, CancellationToken cancellationToken) =>
        _check.ReadPayloads(visit, cancellationToken);

    public void Rebuild()
    {
        try
        {
            _check.Rebuild();
        }
        catch (SqliteException exception) when (Database.IsReadOnlyRefusal(exception))
        {
            throw _database.ReadOnlyRefusal("rebuilding an index", Name, exception);
        }
    }

    public void Optimize()
    {
        try
        {
            _check.Optimize();
        }
        catch (SqliteException exception) when (Database.IsReadOnlyRefusal(exception))
        {
            throw _database.ReadOnlyRefusal("optimizing an index", Name, exception);
        }
    }

    public bool UpdateWeight(string key, int weight, SqliteTransaction? transaction = null)
    {
        _database.RefuseTheTransactionOfTheApplication("changing the weight of an entry", Name, transaction);
        var keyText = key;
        RowId.ValidateWeight(weight);

        using var scope = _database.Begin();
        try
        {
            if (transaction is not null)
            {
                var statements = StatementsFor(transaction);
                CheckIn(transaction);
                return statements.UpdateWeight(keyText, weight, transaction);
            }

            using var own = BeginOwnWrite();
            var updated = OwnStatements(own.Connection).UpdateWeight(keyText, weight, own.Transaction);
            own.Commit();
            return updated;
        }
        catch (SqliteException exception) when (Database.IsReadOnlyRefusal(exception))
        {
            throw _database.ReadOnlyRefusal("changing the weight of an entry", Name, exception);
        }
    }

    public bool UpdatePriority(string key, LuiPriority priority, SqliteTransaction? transaction = null)
    {
        _database.RefuseTheTransactionOfTheApplication("changing the priority of an entry", Name, transaction);
        var keyText = key;
        RowId.ValidatePriority(priority);

        using var scope = _database.Begin();
        try
        {
            if (transaction is not null)
            {
                var statements = StatementsFor(transaction);
                CheckIn(transaction);
                return statements.UpdatePriority(keyText, (int)priority, transaction);
            }

            using var own = BeginOwnWrite();
            var updated = OwnStatements(own.Connection).UpdatePriority(keyText, (int)priority, own.Transaction);
            own.Commit();
            return updated;
        }
        catch (SqliteException exception) when (Database.IsReadOnlyRefusal(exception))
        {
            throw _database.ReadOnlyRefusal("changing the priority of an entry", Name, exception);
        }
    }

    public bool Remove(string key, SqliteTransaction? transaction = null)
    {
        _database.RefuseTheTransactionOfTheApplication("removing an entry", Name, transaction);
        var keyText = key;

        using var scope = _database.Begin();
        try
        {
            if (transaction is not null)
            {
                var statements = StatementsFor(transaction);
                CheckIn(transaction);
                return statements.Remove(keyText, transaction);
            }

            using var own = BeginOwnWrite();
            var removed = OwnStatements(own.Connection).Remove(keyText, own.Transaction);
            own.Commit();
            return removed;
        }
        catch (SqliteException exception) when (Database.IsReadOnlyRefusal(exception))
        {
            throw _database.ReadOnlyRefusal("removing an entry", Name, exception);
        }
    }

    public void RebuildTypoDictionary(CancellationToken cancellationToken = default)
    {
        using var scope = _database.Begin();
        Cold(() => _typos.Rebuild(cancellationToken));
    }

    public void WarmUpTypoDictionary(CancellationToken cancellationToken = default)
    {
        using var scope = _database.Begin();
        Cold(() => _typos.EnsureBuilt(cancellationToken));
    }

    public long Count()
    {
        using var scope = _database.Begin();
        using var connection = _database.OpenReadConnection();

        // the check and the count in one snapshot: the index that was created again in between is not the one that is counted
        using var snapshot = _guard is null ? null : connection.BeginTransaction(deferred: true);
        _guard?.Check(connection, snapshot);
        using var command = connection.CreateCommand();
        command.Transaction = snapshot;
        command.CommandText = _source.CountSql;
        return (long)command.ExecuteScalar()!;
    }

    public RawResult Search(
        string text,
        LuiQueryOptions? options = null,
        CancellationToken cancellationToken = default) =>
        _runner.Execute(text, options ?? DefaultOptions, cancellationToken);

    /// <summary>
    /// The schema counter on the connection of the application's transaction, before the first statement of the write
    /// (ADR-0075): a write on a dropped index says so, instead of the missing table.
    /// </summary>
    private void CheckIn(SqliteTransaction transaction)
    {
        if (_guard is not null && transaction.Connection is { } connection)
        {
            _guard.Check(connection, transaction, application: true);
        }
    }

    /// <summary>
    /// A write of LUI's own, in one transaction that holds the write lock from the check to the commit (ADR-0075): nobody can drop the
    /// index, or create it again, between the schema counter and the statement. An index that is built without a definition (the maintenance by name) has no guard to ask.
    /// </summary>
    private OwnWrite BeginOwnWrite()
    {
        if (_guard is null)
        {
            return new OwnWrite(_database.OpenConnection(), null, null);
        }

        // a handle that failed says so without taking the write lock
        _guard.ThrowIfFailed();

        // the lock of the writes first, as a batch has it: two writers of one process do not wait for each other in SQLite
        var gate = _database.WriterGate;
        gate.Enter();
        SqliteConnection? connection = null;
        try
        {
            connection = _database.OpenConnection();
            var transaction = connection.BeginTransaction(deferred: false);
            try
            {
                _guard.Check(connection, transaction);
            }
            catch
            {
                transaction.Dispose();
                throw;
            }

            return new OwnWrite(connection, transaction, gate);
        }
        catch
        {
            connection?.Dispose();
            gate.Exit();
            throw;
        }
    }

    /// <summary>
    /// Work that cannot share a transaction with its check — the typo dictionary reads the texts on a connection of its own: the
    /// check comes first, and a table that is missing under it is the answer of the check, not a bare SQLite error.
    /// </summary>
    private void Cold(Action work)
    {
        _guard?.Check();
        try
        {
            work();
            CheckAfterTheWork();
        }
        catch (SqliteException exception) when (_guard is not null)
        {
            _guard.Explain(exception);
            throw;
        }
    }

    /// <summary>
    /// The check after work that read on a connection of its own: the index may have been created again while it read, and the
    /// dictionary is then built from the texts of another definition — it is let go, and the handle says so.
    /// </summary>
    private void CheckAfterTheWork()
    {
        try
        {
            _guard?.Check();
        }
        catch (Exception exception) when (exception is LuiIndexNotFoundException or LuiDefinitionMismatchException)
        {
            _typos.Discard();
            throw;
        }
    }

    /// <summary>A connection and, with a guard, the transaction of a write that was checked.</summary>
    private readonly struct OwnWrite(SqliteConnection connection, SqliteTransaction? transaction, Lock? gate) : IDisposable
    {
        internal SqliteConnection Connection => connection;

        internal SqliteTransaction? Transaction => transaction;

        internal void Commit() => transaction?.Commit();

        public void Dispose()
        {
            transaction?.Dispose();
            connection.Dispose();
            gate?.Exit();
        }
    }

    /// <summary>
    /// The statements for a write in the transaction of the application: one set per connection of the application,
    /// kept as long as the connection lives (ADR-0009).
    /// </summary>
    private WriteStatements StatementsFor(SqliteTransaction transaction)
    {
        // a handle that failed says so before anything else is asked of a connection of the application
        _guard?.ThrowIfFailed();

        var connection = transaction.Connection
            ?? throw new ArgumentException("The transaction is complete.", nameof(transaction));

        return _applicationStatements.TryGetValue(connection, out var statements)
            ? statements
            : _applicationStatements.GetValue(connection, c => ForApplication(c, transaction));
    }

    /// <summary>
    /// The statements for a write of LUI's own, on a connection that the caller took from the pool (ADR-0070): they live
    /// as long as that connection is in the caller's hands. The statement objects are made again for every write
    /// (issue #237 measured about a kilobyte for the command, the parameters and the checkout; issue #248); whether the provider keeps the
    /// compiled plan per physical connection is not measured. The lock of the writes is
    /// the lock of the catalogue: two indexes of one catalogue write one after the other.
    /// </summary>
    private WriteStatements OwnStatements(SqliteConnection connection) => new(connection, _source, _database.WriterGate);

    /// <summary>
    /// The statements for a connection of the application. The first use on a connection checks that
    /// the connection reaches this index at all — a transaction of another database would otherwise
    /// fail with a bare "no such table" (review B8).
    /// </summary>
    private WriteStatements ForApplication(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var check = connection.CreateCommand();
        check.Transaction = transaction;
        check.CommandText = "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        check.Parameters.AddWithValue("$name", _source.ContentTable);

        if ((long)check.ExecuteScalar()! == 0)
        {
            throw new ArgumentException(
                $"The transaction belongs to another database: the table '{_source.ContentTable}' of the "
                + $"index '{Name}' is not there.",
                nameof(transaction));
        }

        return new WriteStatements(connection, _source, new Lock());
    }
}
