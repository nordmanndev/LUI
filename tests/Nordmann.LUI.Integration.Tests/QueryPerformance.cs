using System.Diagnostics;
using System.Globalization;
using Shouldly;
using Xunit.Abstractions;

namespace Nordmann.LUI.Integration.Tests;

/// <summary>
/// The budgets of LUI-Spezifikation §16 for a single-word index: latency and
/// allocation of a query, the cost of a single write, the build.
/// <code>LUI_PERF=1 LUI_PERF_ENTRIES=3000000 dotnet test --filter QueryPerformance</code>
/// </summary>
public sealed class QueryPerformance(ITestOutputHelper output)
{
    private const int Queries = 2_000;

    /// <summary><c>LUI_PERF_KIND=multi</c>: titles of two to four words and inputs of one or two words.</summary>
    private static readonly bool Multi =
        string.Equals(Environment.GetEnvironmentVariable("LUI_PERF_KIND"), "multi", StringComparison.Ordinal);

    [PerformanceFact]
    public void A_query_over_the_corpus_stays_within_the_budget()
    {
        var entries = int.Parse(Environment.GetEnvironmentVariable("LUI_PERF_ENTRIES") ?? "200000", CultureInfo.InvariantCulture);
        // LUI_PERF_DB keeps the database between runs: the build of three million entries takes minutes
        using var database = new TestDatabase(Environment.GetEnvironmentVariable("LUI_PERF_DB"));
        using var catalog = database.Open(new LuiCatalogOptions { CacheSizeMegabytes = 512, MemoryMapMegabytes = 2048 });
        var tags = Multi
            ? catalog.Create("tags", index => index.MultiWord())
            : catalog.Create("tags", index => index.SingleWord());

        if (string.Equals(Environment.GetEnvironmentVariable("LUI_PERF_OPTIMIZE"), "1", StringComparison.Ordinal))
        {
            using var optimize = catalog.Database.OpenConnection();
            using var command = optimize.CreateCommand();
            command.CommandText = "INSERT INTO lui_tags_fts(lui_tags_fts) VALUES ('optimize');";
            var merged = Stopwatch.StartNew();
            command.ExecuteNonQuery();
            Report($"optimize: {merged.Elapsed.TotalSeconds:F1} s");
        }

        var build = Stopwatch.StartNew();
        var texts = Build(catalog, tags, Words(), entries);
        build.Stop();
        Report($"build: {entries:N0} entries in {build.Elapsed.TotalSeconds:F1} s ({entries / build.Elapsed.TotalSeconds:N0}/s), file {new FileInfo(database.FilePath).Length / 1_048_576:N0} MB");

        var (latencies, allocations) = MeasureQueries(tags, Inputs(texts, new Random(7)));
        MeasureWrites(tags, texts, entries);

        // The allocation budget is a property of the query, not of the corpus:
        // it does not grow with the number of entries, so it is asserted at
        // every size. A measurement that asserts nothing is a measurement
        // that cannot fail.
        Percentile(allocations, 50).ShouldBeLessThanOrEqualTo(250 * 1024);

        // The latency budget of §16 is stated for three million entries.
        // Below that the numbers are reported, not claimed.
        if (entries >= 3_000_000)
        {
            Percentile(latencies, 50).ShouldBeLessThanOrEqualTo(2.0);
            Percentile(latencies, 95).ShouldBeLessThanOrEqualTo(5.0);
            build.Elapsed.ShouldBeLessThanOrEqualTo(TimeSpan.FromMinutes(3));
        }
    }

    private (double[] Latencies, double[] Allocations) MeasureQueries(ILuiEmbeddedIndex index, List<string> inputs)
    {
        // warm the page cache and the JIT, as a running application would be
        foreach (var input in inputs.Take(500))
        {
            index.Query(input).Execute();
        }

        var latencies = new double[inputs.Count];
        var allocations = new double[inputs.Count];
        for (var i = 0; i < inputs.Count; i++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var started = Stopwatch.GetTimestamp();
            index.Query(inputs[i]).Execute();
            latencies[i] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            allocations[i] = GC.GetAllocatedBytesForCurrentThread() - before;
        }

        Report($"query: p50 {Percentile(latencies, 50):F2} ms, p95 {Percentile(latencies, 95):F2} ms, max {latencies.Max():F2} ms");
        Report($"alloc: p50 {Percentile(allocations, 50) / 1024:F0} KB, p95 {Percentile(allocations, 95) / 1024:F0} KB");

        foreach (var length in Enumerable.Range(1, 12))
        {
            var group = inputs.Select((input, i) => (input.Length, latencies[i])).Where(x => x.Length == length).Select(x => x.Item2).ToArray();
            if (group.Length > 0)
            {
                Report($"  {length} chars: p50 {Percentile(group, 50):F2} ms, p95 {Percentile(group, 95):F2} ms ({group.Length} queries)");
            }
        }

        foreach (var i in Enumerable.Range(0, inputs.Count).OrderByDescending(i => latencies[i]).Take(6))
        {
            var slow = index.Query(inputs[i]).TrackTimings().Execute();
            Report($"  slow '{inputs[i]}': {latencies[i]:F1} ms; again {slow.Timings!.Total.TotalMilliseconds:F1} ms "
                + $"(candidates {slow.Timings.CandidateRead.TotalMilliseconds:F1}, ranking {slow.Timings.Ranking.TotalMilliseconds:F1}, "
                + $"payload {slow.Timings.Payload.TotalMilliseconds:F1}); {slow.CandidatesConsidered} candidates{(slow.Truncated ? " (truncated)" : string.Empty)}");
        }

        return (latencies, allocations);
    }

    private void MeasureWrites(ILuiEmbeddedIndex index, List<string> texts, int entries)
    {
        var writes = new double[500];
        var random = new Random(11);
        for (var i = 0; i < writes.Length; i++)
        {
            var key = random.Next(1, entries);
            var started = Stopwatch.GetTimestamp();
            switch (i % 3)
            {
                case 0:
                    index.UpdateWeight(key, random.Next(0, 1_000_000));
                    break;
                case 1:
                    index.Upsert(key, texts[key - 1], random.Next(0, 1_000_000));
                    break;
                default:
                    index.UpdatePriority(key, i % 2 == 0 ? LuiPriority.High : LuiPriority.Normal);
                    break;
            }

            writes[i] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        }

        Report($"write: p50 {Percentile(writes, 50):F2} ms, p95 {Percentile(writes, 95):F2} ms");
    }

    private void Report(string line)
    {
        output.WriteLine(line);
        Console.Error.WriteLine("[perf] " + line);
    }

    /// <summary>
    /// The words of the corpus: the whole German word list when <c>tools/fetch-wordlist.sh</c> fetched it, otherwise
    /// the sample in the repository. Which one it was is reported — a worktree has no <c>artifacts/</c>, and two
    /// measurements on two corpora are not a comparison (review of pull request 14, N5).
    /// </summary>
    private IReadOnlyList<string> Words()
    {
        var words = TestFiles.MeasurementWords(out var corpus);
        Report($"corpus: {corpus} ({words.Count:N0} words)");
        return words;
    }

    /// <summary>The words first, then compounds of two words — German is full of them.</summary>
    private static List<string> Build(LuiCatalog catalog, ILuiEmbeddedIndex index, IReadOnlyList<string> words, int entries)
    {
        var random = new Random(42);
        var texts = new List<string>(entries);
        // the build runs on a connection with the catalog's cache, as an application would set it up
        using var connection = catalog.Database.OpenConnection();
        using var transaction = connection.BeginTransaction();

        for (var i = 1; i <= entries; i++)
        {
            var text = Multi
                ? Title(words, random)
                : i <= words.Count
                    ? words[i - 1]
                    : Capitalize(words[random.Next(words.Count)]) + words[random.Next(words.Count)].ToLowerInvariant();
            texts.Add(text);

            var weight = Weight(random);
            index.Upsert(i, text, weight, transaction: transaction);
        }

        transaction.Commit();
        return texts;
    }

    /// <summary>
    /// <c>LUI_PERF_WEIGHTS=sparse</c>: half of the entries have weight 0, as in the stock of Anhang A1 —
    /// most tags are used never or once. Default: a long tail of distinct weights, which is the
    /// harder case for the rowid (ADR-0010).
    /// </summary>
    private static int Weight(Random random)
    {
        var sparse = string.Equals(Environment.GetEnvironmentVariable("LUI_PERF_WEIGHTS"), "sparse", StringComparison.Ordinal);
        if (sparse && random.NextDouble() < 0.5)
        {
            return 0;
        }

        // a long tail: most entries are unpopular, a few are very popular
        return (int)Math.Min(536_870_911, 1 + (long)(random.NextDouble() * random.NextDouble() * random.NextDouble() * 1_000_000));
    }

    /// <summary>A title of two to four words, each from the word list.</summary>
    private static string Title(IReadOnlyList<string> words, Random random) =>
        string.Join(' ', Enumerable.Range(0, random.Next(2, 5)).Select(_ => Capitalize(words[random.Next(words.Count)])));

    private static string Capitalize(string word) => char.ToUpperInvariant(word[0]) + word[1..];

    /// <summary>What users type: the beginning of an entry, or a piece out of its middle, 1 … 8 characters.</summary>
    private static List<string> Inputs(List<string> texts, Random random)
    {
        var inputs = new List<string>(Queries);
        while (inputs.Count < Queries)
        {
            var text = texts[random.Next(texts.Count)];
            if (Multi && random.Next(3) == 0)
            {
                // two words of the title, a few letters of each, in either order
                var parts = text.Split(' ').Select(w => w[..Math.Min(w.Length, random.Next(3, 6))]).Take(2).ToArray();
                if (random.Next(2) == 0)
                {
                    Array.Reverse(parts);
                }

                inputs.Add(string.Join(' ', parts));
                continue;
            }

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

    private static double Percentile(double[] values, double percentile)
    {
        var sorted = values.Order().ToArray();
        return sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(sorted.Length * percentile / 100.0) - 1)];
    }
}
