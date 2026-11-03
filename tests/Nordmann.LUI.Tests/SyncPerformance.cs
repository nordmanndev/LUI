using System.Diagnostics;
using System.Globalization;
using Shouldly;
using Xunit.Abstractions;

namespace Nordmann.LUI.Tests;

/// <summary>
/// What a sync of an <c>Owned</c> index costs (ADR-0016, spec §20 step 7: "Abgleich mit 1 Mio. Einträgen, Abbruch und
/// Wiederaufsetzen"): the first sync of an empty index, the same sync again (nothing changes), a sync cancelled halfway
/// and run again, a sync with a small change, the check against the source, the check of the index and the rebuild —
/// and the queries after the first sync, against <c>UpsertMany</c> of the same entries.
/// <code>LUI_PERF=1 LUI_PERF_ENTRIES=1000000 dotnet test --filter SyncPerformance --logger "console;verbosity=detailed"</code>
/// </summary>
public sealed class SyncPerformance(ITestOutputHelper output)
{
    private const int Queries = 2_000;

    [PerformanceFact]
    public void Sync_cancel_resume_check_and_rebuild()
    {
        var entries = int.Parse(Environment.GetEnvironmentVariable("LUI_PERF_ENTRIES") ?? "200000", CultureInfo.InvariantCulture);
        var batch = int.Parse(Environment.GetEnvironmentVariable("LUI_PERF_BATCH") ?? "10000", CultureInfo.InvariantCulture);
        var root = Testing.Repository.RootPath;
        var full = Path.Combine(root, "artifacts", "wordlist", "german-words.txt");
        var path = File.Exists(full) ? full : Path.Combine(root, "tests", "Nordmann.LUI.TestData", "wordlist", "german-words.sample.txt");
        List<string> words = [.. File.ReadLines(path).Where(w => w.Length >= 3)];
        Report($"corpus: {Path.GetRelativePath(root, path)} ({words.Count:N0} words), {entries:N0} entries, batch {batch:N0}");

        var random = new Random(42);
        var source = Enumerable.Range(1, entries)
            .Select(i => new LuiEntry<long, LuiNoPayload>(i, i <= words.Count ? words[i - 1] : words[random.Next(words.Count)] + words[random.Next(words.Count)].ToLowerInvariant())
            {
                Weight = random.Next(0, 1_000_000),
            })
            .OrderBy(_ => random.Next())
            .ToList();
        var options = new LuiSyncOptions { BatchSize = batch };

        using var database = new TestDatabase();
        using var catalog = database.Open(new LuiCatalogOptions { CacheSizeMegabytes = 512 });
        var index = catalog.Index("words").SingleWord().Ensure();

        var report = Timed("sync, empty index", () => index.Sync(source, options));
        Report($"  {report}");
        var (synced50, synced95) = QueryTimes(index, Inputs(source, random));

        Timed("the same sync again", () => index.Sync(source, options)).Unchanged.ShouldBe((long)entries);

        // halfway: cancelled, then run again
        using (var cancellation = new CancellationTokenSource())
        {
            var changed = source.Select((entry, i) => i % 2 == 0 ? entry with { Weight = entry.Weight / 2 } : entry).ToList();
            Timed("sync of 50 % changes, cancelled halfway", () =>
                Should.Throw<OperationCanceledException>(() => index.Sync(CancelAt(changed, entries / 2, cancellation), options, cancellation.Token)));
            Report($"  {Timed("the sync run again", () => index.Sync(changed, options))}");
            source = changed;
        }

        var small = source.Take(entries - 100).Select((entry, i) => i < 100 ? entry with { Weight = entry.Weight + 1 } : entry).ToList();
        Report($"  {Timed("sync of 100 changes and 100 removals", () => index.Sync(small, options))}");

        Timed("check against the source", () => index.Check(small)).IsHealthy.ShouldBeTrue();
        Timed("check of the index", () => index.Check()).IsHealthy.ShouldBeTrue();
        Timed("rebuild", () =>
        {
            index.Rebuild();
            return 0;
        });

        var upserted = catalog.Index("upserted").SingleWord().Ensure();
        Timed("UpsertMany of the same entries", () => upserted.UpsertMany(source));
        var (upserted50, upserted95) = QueryTimes(upserted, Inputs(source, random));
        Report($"query after the first sync p50/p95 {synced50:F2}/{synced95:F2} ms · after UpsertMany {upserted50:F2}/{upserted95:F2} ms");
    }

    private static IEnumerable<LuiEntry<long, LuiNoPayload>> CancelAt(
        List<LuiEntry<long, LuiNoPayload>> source,
        int count,
        CancellationTokenSource cancellation)
    {
        for (var i = 0; i < source.Count; i++)
        {
            if (i == count)
            {
                cancellation.Cancel();
            }

            yield return source[i];
        }
    }

    private static List<string> Inputs(List<LuiEntry<long, LuiNoPayload>> source, Random random) =>
        [.. Enumerable.Range(0, Queries).Select(_ =>
        {
            var text = source[random.Next(source.Count)].Text;
            return text[..Math.Min(text.Length, random.Next(1, 9))];
        })];

    private static (double P50, double P95) QueryTimes(ILuiIndex<long, LuiNoPayload> index, List<string> inputs)
    {
        foreach (var input in inputs.Take(200))
        {
            index.Search(input);
        }

        var times = inputs.Select(input =>
        {
            var started = Stopwatch.GetTimestamp();
            index.Search(input);
            return Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        }).Order().ToArray();
        return (times[(int)(times.Length * 0.50)], times[(int)(times.Length * 0.95)]);
    }

    private T Timed<T>(string what, Func<T> run)
    {
        var clock = Stopwatch.StartNew();
        var result = run();
        Report($"{what,-40} {clock.Elapsed.TotalSeconds,6:F1} s");
        return result;
    }

    private void Report(string line)
    {
        output.WriteLine(line);
        Console.Error.WriteLine("[perf] " + line);
    }
}
