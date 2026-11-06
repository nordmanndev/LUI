using Microsoft.Data.Sqlite;
using Nordmann.LUI.Schema;
using Nordmann.LUI.Storage;
using Nordmann.LUI.Typos;

namespace Nordmann.LUI.Indexes;

/// <summary>
/// The sync of an <c>Embedded</c> index with a source (LUI-Spezifikation §12.4, §14, ADR-0016): batches of entries, each in a
/// transaction of its own on the catalog's writer. A table of the writer's connection holds the keys that were in the
/// index when the sync began; each one the source has is crossed off, and what is left at the end is removed. Then the
/// search index is merged. A key the application adds while the sync runs is never removed.
/// <para>
/// The bulk path (ADR-0010, option 5, measured in <c>EmbeddedBulkExperiment</c>): FTS5 starts a new segment whenever a
/// rowid is smaller than the last one it was given, so each batch is written in the order of the search index —
/// priority, then weight descending; a new entry's document id grows with the order of the inserts —, with FTS5's
/// incremental merge off while it loads, and one merge at the end.
/// </para>
/// </summary>
internal sealed class EmbeddedSync(
    Database database,
    EmbeddedSource source,
    Func<LuiKey, string> keyText,
    Func<object?, byte[]?> payloadBytes,
    TypoDictionary typos)
{
    /// <summary>FTS5's default for <c>automerge</c>: the merge a sync turns off is turned on again with it.</summary>
    private const int DefaultAutomerge = 4;

    private static long _startTables;

    internal LuiSyncReport Run(IEnumerable<LuiEntry> entries, LuiSyncOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(options);

        // a table of its own per run: two syncs on the writer's connection do not share one
        var start = $"lui_sync_{Interlocked.Increment(ref _startTables)}";
        Write(EmbeddedSource.CreateStartSql(start), source.SnapshotSql(start));
        try
        {
            var report = new LuiSyncReport();
            foreach (var chunk in entries.Chunk(options.BatchSize))
            {
                report = WriteBatch(chunk, start, report, cancellationToken);
                options.Progress?.Report(report);
            }

            report = RemoveNotFound(start, options.BatchSize, report, cancellationToken);

            if (report.Added + report.Changed + report.Removed > 0)
            {
                Write(source.OptimizeSql);
                report = report with { Merged = true };
            }

            // The new words are learned already, batch by batch (WriteBatch). A removal or a change takes words out that
            // SymSpell cannot forget: they stay in a dictionary that is built until RebuildTypoDictionary() or a restart —
            // the build of three million entries takes half a minute, and a delta sync should not pay it (ADR-0025).
            options.Progress?.Report(report);
            return report;
        }
        finally
        {
            Forget(start);
        }
    }

    /// <summary>Writes one batch: what is missing or different, in the order of the search index.</summary>
    private LuiSyncReport WriteBatch(LuiEntry[] chunk, string start, LuiSyncReport report, CancellationToken cancellationToken)
    {
        var batch = Prepare(chunk);
        var learned = new List<(string Text, int Weight)>();
        long added = 0, changed = 0, unchanged = 0;

        lock (database.WriterGate)
        {
            var writer = database.Writer;
            using var transaction = writer.BeginTransaction(deferred: false);
            using var statements = new Statements(writer, transaction, source, start);
            Execute(writer, transaction, source.AutomergeSql(0));

            foreach (var (key, entry, payload) in batch)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var stored = statements.Compare(key, entry, payload);
                statements.FoundInSource(key);
                if (stored is true)
                {
                    unchanged++;
                    continue;
                }

                statements.Upsert(key, entry, payload);
                learned.Add((entry.Text, entry.Weight));
                if (stored is null)
                {
                    added++;
                }
                else
                {
                    changed++;
                }
            }

            Execute(writer, transaction, source.AutomergeSql(DefaultAutomerge));
            transaction.Commit();
        }

        // the batch is committed: its words join the typo dictionary, if it is built
        typos.AddRange(learned);

        return report with
        {
            Read = report.Read + chunk.Length,
            Added = report.Added + added,
            Changed = report.Changed + changed,
            Unchanged = report.Unchanged + unchanged,
            Batches = report.Batches + 1,
        };
    }

    /// <summary>
    /// The entries of a batch, each key once — the last one stays, as in <c>UpsertMany</c> — and in the order of the
    /// search index: priority, then weight descending.
    /// </summary>
    private List<(string Key, LuiEntry Entry, byte[]? Payload)> Prepare(LuiEntry[] chunk)
    {
        var last = new Dictionary<string, LuiEntry>(chunk.Length, StringComparer.Ordinal);
        foreach (var entry in chunk)
        {
            ArgumentNullException.ThrowIfNull(entry, "source");
            last[keyText(entry.Key)] = entry;
        }

        return
        [
            .. last
                .OrderBy(pair => (int)pair.Value.Priority)
                .ThenByDescending(pair => pair.Value.Weight)
                .Select(pair => (pair.Key, pair.Value, payloadBytes(pair.Value.Payload))),
        ];
    }

    /// <summary>Removes the keys the source did not have, a batch at a time, in their order.</summary>
    private LuiSyncReport RemoveNotFound(string start, int batchSize, LuiSyncReport report, CancellationToken cancellationToken)
    {
        var after = 0L;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (database.WriterGate)
            {
                var writer = database.Writer;
                using var transaction = writer.BeginTransaction(deferred: false);
                var keys = new List<(long Row, string Key)>(batchSize);
                using (var notFound = Command(writer, transaction, EmbeddedSource.NotFoundSql(start)))
                {
                    notFound.Parameters.AddWithValue("$after", after);
                    notFound.Parameters.AddWithValue("$n", batchSize);
                    using var reader = notFound.ExecuteReader();
                    while (reader.Read())
                    {
                        keys.Add((reader.GetInt64(0), reader.GetString(1)));
                    }
                }

                var removed = 0;
                using var remove = Command(writer, transaction, source.RemoveNotFoundSql);
                var parameter = remove.Parameters.Add("$key", SqliteType.Text);
                foreach (var (row, key) in keys)
                {
                    parameter.Value = key;
                    removed += remove.ExecuteNonQuery();
                    after = row;
                }

                transaction.Commit();
                report = report with { Removed = report.Removed + removed };
                if (keys.Count < batchSize)
                {
                    return report;
                }
            }
        }
    }

    /// <summary>
    /// Drops the table of a sync. A temporary table needs no lock on the file, so no transaction is opened, and a failure
    /// does not cover the exception that ended the sync: the table is the connection's own and has a name of its own, so
    /// a rest of it is harmless (review of pull request 18, N6).
    /// </summary>
    private void Forget(string start)
    {
        try
        {
            lock (database.WriterGate)
            {
                using var drop = database.Writer.CreateCommand();
                drop.CommandText = EmbeddedSource.DropTempSql(start);
                drop.ExecuteNonQuery();
            }
        }
        catch (SqliteException)
        {
            // left behind on the writer's connection until it closes
        }
        catch (ObjectDisposedException)
        {
            // the catalog is gone, and the connection with it
        }
    }

    /// <summary>Statements on the writer, in a transaction of their own.</summary>
    private void Write(params string[] statements)
    {
        lock (database.WriterGate)
        {
            var writer = database.Writer;
            using var transaction = writer.BeginTransaction(deferred: false);
            foreach (var sql in statements)
            {
                Execute(writer, transaction, sql);
            }

            transaction.Commit();
        }
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = Command(connection, transaction, sql);
        command.ExecuteNonQuery();
    }

    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return command;
    }

    /// <summary>The three statements of a batch, prepared once for it: compare, upsert, cross off.</summary>
    private sealed class Statements : IDisposable
    {
        private readonly SqliteCommand _compare;
        private readonly SqliteCommand _upsert;
        private readonly SqliteCommand _found;

        internal Statements(SqliteConnection connection, SqliteTransaction transaction, EmbeddedSource source, string start)
        {
            _compare = Command(connection, transaction, source.CompareSql);
            _upsert = Command(connection, transaction, source.Upsert);
            _found = Command(connection, transaction, EmbeddedSource.FoundInSourceSql(start));
            foreach (var command in new[] { _compare, _upsert })
            {
                command.Parameters.Add("$key", SqliteType.Text);
                command.Parameters.Add("$text", SqliteType.Text);
                command.Parameters.Add("$weight", SqliteType.Integer);
                command.Parameters.Add("$priority", SqliteType.Integer);
                command.Parameters.Add("$payload", SqliteType.Blob);
            }

            _found.Parameters.Add("$key", SqliteType.Text);
        }

        /// <summary>Whether the stored entry of the key is the same; <see langword="null"/> when there is none.</summary>
        internal bool? Compare(string key, LuiEntry entry, byte[]? payload)
        {
            Bind(_compare, key, entry, payload);
            using var reader = _compare.ExecuteReader();
            return reader.Read() ? reader.GetInt64(1) == 1 : null;
        }

        /// <summary>Writes the entry.</summary>
        internal void Upsert(string key, LuiEntry entry, byte[]? payload)
        {
            Bind(_upsert, key, entry, payload);
            _upsert.ExecuteNonQuery();
        }

        internal void FoundInSource(string key)
        {
            _found.Parameters[0].Value = key;
            _found.ExecuteNonQuery();
        }

        public void Dispose()
        {
            _compare.Dispose();
            _upsert.Dispose();
            _found.Dispose();
        }

        private static void Bind(SqliteCommand command, string key, LuiEntry entry, byte[]? payload)
        {
            command.Parameters[0].Value = key;
            command.Parameters[1].Value = entry.Text;
            command.Parameters[2].Value = entry.Weight;
            command.Parameters[3].Value = (int)entry.Priority;
            command.Parameters[4].Value = (object?)payload ?? DBNull.Value;
        }
    }
}
