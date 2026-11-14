// Probe: the correction path of LUI, measured with the library itself.
//
// The question: the committed BenchmarkDotNet report of the CorrectionBenchmarks says "Hits = 0" for all four
// inputs — the queries ranked no candidate at all. The SymSpell reproduction (audit/symspell_probe) says the
// lookups succeed for those very inputs. Which of the two is right?
//
// The corpus is the repository's own file (the first 200 000 entries of words.txt.gz), the index is built with
// the bulk path of Sync (the fixture of the benchmark), the dictionary is warmed up (the fixture of the
// benchmark), and the query is the one of the benchmark.
//
// The database file goes to /tmp — the workspace is a network mount.

using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using Nordmann.LUI;

const string Corpus = "/workspace/LUI/tests/TestData/corpora/words.txt.gz";
const int Entries = 200_000;

var dbPath = Path.Combine(Path.GetTempPath(), $"lui-probe-{Guid.NewGuid():N}.db");
Console.WriteLine($"database: {dbPath}");

var entries = new List<LuiEntry>();
await using (var stream = new GZipStream(File.OpenRead(Corpus), CompressionMode.Decompress))
using (var reader = new StreamReader(stream, Encoding.UTF8))
{
    while (await reader.ReadLineAsync() is { } line && line.Length > 0) { }   // the header block
    while (await reader.ReadLineAsync() is { } line)
    {
        if (line.Length == 0) continue;
        var parts = line.Split('\t');
        entries.Add(new LuiEntry(entries.Count + 1, parts[0]) { Weight = int.Parse(parts[1]) });
        if (entries.Count >= Entries) break;
    }
}
Console.WriteLine($"{entries.Count} entries read");

using var catalog = LuiCatalog.Open($"Data Source={dbPath}", new LuiCatalogOptions
{
    CacheSizeMegabytes = 512,
    MemoryMapMegabytes = 2048,
});

var index = catalog.Create("words", builder => builder.SingleWord().TypoTolerance());

var build = Stopwatch.StartNew();
index.Sync(entries, new LuiSyncOptions { BatchSize = 100_000 });
build.Stop();
Console.WriteLine($"the index of {entries.Count} entries is built in {build.Elapsed.TotalSeconds:F1} s");

var warm = Stopwatch.StartNew();
index.WarmUpTypoDictionary();
warm.Stop();
Console.WriteLine($"the typo dictionary is built in {warm.Elapsed.TotalSeconds:F1} s\n");

Console.WriteLine("| input | candidates ranked | corrected query | correction skipped | hits |");
Console.WriteLine("|---|---:|---|---|---:|");
foreach (var input in new[] { "kartofel", "bahmhof", "fusbal", "strasee", "apfelkuch", "haus", "b", "ka" })
{
    var result = index.Query(input).Candidates(500).TypoTolerance(true).Execute();
    Console.WriteLine($"| `{input}` | {result.CandidatesConsidered} | {result.CorrectedQuery ?? "—"} | " +
                      $"{result.CorrectionSkipped} | {result.Hits.Count} |");
}

// the same, without the correction
Console.WriteLine("\nwithout the correction:");
foreach (var input in new[] { "kartofel", "bahmhof", "fusbal", "strasee" })
{
    var result = index.Query(input).Candidates(500).TypoTolerance(false).Execute();
    Console.WriteLine($"  `{input}`: candidates {result.CandidatesConsidered}, hits {result.Hits.Count}");
}
