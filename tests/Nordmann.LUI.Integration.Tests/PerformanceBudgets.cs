using System.Diagnostics;
using System.Globalization;
using Xunit.Abstractions;

namespace Nordmann.LUI.Integration.Tests;

/// <summary>
/// The performance targets of LUI-Spezifikation §16 as tests (PR-009, ADR-0020): each target in the scenario of the
/// specification, measured on a corpus of <see cref="Corpus"/>, and either <b>met</b> — then it is asserted — or
/// <b>waived</b>, with a reason and the record that holds it. A target that is missed is never lowered quietly
/// (<c>docs/benchmarks.md</c>, rule 5); a waiver that is met is reported, so it can go.
/// <para>
/// The scenario is three million entries (one million titles for the file size). With <c>LUI_PERF_ENTRIES</c> smaller,
/// the numbers are reported and nothing is asserted: a budget of three million entries is not a claim about 200,000.
/// </para>
/// <code>LUI_PERF=1 dotnet test tests/Nordmann.LUI.Integration.Tests --filter PerformanceBudgets --logger "console;verbosity=detailed"</code>
/// </summary>
public sealed class PerformanceBudgets(ITestOutputHelper output)
{
    /// <summary>The scenario of §16: three million entries.</summary>
    private const int Scenario = 3_000_000;

    /// <summary>The scenario of the file size: one million titles.</summary>
    private const int TitlesScenario = 1_000_000;

    private const int Queries = 2_000;

    [PerformanceFact]
    public void The_single_word_index_at_three_million_entries()
    {
        var entries = Entries(Scenario);
        var corpus = Corpus.Generate(Words(), entries, CorpusKind.SingleWord);
        var budget = new PerformanceBudget(output, entries >= Scenario, PerformanceBudget.CurrentMachine());
        budget.SayTheMachine();

        using var database = new TestDatabase();
        using var catalog = database.Open(new LuiCatalogOptions { CacheSizeMegabytes = 512, MemoryMapMegabytes = 2048 });
        var words = catalog.Create("words", index => index.SingleWord().TypoTolerance());

        // the build: the bulk path of a sync into an empty index (ADR-0016); LUI_PERF_BATCH measures another batch
        // the first load of a file no other process writes: the largest batch, the fewest commits (ADR-0016, ADR-0020)
        var batch = int.Parse(Environment.GetEnvironmentVariable("LUI_PERF_BATCH") ?? "1000000", CultureInfo.InvariantCulture);
        var build = Stopwatch.StartNew();
        words.Sync(corpus.Entries(), new LuiSyncOptions { BatchSize = batch });
        build.Stop();
        Report($"build: {entries:N0} entries by Sync (batches of {batch:N0}) in {build.Elapsed.TotalSeconds:F1} s, file {database.FileMegabytes():N0} MB");

        var inputs = corpus.Inputs(Queries);
        var (latencies, allocations) = MeasureQueries(words, inputs, typoTolerance: false);
        budget.AtMost("query p50", "3 M entries, inputs of 1–8 characters, no correction", Percentile(latencies, 50), 2.0, "ms");

        // §16 since the owner's decision on ADR-0010: the p95 per length of the input — FTS5's cost grows with it
        budget.AtMost("query p95, 1–4 characters", "3 M entries, no correction", Percentile(OfLength(inputs, latencies, 1, 4), 95), 5.0, "ms");
        budget.AtMost("query p95, 5–8 characters", "3 M entries, no correction", Percentile(OfLength(inputs, latencies, 5, 8), 95), 20.0, "ms");

        // §16 since ADR-0032: the causes of #30 are measured and the allocation is lowered — the target is p50 ≤ 60 KB, p95 ≤ 100 KB
        budget.AtMost("allocation p50", "per query, the same inputs", Percentile(allocations, 50) / 1024, 60, "KB");
        budget.AtMost("allocation p95", "per query, the same inputs (#30)", Percentile(allocations, 95) / 1024, 100, "KB");

        // the typo dictionary of three million entries is built before the corrected queries are timed
        var warm = Stopwatch.StartNew();
        words.WarmUpTypoDictionary();
        Report($"typo dictionary of {entries:N0} entries: {warm.Elapsed.TotalSeconds:F1} s");
        var (corrected, _) = MeasureQueries(words, corpus.Typos(Queries / 2), typoTolerance: true);
        budget.AtMost("query with correction p95", "3 M entries, a beginning of 5–9 letters with one typo", Percentile(corrected, 95), 35.0, "ms");

        budget.AtMost("single write p95", "Upsert / UpdateWeight / Remove, own transaction", Percentile(MeasureWrites(words, corpus), 95), 1.0, "ms");
        // §16 since the owner's decision of 2026-11-04: the build follows the disk — 172–206 s on two machines (#32)
        budget.AtMost("build", "3 M entries, Embedded, a first load by Sync", build.Elapsed.TotalSeconds, 240, "s");

        budget.Verify();
    }

    [PerformanceFact]
    public void The_typo_dictionary_of_the_whole_word_list()
    {
        var words = Words();
        var budget = new PerformanceBudget(output, words.Count >= TestFiles.FullWordListCount - 1_000, PerformanceBudget.CurrentMachine());
        budget.SayTheMachine();

        using var database = new TestDatabase();
        using var catalog = database.Open(new LuiCatalogOptions { CacheSizeMegabytes = 512 });
        var index = catalog.Create("words", index => index.SingleWord().TypoTolerance());
        index.Sync(Corpus.Generate(words, words.Count, CorpusKind.SingleWord).Entries(), new LuiSyncOptions { BatchSize = 100_000 });

        var before = GC.GetTotalMemory(forceFullCollection: true);
        var build = Stopwatch.StartNew();
        index.WarmUpTypoDictionary();
        build.Stop();
        var after = GC.GetTotalMemory(forceFullCollection: true);
        GC.KeepAlive(index);

        Report($"typo dictionary of {words.Count:N0} words: {build.Elapsed.TotalSeconds:F1} s, {(after - before) / 1_048_576.0:F0} MB");
        budget.AtMost("typo dictionary, memory", $"{words.Count:N0} distinct words", (after - before) / 1_048_576.0, 200, "MB");
        budget.AtMost("typo dictionary, build", $"{words.Count:N0} distinct words", build.Elapsed.TotalSeconds, 10, "s");

        budget.Verify();
    }

    [PerformanceFact]
    public void The_file_of_a_million_titles()
    {
        var entries = Entries(TitlesScenario);
        var budget = new PerformanceBudget(output, entries >= TitlesScenario, PerformanceBudget.CurrentMachine());
        budget.SayTheMachine();

        using var database = new TestDatabase();
        using var catalog = database.Open(new LuiCatalogOptions { CacheSizeMegabytes = 512 });
        var titles = catalog.Create("titles", index => index.MultiWord());
        var build = Stopwatch.StartNew();
        titles.Sync(Corpus.Generate(Words(), entries, CorpusKind.MultiWord).Entries(), new LuiSyncOptions { BatchSize = 100_000 });
        build.Stop();

        var megabytes = database.FileMegabytes();
        Report($"titles: {entries:N0} by Sync in {build.Elapsed.TotalSeconds:F1} s, file {megabytes:N0} MB");
        // §16 since the owner's decision on ADR-0011/0020: titles of 36 characters on average, the text and its forms
        budget.AtMost("file size", "1 M titles of 36 characters on average, MultiWord, the content table included", megabytes, 500, "MB");

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

    /// <summary>The single writes of §16, each in a transaction of its own: an upsert, a weight, a removal (and the entry back).</summary>
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

    private static int Entries(int scenario) =>
        int.Parse(Environment.GetEnvironmentVariable("LUI_PERF_ENTRIES") ?? scenario.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    private IReadOnlyList<string> Words()
    {
        var words = TestFiles.MeasurementWords(out var corpus);
        Report($"corpus: {corpus} ({words.Count:N0} words), {Environment.ProcessorCount} cores, .NET {Environment.Version}");
        return words;
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
