// Probe: what does a correction that actually corrects cost?
//
// The CorrectionBenchmarks measure a fixture in which the index has no typo tolerance
// (IndexFixture.CreateEmbedded creates the index without .TypoTolerance()), so the dictionary is never built
// ("NotBuilt" after the warm-up) and the query answers with what the input found — nothing.
//
// This probe builds the index the way the fixture would build it IF it asked for the typo tolerance, warms the
// dictionary up, and measures both paths: the query without the correction and the query with it.
// The median of 101 runs, the database on a local disk, 200 000 entries of the corpus of the repository.

using System.Diagnostics;
using Nordmann.LUI;
using Nordmann.LUI.Testing;

const int Entries = 200_000;
const int Runs = 101;

var corpus = Corpus.From(CorpusFile.Open("words.txt", Entries, 0));

using var database = new TestDatabase();
using var catalog = database.Open(new LuiCatalogOptions { CacheSizeMegabytes = 512, MemoryMapMegabytes = 2048 });
var index = catalog.Create("bench", builder => builder.SingleWord().TypoTolerance());
index.Sync(corpus.Entries(), new LuiSyncOptions { BatchSize = 100_000 });
index.WarmUpTypoDictionary();
Console.WriteLine($"the index of {corpus.Count} entries, dictionary {index.TypoDictionaryState}");

static double Percentile(double[] values, double percentile)
{
    var sorted = values.Order().ToArray();
    return sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(sorted.Length * percentile / 100.0) - 1)];
}

void Measure(string label, string input, bool correction)
{
    // warm up
    for (var i = 0; i < 20; i++)
    {
        index.Query(input).Candidates(500).Rank(LuiRankingOrder.WordStartThenSimilarity)
            .Similarity(LuiSimilarity.JaroWinkler).TypoTolerance(correction).Execute();
    }

    var latencies = new double[Runs];
    var allocations = new double[Runs];
    var candidates = 0L;
    for (var i = 0; i < Runs; i++)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        var result = index.Query(input).Candidates(500).Rank(LuiRankingOrder.WordStartThenSimilarity)
            .Similarity(LuiSimilarity.JaroWinkler).TypoTolerance(correction).Execute();
        latencies[i] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        allocations[i] = GC.GetAllocatedBytesForCurrentThread() - before;
        candidates = result.CandidatesConsidered;
    }

    Console.WriteLine($"  {label,-34} p50 {Percentile(latencies, 50),8:F3} ms   p95 {Percentile(latencies, 95),8:F3} ms   " +
                      $"alloc p50 {Percentile(allocations, 50) / 1024,7:F1} KB   candidates {candidates}");
}

Console.WriteLine("\nthe same query with and without the correction (200 000 entries, median of 101 runs):");
foreach (var input in new[] { "kartofel", "bahmhof", "fusbal", "strasee" })
{
    Measure($"'{input}' without the correction", input, false);
    Measure($"'{input}' with the correction", input, true);
}

Console.WriteLine("\nfor comparison: the uncorrected paths");
foreach (var input in new[] { "apfelkuch", "haus" })
{
    Measure($"'{input}' without the correction", input, false);
}
