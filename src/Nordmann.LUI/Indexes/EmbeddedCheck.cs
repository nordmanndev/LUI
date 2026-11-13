using Microsoft.Data.Sqlite;
using Nordmann.LUI.Schema;
using Nordmann.LUI.Storage;

namespace Nordmann.LUI.Indexes;

/// <summary>
/// The check and the repair of an <c>Embedded</c> index (ADR-0016, ADR-0016, ADR-0046, ADR-0016): against a source, against
/// itself — definition, triggers, FTS5's integrity check against the content table —, and the rebuild of everything
/// below the content table.
/// </summary>
internal sealed class EmbeddedCheck(Database database, EmbeddedSource source, Func<LuiKey, string> textOfKey, Func<object?, byte[]?> payloadBytes)
{
    private const string Repair = "Rebuild() builds the search index anew from the content table.";

    private static long _foundTables;

    /// <summary>
    /// Compares the index with a source in a snapshot of its own, on a connection of its own; changes nothing. The source is
    /// read once and not held: what each key found is noted in a table of the connection's temporary database, the last
    /// entry of a key replacing the one before — it is the one a sync would leave.
    /// </summary>
    internal LuiIntegrityReport Against(IEnumerable<LuiEntry> entries, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entries);

        using var scope = database.Begin();
        using var connection = database.OpenConnection();
        var found = $"lui_check_{Interlocked.Increment(ref _foundTables)}";

        // the temporary database is written without a lock on the file
        Execute(connection, null, EmbeddedSource.CreateFoundSql(found));
        try
        {
            using var transaction = connection.BeginTransaction(deferred: true);
            NoteAll(connection, transaction, found, entries, cancellationToken);

            var (missing, missingKeys) = Finding(connection, transaction, EmbeddedSource.FoundKeysSql(found, EmbeddedSource.FoundMissing));
            var (different, differentKeys) = Finding(connection, transaction, EmbeddedSource.FoundKeysSql(found, EmbeddedSource.FoundDifferent));
            var (extra, extraKeys) = Finding(connection, transaction, source.NotInSourceSql(found, count: true), source.NotInSourceSql(found, count: false));

            List<string> problems = [];
            if (missing > 0)
            {
                problems.Add($"{missing:N0} entries of the source are not in the index ({IndexCheck.Keys(missingKeys, missing)}): Sync(source) adds them.");
            }

            if (extra > 0)
            {
                problems.Add($"{extra:N0} entries of the index are not in the source ({IndexCheck.Keys(extraKeys, extra)}): Sync(source) removes them.");
            }

            if (different > 0)
            {
                problems.Add(
                    $"{different:N0} entries of the index have another text, weight, priority or payload than the source "
                    + $"({IndexCheck.Keys(differentKeys, different)}): Sync(source) writes them.");
            }

            return new LuiIntegrityReport { Problems = problems, Missing = missing, Extra = extra, Different = different };
        }
        finally
        {
            Execute(connection, null, EmbeddedSource.DropTempSql(found));
        }
    }

    /// <summary>Compares every entry of the source and notes what its key found.</summary>
    private void NoteAll(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string found,
        IEnumerable<LuiEntry> entries,
        CancellationToken cancellationToken)
    {
        using var compare = Command(connection, transaction, source.CompareSql);
        using var note = Command(connection, transaction, EmbeddedSource.FoundSql(found));
        var key = compare.Parameters.Add("$key", SqliteType.Text);
        var text = compare.Parameters.Add("$text", SqliteType.Text);
        var weight = compare.Parameters.Add("$weight", SqliteType.Integer);
        var priority = compare.Parameters.Add("$priority", SqliteType.Integer);
        var payload = compare.Parameters.Add("$payload", SqliteType.Blob);
        var noteKey = note.Parameters.Add("$key", SqliteType.Text);
        var noteState = note.Parameters.Add("$state", SqliteType.Integer);

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(entry, nameof(entries));
            var keyText = textOfKey(entry.Key);
            key.Value = keyText;
            text.Value = entry.Text;
            weight.Value = entry.Weight;
            priority.Value = (int)entry.Priority;
            payload.Value = (object?)payloadBytes(entry.Payload) ?? DBNull.Value;

            using (var reader = compare.ExecuteReader())
            {
                noteState.Value = !reader.Read() ? EmbeddedSource.FoundMissing
                    : reader.GetInt64(1) == 1 ? EmbeddedSource.FoundSame
                    : EmbeddedSource.FoundDifferent;
            }

            noteKey.Value = keyText;
            note.ExecuteNonQuery();
        }
    }

    /// <summary>The count of a finding and its first keys: <paramref name="countSql"/> gives the count, <paramref name="keysSql"/> the keys.</summary>
    private static (long Count, List<string> Keys) Finding(SqliteConnection connection, SqliteTransaction transaction, string countSql, string? keysSql = null)
    {
        if (keysSql is null)
        {
            // one statement with both: count(*) OVER () beside the key
            using var both = Command(connection, transaction, countSql);
            both.Parameters.AddWithValue("$n", IndexCheck.Examples);
            using var reader = both.ExecuteReader();
            var keys = new List<string>();
            long count = 0;
            while (reader.Read())
            {
                keys.Add(reader.GetString(0));
                count = reader.GetInt64(1);
            }

            return (count, keys);
        }

        using var counting = Command(connection, transaction, countSql);
        var total = (long)counting.ExecuteScalar()!;
        return (total, Keys(connection, transaction, keysSql));
    }

    /// <summary>
    /// The definition, the triggers and FTS5's integrity check against the content table — on the writer, because the
    /// integrity check is an insert; the transaction is rolled back.
    /// </summary>
    internal LuiIntegrityReport Itself()
    {
        using var scope = database.Begin();
        List<string> problems = [];
        lock (database.WriterGate)
        {
            var writer = database.Writer;
            using var transaction = writer.BeginTransaction(deferred: false);
            if (IndexCheck.Definition(writer, transaction, source, problems))
            {
                Triggers(writer, transaction, problems);
                IndexCheck.SearchIndex(writer, transaction, source, problems, Repair);
            }

            transaction.Rollback();
        }

        return new LuiIntegrityReport { Problems = problems };
    }

    /// <summary>The three triggers anew, the search index anew from the content table, then merged — one transaction.</summary>
    internal void Rebuild()
    {
        using var scope = database.Begin();
        lock (database.WriterGate)
        {
            var writer = database.Writer;
            using var transaction = writer.BeginTransaction(deferred: false);
            Execute(writer, transaction, source.DropTriggersSql);
            foreach (var (_, sql) in source.TriggerStatements())
            {
                Execute(writer, transaction, sql + ";");
            }

            Execute(writer, transaction, source.RebuildSql);
            Execute(writer, transaction, source.OptimizeSql);
            transaction.Commit();
        }
    }

    internal void Optimize()
    {
        using var scope = database.Begin();
        lock (database.WriterGate)
        {
            var writer = database.Writer;
            using var transaction = writer.BeginTransaction(deferred: false);
            Execute(writer, transaction, source.OptimizeSql);
            transaction.Commit();
        }
    }

    /// <summary>The triggers that carry every write into the search index: there, and the ones this version creates.</summary>
    private void Triggers(SqliteConnection connection, SqliteTransaction transaction, List<string> problems)
    {
        var stored = new Dictionary<string, string>(StringComparer.Ordinal);
        using (var command = Command(connection, transaction, source.StoredTriggersSql))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                stored[reader.GetString(0)] = reader.GetString(1);
            }
        }

        var expected = source.TriggerStatements();
        var missing = expected.Where(trigger => !stored.ContainsKey(trigger.Name)).Select(trigger => trigger.Name).ToList();
        var changed = expected
            .Where(trigger => stored.TryGetValue(trigger.Name, out var sql) && !SqlText.Same(sql, trigger.Sql))
            .Select(trigger => trigger.Name)
            .ToList();

        if (missing.Count > 0)
        {
            problems.Add(
                $"The trigger(s) {string.Join(", ", missing)} of '{source.IndexName}' are missing: writes do not reach the search "
                + "index. Rebuild() creates them and builds the search index anew.");
        }

        if (changed.Count > 0)
        {
            problems.Add(
                $"The trigger(s) {string.Join(", ", changed)} of '{source.IndexName}' are not the ones of this version of LUI: "
                + "Rebuild() creates them anew and builds the search index anew.");
        }
    }

    private static List<string> Keys(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = Command(connection, transaction, sql);
        command.Parameters.AddWithValue("$n", IndexCheck.Examples);
        using var reader = command.ExecuteReader();
        var keys = new List<string>();
        while (reader.Read())
        {
            keys.Add(reader.GetString(0));
        }

        return keys;
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        using var command = Command(connection, transaction, sql);
        command.ExecuteNonQuery();
    }

    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return command;
    }
}
