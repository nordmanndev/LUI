using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Shouldly;

namespace Nordmann.LUI.Performance.Tests;

/// <summary>
/// What a <c>Linked</c> index costs on a table of the application (ADR-0015): the sync of the whole table, the
/// queries through the whole pipeline, the refresh of single rows — against an <c>Embedded</c> index of the same entries.
/// <code>dotnet test tests/Nordmann.LUI.Performance.Tests --filter LinkedPerformance --logger "console;verbosity=detailed"</code>
/// </summary>
[Trait("Category", "Timing")]
public sealed class LinkedPerformance(ITestOutputHelper output)
{
    /// <summary>The scenario of this run (ADR-0058): the corpus, the prefix length and the settings.</summary>
    private static PerformanceScenario Scenario => PerformanceScenario.Current;
    private const int Queries = 2_000;

    /// <summary>The rows of the table of <see cref="A_writer_beside_a_linked_sync_of_a_million_rows_never_gets_busy"/>.</summary>
    private const int Rows = 1_000_000;

    /// <summary>
    /// The busy timeout of the writer of the application in the first phase: long enough that the wait for the lock
    /// is measured and not a failure.
    /// </summary>
    private static readonly TimeSpan WriterBusyTimeout = TimeSpan.FromSeconds(60);

    /// <summary>The busy timeout of the catalog (ADR-0046): the timeout of an application that writes with LUI.</summary>
    private static readonly TimeSpan CatalogBusyTimeout = TimeSpan.FromSeconds(5);

    /// <summary>How long the writer of the application waits between two writes.</summary>
    private static readonly TimeSpan WriterPause = TimeSpan.FromMilliseconds(50);

    /// <summary>The error code of SQLite for a table that is locked by another writer (SQLITE_BUSY).</summary>
    private const int SqliteBusy = 5;

    [Fact]
    public void Sync_queries_and_refreshes_against_embedded()
    {
        var entries = Scenario.PrefixLength ?? 200_000;
        var corpus = CorpusOf("query-p50");
        var words = corpus.Texts;
        Report($"  {entries:N0} entries");

        var random = new Random(42);
        var rows = Enumerable.Range(1, entries)
            .Select(i => (Id: (long)i, Text: words[(i - 1) % words.Count], Weight: corpus.Weights[(i - 1) % corpus.Weights.Count]))
            .ToList();

        using var database = new TestDatabase();
        using var catalog = database.Open(new LuiCatalogOptions { Pragmas = new LuiPragmas { CacheSizeMegabytes = 512 } });
        CreateTable(database, rows);

        var linked = catalog.CreateLinked("tags", index => index.SingleWord().LinkTo("tag", "id", "name", weight: "usage_count"));
        var clock = Stopwatch.StartNew();
        linked.Sync();
        Report($"linked: sync {clock.Elapsed.TotalSeconds:F1} s");

        var embedded = catalog.Create("embedded", index => index.SingleWord());
        clock.Restart();
        embedded.UpsertMany(rows.Select(row => new LuiEntry(row.Id, row.Text) { Weight = row.Weight }));
        Report($"embedded:  UpsertMany {clock.Elapsed.TotalSeconds:F1} s");

        var inputs = Enumerable.Range(0, Queries).Select(_ =>
        {
            var text = rows[random.Next(rows.Count)].Text;
            var length = Math.Min(text.Length, random.Next(1, 9));
            return text[..length];
        }).ToList();

        // warm both, then measure them alternating
        foreach (var input in inputs.Take(200))
        {
            linked.Search(input);
            embedded.Search(input);
        }

        var linkedTimes = new List<double>();
        var embeddedTimes = new List<double>();
        foreach (var input in inputs)
        {
            var started = Stopwatch.GetTimestamp();
            linked.Search(input);
            linkedTimes.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            started = Stopwatch.GetTimestamp();
            embedded.Search(input);
            embeddedTimes.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }

        Report($"query: linked p50 {Percentile(linkedTimes, 50):F2} / p95 {Percentile(linkedTimes, 95):F2} ms, "
            + $"embedded p50 {Percentile(embeddedTimes, 50):F2} / p95 {Percentile(embeddedTimes, 95):F2} ms");

        MeasureRefreshes(database, linked, entries, random);
    }

    private void MeasureRefreshes(TestDatabase database, ILuiLinkedIndex linked, int entries, Random random)
    {
        var refreshes = new List<double>();
        using (var connection = database.Connect())
        {
            for (var i = 0; i < 500; i++)
            {
                var id = random.Next(1, entries);
                using var update = connection.CreateCommand();
                update.CommandText = "UPDATE tag SET usage_count = usage_count + 1 WHERE id = $id;";
                update.Parameters.AddWithValue("$id", id);
                update.ExecuteNonQuery();

                var started = Stopwatch.GetTimestamp();
                linked.Refresh(id);
                refreshes.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
        }

        Report($"refresh: p50 {Percentile(refreshes, 50):F2} / p95 {Percentile(refreshes, 95):F2} ms (own transaction)");
    }

    /// <summary>
    /// The write lock of a linked sync (issue #31, ADR-0016; since #271 and ADR-0077 the sync works in batches). Until 0.3.0
    /// the sync of a linked index was one transaction, so it held the write lock of the file for as long as it ran, and every other
    /// writer of the file waited for it — 26.7 s for a million rows, 4 of 11 writes of a writer with the busy timeout of the catalog
    /// failed. The measurement is the "before" of the batched sync, and it still runs: on the head it measures what a writer
    /// meets beside the batches (<see cref="A_writer_beside_a_linked_sync_of_a_million_rows_never_gets_busy"/> asks whether it ever fails).
    /// <para>
    /// The writer of the application has its own connection and its own busy timeout: it writes beside LUI, not
    /// through it. The first one waits long enough that the wait is measured and not a failure; the second one has
    /// the busy timeout of the catalog (ADR-0046), and its writes fail while the lock is held — on 0.3.0, where the lock was
    /// the whole sync; on the head the sync lets go of it after every batch.
    /// </para>
    /// </summary>
    [Fact]
    public void The_write_lock_of_a_linked_sync()
    {
        var entries = Scenario.PrefixLength ?? 200_000;
        var corpus = CorpusOf("build-3m");
        var words = corpus.Texts;

        var rows = Enumerable.Range(1, entries)
            .Select(i => (Id: (long)i, Text: words[(i - 1) % words.Count], Weight: corpus.Weights[(i - 1) % corpus.Weights.Count]))
            .ToList();

        using var database = new TestDatabase();
        using var catalog = database.Open(new LuiCatalogOptions { Pragmas = new LuiPragmas { CacheSizeMegabytes = 512 } });
        CreateTable(database, rows);

        var linked = catalog.CreateLinked("tags", index => index.SingleWord().LinkTo("tag", "id", "name", weight: "usage_count"));

        var waiting = WriteBesideASync(database, linked, WriterBusyTimeout);
        Report($"sync: {waiting.LockSeconds:F1} s — the write lock of the file");
        Report($"writer: {waiting.Writes} writes, the longest wait {waiting.LongestWait / 1000:F1} s, {waiting.Failures} failed");

        var failing = WriteBesideASync(database, linked, CatalogBusyTimeout);
        Report($"writer with the busy timeout of the catalog ({CatalogBusyTimeout.TotalSeconds:F0} s): "
            + $"{failing.Writes} writes, {failing.Failures} failed with SQLITE_BUSY");
    }

    /// <summary>
    /// The criterion of #271 (ADR-0077): a second connection that writes the file while a linked sync of 1,000,000 rows runs — during the
    /// batches, during the removal and during the merge — and has a busy timeout of <b>twice the measured duration of one batch</b>, never
    /// gets <c>SQLITE_BUSY</c>. It is a correctness test, not a measurement: it asserts that a writer is never refused, and it logs what
    /// it saw so that #275 can look at it.
    /// <para>
    /// How it is built. The table has 1,000,000 distinct words. A first sync fills the index and a second one, in the same session,
    /// replaces every entry and shows how long a batch of the kind that matters takes — the batches of a sync into an index that holds the
    /// rows already, which delete before they write; the longest of its batches is "the duration of one batch" — measured by the seam of
    /// the database that tells every transaction of a sync, not by the progress, which also counts the callback. The busy timeout is twice
    /// that, rounded <b>up</b> to whole seconds (one at least), because the provider takes the timeout of SQLite from the timeout of the command, which
    /// is in seconds. The third sync runs beside the writer, after 100,000 rows of the table were deleted, so that the removal has
    /// ten batches to do, and it ends with the merge.
    /// </para>
    /// <para>
    /// The bounds, argued (ADR-0050). There are no lower bounds on time and no upper bound that a machine decides: the test asserts
    /// <b>counts</b> — no write of the writer failed with <c>SQLITE_BUSY</c>, the writer wrote while the sync ran, and the sync is
    /// complete (every row of the table in the index, the removal done, the flag cleared, no lease left). The one time that is compared
    /// is the longest lock of the sync against the busy timeout: a writer waits for the transaction that holds the lock, so the lock must
    /// be shorter than what the writer is willing to wait for — the timeout is twice a batch and the longest lock is a batch, a step of the
    /// removal or a step of the merge.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_writer_beside_a_linked_sync_of_a_million_rows_never_gets_busy()
    {
        var rows = Rows;
        var corpus = Corpus.From(CorpusFile.Open("words.txt", rows, 0));
        corpus.Count.ShouldBe(rows, "the word list has a million entries");
        Report($"{rows:N0} rows of the word list, {Environment.ProcessorCount} cores, .NET {Environment.Version}");

        using var database = new TestDatabase();
        using var catalog = database.Open(new LuiCatalogOptions { Pragmas = new LuiPragmas { CacheSizeMegabytes = 512 } });
        CreateTable(database, [.. Enumerable.Range(1, rows).Select(i => (Id: (long)i, Text: corpus.Texts[i - 1], Weight: corpus.Weights[i - 1]))]);
        var linked = catalog.Create(new LuiLinkedIndexDefinition<long>("tags", index => index.SingleWord().LinkTo("tag", "id", "name", weight: "usage_count")));

        var first = TimedSync(catalog, linked, "first sync, into an empty index");
        first.Report.Indexed.ShouldBe(rows);

        var second = TimedSync(catalog, linked, "second sync, every entry replaced (no writer)");
        second.Report.Indexed.ShouldBe(rows);
        var batch = second.Longest("batch");
        batch.ShouldBeGreaterThan(TimeSpan.Zero);

        // twice the longest batch, rounded up to whole seconds: the provider's timeout of a command is in seconds
        var timeoutSeconds = Math.Max(1, (int)Math.Ceiling(2 * batch.TotalSeconds));
        Report($"one batch: the longest {batch.TotalSeconds:F2} s; busy timeout of the writer = 2 x that, rounded up: {timeoutSeconds} s");

        // 100,000 rows are gone from the table: the removal of the third sync has ten batches of work
        using (var connection = database.Connect())
        using (var delete = connection.CreateCommand())
        {
            delete.CommandText = "DELETE FROM tag WHERE id % 10 = 0;";
            delete.ExecuteNonQuery();
        }

        var waits = new List<double>();
        var failures = 0;
        using var stop = new CancellationTokenSource();
        var writer = Task.Run(() => WriteWhileTheSyncRuns(database, TimeSpan.FromSeconds(timeoutSeconds), waits, () => failures++, stop.Token));
        var third = TimedSync(catalog, linked, "third sync, beside the writer, 100,000 rows removed");
        await stop.CancelAsync();
        await writer;

        Report($"writer: {waits.Count} writes succeeded, the longest wait {(waits.Count == 0 ? 0 : waits.Max()) / 1000:F2} s, {failures} failed with SQLITE_BUSY "
            + $"(busy timeout {timeoutSeconds} s)");
        Report($"sync: longest lock {third.Report.LongestLock.TotalSeconds:F2} s, {third.Report.Batches} batches, {third.Report.Removed:N0} removed, "
            + $"transactions by phase (count / longest / median s): {third.Summary()}");

        failures.ShouldBe(0, "a writer with a busy timeout of twice a batch gets the file between the transactions of the sync");
        waits.Count.ShouldBeGreaterThan(10, "the writer wrote while the sync ran: the test has seen the batches, the removal and the merge");
        third.Report.LongestLock.TotalSeconds.ShouldBeLessThan(timeoutSeconds, "no transaction of the sync holds the lock for longer than the writer waits");
        third.Report.Removed.ShouldBe(rows / 10);
        third.Report.Indexed.ShouldBe(rows - (rows / 10));
        third.Phases.Count(phase => string.Equals(phase.Name, "removal", StringComparison.Ordinal)).ShouldBeGreaterThan(1, "the writer met the removal");
        third.Phases.Count(phase => string.Equals(phase.Name, "merge", StringComparison.Ordinal)).ShouldBeGreaterThan(1, "and the merge, in steps");
        linked.NeedsInitialSync.ShouldBeFalse();
        database.Scalar<long>("SELECT count(*) FROM lui_meta WHERE sync_lease_owner IS NOT NULL").ShouldBe(0, "the lease is released");
    }

    /// <summary>One sync of the index, with the seconds of every transaction of it that the seam of the database told.</summary>
    private SyncRun TimedSync(LuiCatalog catalog, LuiLinkedIndex<long> linked, string what)
    {
        var phases = new List<(string Name, TimeSpan Duration)>();
        var clock = Stopwatch.StartNew();
        var previous = TimeSpan.Zero;
        catalog.Database.AfterSyncTransaction = phase =>
        {
            // the time since the transaction before — its own duration and the little that lies between two of them
            var now = clock.Elapsed;
            phases.Add((phase, now - previous));
            previous = now;
        };

        var report = linked.Sync();
        catalog.Database.AfterSyncTransaction = null;
        var run = new SyncRun(report, phases, clock.Elapsed);
        Report($"{what}: {run.Total.TotalSeconds:F1} s, {report.Batches} batches, {report.Removed:N0} removed, longest lock {report.LongestLock.TotalSeconds:F2} s; {run.Summary()}");
        return run;
    }

    /// <summary>What one sync did: its report, the transactions it ran by phase, and how long it took.</summary>
    private sealed record SyncRun(LuiLinkedSyncReport Report, List<(string Name, TimeSpan Duration)> Phases, TimeSpan Total)
    {
        public TimeSpan Longest(string phase) =>
            Phases.Where(entry => string.Equals(entry.Name, phase, StringComparison.Ordinal)).Select(entry => entry.Duration).DefaultIfEmpty().Max();

        public string Summary() =>
            string.Join(
                "; ",
                Phases.GroupBy(entry => entry.Name, StringComparer.Ordinal).Select(group =>
                {
                    var durations = group.Select(entry => entry.Duration.TotalSeconds).Order().ToArray();
                    return string.Create(
                        System.Globalization.CultureInfo.InvariantCulture,
                        $"{group.Key} {durations.Length} / {durations[^1]:F2} / {durations[durations.Length / 2]:F2}");
                }));
    }

    /// <summary>
    /// One phase of the measurement: a writer beside a sync of the index, with a busy timeout of its own. It returns
    /// the lock the sync held and what the writer of the application experienced.
    /// </summary>
    /// <param name="database">The file of the measurement.</param>
    /// <param name="linked">The linked index that syncs.</param>
    /// <param name="busyTimeout">The busy timeout of the writer of the application.</param>
    /// <returns>The lock and the waits of the writer.</returns>
    private static LockMeasurement WriteBesideASync(TestDatabase database, ILuiLinkedIndex linked, TimeSpan busyTimeout)
    {
        var waits = new List<double>();
        var failures = 0;
        using var stop = new CancellationTokenSource();
        var writer = Task.Run(() => WriteWhileTheSyncRuns(database, busyTimeout, waits, () => failures++, stop.Token));

        var clock = Stopwatch.StartNew();
        linked.Sync();
        var lockSeconds = clock.Elapsed.TotalSeconds;

        stop.Cancel();
        writer.GetAwaiter().GetResult();

        return new LockMeasurement(lockSeconds, waits.Count, failures, waits.Count == 0 ? 0 : waits.Max());
    }

    /// <summary>
    /// The writer of the application: it writes to the table of the application while the index syncs, and it
    /// measures how long it waits for the write lock of the file.
    /// </summary>
    /// <param name="database">The file of the measurement.</param>
    /// <param name="busyTimeout">The busy timeout of the writer.</param>
    /// <param name="waits">The waits of the writes that succeeded, in milliseconds.</param>
    /// <param name="onFailure">Called for every write that failed with SQLITE_BUSY.</param>
    /// <param name="cancellationToken">The token that stops the writer.</param>
    private static void WriteWhileTheSyncRuns(
        TestDatabase database,
        TimeSpan busyTimeout,
        List<double> waits,
        Action onFailure,
        CancellationToken cancellationToken)
    {
        using var connection = database.Connect();
        var random = new Random(7);
        while (!cancellationToken.IsCancellationRequested)
        {
            var started = Stopwatch.GetTimestamp();
            try
            {
                using var update = connection.CreateCommand();

                // the busy timeout of the writer: Microsoft.Data.Sqlite sets the timeout of SQLite for every command
                // from the timeout of the command, so this is the place where the wait of the writer is set
                update.CommandTimeout = (int)busyTimeout.TotalSeconds;
                update.CommandText = "UPDATE tag SET usage_count = usage_count + 1 WHERE id = $id;";
                update.Parameters.AddWithValue("$id", random.Next(1, 1_000));
                update.ExecuteNonQuery();
                waits.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
            catch (SqliteException exception) when (exception.SqliteErrorCode == SqliteBusy)
            {
                onFailure();
            }

            cancellationToken.WaitHandle.WaitOne(WriterPause);
        }
    }

    /// <summary>What one phase of the measurement has seen: the lock of the sync and the waits of the writer.</summary>
    /// <param name="LockSeconds">How long the sync held the write lock of the file.</param>
    /// <param name="Writes">How many writes of the application succeeded.</param>
    /// <param name="Failures">How many writes of the application failed with SQLITE_BUSY.</param>
    /// <param name="LongestWait">The longest wait of a write that succeeded, in milliseconds.</param>
    private sealed record LockMeasurement(double LockSeconds, int Writes, int Failures, double LongestWait);

    private static void CreateTable(TestDatabase database, List<(long Id, string Text, int Weight)> rows)
    {
        using var connection = database.Connect();
        using (var create = connection.CreateCommand())
        {
            create.CommandText = "CREATE TABLE tag (id INTEGER PRIMARY KEY, name TEXT NOT NULL, usage_count INTEGER NOT NULL);";
            create.ExecuteNonQuery();
        }

        using var transaction = connection.BeginTransaction();
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = "INSERT INTO tag VALUES ($id, $name, $weight);";
        var id = insert.Parameters.Add("$id", SqliteType.Integer);
        var name = insert.Parameters.Add("$name", SqliteType.Text);
        var weight = insert.Parameters.Add("$weight", SqliteType.Integer);
        foreach (var row in rows)
        {
            id.Value = row.Id;
            name.Value = row.Text;
            weight.Value = row.Weight;
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private static double Percentile(List<double> values, double percentile)
    {
        var sorted = values.Order().ToArray();
        return sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(sorted.Length * percentile / 100.0) - 1)];
    }

    /// <summary>
    /// The corpus of the measurement: the file that the budget of its target names (ADR-0056). The
    /// slice that the measurement takes is the one of the budget, cut to the number of entries of the
    /// run. Which corpus it was stands in the report — two measurements on two corpora are no
    /// comparison.
    /// </summary>
    /// <param name="budgetId">The id of the budget whose target is measured.</param>
    /// <returns>The corpus of the run.</returns>
    private Corpus CorpusOf(string budgetId)
    {
        var corpus = Scenario.CorpusOf(budgetId, out var name);
        Report(Scenario.Describe(name, corpus.Count, LuiBudgets.By(budgetId).Scenario.PrefixLength));
        Report($"  {Environment.ProcessorCount} cores, .NET {Environment.Version}");
        return corpus;
    }

    private void Report(string line)
    {
        output.WriteLine(line);
        Console.Error.WriteLine("[perf] " + line);
    }
}
