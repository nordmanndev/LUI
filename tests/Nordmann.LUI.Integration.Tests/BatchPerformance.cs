using System.Diagnostics;
using System.Globalization;
using Xunit.Abstractions;

namespace Nordmann.LUI.Integration.Tests;

/// <summary>
/// What a batch saves over a loop of single upserts, each in a transaction of its own (ADR-0013).
/// <code>LUI_PERF=1 LUI_PERF_ENTRIES=100000 dotnet test --filter BatchPerformance --logger "console;verbosity=detailed"</code>
/// </summary>
public sealed class BatchPerformance(ITestOutputHelper output)
{
    [PerformanceFact]
    public void A_batch_against_a_loop_of_single_upserts()
    {
        var entries = int.Parse(Environment.GetEnvironmentVariable("LUI_PERF_ENTRIES") ?? "100000", CultureInfo.InvariantCulture);
        var random = new Random(42);
        var texts = Enumerable.Range(1, entries)
            .Select(i => $"{(char)('a' + random.Next(26))}{(char)('a' + random.Next(26))}kuchen{i}")
            .ToArray();

        using var database = new TestDatabase();
        using var catalog = database.Open();
        var single = catalog.Create("single", index => index.SingleWord());
        var batch = catalog.Create("batch", index => index.SingleWord());

        var loop = Stopwatch.StartNew();
        for (var i = 0; i < entries; i++)
        {
            single.Upsert(i + 1, texts[i], i);
        }

        loop.Stop();

        var many = Stopwatch.StartNew();
        batch.UpsertMany(texts.Select((text, i) => new LuiEntry<long, LuiNoPayload>(i + 1, text) { Weight = i }));
        many.Stop();

        Report($"{entries:N0} entries: single upserts {loop.Elapsed.TotalSeconds:F1} s ({entries / loop.Elapsed.TotalSeconds:N0}/s), "
            + $"one batch {many.Elapsed.TotalSeconds:F1} s ({entries / many.Elapsed.TotalSeconds:N0}/s), "
            + $"{loop.Elapsed.TotalSeconds / many.Elapsed.TotalSeconds:F1}× faster");
    }

    private void Report(string line)
    {
        output.WriteLine(line);
        Console.WriteLine(line);
    }
}
