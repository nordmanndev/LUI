using System.Diagnostics;
using System.Globalization;
using Shouldly;
using Xunit.Abstractions;

namespace Nordmann.LUI.Integration.Tests;

/// <summary>
/// What a sync of an <c>Embedded</c> index costs (ADR-0016, spec §20 step 7: "Abgleich mit 1 Mio. Einträgen, Abbruch und
/// Wiederaufsetzen"): the first sync of an empty index, the same sync again (nothing changes), a sync cancelled halfway
/// and run again, a sync with a small change, the check against the source, the check of the index and the rebuild —
/// and the queries after the first sync, against <c>UpsertMany</c> of the same entries; and a delta sync beside a built
/// typo dictionary (ADR-0021).
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
        var words = TestCorpus.MeasurementWords(out var corpus);
        Report($"corpus: {corpus} ({words.Count:N0} words), {entries:N0} entries, batch {batch:N0}");

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
        var wordIndex = catalog.Create("words", index => index.SingleWord());

        var report = Timed("sync, empty index", () => wordIndex.Sync(source, options));
        Report($"  {report}");
        var (synced50, synced95) = QueryTimes(wordIndex, Inputs(source, random));

        Timed("the same sync again", () => wordIndex.Sync(source, options)).Unchanged.ShouldBe((long)entries);

        // halfway: cancelled, then run again
        using (var cancellation = new CancellationTokenSource())
        {
            var changed = source.Select((entry, i) => i % 2 == 0 ? entry with { Weight = entry.Weight / 2 } : entry).ToList();
            Timed("sync of 50 % changes, cancelled halfway", () =>
                Should.Throw<OperationCanceledException>(() => wordIndex.Sync(CancelAt(changed, entries / 2, cancellation), options, cancellation.Token)));
            Report($"  {Timed("the sync run again", () => wordIndex.Sync(changed, options))}");
            source = changed;
        }

        var small = source.Take(entries - 100).Select((entry, i) => i < 100 ? entry with { Weight = entry.Weight + 1 } : entry).ToList();
        Report($"  {Timed("sync of 100 changes and 100 removals", () => wordIndex.Sync(small, options))}");

        Timed("check against the source", () => wordIndex.Check(small)).IsHealthy.ShouldBeTrue();
        Timed("check of the index", () => wordIndex.Check()).IsHealthy.ShouldBeTrue();
        Timed("rebuild", () =>
        {
            wordIndex.Rebuild();
            return 0;
        });

        var upserted = catalog.Create("upserted", index => index.SingleWord());
        Timed("UpsertMany of the same entries", () => upserted.UpsertMany(source));
        var (upserted50, upserted95) = QueryTimes(upserted, Inputs(source, random));
        Report($"query after the first sync p50/p95 {synced50:F2}/{synced95:F2} ms · after UpsertMany {upserted50:F2}/{upserted95:F2} ms");
    }

    /// <summary>
    /// A delta sync of an index whose typo dictionary is built (#19, ADR-0021, ADR-0025): it teaches the dictionary the
    /// new words and does not build it anew; the rebuild is measured on its own — the two together are what the sync cost
    /// in ADR-0016.
    /// </summary>
    [PerformanceFact]
    public void A_delta_sync_with_a_built_typo_dictionary()
    {
        var entries = int.Parse(Environment.GetEnvironmentVariable("LUI_PERF_ENTRIES") ?? "300000", CultureInfo.InvariantCulture);
        var words = TestCorpus.MeasurementWords(out var corpus);
        Report($"corpus: {corpus} ({words.Count:N0} words), {entries:N0} entries, typo tolerance");
        var source = Corpus.Generate(words, entries, CorpusKind.SingleWord).Entries().ToList();

        using var database = new TestDatabase();
        using var catalog = database.Open(new LuiCatalogOptions { CacheSizeMegabytes = 512 });
        var wordIndex = catalog.Create("words", index => index.SingleWord().TypoTolerance());
        Timed("sync, empty index", () => wordIndex.Sync(source, new LuiSyncOptions { BatchSize = 1_000_000 }));
        Timed("warm-up of the typo dictionary", () =>
        {
            wordIndex.WarmUpTypoDictionary();
            return 0;
        });

        var delta = source.Skip(100).Select((entry, i) => i < 100 ? entry with { Weight = entry.Weight + 1 } : entry).ToList();
        Report($"  {Timed("sync of 100 changes and 100 removals", () => wordIndex.Sync(delta))}");
        wordIndex.TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.Current);

        Timed("rebuild of the typo dictionary", () =>
        {
            wordIndex.RebuildTypoDictionary();
            return 0;
        });
        wordIndex.TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.Current);
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
