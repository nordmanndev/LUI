// Probe: why does the benchmark report "Hits = 0" for the correction cases while the same query on the same
// corpus finds 72..402 candidates?
//
// This probe replicates the fixture of the benchmark (IndexFixture.cs) exactly — including the repository's own
// CorpusFile — and runs the four correction queries, printing the state of the typo dictionary before and after
// the warm-up. It also runs the same query on a corpus read by hand (the same file, the same prefix) to see
// whether the corpus reader makes the difference.

using Microsoft.Data.Sqlite;
using Nordmann.LUI;
using Nordmann.LUI.Testing;

var corpusName = "words";
var entries = 200_000;

void Show(string label, ILuiIndex index)
{
    Console.WriteLine($"\n{label} (dictionary: {index.TypoDictionaryState})");
    foreach (var input in new[] { "kartofel", "bahmhof", "fusbal", "strasee" })
    {
        var result = index.Query(input)
            .Candidates(500)
            .Rank(LuiRankingOrder.WordStartThenSimilarity)
            .Similarity(LuiSimilarity.JaroWinkler)
            .TypoTolerance(true)
            .Execute();
        Console.WriteLine($"  '{input}': {result.CandidatesConsidered} candidates, " +
                          $"corrected: {result.CorrectedQuery ?? "—"}, skipped: {result.CorrectionSkipped}");
    }
}

// ---- the fixture of the benchmark, with the corpus of the repository ---------------------------------------

var corpus = Corpus.From(CorpusFile.Open("words.txt", entries, 0));
Console.WriteLine($"the corpus of the repository: {corpus.Count} entries, kind {corpus.Kind}");

using var database = new TestDatabase();
using var catalog = database.Open(new LuiCatalogOptions { CacheSizeMegabytes = 512, MemoryMapMegabytes = 2048 });
var index = catalog.Create("bench", builder => builder.SingleWord().TypoTolerance());
index.Sync(corpus.Entries(), new LuiSyncOptions { BatchSize = 100_000 });

Show("the fixture of the benchmark, before the warm-up:", index);

index.WarmUpTypoDictionary();
Show("the fixture of the benchmark, after the warm-up:", index);

// ---- the same, with the corpus read by hand ---------------------------------------------------------------

var byHand = new List<(long Key, string Text, int Weight)>();
await using (var stream = new System.IO.Compression.GZipStream(
    File.OpenRead("/workspace/LUI/tests/TestData/corpora/words.txt.gz"), System.IO.Compression.CompressionMode.Decompress))
using (var reader = new StreamReader(stream))
{
    while (await reader.ReadLineAsync() is { } line && line.Length > 0) { }
    while (await reader.ReadLineAsync() is { } line)
    {
        if (line.Length == 0) continue;
        var parts = line.Split('\t');
        byHand.Add((byHand.Count + 1, parts[0], int.Parse(parts[1])));
        if (byHand.Count >= entries) break;
    }
}
Console.WriteLine($"\nthe corpus read by hand: {byHand.Count} entries");

using var database2 = new TestDatabase();
using var catalog2 = database2.Open(new LuiCatalogOptions { CacheSizeMegabytes = 512, MemoryMapMegabytes = 2048 });
var index2 = catalog2.Create("bench", builder => builder.SingleWord().TypoTolerance());
index2.Sync(byHand.Select(entry => new LuiEntry(entry.Key, entry.Text) { Weight = entry.Weight }),
            new LuiSyncOptions { BatchSize = 100_000 });
index2.WarmUpTypoDictionary();
Show("the same corpus, read by hand:", index2);
