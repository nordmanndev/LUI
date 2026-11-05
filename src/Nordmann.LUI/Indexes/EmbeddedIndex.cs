using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using Nordmann.LUI.Querying;
using Nordmann.LUI.Schema;
using Nordmann.LUI.Storage;
using Nordmann.LUI.Typos;
using Nordmann.LUI.Writing;

namespace Nordmann.LUI.Indexes;

/// <summary>
/// An index whose content lives in a table of the library (LUI-Spezifikation
/// §5). The writes are plain statements: the generated columns compute the
/// forms, the triggers keep the search index in step (E3).
/// </summary>
#pragma warning disable CA1001 // see TypoDictionary: the lock of the dictionary needs no disposal, and an index lives as long as its catalog
internal sealed class EmbeddedIndex<TKey, TPayload> : ILuiEmbeddedIndex<TKey, TPayload>
#pragma warning restore CA1001
{
    private static readonly LuiQueryOptions DefaultOptions = new();
    private static readonly LuiSyncOptions DefaultSyncOptions = new();

    private readonly Database _database;
    private readonly ILuiPayloadSerializer<TPayload>? _payload;
    private readonly QueryRunner<TKey, TPayload> _runner;
    /// <summary><c>SQLITE_CONSTRAINT_CHECK</c>.</summary>
    private const int SqliteCheckConstraintFailed = 275;

    private readonly EmbeddedSource _source;
    private readonly TypoDictionary _typos;
    private readonly EmbeddedSync<TKey, TPayload> _sync;
    private readonly EmbeddedCheck<TKey, TPayload> _check;

    // The statements of the application's own connections, one set per connection.
    private readonly ConditionalWeakTable<SqliteConnection, WriteStatements> _applicationStatements = [];
    private readonly Lock _ownGate = new();
    private WriteStatements? _ownStatements;

    internal EmbeddedIndex(
        Database database,
        TypoDictionaries typoDictionaries,
        EmbeddedSource source,
        ILuiPayloadSerializer<TPayload>? payload,
        ILuiSimilarity similarity,
        bool typoTolerance)
    {
        _database = database;
        _payload = payload;
        _source = source;
        Name = source.IndexName;
        Kind = source.Rules.Kind;
        _typos = typoDictionaries.For(Name, source.Normalization, () => TypoSource.Read(database, source.TextsSql));
        _runner = new QueryRunner<TKey, TPayload>(
            database, source, new SerializedPayload<TPayload>(payload), similarity, _typos, typoTolerance);
        _sync = new EmbeddedSync<TKey, TPayload>(database, source, PayloadBytes, _typos);
        _check = new EmbeddedCheck<TKey, TPayload>(database, source, PayloadBytes);
    }

    public string Name { get; }

    public LuiIndexKind Kind { get; }

    public bool TypoTolerance => _runner.TypoTolerance;

    public LuiTypoDictionaryState TypoDictionaryState => _typos.State;

    public void Upsert(LuiEntry<TKey, TPayload> entry, SqliteTransaction? transaction = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var keyText = KeyCodec<TKey>.ToText(entry.Key);
        var bytes = PayloadBytes(entry.Payload);

        using var scope = _database.Begin();
        try
        {
            StatementsFor(transaction).Upsert(keyText, entry.Text, entry.Weight, (int)entry.Priority, bytes, transaction);
        }
        catch (SqliteException exception) when (IsDocIdExhausted(exception))
        {
            throw DocIdsExhausted(exception);
        }

        // the new word beginnings join the typo dictionary, if it is built (§10.1)
        _typos.Add(entry.Text, entry.Weight);
    }

    public void Upsert(
        TKey key,
        string text,
        int weight = 0,
        LuiPriority priority = LuiPriority.Normal,
        SqliteTransaction? transaction = null) =>
        Upsert(new LuiEntry<TKey, TPayload>(key, text) { Weight = weight, Priority = priority }, transaction);

    public int UpsertMany(
        IEnumerable<LuiEntry<TKey, TPayload>> entries,
        SqliteTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);

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
                WriteBatch(StatementsFor(transaction), entries, transaction, written, cancellationToken);
            }
        }
        catch (SqliteException exception) when (IsDocIdExhausted(exception))
        {
            throw DocIdsExhausted(exception);
        }

        // once, after the writes: the dictionary takes its write lock one time for the whole batch
        _typos.AddRange(written);
        return written.Count;
    }

    /// <summary>
    /// A batch in a transaction of LUI's own, on the catalog's writer: the writer's lock is held for the whole
    /// batch, so no single write of another index slips into the transaction (there is one writer per file
    /// anyway, §15).
    /// </summary>
    private void WriteOwnBatch(
        IEnumerable<LuiEntry<TKey, TPayload>> entries,
        List<(string Text, int Weight)> written,
        CancellationToken cancellationToken)
    {
        var statements = Volatile.Read(ref _ownStatements) ?? CreateOwnStatements();
        lock (_database.WriterGate)
        {
            using var transaction = _database.Writer.BeginTransaction(deferred: false);
            WriteBatch(statements, entries, transaction, written, cancellationToken);
            transaction.Commit();
        }
    }

    private void WriteBatch(
        WriteStatements statements,
        IEnumerable<LuiEntry<TKey, TPayload>> entries,
        SqliteTransaction transaction,
        List<(string Text, int Weight)> written,
        CancellationToken cancellationToken)
    {
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(entry, nameof(entries));

            var keyText = KeyCodec<TKey>.ToText(entry.Key);
            statements.Upsert(keyText, entry.Text, entry.Weight, (int)entry.Priority, PayloadBytes(entry.Payload), transaction);
            written.Add((entry.Text, entry.Weight));
        }
    }

    /// <summary>
    /// The stored form of a payload. A payload that is null, or the default of a value type, is "no payload" —
    /// it is stored as NULL and read back as the default. A value type cannot say more (review B1).
    /// </summary>
    private byte[]? PayloadBytes(TPayload? payload)
    {
        if (payload is null || EqualityComparer<TPayload>.Default.Equals(payload, default))
        {
            return null;
        }

        return _payload?.Serialize(payload)
            ?? throw new InvalidOperationException(
                $"The index '{Name}' stores no payload: give it one with WithPayload(…) when it is created.");
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
        IEnumerable<LuiEntry<TKey, TPayload>> entries,
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
    }

    public LuiSyncReport Sync(
        IAsyncEnumerable<LuiEntry<TKey, TPayload>> entries,
        LuiSyncOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);

        // the source may be asynchronous, the writes to SQLite are not (§13): the entries are awaited one by one
        return Sync(entries.ToBlockingEnumerable(cancellationToken), options, cancellationToken);
    }

    public LuiIntegrityReport Check(IEnumerable<LuiEntry<TKey, TPayload>> entries, CancellationToken cancellationToken = default) =>
        _check.Against(entries, cancellationToken);

    public LuiIntegrityReport Check() => _check.Itself();

    public void Rebuild() => _check.Rebuild();

    public void Optimize() => _check.Optimize();

    public bool UpdateWeight(TKey key, int weight, SqliteTransaction? transaction = null)
    {
        var keyText = KeyCodec<TKey>.ToText(key);
        RowId.ValidateWeight(weight);

        using var scope = _database.Begin();
        return StatementsFor(transaction).UpdateWeight(keyText, weight, transaction);
    }

    public bool UpdatePriority(TKey key, LuiPriority priority, SqliteTransaction? transaction = null)
    {
        var keyText = KeyCodec<TKey>.ToText(key);
        RowId.ValidatePriority(priority);

        using var scope = _database.Begin();
        return StatementsFor(transaction).UpdatePriority(keyText, (int)priority, transaction);
    }

    public bool Remove(TKey key, SqliteTransaction? transaction = null)
    {
        var keyText = KeyCodec<TKey>.ToText(key);

        using var scope = _database.Begin();
        return StatementsFor(transaction).Remove(keyText, transaction);
    }

    public void RebuildTypoDictionary(CancellationToken cancellationToken = default)
    {
        using var scope = _database.Begin();
        _typos.Rebuild(cancellationToken);
    }

    public void WarmUpTypoDictionary(CancellationToken cancellationToken = default)
    {
        using var scope = _database.Begin();
        _typos.EnsureBuilt(cancellationToken);
    }

    public long Count()
    {
        using var scope = _database.Begin();
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = _source.CountSql;
        return (long)command.ExecuteScalar()!;
    }

    public LuiQuery<TKey, TPayload> Query(string text) => new(this, text);

    public LuiResult<TKey, TPayload> Search(
        string text,
        LuiQueryOptions? options = null,
        CancellationToken cancellationToken = default) =>
        _runner.Execute(text, options ?? DefaultOptions, cancellationToken);

    /// <summary>
    /// The statements for a write: those of the application's connection when
    /// it brought a transaction, otherwise those of the catalog's own writer
    /// (one writer per file anyway, §15).
    /// </summary>
    private WriteStatements StatementsFor(SqliteTransaction? transaction)
    {
        if (transaction is null)
        {
            return Volatile.Read(ref _ownStatements) ?? CreateOwnStatements();
        }

        var connection = transaction.Connection
            ?? throw new ArgumentException("The transaction is complete.", nameof(transaction));

        return _applicationStatements.TryGetValue(connection, out var statements)
            ? statements
            : _applicationStatements.GetValue(connection, c => ForApplication(c, transaction));
    }

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

    private WriteStatements CreateOwnStatements()
    {
        lock (_ownGate)
        {
            return _ownStatements ??= new WriteStatements(_database.Writer, _source, _database.WriterGate);
        }
    }
}
