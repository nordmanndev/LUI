using System.Globalization;
using Shouldly;

namespace Nordmann.LUI.Integration.Tests;

/// <summary>
/// Reads run in parallel, writes are serial (LUI-Spezifikation §15): queries
/// and writes at the same time neither fail nor return nonsense.
/// <para>
/// The round of issue #67 asks for three more cases around that rule:
/// </para>
/// <list type="bullet">
/// <item>the <b>soak</b>: eight readers and one writer over a corpus of five thousand entries — no exception, and the count stays the number the writer wrote minus the number it removed;</item>
/// <item><b>two catalogs on one file</b>: a second connection sees a picture that exists — the text of a hit belongs to its key — and never more rows than were written;</item>
/// <item>the <b>dispose during a warm-up</b>: it ends, it does not hang.</item>
/// </list>
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

    /// <summary>
    /// The soak: more readers, more rounds, a bigger corpus. A count that drifts is a lost or a
    /// doubled entry — and a lost entry is only visible at the end, when the writer is done.
    /// </summary>
    [Fact]
    public async Task A_soak_of_readers_and_one_writer_keeps_the_count()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());

        const int Seed = 4_000;
        const int Written = 800;

        tags.UpsertMany(Enumerable.Range(0, Seed).Select(i => new LuiEntry(i, TextOf(i))));

        var writer = Task.Run(() =>
        {
            for (var i = Seed; i < Seed + Written; i++)
            {
                tags.Upsert(i, TextOf(i), i);
                tags.UpdateWeight(i - 500, i);

                // half of the writes remove an entry of the seed — the count has to end
                // at Seed + Written - Written / 2 and not at Seed + Written
                if (i < Seed + Written / 2)
                {
                    tags.Remove(i - 900).ShouldBeTrue();
                }
            }
        });

        var readers = Enumerable.Range(0, 6).Select(_ => Task.Run(() =>
        {
            for (var round = 0; round < 200; round++)
            {
                var result = tags.Query("kuchen").Take(20).Execute();
                result.Hits.Count.ShouldBe(20);
                result.Hits.ShouldAllBe(hit => hit.Text == TextOf(hit.Key.ToInt64()));

                // the count is a scan of the whole table — every tenth round is enough to
                // watch it, and the suite is not slowed down by the rest of it
                if (round % 10 == 0)
                {
                    tags.Count().ShouldBeInRange(Seed, Seed + Written);
                }
            }
        })).ToArray();

        await Task.WhenAll(readers);
        await writer;

        tags.Count().ShouldBe(Seed + Written - Written / 2);
    }

    /// <summary>
    /// Two catalogs on one file: two connections, one writer, one reader. The reader sees a picture
    /// that exists — the text of a hit belongs to its key, never to another one — and never more
    /// rows than were written. When the writer is done, the reader sees the same count.
    /// </summary>
    [Fact]
    public async Task Two_catalogs_on_one_file_see_a_consistent_picture()
    {
        using var database = new TestDatabase();
        using var writer = database.Open();
        var written = writer.Create("tags", index => index.SingleWord());

        // the second catalog opens the file after the index exists — the way a second
        // process of an application finds the index of the first one
        using var reader = database.Open();
        var read = reader.Index("tags");

        const int Rows = 3_000;

        var writing = Task.Run(() =>
        {
            for (var i = 0; i < Rows; i++)
            {
                written.Upsert(i, TextOf(i), i);
            }
        });

        var reading = Task.Run(() =>
        {
            for (var round = 0; round < 150; round++)
            {
                var result = read.Query("kuchen").Take(25).Execute();
                result.Hits.ShouldAllBe(hit => hit.Text == TextOf(hit.Key.ToInt64()));
                read.Count().ShouldBeLessThanOrEqualTo(Rows);
            }
        });

        await Task.WhenAll(reading, writing);

        // the picture after the writer is done is the same on both connections
        read.Count().ShouldBe(Rows);
        written.Count().ShouldBe(Rows);
    }

    /// <summary>
    /// A dispose while a warm-up runs. The catalog counts the operations that run and waits for
    /// them (its <c>Dispose</c> waits for the running ones) — so the dispose comes back, and the
    /// build comes back. Neither of them hangs.
    /// </summary>
    [Fact]
    public async Task A_dispose_during_a_warm_up_ends_and_does_not_hang()
    {
        using var database = new TestDatabase();
        var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord().TypoTolerance());

        tags.UpsertMany(Enumerable.Range(0, 20_000).Select(i => new LuiEntry(i, "wort" + i.ToString("D6", CultureInfo.InvariantCulture))));

        var warm = Task.Run(() => tags.WarmUpTypoDictionary());

        // the build has to run before the dispose comes — a dispose after the build would
        // prove nothing about a dispose *during* one
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (tags.TypoDictionaryState != LuiTypoDictionaryState.Building && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        tags.TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.Building, "the warm-up builds while the dispose comes");

        var disposing = Task.Run(() => catalog.Dispose());

        var both = Task.WhenAll(warm, disposing);
        var finished = await Task.WhenAny(both, Task.Delay(TimeSpan.FromSeconds(30)));

        finished.ShouldBe(both, "the warm-up and the dispose end — neither of them hangs");
        await both;

        // the catalog is closed afterwards
        Should.Throw<ObjectDisposedException>(() => tags.Query("wort").Execute());
    }

    /// <summary>The text of an entry — the same on every side, so that a reader can tell whose text it sees.</summary>
    private static string TextOf(long id) => "kuchen" + id.ToString("D6", CultureInfo.InvariantCulture);
}
