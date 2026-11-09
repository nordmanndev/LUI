using System.Diagnostics;
using System.Globalization;
using Xunit.Abstractions;

namespace Nordmann.LUI.Integration.Tests.Performance;

/// <summary>
/// What the typo tolerance costs and how good it is on a large stock (LUI-Spezifikation §16: dictionary of
/// 700,000 words ≤ 200 MB and ≤ 10 s, corrected query p95 ≤ 10 ms; Anhang A4).
/// <code>LUI_PERF=1 dotnet test --filter TypoPerformance --logger "console;verbosity=detailed"</code>
/// Needs <c>tools/fetch-wordlist.sh</c>.
/// </summary>
public sealed class TypoPerformance(ITestOutputHelper output)
{
    [PerformanceFact]
    public void The_dictionary_of_the_whole_word_list_and_the_corrected_queries()
    {
        var words = File.ReadLines(TestFiles.FullWordListPath)
            .Where(w => w.Length >= 3 && w.All(char.IsLetter)).Distinct(StringComparer.Ordinal).ToList();
        var limit = int.Parse(Environment.GetEnvironmentVariable("LUI_PERF_ENTRIES") ?? words.Count.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        words = [.. words.Take(limit)];

        using var database = new TestDatabase();
        using var catalog = database.Open(new LuiCatalogOptions { CacheSizeMegabytes = 512, MemoryMapMegabytes = 2048 });
        var wordIndex = catalog.Create("words", index => index.SingleWord().TypoTolerance());

        var random = new Random(5);
        using (var connection = catalog.Database.OpenConnection())
        {
            using var transaction = connection.BeginTransaction();
            for (var i = 0; i < words.Count; i++)
            {
                wordIndex.Upsert(i + 1, words[i], random.Next(0, 1_000_000), transaction: transaction);
            }

            transaction.Commit();
        }

        Report($"index: {words.Count:N0} words");

        // the warm-up builds the dictionary (ADR-0042): a query never does
        GC.Collect();
        var before = GC.GetTotalMemory(forceFullCollection: true);
        var build = Stopwatch.StartNew();
        wordIndex.WarmUpTypoDictionary();
        build.Stop();
        GC.Collect();
        var after = GC.GetTotalMemory(forceFullCollection: true);
        Report($"dictionary: built in {build.Elapsed.TotalSeconds:F1} s, {(after - before) / 1_048_576.0:F0} MB");

        Quality(wordIndex, words, random);
        Latency(wordIndex, words, random);
    }

    private void Quality(ILuiEmbeddedIndex index, List<string> words, Random random)
    {
        foreach (var length in new[] { 4, 5, 6, 7 })
        {
            var cases = words.Where(w => w.Length >= length).OrderBy(_ => random.Next()).Take(400).ToList();
            int clean = 0, typo = 0;
            foreach (var word in cases)
            {
                var beginning = word[..length].ToLowerInvariant();
                clean += Found(index, beginning, word, false) ? 1 : 0;
                typo += Found(index, TypoMaker.WithOneTypo(beginning, random), word, true) ? 1 : 0;
            }

            Report($"  {length} letters: right {100.0 * clean / cases.Count:F0} %, with a typo {100.0 * typo / cases.Count:F0} % of {cases.Count} (Hit@20), ratio {100.0 * typo / Math.Max(1, clean):F0} %");
        }
    }

    private void Latency(ILuiEmbeddedIndex index, List<string> words, Random random)
    {
        var inputs = Enumerable.Range(0, 1_000).Select(_ =>
        {
            var word = words[random.Next(words.Count)];
            var length = Math.Min(word.Length, random.Next(5, 10));
            return TypoMaker.WithOneTypo(word[..length].ToLowerInvariant(), random);
        }).ToList();

        foreach (var input in inputs.Take(200))
        {
            index.Query(input).Execute();
        }

        var latencies = new List<double>();
        var allocations = new List<double>();
        var corrected = 0;
        foreach (var input in inputs)
        {
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var started = Stopwatch.GetTimestamp();
            var result = index.Query(input).Execute();
            latencies.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            allocations.Add(GC.GetAllocatedBytesForCurrentThread() - allocated);
            corrected += result.CorrectedQuery is null ? 0 : 1;
        }

        latencies.Sort();
        allocations.Sort();
        Report($"corrected queries: p50 {latencies[latencies.Count / 2]:F2} ms, p95 {latencies[(int)(latencies.Count * 0.95)]:F2} ms, max {latencies[^1]:F1} ms; "
            + $"alloc p50 {allocations[allocations.Count / 2] / 1024:F0} KB, p95 {allocations[(int)(allocations.Count * 0.95)] / 1024:F0} KB; corrected {corrected} of {inputs.Count}");
    }

    private static bool Found(ILuiEmbeddedIndex index, string typed, string word, bool typoTolerance) =>
        index.Query(typed).Take(20).TypoTolerance(typoTolerance).Execute().Hits
            .Any(h => string.Equals(h.Text, word, StringComparison.Ordinal));

    private void Report(string line)
    {
        output.WriteLine(line);
        Console.Error.WriteLine("[perf] " + line);
    }
}
