using System.Diagnostics;

namespace Nordmann.LUI.Performance.Tests;

/// <summary>
/// The performance targets of ADR-0020 as tests (PR-009, ADR-0020): each target in the scenario of ADR-0020,
/// measured on a corpus of <see cref="Corpus"/>, and either <b>met</b> — then it is asserted — or
/// <b>waived</b>, with a reason and the record that holds it. A target that is missed is never lowered quietly
/// (<c>docs/benchmarks.md</c>, rule 5); a waiver that is met is reported, so it can go.
/// <para>
/// The scenario is three million entries (one million titles for the file size). With the <c>entries</c> of the
/// scenario file smaller, the numbers are reported and nothing is asserted: a budget of three million entries is
/// not a claim about 200,000.
/// </para>
/// <code>dotnet test tests/Nordmann.LUI.Performance.Tests --filter BudgetPerformance --logger "console;verbosity=detailed"</code>
/// </summary>
[Trait("Category", "Timing")]
public sealed class BudgetPerformance(ITestOutputHelper output)
{
    /// <summary>The scenario of this run (ADR-0058): the corpus, the prefix length and the settings.</summary>
    private static PerformanceScenario Scenario => PerformanceScenario.Current;
    /// <summary>The scenario of ADR-0020: three million entries — the one the budgets name.</summary>
    private static int ScenarioEntries => LuiBudgets.By("query-p50").Scenario.PrefixLength;

    /// <summary>The scenario of the file size: one million titles — the one the budget names.</summary>
    private static int TitlesScenario => LuiBudgets.By("file-size-1m").Scenario.PrefixLength;

    /// <summary>The scenario of the typo dictionary — the one the budget names.</summary>
    private static int TypoScenario => LuiBudgets.By("typo-dictionary-size").Scenario.PrefixLength;

    private static int TwoErrorsScenario => LuiBudgets.By("typo-dictionary-two-errors-size").Scenario.PrefixLength;

    private const int Queries = 2_000;

    [Fact]
    public void The_single_word_index_at_three_million_entries()
    {
        var corpus = CorpusOf("query-p50");
        var entries = corpus.Count;
        var budget = new PerformanceBudget(output.WriteLine, entries >= ScenarioEntries, Scenario.Machine);
        budget.SayTheMachine();

        using var database = new TestDatabase();
        using var catalog = database.Open(new LuiCatalogOptions { Pragmas = new LuiPragmas { CacheSizeMegabytes = 512 } });
        var words = catalog.Create("words", index => index.SingleWord().TypoTolerance());

        // the build: the bulk path of a sync into an empty index (ADR-0016); the batch of the scenario measures another
        // batch — the first load of a file no other process writes: the largest batch, the fewest commits (ADR-0016, ADR-0020)
        var batch = Scenario.Batch;
        var build = Stopwatch.StartNew();
        words.Sync(corpus.Entries(), new LuiSyncOptions { BatchSize = batch });
        build.Stop();
        Report($"build: {entries:N0} entries by Sync (batches of {batch:N0}) in {build.Elapsed.TotalSeconds:F1} s, file {database.FileMegabytes():N0} MB");

        var inputs = corpus.Inputs(Queries);
        var (latencies, allocations) = MeasureQueries(words, inputs, typoTolerance: false);
        budget.AtMost("query-p50", Percentile(latencies, 50), "ms");

        // ADR-0020 since the owner's decision on ADR-0010: the p95 per length of the input — FTS5's cost grows with it
        budget.AtMost("query-p95-short", Percentile(OfLength(inputs, latencies, 1, 4), 95), "ms");
        budget.AtMost("query-p95-long", Percentile(OfLength(inputs, latencies, 5, 8), 95), "ms");

        // ADR-0020 since ADR-0032: the causes of #30 are measured and the allocation is lowered — the target is p50 ≤ 60 KB, p95 ≤ 100 KB
        budget.AtMost("allocation-p50", Percentile(allocations, 50) / 1024, "KB");
        budget.AtMost("allocation-p95", Percentile(allocations, 95) / 1024, "KB");

        // the typo dictionary of three million entries is built before the corrected queries are timed
        var warm = Stopwatch.StartNew();
        words.WarmUpTypoDictionary();
        Report($"typo dictionary of {entries:N0} entries: {warm.Elapsed.TotalSeconds:F1} s");
        var (corrected, _) = MeasureQueries(words, corpus.Typos(Queries / 2), typoTolerance: true);
        budget.AtMost("query-corrected-p95", Percentile(corrected, 95), "ms");

        budget.AtMost("write-p95", Percentile(MeasureWrites(words, corpus), 95), "ms");
        // ADR-0020 since the owner's decision of 2026-11-04: the build follows the disk — 172–206 s on two machines (#32)
        budget.AtMost("build-3m", build.Elapsed.TotalSeconds, "s");

        budget.Verify();
    }

    /// <summary>
    /// The first lever of issue #32: a longer write-ahead log while the first load runs. SQLite checkpoints the log
    /// every thousand pages; a larger interval means fewer checkpoints — and a log of up to 400 MB. ADR-0016 measured
    /// 12 % at a million entries. The pragma is set on the connection of the writer, which is the connection the sync
    /// writes through. This is a spike: its numbers go into the record, its code goes with the decision.
    /// </summary>
    [Fact]
    public void The_first_load_with_a_longer_write_ahead_log()
    {
        var corpus = CorpusOf("query-p50");
        var batch = Scenario.Batch;

        // the baseline twice: the disk of the machine warms up, and the second baseline says how much of the
        // difference is the machine and how much is the lever
        var baseline = Build(corpus, batch, null);
        var lever = Build(corpus, batch, 100_000);
        var again = Build(corpus, batch, null);

        Report($"build: baseline {baseline.Seconds:F1} s, with a log of 100,000 pages {lever.Seconds:F1} s, baseline again {again.Seconds:F1} s");
        Report($"the log of the file: {baseline.LogMegabytes:N0} MB without the lever, {lever.LogMegabytes:N0} MB with it");
    }

    /// <summary>
    /// One build of the index, with or without the lever of the first load.
    /// </summary>
    /// <param name="corpus">The corpus of the run.</param>
    /// <param name="batch">The batch of the scenario.</param>
    /// <param name="checkpointPages">The pages between two checkpoints of the write-ahead log, or null for the default
    /// of SQLite.</param>
    /// <returns>How long the build took, and how large the log of the file was.</returns>
    private static BuildResult Build(Corpus corpus, int batch, int? checkpointPages)
    {
        using var database = new TestDatabase();
        using var catalog = database.Open(new LuiCatalogOptions { Pragmas = new LuiPragmas { CacheSizeMegabytes = 512 } });
        var words = catalog.Create("words", index => index.SingleWord().TypoTolerance());

        if (checkpointPages is { } pages)
        {
            // the setting belongs to the physical connection: the one the pool hands out to the sync that follows (ADR-0070)
            using var connection = catalog.Database.OpenConnection();
            using var pragma = connection.CreateCommand();
            pragma.CommandText = $"PRAGMA wal_autocheckpoint = {pages};";
            pragma.ExecuteNonQuery();
        }

        var clock = Stopwatch.StartNew();
        words.Sync(corpus.Entries(), new LuiSyncOptions { BatchSize = batch });
        clock.Stop();

        return new BuildResult(clock.Elapsed.TotalSeconds, LogMegabytes(database.FilePath));
    }

    /// <summary>
    /// The size of the write-ahead log of the file, in megabytes — what the lever of a longer log costs.
    /// </summary>
    /// <param name="path">The path of the file of the database.</param>
    /// <returns>The size of the log, in megabytes.</returns>
    private static double LogMegabytes(string path)
    {
        var log = path + "-wal";
        return File.Exists(log) ? new FileInfo(log).Length / 1_048_576.0 : 0;
    }

    /// <summary>What one build of the measurement has shown: the time and the size of the log of the file.</summary>
    /// <param name="Seconds">How long the build took.</param>
    /// <param name="LogMegabytes">How large the write-ahead log of the file was.</param>
    private sealed record BuildResult(double Seconds, double LogMegabytes);

    [Fact]
    public void The_typo_dictionary_of_the_whole_word_list()
    {
        var corpus = CorpusOf("typo-dictionary-size");
        var words = corpus.Texts;
        var budget = new PerformanceBudget(output.WriteLine, words.Count >= TypoScenario, Scenario.Machine);
        budget.SayTheMachine();

        using var database = new TestDatabase();
        using var catalog = database.Open(new LuiCatalogOptions { Pragmas = new LuiPragmas { CacheSizeMegabytes = 512 } });
        var index = catalog.Create("words", index => index.SingleWord().TypoTolerance());
        index.Sync(corpus.Entries(), new LuiSyncOptions { BatchSize = 100_000 });

        var before = GC.GetTotalMemory(forceFullCollection: true);
        var build = Stopwatch.StartNew();
        index.WarmUpTypoDictionary();
        build.Stop();
        var after = GC.GetTotalMemory(forceFullCollection: true);
        GC.KeepAlive(index);

        Report($"typo dictionary of {words.Count:N0} words: {build.Elapsed.TotalSeconds:F1} s, {(after - before) / 1_048_576.0:F0} MB");
        budget.AtMost("typo-dictionary-size", (after - before) / 1_048_576.0, "MB");
        budget.AtMost("typo-dictionary-build", build.Elapsed.TotalSeconds, "s");

        budget.Verify();
    }

    /// <summary>
    /// The dictionary of the distance <c>TwoErrors</c> (ADR-0054) against the budget of its own (issue #242, ADR-0056): the size
    /// is asserted on every runner — a size is the same on every machine. Before, the limit stood in the file and in the tables
    /// and no measurement held a size against it.
    /// </summary>
    [Fact]
    public void The_typo_dictionary_of_two_errors_of_the_whole_word_list()
    {
        var corpus = CorpusOf("typo-dictionary-two-errors-size");
        var words = corpus.Texts;
        var budget = new PerformanceBudget(output.WriteLine, words.Count >= TwoErrorsScenario, Scenario.Machine);
        budget.SayTheMachine();

        using var database = new TestDatabase();
        using var catalog = database.Open(new LuiCatalogOptions { Pragmas = new LuiPragmas { CacheSizeMegabytes = 512 } });
        var index = catalog.Create("words", index => index.SingleWord().TypoTolerance(LuiTypoDistance.TwoErrors));
        index.Sync(corpus.Entries(), new LuiSyncOptions { BatchSize = 100_000 });

        var before = GC.GetTotalMemory(forceFullCollection: true);
        var build = Stopwatch.StartNew();
        index.WarmUpTypoDictionary();
        build.Stop();
        var after = GC.GetTotalMemory(forceFullCollection: true);
        GC.KeepAlive(index);

        Report($"typo dictionary of two errors, {words.Count:N0} words: {build.Elapsed.TotalSeconds:F1} s, {(after - before) / 1_048_576.0:F0} MB");
        budget.AtMost("typo-dictionary-two-errors-size", (after - before) / 1_048_576.0, "MB");

        budget.Verify();
    }

    [Fact]
    public void The_file_of_a_million_titles()
    {
        var corpus = CorpusOf("file-size-1m");
        var entries = corpus.Count;
        var budget = new PerformanceBudget(output.WriteLine, entries >= TitlesScenario, Scenario.Machine);
        budget.SayTheMachine();

        using var database = new TestDatabase();
        using var catalog = database.Open(new LuiCatalogOptions { Pragmas = new LuiPragmas { CacheSizeMegabytes = 512 } });
        // the kind comes out of the corpus file (ADR-0059): a title is more than one word
        if (corpus.Kind != CorpusKind.MultiWord)
        {
            throw new InvalidOperationException(
                $"The corpus '{corpus.Name}' is not a corpus of titles — the index of this measurement is a multi-word one.");
        }

        var titles = catalog.Create("titles", index => index.MultiWord());
        var build = Stopwatch.StartNew();
        titles.Sync(corpus.Entries(), new LuiSyncOptions { BatchSize = 100_000 });
        build.Stop();

        var megabytes = database.FileMegabytes();
        Report($"titles: {entries:N0} by Sync in {build.Elapsed.TotalSeconds:F1} s, file {megabytes:N0} MB");
        // ADR-0020 since the owner's decision on ADR-0011/0020: titles of 36 characters on average, the text and its forms
        budget.AtMost("file-size-1m", megabytes, "MB");

        budget.Verify();
    }

    private (double[] Latencies, double[] Allocations) MeasureQueries(ILuiIndex index, IReadOnlyList<string> inputs, bool typoTolerance)
    {
        var options = new LuiQueryOptions { TypoTolerance = typoTolerance };

        // warm the page cache and the JIT, as a running application would be
        foreach (var input in inputs.Take(300))
        {
            index.Search(input, options);
        }

        var latencies = new double[inputs.Count];
        var allocations = new double[inputs.Count];
        var full = new bool[inputs.Count];
        var corrections = 0;
        for (var i = 0; i < inputs.Count; i++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var started = Stopwatch.GetTimestamp();
            var result = index.Search(inputs[i], options);
            latencies[i] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            allocations[i] = GC.GetAllocatedBytesForCurrentThread() - before;
            corrections += result.CorrectedQuery is null ? 0 : 1;
            full[i] = result.Truncated;
        }

        // where the allocation comes from: a query that reads its whole budget of candidates reads 500 texts
        var spent = allocations.Where((_, i) => full[i]).ToArray();
        var rest = allocations.Where((_, i) => !full[i]).ToArray();
        if (spent.Length > 0 && rest.Length > 0)
        {
            Report($"  alloc, budget of candidates spent ({spent.Length:N0} queries): p50 {Percentile(spent, 50) / 1024:F0} KB, p95 {Percentile(spent, 95) / 1024:F0} KB; "
                + $"the rest ({rest.Length:N0}): p50 {Percentile(rest, 50) / 1024:F0} KB, p95 {Percentile(rest, 95) / 1024:F0} KB");
        }

        Report($"{(typoTolerance ? "corrected " : string.Empty)}query: p50 {Percentile(latencies, 50):F2} ms, p95 {Percentile(latencies, 95):F2} ms, "
            + $"max {latencies.Max():F1} ms; alloc p50 {Percentile(allocations, 50) / 1024:F0} KB, p95 {Percentile(allocations, 95) / 1024:F0} KB"
            + (typoTolerance ? $"; {corrections:N0} of {inputs.Count:N0} corrected" : string.Empty));

        foreach (var length in Enumerable.Range(1, 10))
        {
            var group = inputs.Select((input, i) => (input.Length, Latency: latencies[i])).Where(x => x.Length == length).Select(x => x.Latency).ToArray();
            if (group.Length > 0)
            {
                Report($"  {length} chars: p50 {Percentile(group, 50):F2} ms, p95 {Percentile(group, 95):F2} ms ({group.Length} queries)");
            }
        }

        return (latencies, allocations);
    }

    /// <summary>The single writes of ADR-0020, each in a transaction of its own: an upsert, a weight, a removal (and the entry back).</summary>
    private static double[] MeasureWrites(ILuiEmbeddedIndex index, Corpus corpus)
    {
        var writes = new List<double>(600);
        var random = new Random(11);
        for (var i = 0; i < 600; i++)
        {
            var key = random.Next(1, corpus.Count + 1);
            var text = corpus.Texts[key - 1];
            var started = Stopwatch.GetTimestamp();
            switch (i % 3)
            {
                case 0:
                    index.Upsert(key, text, random.Next(0, 1_000_000));
                    break;
                case 1:
                    index.UpdateWeight(key, random.Next(0, 1_000_000));
                    break;
                default:
                    index.Remove(key);
                    writes.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                    index.Upsert(key, text, corpus.Weights[key - 1]);
                    continue;
            }

            writes.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }

        return [.. writes];
    }

    /// <summary>The latencies of the inputs of a length between <paramref name="shortest"/> and <paramref name="longest"/>.</summary>
    private static double[] OfLength(IReadOnlyList<string> inputs, double[] latencies, int shortest, int longest) =>
        [.. latencies.Where((_, i) => inputs[i].Length >= shortest && inputs[i].Length <= longest)];

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

    private static double Percentile(double[] values, double percentile)
    {
        var sorted = values.Order().ToArray();
        return sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(sorted.Length * percentile / 100.0) - 1)];
    }

    private void Report(string line)
    {
        output.WriteLine(line);
        Console.Error.WriteLine("[perf] " + line);
    }
}
