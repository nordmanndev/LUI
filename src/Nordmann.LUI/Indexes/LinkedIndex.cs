using Microsoft.Data.Sqlite;
using Nordmann.LUI.Querying;
using Nordmann.LUI.Schema;
using Nordmann.LUI.Storage;
using Nordmann.LUI.Typos;

namespace Nordmann.LUI.Indexes;

/// <summary>
/// An index over a table of the application (LUI-Spezifikation §5, §7.3, ADR-0015). It writes only its own tables;
/// what it indexes it reads from the application's table, by the statements of its <see cref="LinkedSource"/>.
/// </summary>
/// <typeparam name="TPayload">The payload that comes back with a hit.</typeparam>
#pragma warning disable CA1001 // see TypoDictionary: the lock of the dictionary needs no disposal, and an index lives as long as its catalog
internal sealed class LinkedIndex<TPayload> : ILuiLinkedIndex<TPayload>
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
    private readonly QueryRunner<long, TPayload> _runner;

    internal LinkedIndex(
        Database database,
        TypoDictionaries typoDictionaries,
        LinkedSource source,
        PayloadReader<TPayload> payload,
        ILuiSimilarity similarity,
        bool typoTolerance)
    {
        _database = database;
        _source = source;
        Name = source.IndexName;
        Kind = source.Rules.Kind;
        _typos = typoDictionaries.For(Name, source.Normalization, () => TypoSource.Read(database, source.TextsSql));
        _runner = new QueryRunner<long, TPayload>(database, source, payload, similarity, _typos, typoTolerance);
    }

    public string Name { get; }

    public LuiIndexKind Kind { get; }

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
        using var command = connection.CreateCommand();
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
            && expected.All(trigger => stored.TryGetValue(trigger.Name, out var sql) && SameStatement(sql, trigger.Sql))
            ? LuiTriggerState.Current
            : LuiTriggerState.Stale;
    }

    public LuiQuery<long, TPayload> Query(string text) => new(this, text);

    public LuiResult<long, TPayload> Search(
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

    public LuiSyncResult Sync(CancellationToken cancellationToken = default)
    {
        using var scope = _database.Begin();
        LuiSyncResult? result = null;
        Guarded(() => Write(null, (connection, transaction) => result = SyncAll(connection, transaction, cancellationToken)));

        // the words of the table as they are now: a built dictionary is built anew
        if (_typos.IsBuilt)
        {
            _typos.Rebuild(cancellationToken);
        }

        return result!;
    }

    /// <summary>Clears the index and fills it again from the table, a chunk of rows at a time, then merges it.</summary>
    private LuiSyncResult SyncAll(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
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
        return new LuiSyncResult
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
                $"The table '{_source.Link.Table}' does not fit the index '{Name}' any more ({exception.Message}). Ensure the index "
                + "again to see what is missing; a changed table is a new definition: drop the index and ensure it anew.",
                exception);
        }
    }

    private void Guarded(Action run) => Guarded(() =>
    {
        run();
        return true;
    });

    /// <summary>Two statements are the same when they differ only in white space and a closing semicolon.</summary>
    private static bool SameStatement(string stored, string expected) =>
        string.Equals(Collapse(stored), Collapse(expected), StringComparison.Ordinal);

    private static string Collapse(string sql) =>
        string.Join(' ', sql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).TrimEnd(';').TrimEnd();

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
