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
        var tags = catalog.Create("tags", index => index.SingleWord());
        for (var i = 0; i < 2_000; i++)
        {
            tags.Upsert(i, $"kuchen{i}", i);
        }

        using var stop = new CancellationTokenSource();

        var writer = Task.Run(() =>
        {
            for (var i = 2_000; i < 2_400 && !stop.IsCancellationRequested; i++)
            {
                tags.Upsert(i, $"kuchen{i}", i);
                tags.UpdateWeight(i - 1_000, i);
                tags.Remove(i - 1_900);
            }
        });

        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            for (var round = 0; round < 150; round++)
            {
                var result = tags.Query("kuchen").Take(10).Execute();
                result.Hits.Count.ShouldBe(10);
                result.Hits.ShouldAllBe(hit => hit.Text.StartsWith("kuchen", StringComparison.Ordinal));
                tags.Query("ku").Take(5).Execute().Hits.Count.ShouldBe(5);
            }
        })).ToArray();

        await Task.WhenAll(readers);
        await writer;

        tags.Count().ShouldBe(2_000);   // 400 added, 400 removed
    }
}
