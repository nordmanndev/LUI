using System.Diagnostics;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Xunit.Abstractions;

namespace Nordmann.LUI.Integration.Tests;

/// <summary>
/// The bulk path of an <c>Embedded</c> index, measured before it is built (ADR-0010, option 5; PR-007): how much does the
/// order in which the rows reach the search index matter, and does a sync in batches keep it?
/// <list type="bullet">
/// <item><b>row order</b> — batches of 10,000 in the order of the source, each in a transaction, through the triggers
/// (what <c>UpsertMany</c> does per batch);</item>
/// <item><b>sorted batches</b> — the same batches, each sorted by its future rowid (priority, then weight descending);</item>
/// <item><b>sorted, automerge off</b> — as before, with FTS5's incremental merge switched off while loading;</item>
/// <item><b>content, then rebuild</b> — the content table without the triggers, then FTS5's <c>rebuild</c>, which reads
/// the content table in rowid order (the reference: the whole index sorted).</item>
/// </list>
/// Each variant is merged (<c>optimize</c>) at the end; the queries run before and after the merge.
/// <code>LUI_PERF=1 dotnet test --filter EmbeddedBulkExperiment --logger "console;verbosity=detailed"</code>
/// </summary>
public sealed class EmbeddedBulkExperiment(ITestOutputHelper output)
{
    private static readonly int Batch = int.Parse(Environment.GetEnvironmentVariable("LUI_PERF_BATCH") ?? "10000", CultureInfo.InvariantCulture);
    private const int Queries = 2_000;

    private static readonly string[] Variants = ["row order", "sorted batches", "sorted, automerge off", "content, then rebuild"];

    [PerformanceFact]
    public void The_order_of_the_rows_and_the_merge()
    {
        var entries = int.Parse(Environment.GetEnvironmentVariable("LUI_PERF_ENTRIES") ?? "200000", CultureInfo.InvariantCulture);
        var words = TestCorpus.MeasurementWords(out var corpus);
        Report($"corpus: {corpus} ({words.Count:N0} words), {entries:N0} entries");

        var random = new Random(42);
        var rows = Enumerable.Range(1, entries)
            .Select(i => (Key: (long)i, Text: i <= words.Count ? words[i - 1] : words[random.Next(words.Count)] + words[random.Next(words.Count)].ToLowerInvariant(), Weight: random.Next(0, 1_000_000)))
            .OrderBy(_ => random.Next())
            .ToList();
        var inputs = Enumerable.Range(0, Queries).Select(_ =>
        {
            var text = rows[random.Next(rows.Count)].Text;
            return text[..Math.Min(text.Length, random.Next(1, 9))];
        }).ToList();

        var only = Environment.GetEnvironmentVariable("LUI_PERF_VARIANT");
        foreach (var variant in Variants
            .Where(name => only is null || name.StartsWith(only, StringComparison.Ordinal)))
        {
            Measure(variant, rows, inputs);
        }
    }

    private void Measure(string variant, List<(long Key, string Text, int Weight)> rows, List<string> inputs)
    {
        using var database = new TestDatabase();
        using var catalog = database.Open(new LuiCatalogOptions { CacheSizeMegabytes = 512 });
        var index = catalog.Index("words").SingleWord().Ensure();

        // the catalog's writer has the cache of the options; a plain connection would measure SQLite's 2 MB default
        using var connection = database.Connect();
        Execute(connection, "PRAGMA cache_size = -524288; PRAGMA synchronous = NORMAL;");
        if (Environment.GetEnvironmentVariable("LUI_PERF_CHECKPOINT") is { } pages)
        {
            Execute(connection, $"PRAGMA wal_autocheckpoint = {pages};");
        }
        var clock = Stopwatch.StartNew();
        switch (variant)
        {
            case "row order":
                LoadInBatches(connection, rows, sort: false);
                break;
            case "sorted batches":
                LoadInBatches(connection, rows, sort: true);
                break;
            case "sorted, automerge off":
                Execute(connection, "INSERT INTO lui_words_fts(lui_words_fts, rank) VALUES ('automerge', 0);");
                LoadInBatches(connection, rows, sort: true);
                Execute(connection, "INSERT INTO lui_words_fts(lui_words_fts, rank) VALUES ('automerge', 4);");
                break;
            default:
                var trigger = Scalar<string>(connection, "SELECT sql FROM sqlite_master WHERE name = 'lui_words_ai';");
                Execute(connection, "DROP TRIGGER lui_words_ai;");
                LoadInBatches(connection, rows, sort: false);
                Report($"  content table alone {clock.Elapsed.TotalSeconds:F1} s");
                var rebuild = Stopwatch.StartNew();
                Execute(connection, "INSERT INTO lui_words_fts(lui_words_fts) VALUES ('rebuild');");
                Report($"  rebuild alone {rebuild.Elapsed.TotalSeconds:F1} s");
                Execute(connection, trigger);
                break;
        }

        var load = clock.Elapsed;
        var segments = Scalar<long>(connection, "SELECT count(*) FROM lui_words_fts_data;");
        var (before50, before95) = Queries95(index, inputs);

        clock.Restart();
        Execute(connection, "INSERT INTO lui_words_fts(lui_words_fts) VALUES ('optimize');");
        var optimize = clock.Elapsed;
        var (after50, after95) = Queries95(index, inputs);

        Report($"{variant,-22} load {load.TotalSeconds,6:F1} s ({Scalar<long>(connection, "SELECT count(*) FROM lui_words;"):N0} rows, "
            + $"{segments:N0} data blocks) · query p50/p95 {before50:F2}/{before95:F2} ms · optimize {optimize.TotalSeconds:F1} s · "
            + $"query after {after50:F2}/{after95:F2} ms");
    }

    private static void LoadInBatches(SqliteConnection connection, List<(long Key, string Text, int Weight)> rows, bool sort)
    {
        using var insert = connection.CreateCommand();
        insert.CommandText = "INSERT INTO lui_words(key, text, weight, priority) VALUES ($key, $text, $weight, 3);";
        var key = insert.Parameters.Add("$key", SqliteType.Text);
        var text = insert.Parameters.Add("$text", SqliteType.Text);
        var weight = insert.Parameters.Add("$weight", SqliteType.Integer);

        foreach (var chunk in rows.Chunk(Batch))
        {
            var batch = sort ? chunk.OrderByDescending(row => row.Weight).ToArray() : chunk;
            using var transaction = connection.BeginTransaction();
            insert.Transaction = transaction;
            foreach (var row in batch)
            {
                key.Value = row.Key.ToString(CultureInfo.InvariantCulture);
                text.Value = row.Text;
                weight.Value = row.Weight;
                insert.ExecuteNonQuery();
            }

            transaction.Commit();
        }
    }

    private static (double P50, double P95) Queries95(ILuiIndex<long, LuiNoPayload> index, List<string> inputs)
    {
        foreach (var input in inputs.Take(200))
        {
            index.Search(input);
        }

        var times = new List<double>();
        foreach (var input in inputs)
        {
            var started = Stopwatch.GetTimestamp();
            index.Search(input);
            times.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }

        var sorted = times.Order().ToArray();
        return (sorted[(int)(sorted.Length * 0.50)], sorted[(int)(sorted.Length * 0.95)]);
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static T Scalar<T>(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (T)command.ExecuteScalar()!;
    }

    private void Report(string line)
    {
        output.WriteLine(line);
        Console.Error.WriteLine("[perf] " + line);
    }
}
