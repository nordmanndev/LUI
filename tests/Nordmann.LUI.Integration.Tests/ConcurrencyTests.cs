using Shouldly;

namespace Nordmann.LUI.Integration.Tests;

/// <summary>
/// Reads run in parallel, writes are serial (LUI-Spezifikation §15): queries
/// and writes at the same time neither fail nor return nonsense.
/// </summary>
public sealed class ConcurrencyTests
{
    [Fact]
    public async Task Parallel_queries_and_a_writer_do_not_disturb_each_other()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("tags").SingleWord().Ensure();
        for (var i = 0; i < 2_000; i++)
        {
            index.Upsert(i, $"kuchen{i}", i);
        }

        using var stop = new CancellationTokenSource();

        var writer = Task.Run(() =>
        {
            for (var i = 2_000; i < 2_400 && !stop.IsCancellationRequested; i++)
            {
                index.Upsert(i, $"kuchen{i}", i);
                index.UpdateWeight(i - 1_000, i);
                index.Remove(i - 1_900);
            }
        });

        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            for (var round = 0; round < 150; round++)
            {
                var result = index.Query("kuchen").Take(10).Execute();
                result.Hits.Count.ShouldBe(10);
                result.Hits.ShouldAllBe(hit => hit.Text.StartsWith("kuchen", StringComparison.Ordinal));
                index.Query("ku").Take(5).Execute().Hits.Count.ShouldBe(5);
            }
        })).ToArray();

        await Task.WhenAll(readers);
        await writer;

        index.Count().ShouldBe(2_000);   // 400 added, 400 removed
    }
}
