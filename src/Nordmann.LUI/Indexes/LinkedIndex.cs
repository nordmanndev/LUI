using Microsoft.Data.Sqlite;
using Nordmann.LUI.Querying;
using Nordmann.LUI.Schema;
using Nordmann.LUI.Storage;
using Nordmann.LUI.Typos;

namespace Nordmann.LUI.Indexes;

/// <summary>
/// An index over a table of the application (LUI-Spezifikation §5, §7.3, ADR-0015). It writes only its own tables;
/// what it indexes it reads from the application's table, by the statements of its <see cref="LinkedSource"/>.
/// <para>
/// Whether it may write is decided by the file, not by the index (ADR-0033): an index of a catalog that was
/// opened read-only refuses every write with a <see cref="LuiReadOnlyException"/>.
/// </para>
/// </summary>
#pragma warning disable CA1001 // see TypoDictionary: the lock of the dictionary needs no disposal, and an index lives as long as its catalog
internal sealed class LinkedIndex : ILuiLinkedIndex
#pragma warning restore CA1001
{
    /// <summary>The rows a sync indexes per statement: the cancellation is checked between chunks.</summary>
    internal const int SyncChunk = 10_000;

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

    internal LinkedIndex(
        Database database,
        TypoDictionaries typoDictionaries,
        LinkedSource source,
        PayloadReader payload,
        ILuiSimilarity similarity,
        LuiTypoDistance typoDistance)
    {
        _database = database;
        _source = source;
        Name = source.IndexName;
        Kind = source.Rules.Kind;
        _typos = typoDictionaries.For(Name, source.Normalization, () => TypoSource.Read(database, source.TextsSql), typoDistance);
        _runner = new QueryRunner(database, source, payload, similarity, _typos, typoDistance);
        _payloadColumns = payload.Columns;
    }

    public string Name { get; }

    public LuiIndexKind Kind { get; }

    public LuiKeyKind KeyKind => LuiKeyKind.Number;

    public LuiTypoDistance TypoDistance => _runner.TypoDistance;

    public LuiTypoDictionaryState TypoDictionaryState => _typos.State;

    public long Count()
    {
        using var scope = _database.Begin();
        using var connection = _database.OpenConnection();
        return Scalar(connection, null, _source.CountSql);
    }

    public LuiTriggerState Triggers()
    {
        using var scope = _database.Begin();
        using var connection = _database.OpenConnection();
        return TriggerState(connection, null);
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

    public LuiQuery Query(string text) => new(this, text);

    public LuiResult Search(
        string text,
        LuiQueryOptions? options = null,
        CancellationToken cancellationToken = default) =>
        Guarded(() => _runner.Execute(text, options ?? DefaultOptions, cancellationToken));

    public void WarmUpTypoDictionary(CancellationToken cancellationToken = default)
    {
        using var scope = _database.Begin();
        _typos.EnsureBuilt(cancellationToken);
    }

    public void RebuildTypoDictionary(CancellationToken cancellationToken = default)
    {
        using var scope = _database.Begin();
        _typos.Rebuild(cancellationToken);
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

        // the new word beginnings join the typo dictionary, if it is built (§10.1)
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
            if (key is < 0 or > RowId.MaxDocId)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(keys), key, $"The key of a linked index is its document id: 0 … {RowId.MaxDocId:N0}.");
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
        using var connection = _database.OpenConnection();
        return Guarded(() => Scalar(connection, null, _source.PendingCountSql));
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

            // a key that cannot be a document id is not indexed — it only leaves the outbox (M2)
            if (key is < 0 or > RowId.MaxDocId)
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

    public LuiLinkedSyncReport Sync(CancellationToken cancellationToken = default)
    {
        using var scope = _database.Begin();
        LuiLinkedSyncReport? result = null;
        Guarded(() => Write(null, (connection, transaction) => result = SyncAll(connection, transaction, cancellationToken)));

        // the words of the table as they are now: a built dictionary is built anew
        if (_typos.IsBuilt)
        {
            _typos.Rebuild(cancellationToken);
        }

        return result!;
    }

    /// <summary>Clears the index and fills it again from the table, a chunk of rows at a time, then merges it.</summary>
    private LuiLinkedSyncReport SyncAll(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        Execute(connection, transaction, _source.ClearSql);

        using var end = Command(connection, transaction, _source.ChunkEndSql);
        using var chunk = Command(connection, transaction, _source.SyncChunkSql);
        end.Parameters.AddWithValue("$size", SyncChunk);
        var endFrom = end.Parameters.Add("$from", SqliteType.Integer);
        var from = chunk.Parameters.Add("$from", SqliteType.Integer);
        var to = chunk.Parameters.Add("$to", SqliteType.Integer);

        // from the smallest key on; a key out of range is left out by the statement and counted below
        var last = long.MinValue;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            endFrom.Value = last;
            var upper = end.ExecuteScalar() is long next ? next : long.MaxValue;

            from.Value = last;
            to.Value = upper;
            chunk.ExecuteNonQuery();

            if (upper == long.MaxValue)
            {
                break;
            }

            last = upper;
        }

        Execute(connection, transaction, _source.OptimizeSql);

        using var skipped = Command(connection, transaction, _source.SkippedSql);
        using var reader = skipped.ExecuteReader();
        reader.Read();
        return new LuiLinkedSyncReport
        {
            Indexed = Scalar(connection, transaction, _source.CountSql),
            WithoutText = reader.GetInt64(0),
            KeyOutOfRange = reader.GetInt64(1),
        };
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
        lock (_database.WriterGate)
        {
            // the integrity check of FTS5 is an insert: it runs on the writer, and the transaction is rolled back
            var writer = _database.Writer;
            using var transaction = writer.BeginTransaction(deferred: false);
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

        if (!fits)
        {
            return new LuiIntegrityReport { Problems = problems };
        }

        // the comparison with the table only reads: a snapshot of its own, beside the writers (review of pull request 18, S3)
        using var connection = _database.OpenConnection();
        using var snapshot = connection.BeginTransaction(deferred: true);
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
        Write(null, (connection, transaction) =>
        {
            // a second call replaces them: the definition of the index may have changed the expressions
            Execute(connection, transaction, LinkedSource.DropTriggersSql(Name));
            Execute(connection, transaction, _source.TriggerScript());
        });
    }

    public void DropTriggers()
    {
        using var scope = _database.Begin();
        Write(null, (connection, transaction) => Execute(connection, transaction, LinkedSource.DropTriggersSql(Name)));
    }

    /// <summary>
    /// Runs a write in the transaction of the application, or in one of LUI's own on the catalog's writer — then the
    /// writer's lock is held for the whole of it (one writer per file anyway, §15), and an error or a cancellation
    /// rolls it back.
    /// </summary>
    private void Write(SqliteTransaction? transaction, Action<SqliteConnection, SqliteTransaction> write)
    {
        _database.ThrowIfReadOnly("writing to a linked index");

        if (transaction is not null)
        {
            var connection = transaction.Connection
                ?? throw new ArgumentException("The transaction is complete.", nameof(transaction));
            EnsureSameDatabase(connection, transaction);
            write(connection, transaction);
            return;
        }

        lock (_database.WriterGate)
        {
            using var own = _database.Writer.BeginTransaction(deferred: false);
            write(_database.Writer, own);
            own.Commit();
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

    /// <summary>
    /// A table that changed under the index — a column renamed, the table dropped — answers with a statement that names
    /// what is missing, not with a bare SQLite error (review of pull request 16, S4).
    /// </summary>
    private T Guarded<T>(Func<T> run)
    {
        try
        {
            return run();
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

    private void Guarded(Action run) => Guarded(() =>
    {
        run();
        return true;
    });

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
