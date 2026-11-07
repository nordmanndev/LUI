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
    /// Two catalogs on one file: two connections, one writer, one reader. The reader holds a query
    /// open — it reads, and the snapshot is pinned while it ranks (a deferred transaction pins it).
    /// The writer writes beside it: with the write-ahead log the file belongs to both of them at the
    /// same time, and the reader sees the picture it pinned. Without it the writer waits for the
    /// reader's lock, gives up and fails — that is the difference the mutation check mutates.
    /// </summary>
    [Fact]
    public async Task Two_catalogs_on_one_file_see_a_consistent_picture()
    {
        using var database = new TestDatabase();

        // the reader holds the query — and with it the snapshot of the file
        using var reader = database.Open();
        var blocking = new BlockingSimilarity();
        var ranked = reader.Create("tags", index => index.SingleWord().Similarity(blocking));

        const int Seed = 50;
        for (var i = 0; i < Seed; i++)
        {
            ranked.Upsert(i, TextOf(i), i);
        }

        // the writer waits a second for a lock: a conflict has to be an error, not a wait
        using var writer = database.Open(new LuiCatalogOptions { BusyTimeout = TimeSpan.FromSeconds(1) });
        var written = writer.Index("tags");

        const int Written = 500;

        var reading = Task.Run(() => ranked.Query("kuchen").Take(25).Execute());
        blocking.Entered.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue("the reader reached the ranking");

        try
        {
            // the writer writes while the reader holds the snapshot
            for (var i = Seed; i < Seed + Written; i++)
            {
                written.Upsert(i, TextOf(i), i);
            }
        }
        finally
        {
            // the reader is let go in every case: a test that leaves a query running makes
            // the dispose wait for it, and a failure would look like a hang
            blocking.Release.Set();
        }

        var result = await reading;

        // the reader sees the picture it pinned: the rows that were there when its query started
        result.Hits.Count.ShouldBe(25);
        result.Hits.ShouldAllBe(hit => hit.Text == TextOf(hit.Key.ToInt64()));

        // and both connections see the same count at the end
        written.Count().ShouldBe(Seed + Written);
        reader.Index("tags").Count().ShouldBe(Seed + Written);
    }

    /// <summary>
    /// Holds a query in its ranking until the test lets it go. The payload is not read during a
    /// query any more (ADR-0035), so the ranking is where a query is caught running — and where it
    /// holds the snapshot of the file.
    /// </summary>
    private sealed class BlockingSimilarity : ILuiSimilarity
    {
        internal ManualResetEventSlim Entered { get; } = new();

        internal ManualResetEventSlim Release { get; } = new();

        public double Similarity(string query, string text)
        {
            Entered.Set();
            Release.Wait(TimeSpan.FromSeconds(30));
            return 1;
        }
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
