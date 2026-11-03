using System.Diagnostics;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Nordmann.LUI.Normalization;
using Nordmann.LUI.Querying;
using Nordmann.LUI.Schema;
using Shouldly;
using Xunit.Abstractions;

namespace Nordmann.LUI.Tests;

/// <summary>
/// The spike of PR-006 (issue #15): can a <c>Linked</c> index live without a column of the application's table and
/// without a copy of its text? The layout under test: a <b>contentless</b> FTS5 table (<c>content=''</c>,
/// <c>contentless_delete=1</c>) over the search form, and a small table of LUI without text (doc_id, packed rowid,
/// the two prefix keys); the text is read from the application's table by its primary key. It is measured against
/// <c>Owned</c> on the same stock: the same candidates for every input, the plan, the time of the candidate and the
/// prefix statements, the size of the file.
/// <code>LUI_PERF=1 dotnet test --filter LinkedLayoutExperiment --logger "console;verbosity=detailed"</code>
/// </summary>
public sealed class LinkedLayoutExperiment(ITestOutputHelper output)
{
    private const int Queries = 2_000;

    [PerformanceFact]
    public void A_contentless_index_over_the_application_table_against_owned()
    {
        var entries = int.Parse(Environment.GetEnvironmentVariable("LUI_PERF_ENTRIES") ?? "200000", CultureInfo.InvariantCulture);
        var stock = Stock(entries);

        using var ownedDatabase = new TestDatabase();
        using var linkedDatabase = new TestDatabase();
        using var catalog = ownedDatabase.Open(new LuiCatalogOptions { CacheSizeMegabytes = 512 });

        // both files hold the application's table; the owned one adds its index with a copy of the text
        CreateHost(ownedDatabase, stock);
        CreateHost(linkedDatabase, stock);

        var build = Stopwatch.StartNew();
        var owned = catalog.Index("tags").SingleWord().Owned<long>().Ensure();
        owned.UpsertMany(stock.Select(e => new LuiEntry<long, LuiNoPayload>(e.Id, e.Text) { Weight = e.Weight }));
        Report($"owned:  build {build.Elapsed.TotalSeconds:F1} s");

        using var linked = linkedDatabase.Connect();
        Pragmas(linked);
        build.Restart();
        CreateLinked(linked, stock);
        Report($"linked: build {build.Elapsed.TotalSeconds:F1} s");

        using var ownedConnection = ownedDatabase.Connect();
        Pragmas(ownedConnection);
        Checkpoint(ownedConnection);
        Checkpoint(linked);
        Report($"file: owned {Megabytes(ownedDatabase)} MB, linked {Megabytes(linkedDatabase)} MB, application table alone {HostMegabytes(stock)} MB");

        var source = new OwnedSource("tags", new IndexDefinition(LuiIndexKind.SingleWord, OwnedSource.SourceName, "long", true, false, TokenizerFold.Version));
        var inputs = Inputs(stock, new Random(7));

        Plan(linked, LinkedCandidateSql, "$expr", "s_norm : \"gan\"").ShouldContain(line => line.Contains("VIRTUAL TABLE INDEX 64", StringComparison.Ordinal));
        Report("plan (linked candidates): " + string.Join(" | ", Plan(linked, LinkedCandidateSql, "$expr", "s_norm : \"gan\"")));
        Report("plan (linked prefix):     " + string.Join(" | ", Plan(linked, LinkedPrefixSql(1), "$q", "a")));

        Compare(ownedConnection, source, linked, inputs, "fresh");

        ApplyWrites(owned, linked, stock);

        Compare(ownedConnection, source, linked, inputs, "after writes");

        Execute(linked, "INSERT INTO lui_t_fts(lui_t_fts) VALUES ('integrity-check');");
        Execute(linked, "INSERT INTO lui_t_fts(lui_t_fts) VALUES ('optimize');");
        Execute(ownedConnection, "INSERT INTO lui_tags_fts(lui_tags_fts) VALUES ('optimize');");
        Report("linked: integrity-check and optimize pass");

        Compare(ownedConnection, source, linked, inputs, "both optimized");
        Compare(ownedConnection, source, linked, inputs, "both optimized, again");
    }

    /// <summary>Removes a thousand entries and changes the weight of a thousand, in both layouts — contentless_delete by rowid alone.</summary>
    private void ApplyWrites(ILuiOwnedIndex<long, LuiNoPayload> owned, SqliteConnection linked, List<Entry> stock)
    {
        var random = new Random(3);
        var changed = Enumerable.Range(0, 2_000).Select(_ => stock[random.Next(stock.Count)]).DistinctBy(e => e.Id).ToList();
        var write = Stopwatch.StartNew();
        using (var transaction = linked.BeginTransaction())
        {
            for (var i = 0; i < changed.Count; i++)
            {
                var entry = changed[i];
                LinkedRemove(linked, transaction, entry.Id);
                if (i % 2 == 1)
                {
                    LinkedInsert(linked, transaction, entry with { Weight = entry.Weight + 17 });
                }
            }

            transaction.Commit();
        }

        Report($"linked: {changed.Count:N0} removals and {changed.Count / 2:N0} re-inserts in {write.Elapsed.TotalMilliseconds:F0} ms");

        for (var i = 0; i < changed.Count; i++)
        {
            var entry = changed[i];
            if (i % 2 == 1)
            {
                owned.UpdateWeight(entry.Id, entry.Weight + 17);
            }
            else
            {
                owned.Remove(entry.Id);
            }
        }

    }

    private const string LinkedCandidateSql =
        "SELECT f.rowid, h.name FROM lui_t_fts f JOIN tag h ON h.id = (f.rowid & 4294967295) "
        + "WHERE lui_t_fts MATCH $expr ORDER BY f.rowid LIMIT $n;";

    private static string LinkedPrefixSql(int length) =>
        $"SELECT s.s_rowid, h.name FROM lui_t s JOIN tag h ON h.id = s.doc_id WHERE s.s_p{length} = $q ORDER BY s.s_rowid LIMIT $n;";

    /// <summary>For every input: the same candidates in both layouts, and the time of both statements, alternating.</summary>
    private void Compare(SqliteConnection owned, OwnedSource source, SqliteConnection linked, List<string> inputs, string stage)
    {
        var ownedTimes = new List<double>();
        var linkedTimes = new List<double>();
        var different = 0;

        foreach (var input in inputs)
        {
            var query = PreparedQuery.Create(input, NormalizationOptions.Default, SingleWordRules.Instance);
            if (query.IsEmpty)
            {
                continue;
            }

            var (ownedSql, linkedSql, name, value) = query.IsPrefixPath
                ? (source.PrefixSql(query.PrefixLength), LinkedPrefixSql(query.PrefixLength), "$q", query.PrefixKey)
                : (source.CandidateSql, LinkedCandidateSql, "$expr", query.Expression);

            var started = Stopwatch.GetTimestamp();
            var fromOwned = Read(owned, ownedSql, name, value);
            ownedTimes.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);

            started = Stopwatch.GetTimestamp();
            var fromLinked = Read(linked, linkedSql, name, value);
            linkedTimes.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);

            if (!fromOwned.SequenceEqual(fromLinked))
            {
                different++;
            }
        }

        Report($"{stage}: {ownedTimes.Count:N0} inputs, different candidates: {different}; "
            + $"owned p50 {Percentile(ownedTimes, 50):F2} / p95 {Percentile(ownedTimes, 95):F2} ms, "
            + $"linked p50 {Percentile(linkedTimes, 50):F2} / p95 {Percentile(linkedTimes, 95):F2} ms");
        different.ShouldBe(0);
    }

    private static List<(long RowId, string Text)> Read(SqliteConnection connection, string sql, string name, string value)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue(name, value);
        command.Parameters.AddWithValue("$n", 501);
        var rows = new List<(long, string)>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add((reader.GetInt64(0), reader.GetString(1)));
        }

        return rows;
    }

    private static void CreateHost(TestDatabase database, List<Entry> stock)
    {
        using var connection = database.Connect();
        Execute(connection, "PRAGMA journal_mode = WAL; CREATE TABLE tag (id INTEGER PRIMARY KEY, name TEXT NOT NULL, usage_count INTEGER NOT NULL);");
        using var transaction = connection.BeginTransaction();
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = "INSERT INTO tag(id, name, usage_count) VALUES ($id, $name, $weight);";
        var id = insert.Parameters.Add("$id", SqliteType.Integer);
        var name = insert.Parameters.Add("$name", SqliteType.Text);
        var weight = insert.Parameters.Add("$weight", SqliteType.Integer);
        foreach (var entry in stock)
        {
            id.Value = entry.Id;
            name.Value = entry.Text;
            weight.Value = entry.Weight;
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private static void CreateLinked(SqliteConnection connection, List<Entry> stock)
    {
        Execute(connection, """
            CREATE TABLE lui_t (
                doc_id  INTEGER PRIMARY KEY,
                s_rowid INTEGER NOT NULL UNIQUE,
                s_p1    TEXT NOT NULL,
                s_p2    TEXT NOT NULL);
            CREATE INDEX lui_t_p1 ON lui_t(s_p1, s_rowid);
            CREATE INDEX lui_t_p2 ON lui_t(s_p2, s_rowid);
            CREATE VIRTUAL TABLE lui_t_fts USING fts5(
                s_norm, content = '', contentless_delete = 1,
                tokenize = 'trigram remove_diacritics 1');
            """);

        using var transaction = connection.BeginTransaction();
        foreach (var entry in stock)
        {
            LinkedInsert(connection, transaction, entry);
        }

        transaction.Commit();
    }

    // the forms are computed by the same SQL expressions as the generated columns of Owned
    private static readonly string Norm = SqlNormalizer.Transliteration("$text");

    private static void LinkedInsert(SqliteConnection connection, SqliteTransaction transaction, Entry entry)
    {
        var rowId = RowId.Encode(LuiPriority.Normal, entry.Weight, entry.Id);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"INSERT INTO lui_t(doc_id, s_rowid, s_p1, s_p2) VALUES ($id, $rowid, "
            + $"substr(lower(replace({Norm}, '#', '')), 1, 1), substr(lower(replace({Norm}, '#', '')), 1, 2)); "
            + $"INSERT INTO lui_t_fts(rowid, s_norm) VALUES ($rowid, {Norm});";
        command.Parameters.AddWithValue("$id", entry.Id);
        command.Parameters.AddWithValue("$rowid", rowId);
        command.Parameters.AddWithValue("$text", entry.Text);
        command.ExecuteNonQuery();
    }

    private static void LinkedRemove(SqliteConnection connection, SqliteTransaction transaction, long id)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "DELETE FROM lui_t_fts WHERE rowid = (SELECT s_rowid FROM lui_t WHERE doc_id = $id); "
            + "DELETE FROM lui_t WHERE doc_id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    private static List<string> Plan(SqliteConnection connection, string sql, string name, string value)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "EXPLAIN QUERY PLAN " + sql;
        command.Parameters.AddWithValue(name, value);
        command.Parameters.AddWithValue("$n", 501);
        var lines = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            lines.Add(reader.GetString(3));
        }

        return lines;
    }

    private static void Pragmas(SqliteConnection connection) =>
        Execute(connection, "PRAGMA cache_size = -524288; PRAGMA synchronous = NORMAL;");

    private static void Checkpoint(SqliteConnection connection) => Execute(connection, "PRAGMA wal_checkpoint(TRUNCATE);");

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static long Megabytes(TestDatabase database) => new FileInfo(database.FilePath).Length / 1_048_576;

    private static long HostMegabytes(List<Entry> stock)
    {
        using var database = new TestDatabase();
        CreateHost(database, stock);
        using var connection = database.Connect();
        Checkpoint(connection);
        return Megabytes(database);
    }

    /// <summary>The stock of <c>QueryPerformance</c>: the words, then compounds of two, with a long tail of weights.</summary>
    private List<Entry> Stock(int entries)
    {
        var root = Testing.Repository.RootPath;
        var full = Path.Combine(root, "artifacts", "wordlist", "german-words.txt");
        var path = File.Exists(full) ? full : Path.Combine(root, "tests", "Nordmann.LUI.TestData", "wordlist", "german-words.sample.txt");
        List<string> words = [.. File.ReadLines(path).Where(w => w.Length >= 3)];
        Report($"corpus: {Path.GetRelativePath(root, path)} ({words.Count:N0} words), {entries:N0} entries");

        var random = new Random(42);
        var stock = new List<Entry>(entries);
        for (var i = 1; i <= entries; i++)
        {
            var text = i <= words.Count
                ? words[i - 1]
                : char.ToUpperInvariant(words[random.Next(words.Count)][0]) + words[random.Next(words.Count)][1..] + words[random.Next(words.Count)].ToLowerInvariant();
            var weight = (int)Math.Min(536_870_000, 1 + (long)(random.NextDouble() * random.NextDouble() * random.NextDouble() * 1_000_000));
            stock.Add(new Entry(i, text, weight));
        }

        return stock;
    }

    private static List<string> Inputs(List<Entry> stock, Random random)
    {
        var inputs = new List<string>(Queries);
        while (inputs.Count < Queries)
        {
            var text = stock[random.Next(stock.Count)].Text;
            var length = Math.Min(text.Length, random.Next(1, 9));
            var start = random.Next(3) == 0 && text.Length > length ? random.Next(text.Length - length + 1) : 0;
            var input = text.Substring(start, length).Trim();
            if (input.Length > 0)
            {
                inputs.Add(input);
            }
        }

        return inputs;
    }

    private static double Percentile(List<double> values, double percentile)
    {
        var sorted = values.Order().ToArray();
        return sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(sorted.Length * percentile / 100.0) - 1)];
    }

    private void Report(string line)
    {
        output.WriteLine(line);
        Console.Error.WriteLine("[spike] " + line);
    }

    private sealed record Entry(long Id, string Text, int Weight);
}
