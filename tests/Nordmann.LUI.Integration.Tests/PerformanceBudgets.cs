using System.Diagnostics;
using System.Globalization;
using Shouldly;
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

    /// <summary>
    /// The targets that are missed on purpose, with the reason and the record that holds it. A target that is not here
    /// has to be met.
    /// </summary>
    private static readonly Dictionary<string, string> Waivers = new(StringComparer.Ordinal)
    {
        // measured 9.6–10.3 ms; it grows with the input: p95 1.2 ms at one character, 17 ms at eight — FTS5's own cost
        // of a long trigram expression over three million entries. The options are the owner's (ADR-0010, Proposed).
        ["query p95"] = "ADR-0010 (owner's decision): FTS5's cost of long inputs at 3 M",

        // measured 298 KB; it follows the candidates read: a query that spends the budget of 500 allocates p95 306 KB,
        // one that does not 173 KB, and at three million entries 60 % spend it. The lever is the budget (ADR-0020).
        ["allocation p95"] = "ADR-0020: the allocation follows the candidates read; the budget is the lever",

        // measured 28.5–29.0 ms; up to three corrections, each one more FTS5 statement of the cost above (ADR-0012:
        // 8.7 ms at 685,000 entries) — the same lever as the query without a correction.
        ["query with correction p95"] = "ADR-0010, ADR-0012: up to three more FTS5 statements at 3 M",

        // measured 457 MB (ADR-0011: 445 MB) — titles of 36 characters on average, the table holds the text and two
        // generated forms, the search index the compact form.
        ["file size"] = "ADR-0011: the content table holds the text and its forms",
    };

    [PerformanceFact]
    public void The_single_word_index_at_three_million_entries()
    {
        var entries = Entries(Scenario);
        var corpus = Corpus.Generate(Words(), entries, CorpusKind.SingleWord);
        var budget = new Budget(output, entries >= Scenario);

        using var database = new TestDatabase();
        using var catalog = database.Open(new LuiCatalogOptions { CacheSizeMegabytes = 512, MemoryMapMegabytes = 2048 });
        var index = catalog.Index("words").SingleWord().TypoTolerance().Ensure();

        // the build: the bulk path of a sync into an empty index (ADR-0016); LUI_PERF_BATCH measures another batch
        // the first load of a file no other process writes: the largest batch, the fewest commits (ADR-0016, ADR-0020)
        var batch = int.Parse(Environment.GetEnvironmentVariable("LUI_PERF_BATCH") ?? "1000000", CultureInfo.InvariantCulture);
        var build = Stopwatch.StartNew();
        index.Sync(corpus.Entries(), new LuiSyncOptions { BatchSize = batch });
        build.Stop();
        Report($"build: {entries:N0} entries by Sync (batches of {batch:N0}) in {build.Elapsed.TotalSeconds:F1} s, file {database.FileMegabytes():N0} MB");

        var (latencies, allocations) = MeasureQueries(index, corpus.Inputs(Queries), typoTolerance: false);
        budget.AtMost("query p50", "3 M entries, inputs of 1–8 characters, no correction", Percentile(latencies, 50), 2.0, "ms");
        budget.AtMost("query p95", "3 M entries, inputs of 1–8 characters, no correction", Percentile(latencies, 95), 5.0, "ms");
        budget.AtMost("allocation p95", "per query, the same inputs", Percentile(allocations, 95) / 1024, 250, "KB");

        // the typo dictionary of three million entries is built before the corrected queries are timed
        var warm = Stopwatch.StartNew();
        index.WarmUpTypoDictionary();
        Report($"typo dictionary of {entries:N0} entries: {warm.Elapsed.TotalSeconds:F1} s");
        var (corrected, _) = MeasureQueries(index, corpus.Typos(Queries / 2), typoTolerance: true);
        budget.AtMost("query with correction p95", "3 M entries, a beginning of 5–9 letters with one typo", Percentile(corrected, 95), 10.0, "ms");

        budget.AtMost("single write p95", "Upsert / UpdateWeight / Remove, own transaction", Percentile(MeasureWrites(index, corpus), 95), 1.0, "ms");
        budget.AtMost("build", "3 M entries, Owned", build.Elapsed.TotalSeconds, 180, "s");

        budget.Verify();
    }

    [PerformanceFact]
    public void The_typo_dictionary_of_the_whole_word_list()
    {
        var words = Words();
        var budget = new Budget(output, words.Count >= TestCorpus.FullWordListCount - 1_000);

        using var database = new TestDatabase();
        using var catalog = database.Open(new LuiCatalogOptions { CacheSizeMegabytes = 512 });
        var index = catalog.Index("words").SingleWord().TypoTolerance().Ensure();
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
        var budget = new Budget(output, entries >= TitlesScenario);

        using var database = new TestDatabase();
        using var catalog = database.Open(new LuiCatalogOptions { CacheSizeMegabytes = 512 });
        var index = catalog.Index("titles").MultiWord().Ensure();
        var build = Stopwatch.StartNew();
        index.Sync(Corpus.Generate(Words(), entries, CorpusKind.MultiWord).Entries(), new LuiSyncOptions { BatchSize = 100_000 });
        build.Stop();

        var megabytes = database.FileMegabytes();
        Report($"titles: {entries:N0} by Sync in {build.Elapsed.TotalSeconds:F1} s, file {megabytes:N0} MB");
        budget.AtMost("file size", "1 M entries, MultiWord, the content table included", megabytes, 300, "MB");

        budget.Verify();
    }

    private (double[] Latencies, double[] Allocations) MeasureQueries(ILuiIndex<long, LuiNoPayload> index, IReadOnlyList<string> inputs, bool typoTolerance)
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
    private static double[] MeasureWrites(ILuiOwnedIndex<long, LuiNoPayload> index, Corpus corpus)
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

    private static int Entries(int scenario) =>
        int.Parse(Environment.GetEnvironmentVariable("LUI_PERF_ENTRIES") ?? scenario.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    private IReadOnlyList<string> Words()
    {
        var words = TestCorpus.MeasurementWords(out var corpus);
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

    /// <summary>The targets of one scenario: each one measured, then met, waived or missed.</summary>
    private sealed class Budget(ITestOutputHelper output, bool scenario)
    {
        private readonly List<string> _missed = [];

        internal void AtMost(string target, string condition, double measured, double limit, string unit)
        {
            var met = measured <= limit;
            var waiver = Waivers.GetValueOrDefault(target);
            var verdict = (met, waiver, scenario) switch
            {
                (_, _, false) => "reported (not the scenario)",
                (true, null, _) => "met",
                (true, _, _) => "met — the waiver can go",
                (false, null, _) => "MISSED",
                (false, _, _) => $"waived: {waiver}",
            };

            var line = string.Create(CultureInfo.InvariantCulture, $"budget | {target,-28} | {condition,-58} | {measured,8:F2} {unit,-2} | ≤ {limit} {unit} | {verdict}");
            output.WriteLine(line);
            Console.Error.WriteLine("[perf] " + line);

            if (scenario && !met && waiver is null)
            {
                _missed.Add($"{target}: {measured:F2} {unit} > {limit} {unit} ({condition})");
            }
        }

        internal void Verify() =>
            _missed.ShouldBeEmpty("A target of §16 that is missed is fixed or waived with a reason and a record (ADR-0020) — never lowered quietly.");
    }
}
