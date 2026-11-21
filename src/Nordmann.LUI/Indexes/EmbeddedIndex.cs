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
internal sealed class EmbeddedIndex : ILuiEmbeddedIndex, IMaintainedIndex
#pragma warning restore CA1001
{
    private static readonly LuiQueryOptions DefaultOptions = new();
    private static readonly LuiSyncOptions DefaultSyncOptions = new();

    private readonly Database _database;
    private readonly ILuiPayloadSerializer? _payload;
    private readonly QueryRunner _runner;
    /// <summary><c>SQLITE_CONSTRAINT_CHECK</c>.</summary>
    private const int SqliteCheckConstraintFailed = 275;

    private readonly EmbeddedSource _source;
    private readonly LuiKeyType _keyType;
    private readonly TypoDictionary _typos;
    private readonly EmbeddedSync _sync;
    private readonly EmbeddedCheck _check;

    // asks, on the connection of every operation, whether the definition of the index is still the one in the file
    // (ADR-0075); null for an index of the old path, which holds no definition
    private readonly IndexGuard? _guard;

    // The statements of the application's own connections, one set per connection.
    private readonly ConditionalWeakTable<SqliteConnection, WriteStatements> _applicationStatements = [];

    internal EmbeddedIndex(
        Database database,
        TypoDictionaries typoDictionaries,
        EmbeddedSource source,
        ILuiPayloadSerializer? payload,
        ILuiSimilarity similarity,
        LuiTypoDistance typoDistance,
        IndexGuard? guard = null)
    {
        _database = database;
        _guard = guard;
        _payload = payload;
        _source = source;
        Name = source.IndexName;
        Kind = source.Rules.Kind;
        _keyType = KeyCodec.TypeOf(source.Definition.KeyType);
        KeyKind = KeyCodec.KindOf(_keyType);
        _typos = typoDictionaries.For(Name, source.Normalization, () => TypoSource.Read(database, source.TextsSql), typoDistance);
        _runner = new QueryRunner(
            database, source, new SerializedPayload(Name, payload), similarity, _typos, typoDistance, guard);
        _sync = new EmbeddedSync(database, source, KeyText, PayloadBytes, _typos);
        _check = new EmbeddedCheck(database, source, KeyText, PayloadBytes);
    }

    public string Name { get; }

    public LuiIndexKind Kind { get; }

    public LuiKeyKind KeyKind { get; }

    public LuiTypoDistance TypoDistance => _runner.TypoDistance;

    public LuiTypoDictionaryState TypoDictionaryState => _typos.State;

    public void Upsert(LuiEntry entry, SqliteTransaction? transaction = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _database.RefuseTheTransactionOfTheApplication("writing an entry", Name, transaction);
        var keyText = KeyText(entry.Key);
        var bytes = PayloadBytes(entry.Payload);

        using var scope = _database.Begin();
        try
        {
            if (transaction is null)
            {
                using var connection = _database.OpenConnection();
                _guard?.Check(connection);
                OwnStatements(connection).Upsert(keyText, entry.Text, entry.Weight, (int)entry.Priority, bytes, null);
            }
            else
            {
                CheckIn(transaction);
                StatementsFor(transaction).Upsert(keyText, entry.Text, entry.Weight, (int)entry.Priority, bytes, transaction);
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

    public void Upsert(
        LuiKey key,
        string text,
        int weight = 0,
        LuiPriority priority = LuiPriority.Normal,
        SqliteTransaction? transaction = null) =>
        Upsert(new LuiEntry(key, text) { Weight = weight, Priority = priority }, transaction);

    public int UpsertMany(
        IEnumerable<LuiEntry> entries,
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
                CheckIn(transaction);
                WriteBatch(StatementsFor(transaction), entries, transaction, written, cancellationToken);
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
        IEnumerable<LuiEntry> entries,
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
        IEnumerable<LuiEntry> entries,
        SqliteTransaction transaction,
        List<(string Text, int Weight)> written,
        CancellationToken cancellationToken)
    {
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(entry, nameof(entries));

            var keyText = KeyText(entry.Key);
            statements.Upsert(keyText, entry.Text, entry.Weight, (int)entry.Priority, PayloadBytes(entry.Payload), transaction);
            written.Add((entry.Text, entry.Weight));
        }
    }

    /// <summary>The stored text of a key; a key of the other kind, or one the key type does not allow, is refused (ADR-0035, ADR-0073).</summary>
    private string KeyText(LuiKey key) => KeyCodec.ToText(key, _keyType, Name);

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

        // the payload is written with the type it has (ADR-0035)
        return _payload?.Serialize(payload)
            ?? throw new InvalidOperationException(
                $"The entry of the index '{Name}' has a payload, and the catalog has no serialiser: set "
                + "LuiCatalogOptions.PayloadSerializer (LuiPayloadSerializer.Json(context)).");
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
        IEnumerable<LuiEntry> entries,
        LuiSyncOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        using var scope = _database.Begin();
        try
        {
            _guard?.Check();
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

    public LuiSyncReport Sync(
        IAsyncEnumerable<LuiEntry> entries,
        LuiSyncOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);

        // the source may be asynchronous, the writes to SQLite are not (ADR-0016): the entries are awaited one by one
        return Sync(entries.ToBlockingEnumerable(cancellationToken), options, cancellationToken);
    }

    public LuiIntegrityReport Check(IEnumerable<LuiEntry> entries, CancellationToken cancellationToken = default)
    {
        try
        {
            _guard?.Check();
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
            _guard?.Check();
            return _check.Itself();
        }
        catch (SqliteException exception) when (Database.IsReadOnlyRefusal(exception))
        {
            throw _database.ReadOnlyRefusal("checking an index", Name, exception);
        }
    }

    public void Rebuild()
    {
        try
        {
            _guard?.Check();
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
            _guard?.Check();
            _check.Optimize();
        }
        catch (SqliteException exception) when (Database.IsReadOnlyRefusal(exception))
        {
            throw _database.ReadOnlyRefusal("optimizing an index", Name, exception);
        }
    }

    public bool UpdateWeight(LuiKey key, int weight, SqliteTransaction? transaction = null)
    {
        _database.RefuseTheTransactionOfTheApplication("changing the weight of an entry", Name, transaction);
        var keyText = KeyText(key);
        RowId.ValidateWeight(weight);

        using var scope = _database.Begin();
        try
        {
            if (transaction is not null)
            {
                CheckIn(transaction);
                return StatementsFor(transaction).UpdateWeight(keyText, weight, transaction);
            }

            using var connection = _database.OpenConnection();
            _guard?.Check(connection);
            return OwnStatements(connection).UpdateWeight(keyText, weight, null);
        }
        catch (SqliteException exception) when (Database.IsReadOnlyRefusal(exception))
        {
            throw _database.ReadOnlyRefusal("changing the weight of an entry", Name, exception);
        }
    }

    public bool UpdatePriority(LuiKey key, LuiPriority priority, SqliteTransaction? transaction = null)
    {
        _database.RefuseTheTransactionOfTheApplication("changing the priority of an entry", Name, transaction);
        var keyText = KeyText(key);
        RowId.ValidatePriority(priority);

        using var scope = _database.Begin();
        try
        {
            if (transaction is not null)
            {
                CheckIn(transaction);
                return StatementsFor(transaction).UpdatePriority(keyText, (int)priority, transaction);
            }

            using var connection = _database.OpenConnection();
            _guard?.Check(connection);
            return OwnStatements(connection).UpdatePriority(keyText, (int)priority, null);
        }
        catch (SqliteException exception) when (Database.IsReadOnlyRefusal(exception))
        {
            throw _database.ReadOnlyRefusal("changing the priority of an entry", Name, exception);
        }
    }

    public bool Remove(LuiKey key, SqliteTransaction? transaction = null)
    {
        _database.RefuseTheTransactionOfTheApplication("removing an entry", Name, transaction);
        var keyText = KeyText(key);

        using var scope = _database.Begin();
        try
        {
            if (transaction is not null)
            {
                CheckIn(transaction);
                return StatementsFor(transaction).Remove(keyText, transaction);
            }

            using var connection = _database.OpenConnection();
            _guard?.Check(connection);
            return OwnStatements(connection).Remove(keyText, null);
        }
        catch (SqliteException exception) when (Database.IsReadOnlyRefusal(exception))
        {
            throw _database.ReadOnlyRefusal("removing an entry", Name, exception);
        }
    }

    public void RebuildTypoDictionary(CancellationToken cancellationToken = default)
    {
        using var scope = _database.Begin();
        _guard?.Check();
        _typos.Rebuild(cancellationToken);
    }

    public void WarmUpTypoDictionary(CancellationToken cancellationToken = default)
    {
        using var scope = _database.Begin();
        _guard?.Check();
        _typos.EnsureBuilt(cancellationToken);
    }

    public long Count()
    {
        using var scope = _database.Begin();
        using var connection = _database.OpenReadConnection();
        _guard?.Check(connection);
        using var command = connection.CreateCommand();
        command.CommandText = _source.CountSql;
        return (long)command.ExecuteScalar()!;
    }

    public LuiQuery Query(string text) => new(this, text);

    public LuiResult Search(
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
            _guard.Check(connection, transaction);
        }
    }

    /// <summary>
    /// The statements for a write in the transaction of the application: one set per connection of the application,
    /// kept as long as the connection lives (ADR-0009).
    /// </summary>
    private WriteStatements StatementsFor(SqliteTransaction transaction)
    {
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
