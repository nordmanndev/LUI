using Microsoft.Data.Sqlite;
using Nordmann.LUI.Querying;
using Nordmann.LUI.Schema;
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

    /// <summary><c>SQLITE_ERROR</c>: among others, a table or a column that is not there.</summary>
    private const int SqliteError = 1;

    private static readonly LuiQueryOptions DefaultOptions = new();

    private readonly LuiCatalog _catalog;
    private readonly LinkedSource _source;
    private readonly TypoDictionary _typos;
    private readonly QueryRunner<long, TPayload> _runner;

    internal LinkedIndex(
        LuiCatalog catalog,
        LinkedSource source,
        PayloadReader<TPayload> payload,
        ILuiSimilarity similarity,
        bool typoTolerance)
    {
        _catalog = catalog;
        _source = source;
        Name = source.IndexName;
        Kind = source.Rules.Kind;
        _typos = catalog.TypoDictionaryFor(Name, source.Normalization, () => TypoSource.Read(catalog, source.TextsSql));
        _runner = new QueryRunner<long, TPayload>(catalog, source, payload, similarity, _typos, typoTolerance);
    }

    public string Name { get; }

    public LuiIndexKind Kind { get; }

    public long Count()
    {
        using var scope = _catalog.Begin();
        using var connection = _catalog.OpenConnection();
        return Scalar(connection, null, _source.CountSql);
    }

    public LuiTriggerState Triggers()
    {
        using var scope = _catalog.Begin();
        using var connection = _catalog.OpenConnection();
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
        using var scope = _catalog.Begin();
        _typos.EnsureBuilt(cancellationToken);
    }

    public void RebuildTypoDictionary(CancellationToken cancellationToken = default)
    {
        using var scope = _catalog.Begin();
        _typos.Rebuild(cancellationToken);
    }

    public bool Refresh(long key, SqliteTransaction? transaction = null) =>
        RefreshMany([key], transaction) == 1;

    public int RefreshMany(IEnumerable<long> keys, SqliteTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);

        // a key that comes twice is refreshed once and counted once (review of pull request 16, N2)
        var distinct = keys.Distinct();

        using var scope = _catalog.Begin();
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
        {
            using var read = Command(connection, inTransaction, _source.ReadRowSql);
            using var refresh = Command(connection, inTransaction, _source.RefreshSql("$id"));
            var id = read.Parameters.Add("$id", SqliteType.Integer);
            var refreshed = refresh.Parameters.Add("$id", SqliteType.Integer);

            foreach (var key in keys)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (key is < 0 or > RowId.MaxDocId)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(keys), key, $"The key of a linked index is its document id: 0 … {RowId.MaxDocId:N0}.");
                }

                id.Value = key;
                using (var reader = read.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        learned.Add((reader.GetString(0), reader.GetInt32(1)));
                    }
                }

                refreshed.Value = key;
                refresh.ExecuteNonQuery();
            }
        }
    }

    public LuiSyncResult Sync(CancellationToken cancellationToken = default)
    {
        using var scope = _catalog.Begin();
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
        using var scope = _catalog.Begin();
        Write(null, (connection, transaction) => Execute(connection, transaction, _source.OptimizeSql));
    }

    public string TriggerScript() => _source.TriggerScript();

    public void CreateTriggers()
    {
        using var scope = _catalog.Begin();
        Write(null, (connection, transaction) =>
        {
            // a second call replaces them: the definition of the index may have changed the expressions
            Execute(connection, transaction, LinkedSource.DropTriggersSql(Name));
            Execute(connection, transaction, _source.TriggerScript());
        });
    }

    public void DropTriggers()
    {
        using var scope = _catalog.Begin();
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

        lock (_catalog.WriterGate)
        {
            using var own = _catalog.Writer.BeginTransaction(deferred: false);
            write(_catalog.Writer, own);
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
}
