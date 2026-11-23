using Microsoft.Data.Sqlite;
using Nordmann.LUI.Querying;
using Nordmann.LUI.Schema;
using Nordmann.LUI.Storage;
using Nordmann.LUI.Typos;

namespace Nordmann.LUI.Indexes;

/// <summary>
/// An index over a table of the application (ADR-0014, ADR-0015, ADR-0015, ADR-0015). It writes only its own tables;
/// what it indexes it reads from the application's table, by the statements of its <see cref="LinkedSource"/>.
/// <para>
/// Whether it may write is decided by the file, not by the index (ADR-0033): an index of a catalog that was
/// opened read-only refuses every write with a <see cref="LuiReadOnlyException"/>.
/// </para>
/// </summary>
#pragma warning disable CA1001 // see TypoDictionary: the lock of the dictionary needs no disposal, and an index lives as long as its catalog
internal sealed class LinkedIndex : IMaintainedIndex
#pragma warning restore CA1001
{
    /// <summary>The keys of the outbox that <c>ApplyPending</c> brings in step per transaction.</summary>
    internal const int PendingBatch = 1_000;

    /// <summary><c>SQLITE_ERROR</c>: among others, a table or a column that is not there.</summary>
    private const int SqliteError = 1;

    private static readonly LuiQueryOptions DefaultOptions = new();

    private readonly Database _database;
    private readonly LinkedSource _source;
    private readonly TypoDictionary _typos;
    private readonly QueryRunner _runner;
    private readonly IReadOnlyList<string> _payloadColumns;

    // asks, on the connection of every operation, whether the definition of the index is still the one in the file
    // (ADR-0075); null for an index that is built without a definition (the maintenance by name)
    private readonly IndexGuard? _guard;

    internal LinkedIndex(
        Database database,
        TypoDictionaries typoDictionaries,
        LinkedSource source,
        PayloadReader payload,
        ILuiSimilarity similarity,
        LuiTypoDistance typoDistance,
        IndexGuard? guard = null)
    {
        _database = database;
        _guard = guard;
        _source = source;
        Name = source.IndexName;
        Kind = source.Rules.Kind;
        _typos = typoDictionaries.For(Name, source.Normalization, () => TypoSource.Read(database, source.TextsSql), typoDistance, source.Definition.ToJson());
        _runner = new QueryRunner(database, source, payload, similarity, _typos, typoDistance, guard);
        _payloadColumns = payload.Columns;
    }

    public string Name { get; }

    public LuiIndexKind Kind { get; }

    public LuiTypoDistance TypoDistance => _runner.TypoDistance;

    public LuiTypoDictionaryState TypoDictionaryState => _typos.State;

    public long Count()
    {
        using var scope = _database.Begin();
        using var connection = _database.OpenReadConnection();

        // the check and the count in one snapshot (ADR-0075)
        using var snapshot = _guard is null ? null : connection.BeginTransaction(deferred: true);
        _guard?.Check(connection, snapshot);
        return Scalar(connection, snapshot, _source.CountSql);
    }

    /// <summary>
    /// Whether the first complete <see cref="Sync(LuiSyncOptions?, CancellationToken)"/> is still to be done (ADR-0077). It is read from the file on every call — in the
    /// snapshot that the guard checked — because another process can complete the sync without a change of the SQLite schema.
    /// </summary>
    /// <returns><see langword="true"/> from the creation of the index until its first complete sync.</returns>
    public bool NeedsInitialSync()
    {
        using var scope = _database.Begin();
        using var connection = _database.OpenReadConnection();

        // the check and the read in one snapshot (ADR-0075): the answer is the one of the index the guard judged
        using var snapshot = connection.BeginTransaction(deferred: true);
        _guard?.Check(connection, snapshot);
        return IndexStore.ReadNeedsInitialSync(connection, snapshot, Name) ?? throw new LuiIndexNotFoundException(Name);
    }

    public LuiTriggerState Triggers()
    {
        using var scope = _database.Begin();
        using var connection = _database.OpenReadConnection();
        using var snapshot = _guard is null ? null : connection.BeginTransaction(deferred: true);
        _guard?.Check(connection, snapshot);
        return TriggerState(connection, snapshot);
    }

    /// <summary>
    /// The state of the triggers, the flag of the first sync and the lease, in one snapshot that the guard checked (ADR-0075, ADR-0076): the answer is the one of the index
    /// the guard judged. It is what <c>Initialize()</c> decides on, and what <c>Indexes()</c> turns into <see cref="LuiInitializationNeeds"/>.
    /// </summary>
    /// <returns>The state.</returns>
    internal LinkedInitialization ReadInitializationState()
    {
        using var scope = _database.Begin();
        using var connection = _database.OpenReadConnection();
        using var snapshot = connection.BeginTransaction(deferred: true);
        _guard?.Check(connection, snapshot);

        var triggers = TriggerState(connection, snapshot);
        var due = IndexStore.ReadNeedsInitialSync(connection, snapshot, Name) ?? throw new LuiIndexNotFoundException(Name);

        // a lease says something only about a sync that is due: the owner and its expiry, as the sync of #271 writes them
        var running = false;
        if (due)
        {
            using var read = Command(connection, snapshot, SyncLease.ReadSql);
            read.Parameters.AddWithValue("$name", Name);
            using var lease = read.ExecuteReader();
            running = lease.Read()
                && !lease.IsDBNull(0)
                && !lease.IsDBNull(1)
                && lease.GetInt64(1) > _database.TimeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        }

        return new LinkedInitialization(triggers, due, running);
    }

    private LuiTriggerState TriggerState(SqliteConnection connection, SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = _source.StoredTriggersSql;

        var stored = new Dictionary<string, string>(StringComparer.Ordinal);
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                stored[reader.GetString(0)] = reader.GetString(1);
            }
        }

        if (stored.Count == 0)
        {
            return LuiTriggerState.None;
        }

        var expected = _source.TriggerStatements();
        return expected.Count == stored.Count
            && expected.All(trigger => stored.TryGetValue(trigger.Name, out var sql) && SqlText.Same(sql, trigger.Sql))
            ? LuiTriggerState.Current
            : LuiTriggerState.Stale;
    }

    public RawResult Search(
        string text,
        LuiQueryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        RawResult result = null!;
        Guarded(() => result = _runner.Execute(text, options ?? DefaultOptions, cancellationToken));
        return result;
    }

    public void WarmUpTypoDictionary(CancellationToken cancellationToken = default)
    {
        using var scope = _database.Begin();
        _guard?.Check();
        try
        {
            _typos.EnsureBuilt(cancellationToken);

            // the index may have been created again while the texts were read: the dictionary is of another definition then
            CheckAfterTheWork();
        }
        catch (SqliteException exception) when (_guard is not null)
        {
            // the texts are read on a connection of their own: a table that is missing under the check is the check's answer
            _guard.Explain(exception);
            throw;
        }
    }

    public void RebuildTypoDictionary(CancellationToken cancellationToken = default)
    {
        using var scope = _database.Begin();
        _guard?.Check();
        try
        {
            _typos.Rebuild(cancellationToken);

            // the index may have been created again while the texts were read: the dictionary is of another definition then
            CheckAfterTheWork();
        }
        catch (SqliteException exception) when (_guard is not null)
        {
            _guard.Explain(exception);
            throw;
        }
    }

    public bool Refresh(long key, SqliteTransaction? transaction = null) =>
        RefreshMany([key], transaction) == 1;

    public int RefreshMany(IEnumerable<long> keys, SqliteTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);

        // a key that comes twice is refreshed once and counted once (review of pull request 16, N2)
        var distinct = keys.Distinct();

        using var scope = _database.Begin();
        var learned = new List<(string Text, int Weight)>();
        Guarded(() => Write(transaction, (connection, inTransaction) => RefreshAll(connection, inTransaction, distinct, learned, cancellationToken)));

        // the new word beginnings join the typo dictionary, if it is built (ADR-0012)
        _typos.AddRange(learned);
        return learned.Count;
    }

    private void RefreshAll(
        SqliteConnection connection,
        SqliteTransaction inTransaction,
        IEnumerable<long> keys,
        List<(string Text, int Weight)> learned,
        CancellationToken cancellationToken)
    {
        using var refresher = new Refresher(connection, inTransaction, _source);
        foreach (var key in keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (key < 0 || key > _source.MaxKey)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(keys), key, $"The key of the linked index '{Name}' is its document id: 0 … {_source.MaxKey:N0}.");
            }

            refresher.Refresh(key, learned);
        }
    }

    public int ApplyPending(CancellationToken cancellationToken = default)
    {
        using var scope = _database.Begin();
        var applied = 0;
        var learned = new List<(string Text, int Weight)>();
        while (true)
        {
            var batch = 0;
            Guarded(() => Write(null, (connection, transaction) => batch = ApplyBatch(connection, transaction, learned, cancellationToken)));

            // a batch is committed: its words join the typo dictionary, if it is built
            _typos.AddRange(learned);
            learned.Clear();
            applied += batch;

            if (batch < PendingBatch)
            {
                return applied;
            }
        }
    }

    public long Pending()
    {
        using var scope = _database.Begin();
        using var connection = _database.OpenReadConnection();
        using var snapshot = _guard is null ? null : connection.BeginTransaction(deferred: true);
        _guard?.Check(connection, snapshot);
        long pending = 0;
        Guarded(() => pending = Scalar(connection, snapshot, _source.PendingCountSql));
        return pending;
    }

    /// <summary>Brings up to <see cref="PendingBatch"/> keys of the outbox in step, the smallest first, and takes them out.</summary>
    private int ApplyBatch(
        SqliteConnection connection,
        SqliteTransaction transaction,
        List<(string Text, int Weight)> learned,
        CancellationToken cancellationToken)
    {
        var keys = new List<long>(PendingBatch);
        using (var next = Command(connection, transaction, _source.PendingBatchSql))
        {
            next.Parameters.AddWithValue("$n", PendingBatch);
            using var reader = next.ExecuteReader();
            while (reader.Read())
            {
                keys.Add(reader.GetInt64(0));
            }
        }

        using var refresher = new Refresher(connection, transaction, _source);
        foreach (var key in keys)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // a key that cannot be a document id of this index (above the int range, for an int index) is not indexed — it only leaves the outbox (M2)
            if (key < 0 || key > _source.MaxKey)
            {
                refresher.Forget(key);
            }
            else
            {
                refresher.Refresh(key, learned);
            }
        }

        return keys.Count;
    }

    public LuiLinkedSyncReport Sync(CancellationToken cancellationToken = default) => Sync(null, cancellationToken);

    /// <summary>
    /// Takes the whole table into the index, in batches (ADR-0077): see <see cref="LinkedSync"/>. A built typo dictionary is rebuilt once, at
    /// the end, from the table as it is then.
    /// </summary>
    /// <param name="options">The options of the sync; <see langword="null"/> for the defaults.</param>
    /// <param name="cancellationToken">Checked between the batches and the steps.</param>
    /// <returns>What was indexed, what was left out, and how the sync worked.</returns>
    public LuiLinkedSyncReport Sync(LuiSyncOptions? options, CancellationToken cancellationToken = default) =>
        SyncCore(options, onlyWhenNeeded: false, cancellationToken)!; // never null without onlyWhenNeeded

    /// <summary>
    /// The first sync of <c>Initialize()</c> (ADR-0076): the sync of <see cref="Sync(LuiSyncOptions?, CancellationToken)"/>, but only while the first complete sync is still
    /// due — checked in the transaction that takes the lease, so of two callers exactly one syncs.
    /// </summary>
    /// <param name="options">The options of the sync; <see langword="null"/> for the defaults.</param>
    /// <param name="cancellationToken">Checked between the batches and the steps.</param>
    /// <returns>The report; <see langword="null"/> when nothing was due.</returns>
    /// <exception cref="LuiSyncInProgressException">Another sync holds the lease.</exception>
    internal LuiLinkedSyncReport? SyncWhenNeeded(LuiSyncOptions? options, CancellationToken cancellationToken) =>
        SyncCore(options, onlyWhenNeeded: true, cancellationToken);

    private LuiLinkedSyncReport? SyncCore(LuiSyncOptions? options, bool onlyWhenNeeded, CancellationToken cancellationToken)
    {
        using var scope = _database.Begin();
        LuiLinkedSyncReport? result = null;
        Guarded(() => result = new LinkedSync(_database, _source, _guard).Run(options ?? new LuiSyncOptions(), onlyWhenNeeded, cancellationToken));
        if (result is null)
        {
            return null;
        }

        // the words of the table as they are now: a built dictionary is built anew
        if (_typos.IsBuilt)
        {
            _guard?.Check();
            try
            {
                _typos.Rebuild(cancellationToken);
                CheckAfterTheWork();
            }
            catch (SqliteException exception) when (_guard is not null)
            {
                // the texts are read on a connection of their own: a table that is missing under the check is the check's answer
                _guard.Explain(exception);
                throw;
            }
        }

        return result;
    }

    public void Optimize()
    {
        using var scope = _database.Begin();
        Write(null, (connection, transaction) => Execute(connection, transaction, _source.OptimizeSql));
    }

    public LuiIntegrityReport Check()
    {
        using var scope = _database.Begin();
        List<string> problems = [];
        var fits = false;
        try
        {
            lock (_database.WriterGate)
            {
                // the integrity check of FTS5 is an insert: it runs on a connection that may write, and the transaction is rolled back
                using var writer = _database.OpenConnection();
                using var transaction = writer.BeginTransaction(deferred: false);
                _guard?.Check(writer, transaction);
                fits = IndexCheck.Definition(writer, transaction, _source, problems) && TableFits(writer, transaction, problems);
                if (fits)
                {
                    if (TriggerState(writer, transaction) == LuiTriggerState.Stale)
                    {
                        problems.Add(
                            $"The triggers of '{Name}' on the table '{_source.Link.Table}' are not the ones the index writes now: "
                            + "CreateTriggers() creates them anew — or put TriggerScript() into a migration.");
                    }

                    IndexCheck.SearchIndex(writer, transaction, _source, problems, "Sync() builds the index anew.");
                }

                transaction.Rollback();
            }
        }
        catch (SqliteException exception) when (Database.IsReadOnlyRefusal(exception))
        {
            throw _database.ReadOnlyRefusal("checking an index", Name, exception);
        }

        if (!fits)
        {
            return new LuiIntegrityReport { Problems = problems };
        }

        // the comparison with the table only reads: a snapshot of its own, beside the writers (review of pull request 18, S3)
        using var connection = _database.OpenReadConnection();
        using var snapshot = connection.BeginTransaction(deferred: true);
        _guard?.Check(connection, snapshot);
        return CompareWithTable(connection, snapshot, problems);
    }

    /// <summary>The table still has what the link reads — a finding, not an exception, when it does not.</summary>
    private bool TableFits(SqliteConnection connection, SqliteTransaction transaction, List<string> problems)
    {
        try
        {
            LinkedTable.Verify(connection, transaction, _source.Link, _payloadColumns);
            return true;
        }
        catch (LuiLinkedTableException exception)
        {
            problems.Add($"{exception.Message} The index cannot read the table: drop the index and create it anew.");
            return false;
        }
    }

    /// <summary>The entries against the rows of the table: missing, extra, with another weight, priority or beginning.</summary>
    private LuiIntegrityReport CompareWithTable(SqliteConnection connection, SqliteTransaction transaction, List<string> problems)
    {
        var (missing, missingKeys) = Finding(connection, transaction, _source.MissingSql(count: true), _source.MissingSql(count: false));
        var (extra, extraKeys) = Finding(connection, transaction, _source.ExtraSql(count: true), _source.ExtraSql(count: false));
        var (different, differentKeys) = Finding(connection, transaction, _source.DifferentSql(count: true), _source.DifferentSql(count: false));

        var repair = Scalar(connection, transaction, _source.PendingCountSql) is var pending and > 0
            ? $"ApplyPending() brings the {pending:N0} rows the triggers noted in step, Sync() all of them"
            : "Refresh(id) or Sync() brings them in step";

        if (missing > 0)
        {
            problems.Add($"{missing:N0} rows of '{_source.Link.Table}' are not in the index ({IndexCheck.Keys(missingKeys, missing)}): {repair}.");
        }

        if (extra > 0)
        {
            problems.Add($"{extra:N0} entries of the index have no row to index any more ({IndexCheck.Keys(extraKeys, extra)}): {repair}.");
        }

        if (different > 0)
        {
            problems.Add(
                $"{different:N0} entries of the index have another weight, priority or beginning than their row "
                + $"({IndexCheck.Keys(differentKeys, different)}): {repair}.");
        }

        return new LuiIntegrityReport { Problems = problems, Missing = missing, Extra = extra, Different = different };
    }

    private static (long Count, List<string> Keys) Finding(SqliteConnection connection, SqliteTransaction transaction, string countSql, string keysSql)
    {
        var count = Scalar(connection, transaction, countSql);
        using var command = Command(connection, transaction, keysSql);
        command.Parameters.AddWithValue("$n", IndexCheck.Examples);
        using var reader = command.ExecuteReader();
        var keys = new List<string>();
        while (reader.Read())
        {
            keys.Add(reader.GetInt64(0).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return (count, keys);
    }

    public string TriggerScript() => _source.TriggerScript();

    public void CreateTriggers()
    {
        using var scope = _database.Begin();

        // the state is read and acted on in the one write transaction that the guard checked (ADR-0075, ADR-0077): another process
        // cannot create, drop or change the triggers between the reading and the work
        Write(null, (connection, transaction) =>
        {
            var state = TriggerState(connection, transaction);
            if (state == LuiTriggerState.Current)
            {
                return;
            }

            if (state == LuiTriggerState.Stale)
            {
                Execute(connection, transaction, LinkedSource.DropTriggersSql(Name));
            }

            Execute(connection, transaction, _source.TriggerScript());
        });
    }

    public void DropTriggers()
    {
        using var scope = _database.Begin();
        Write(null, (connection, transaction) => Execute(connection, transaction, LinkedSource.DropTriggersSql(Name)));
    }

    /// <summary>
    /// Runs a write in the transaction of the application, or in one of LUI's own on a connection from the pool — then the
    /// lock of the writes is held for the whole of it (one writer per file anyway, ADR-0015, ADR-0046), and an error or a cancellation
    /// rolls it back.
    /// </summary>
    private void Write(SqliteTransaction? transaction, Action<SqliteConnection, SqliteTransaction> write)
    {
        _database.RefuseTheTransactionOfTheApplication("writing to a linked index", Name, transaction);

        try
        {
            if (transaction is not null)
            {
                var connection = transaction.Connection
                    ?? throw new ArgumentException("The transaction is complete.", nameof(transaction));
                _guard?.ThrowIfFailed();
                EnsureSameDatabase(connection, transaction);
                _guard?.Check(connection, transaction, application: true);
                write(connection, transaction);
                return;
            }

            lock (_database.WriterGate)
            {
                using var connection = _database.OpenConnection();
                using var own = connection.BeginTransaction(deferred: false);
                _guard?.Check(connection, own);
                write(connection, own);
                own.Commit();
            }
        }
        catch (SqliteException exception) when (Database.IsReadOnlyRefusal(exception))
        {
            throw _database.ReadOnlyRefusal("writing to a linked index", Name, exception);
        }
    }

    /// <summary>
    /// A transaction of another database would end in a bare "no such table" (review of pull request 16, N5): the
    /// table of the index has to be on the connection.
    /// </summary>
    private void EnsureSameDatabase(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var check = Command(connection, transaction, "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = $name;");
        check.Parameters.AddWithValue("$name", _source.EntryTable);
        if ((long)check.ExecuteScalar()! == 0)
        {
            throw new ArgumentException(
                $"The transaction belongs to another database: the table '{_source.EntryTable}' of the index '{Name}' is not there.",
                nameof(transaction));
        }
    }

    /// <summary>The check after the texts were read on a connection of their own: a dictionary of an index that was created again under it is let go.</summary>
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

    /// <summary>
    /// A table that changed under the index — a column renamed, the table dropped — answers with a statement that names
    /// what is missing, not with a bare SQLite error (review of pull request 16, S4).
    /// </summary>
    private void Guarded(Action run)
    {
        try
        {
            run();
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == SqliteError
            && (exception.Message.Contains("no such column", StringComparison.Ordinal)
                || exception.Message.Contains("no such table", StringComparison.Ordinal)))
        {
            throw new LuiLinkedTableException(
                $"The table '{_source.Link.Table}' does not fit the index '{Name}' any more ({exception.Message}). Create the index "
                + "again to see what is missing; a changed table is a new definition: drop the index and create it anew.",
                exception);
        }
    }

    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return command;
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = Command(connection, transaction, sql);
        command.ExecuteNonQuery();
    }

    private static long Scalar(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return (long)command.ExecuteScalar()!;
    }

    /// <summary>
    /// The statements of a refresh, prepared once for a batch: read the row (for the typo dictionary), bring its entry in
    /// step, and take its key out of the outbox — it is current now.
    /// </summary>
    private sealed class Refresher : IDisposable
    {
        private readonly SqliteCommand _read;
        private readonly SqliteCommand _refresh;
        private readonly SqliteCommand _forget;
        private readonly SqliteParameter _readKey;
        private readonly SqliteParameter _refreshKey;
        private readonly SqliteParameter _forgetKey;

        internal Refresher(SqliteConnection connection, SqliteTransaction transaction, LinkedSource source)
        {
            _read = Command(connection, transaction, source.ReadRowSql);
            _refresh = Command(connection, transaction, source.RefreshSql("$id"));
            _forget = Command(connection, transaction, source.ForgetPendingSql);
            _readKey = _read.Parameters.Add("$id", SqliteType.Integer);
            _refreshKey = _refresh.Parameters.Add("$id", SqliteType.Integer);
            _forgetKey = _forget.Parameters.Add("$id", SqliteType.Integer);
        }

        internal void Refresh(long key, List<(string Text, int Weight)> learned)
        {
            _readKey.Value = key;
            using (var reader = _read.ExecuteReader())
            {
                if (reader.Read())
                {
                    learned.Add((reader.GetString(0), reader.GetInt32(1)));
                }
            }

            _refreshKey.Value = key;
            _refresh.ExecuteNonQuery();
            Forget(key);
        }

        internal void Forget(long key)
        {
            _forgetKey.Value = key;
            _forget.ExecuteNonQuery();
        }

        public void Dispose()
        {
            _read.Dispose();
            _refresh.Dispose();
            _forget.Dispose();
        }
    }
}
