using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Nordmann.LUI.Schema;
using Nordmann.LUI.Storage;

namespace Nordmann.LUI.Indexes;

/// <summary>
/// The sync of a <c>Linked</c> index with the table of the application (ADR-0077, extending ADR-0016): the rows are read in the order of
/// their key, <see cref="LuiSyncOptions.BatchSize"/> at a time, and each batch is written in a transaction of its own — added or
/// replaced by key, with the generation of the sync. There is no clear at the start: a search during the sync sees the old state plus
/// the batches that are committed, never an empty index. Then the entries that still carry an older generation are removed, in batches
/// too, and the search index is merged in steps; the transaction of the last step clears <c>NeedsInitialSync</c>.
/// <para>
/// <b>One sync at a time.</b> The run takes the <b>lease</b> of the index — a row of <c>lui_meta</c>: an owner and the time it expires — in
/// a short transaction of its own, and renews it in <i>every</i> transaction, in the same transaction that checks the guard and does the
/// work: a run that lost its lease finds out at its next transaction, before it writes, and stops. A lease of a run that died expires on
/// its own. The run releases it in <c>finally</c>, and only its own.
/// </para>
/// <para>
/// A run that is cancelled or fails keeps the batches it committed, removes nothing that was not removed yet, does not finish the merge,
/// and leaves <c>NeedsInitialSync</c> as it was. Running it again is always correct: it takes the next generation and writes every row
/// again.
/// </para>
/// </summary>
internal sealed class LinkedSync
{
    /// <summary>
    /// The pages of FTS5 that one step of the merge merges. FTS5 counts in pages of about 4 KB of the search index, and a step that
    /// merges 4,096 of them wrote some 16 MB — about the work of one batch of the default size, the order the step has to stay in so that
    /// no step holds the write lock for much longer than a batch does. Chosen from a probe on a trigram index (400,000 rows: 4,096 pages
    /// took 130–190 ms, a batch of 10,000 rows about 700 ms), and checked by <c>LongestLock</c> of the million-row run of #275 — the number
    /// is measured again there.
    /// </summary>
    internal const int DefaultMergePages = 4_096;

    /// <summary>How long a lease is good for without being renewed — a crashed process holds its index for no longer than this.</summary>
    internal static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(60);

    /// <summary>FTS5's default for <c>automerge</c>: the merge a batch turns off while it loads is turned on again with it.</summary>
    private const int DefaultAutomerge = 4;

    private static long _runs;

    private readonly Database _database;
    private readonly LinkedSource _source;
    private readonly IndexGuard? _guard;
    private readonly string _owner;
    private readonly string _name;

    private long _generation;
    private bool _taken;
    private bool _released;
    private TimeSpan _longestLock;
    private int _batches;
    private long _read;
    private long _withoutText;
    private long _outOfRange;
    private long _removed;
    private long _indexed;

    internal LinkedSync(Database database, LinkedSource source, IndexGuard? guard)
    {
        _database = database;
        _source = source;
        _guard = guard;
        _name = source.IndexName;

        // who holds the lease, as the exception that refuses a second sync names it: the process, the catalog, and the run — two syncs of
        // one catalog must not take each other for the owner
        _owner = $"pid{Environment.ProcessId}-{database.InstanceId}-{Interlocked.Increment(ref _runs)}";
    }

    internal LuiLinkedSyncReport Run(LuiSyncOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            // one connection for the whole run, as the sync of an embedded index has it (ADR-0070): the pool is not asked again for every batch
            using var writer = _database.OpenConnection();
            try
            {
                Take(writer);
                WriteBatches(writer, options, cancellationToken);
                RemoveWhatIsOld(writer, options, cancellationToken);
                Merge(writer, options, cancellationToken);
            }
            finally
            {
                Release(writer);
            }
        }
        catch (SqliteException exception) when (Database.IsReadOnlyRefusal(exception))
        {
            throw _database.ReadOnlyRefusal("writing to a linked index", _name, exception);
        }

        return Report();
    }

    // ---- the lease and the transaction -------------------------------------------------------------------------------

    /// <summary>
    /// The first transaction: the lease and the generation. A lease that is held and not expired is refused — the sync does not wait, and
    /// it has written nothing; one that is free, or expired, is taken, and the generation of this run is the next one.
    /// </summary>
    private void Take(SqliteConnection writer) =>
        InTransaction(writer, "lease", ownsTheLease: false, (connection, transaction) =>
        {
            var now = Now();
            using (var take = Command(connection, transaction, SyncLease.TakeSql))
            {
                take.Parameters.AddWithValue("$name", _name);
                take.Parameters.AddWithValue("$owner", _owner);
                take.Parameters.AddWithValue("$now", now);
                take.Parameters.AddWithValue("$expires", now + (long)LeaseDuration.TotalMilliseconds);

                // the check and the take are one statement, in the transaction that holds the write lock: no one slips in between
                if (take.ExecuteNonQuery() == 0)
                {
                    throw Refused(connection, transaction, lost: false);
                }
            }

            _taken = true;
            using var generation = Command(connection, transaction, SyncLease.GenerationSql);
            generation.Parameters.AddWithValue("$name", _name);
            _generation = (long)generation.ExecuteScalar()!;
        });

    /// <summary>
    /// Runs <paramref name="work"/> in a write transaction on the connection of the run, under the write lock of LUI's own writes, and
    /// tells the seam of the tests once it has committed. <b>The check and the work are one step</b> (ADR-0075): the guard is asked in the
    /// transaction, and so is the lease — a run that is not the owner of the lease any more stops here, before its first statement, and
    /// the lease is renewed again before the commit, so that a long batch does not leave the lease at the time the batch began.
    /// </summary>
    private T InTransaction<T>(
        SqliteConnection writer,
        string phase,
        bool ownsTheLease,
        Func<SqliteConnection, SqliteTransaction, T> work)
    {
        T result;
        lock (_database.WriterGate)
        {
            ResetTheLog(writer);
            using var transaction = writer.BeginTransaction(deferred: false);

            // the lock of the file is held from here: this is the time another writer waits for
            var held = Stopwatch.StartNew();
            _guard?.Check(writer, transaction);
            if (ownsTheLease)
            {
                Renew(writer, transaction, throwWhenLost: true);
            }

            result = work(writer, transaction);
            if (ownsTheLease)
            {
                Renew(writer, transaction, throwWhenLost: false);
            }

            transaction.Commit();
            if (held.Elapsed > _longestLock)
            {
                _longestLock = held.Elapsed;
            }
        }

        _database.AfterSyncTransaction?.Invoke(phase);
        return result;
    }

    private void InTransaction(SqliteConnection writer, string phase, bool ownsTheLease, Action<SqliteConnection, SqliteTransaction> work) =>
        InTransaction(writer, phase, ownsTheLease, (connection, transaction) =>
        {
            work(connection, transaction);
            return true;
        });

    /// <summary>
    /// Gives the write-ahead log back before a transaction of the sync begins (ADR-0077): a checkpoint that backfills the log and truncates it.
    /// SQLite starts a log over only when the connection that begins to write has read nothing from it — every frame is backfilled — and no
    /// reader holds it; the checkpoint at the commit of the transaction before is not enough once another writer has committed between that
    /// checkpoint and this begin, or a reader met it: then the log is not started over, the transaction appends to it, and beside a busy
    /// application the file grows by a batch at a time. Done right before the begin, in the same hold of the gate, the window is a few
    /// microseconds. <b>Best effort and never a wait</b>: the busy timeout is 0 for this one statement — a reader, or a writer, that is in the way
    /// leaves the log as it is and the sync goes on; a long reader of the application is not made to hold up a sync by five seconds a batch.
    /// A file that is not in write-ahead logging has nothing to do.
    /// </summary>
    private void ResetTheLog(SqliteConnection writer)
    {
        try
        {
            Execute(writer, null, "PRAGMA busy_timeout = 0;");
            try
            {
                using var checkpoint = writer.CreateCommand();
                checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                using var result = checkpoint.ExecuteReader();
                while (result.Read())
                {
                    // (busy, frames in the log, frames backfilled): a busy 1 is the answer "not now" — nothing else is asked of it
                }
            }
            finally
            {
                Execute(writer, null, $"PRAGMA busy_timeout = {_database.BusyTimeoutMilliseconds};");
            }
        }
        catch (SqliteException)
        {
            // the log is somebody else's business this time: a transaction that begins and finds the lock busy says so itself
        }
    }

    /// <summary>
    /// Sets the expiry of the lease to a lease duration from now, if the run still owns it — one statement that checks and renews. The first
    /// call of a transaction throws when the run does not own it; the one before the commit leaves the answer to the next transaction.
    /// </summary>
    private void Renew(SqliteConnection connection, SqliteTransaction transaction, bool throwWhenLost)
    {
        using var renew = Command(connection, transaction, SyncLease.RenewSql);
        renew.Parameters.AddWithValue("$name", _name);
        renew.Parameters.AddWithValue("$owner", _owner);
        renew.Parameters.AddWithValue("$expires", Now() + (long)LeaseDuration.TotalMilliseconds);
        if (renew.ExecuteNonQuery() == 0 && throwWhenLost)
        {
            throw Refused(connection, transaction, lost: true);
        }
    }

    /// <summary>
    /// The exception for a lease that is not the run's: the holder as the transaction sees it. An index that is not there any more is the
    /// exception of the guard — it names the index — and not a lease.
    /// </summary>
    private LuiException Refused(SqliteConnection connection, SqliteTransaction transaction, bool lost)
    {
        using var read = Command(connection, transaction, SyncLease.ReadSql);
        read.Parameters.AddWithValue("$name", _name);
        using var reader = read.ExecuteReader();
        if (!reader.Read())
        {
            return new LuiIndexNotFoundException(_name);
        }

        var owner = reader.IsDBNull(0) ? null : reader.GetString(0);
        DateTimeOffset? expires = owner is null || reader.IsDBNull(1) ? null : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1));
        return new LuiSyncInProgressException(_name, owner, expires, lost);
    }

    /// <summary>
    /// Gives the lease back, if the run still holds it: <c>finally</c> of the run, also after a cancellation and a failure. A lease that
    /// cannot be released — the file is busy, the catalog is gone — expires on its own; that is no reason to hide the exception that ended
    /// the sync.
    /// </summary>
    private void Release(SqliteConnection writer)
    {
        if (!_taken || _released)
        {
            return;
        }

        try
        {
            lock (_database.WriterGate)
            {
                using var transaction = writer.BeginTransaction(deferred: false);
                ReleaseIn(writer, transaction);
                transaction.Commit();
                _released = true;
            }
        }
        catch (Exception exception) when (exception is SqliteException or InvalidOperationException or ObjectDisposedException)
        {
            // it expires: a sync that does not renew it loses it after LeaseDuration
        }
    }

    private void ReleaseIn(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var release = Command(connection, transaction, SyncLease.ReleaseSql);
        release.Parameters.AddWithValue("$name", _name);
        release.Parameters.AddWithValue("$owner", _owner);
        release.ExecuteNonQuery();
    }

    private long Now() => _database.TimeProvider.GetUtcNow().ToUnixTimeMilliseconds();

    // ---- the batches -----------------------------------------------------------------------------------------------

    private void WriteBatches(SqliteConnection writer, LuiSyncOptions options, CancellationToken cancellationToken)
    {
        // from the smallest key on, a batch of rows of the table at a time; a key out of range is left out by the statements and counted
        var last = long.MinValue;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = InTransaction(writer, "batch", ownsTheLease: true, (connection, transaction) => WriteBatch(connection, transaction, last, options.BatchSize, cancellationToken));

            if (batch.Read > 0)
            {
                _batches++;
                _read += batch.Read;
                _withoutText += batch.WithoutText;
                _outOfRange += batch.OutOfRange;
                options.Progress?.Report(Progress(merged: false));
            }

            if (batch.Upper is not { } upper)
            {
                return;
            }

            last = upper;
        }
    }

    /// <summary>One batch: the rows with a key in <c>(from, upper]</c> — <c>upper</c> is the key that ends a batch of <paramref name="size"/> rows.</summary>
    private BatchResult WriteBatch(SqliteConnection connection, SqliteTransaction transaction, long from, int size, CancellationToken cancellationToken)
    {
        // the key that ends the batch, if the table has more rows than the batch — and the one after it, to know that there is a next batch
        long? upper = null;
        using (var end = Command(connection, transaction, _source.ChunkEndSql))
        {
            end.Parameters.AddWithValue("$from", from);
            end.Parameters.AddWithValue("$size", size);
            using var keys = end.ExecuteReader();
            if (keys.Read())
            {
                var boundary = keys.GetInt64(0);
                upper = keys.Read() ? boundary : null;
            }
        }

        var to = upper ?? long.MaxValue;
        long read, withoutText, outOfRange;
        using (var rows = Command(connection, transaction, _source.BatchRowsSql))
        {
            rows.Parameters.AddWithValue("$from", from);
            rows.Parameters.AddWithValue("$to", to);
            using var reader = rows.ExecuteReader();
            reader.Read();
            (read, withoutText, outOfRange) = (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2));
        }

        // a batch that read no row still runs: the range can hold the entry that a refresh wrote for a row that is gone now
        // FTS5 does not merge while the batch loads — one merge at the end, in steps — and the setting is back before the commit
        Execute(connection, transaction, _source.AutomergeSql(0));
        foreach (var sql in new[] { _source.BatchDropUnreachedFtsSql, _source.BatchDropUnreachedEntriesSql, _source.BatchDropReplacedSql, _source.BatchEntriesSql, _source.BatchSearchSql, _source.BatchForgetPendingSql })
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var statement = Command(connection, transaction, sql);
            statement.Parameters.AddWithValue("$from", from);
            statement.Parameters.AddWithValue("$to", to);
            if (sql.Contains("$gen", StringComparison.Ordinal))
            {
                statement.Parameters.AddWithValue("$gen", _generation);
            }

            statement.ExecuteNonQuery();
        }

        Execute(connection, transaction, _source.AutomergeSql(DefaultAutomerge));
        return new BatchResult(upper, read, withoutText, outOfRange);
    }

    // ---- the removal ------------------------------------------------------------------------------------------------

    /// <summary>
    /// Removes the entries that carry an older generation than this sync's — the rows that are in the index and not in the table any more
    /// — in batches of <see cref="LuiSyncOptions.BatchSize"/>, each in a transaction of its own. It starts when every batch of rows is
    /// written: until then nothing of the previous state goes, so a sync that is cancelled leaves the index as it was plus what it wrote.
    /// </summary>
    private void RemoveWhatIsOld(SqliteConnection writer, LuiSyncOptions options, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var removed = InTransaction(writer, "removal", ownsTheLease: true, (connection, transaction) => RemoveBatch(connection, transaction, options.BatchSize, cancellationToken));
            _removed += removed;
            if (removed > 0)
            {
                options.Progress?.Report(Progress(merged: false));
            }

            if (removed < options.BatchSize)
            {
                return;
            }
        }
    }

    private int RemoveBatch(SqliteConnection connection, SqliteTransaction transaction, int size, CancellationToken cancellationToken)
    {
        // a delete writes into the search index, and with FTS5's own merge on, the commit of the batch can merge the segments of the whole
        // index in this one transaction: it is off while the sync runs — the removal included — and back on before the commit; the merge is
        // the business of the steps at the end
        Execute(connection, transaction, _source.AutomergeSql(0));
        var stale = new List<long>(Math.Min(size, 10_000));
        using (var select = Command(connection, transaction, _source.StaleEntriesSql))
        {
            select.Parameters.AddWithValue("$gen", _generation);
            select.Parameters.AddWithValue("$n", size);
            using var reader = select.ExecuteReader();
            while (reader.Read())
            {
                stale.Add(reader.GetInt64(0));
            }
        }

        using var remove = Command(connection, transaction, _source.RemoveStaleSql);
        var id = remove.Parameters.Add("$id", SqliteType.Integer);
        foreach (var key in stale)
        {
            cancellationToken.ThrowIfCancellationRequested();
            id.Value = key;
            remove.ExecuteNonQuery();
        }

        Execute(connection, transaction, _source.AutomergeSql(DefaultAutomerge));
        return stale.Count;
    }

    // ---- the merge --------------------------------------------------------------------------------------------------

    /// <summary>
    /// Merges the search index in steps, each in a transaction of its own, until a step reports that nothing is left — instead of one
    /// <c>optimize</c>, which over a large index is one long write transaction. The step that finds nothing left is the last one of the
    /// sync: in its transaction the sync is complete — <c>NeedsInitialSync</c> is cleared, the lease is released — or none of it is.
    /// </summary>
    private void Merge(SqliteConnection writer, LuiSyncOptions options, CancellationToken cancellationToken)
    {
        var pages = _database.MergePages ?? DefaultMergePages;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var nothingLeft = InTransaction(writer, "merge", ownsTheLease: true, (connection, transaction) => MergeStep(connection, transaction, pages));
            if (nothingLeft)
            {
                // the commit of the last step has gone through: the lease went with it (set before it, a failing commit would leave the lease to expire)
                _released = true;
                options.Progress?.Report(Progress(merged: true));
                return;
            }
        }
    }

    private bool MergeStep(SqliteConnection connection, SqliteTransaction transaction, int pages)
    {
        // FTS5 does its work in its own tables: what it changed shows in total_changes(), not in changes() (LinkedMergeStepTests)
        var before = Total(connection, transaction);
        using (var merge = Command(connection, transaction, _source.MergeStepSql))
        {
            merge.Parameters.AddWithValue("$pages", pages);
            merge.ExecuteNonQuery();
        }

        if (Total(connection, transaction) - before > 1)
        {
            return false;
        }

        // nothing left: the last step. The sync is complete in this transaction, and the lease goes with it
        IndexStore.MarkSynced(connection, transaction, _name);
        using (var count = Command(connection, transaction, _source.CountSql))
        {
            _indexed = (long)count.ExecuteScalar()!;
        }

        ReleaseIn(connection, transaction);
        return true;
    }

    private static long Total(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var total = Command(connection, transaction, LinkedSource.TotalChangesSql);
        return (long)total.ExecuteScalar()!;
    }

    // ---- what it did ------------------------------------------------------------------------------------------------

    private LuiSyncReport Progress(bool merged) => new()
    {
        Read = _read,
        Batches = _batches,
        Removed = _removed,
        Merged = merged,
    };

    private LuiLinkedSyncReport Report() => new()
    {
        Indexed = _indexed,
        WithoutText = _withoutText,
        KeyOutOfRange = _outOfRange,
        Batches = _batches,
        Removed = _removed,
        LongestLock = _longestLock,
    };

    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return command;
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        using var command = Command(connection, transaction, sql);
        command.ExecuteNonQuery();
    }

    /// <summary>What a batch found: the key that ends it (none for the last one), the rows it read, and the rows it left out.</summary>
    private sealed record BatchResult(long? Upper, long Read, long WithoutText, long OutOfRange);
}
