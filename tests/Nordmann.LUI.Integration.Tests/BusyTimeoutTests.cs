using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Shouldly;
using Xunit.Abstractions;

namespace Nordmann.LUI.Integration.Tests;

/// <summary>
/// The busy timeout of the catalog (issue #70, ADR-0046): <c>LuiCatalogOptions.BusyTimeout</c> is how long a write waits
/// for a lock before SQLite reports <c>SQLITE_BUSY</c> — not the 30 seconds of the provider. Microsoft.Data.Sqlite
/// retries a busy command on its own until the timeout of the command; the catalog sets that timeout to its floor of one
/// second, so the wait is the busy timeout of the catalog, and at least about one second.
/// </summary>
public sealed class BusyTimeoutTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(1_000, 1_000, 2_500)]
    [InlineData(2_000, 2_000, 3_500)]
    [InlineData(200, 200, 2_000)]
    public void A_write_that_waits_for_a_lock_waits_the_busy_timeout_of_the_catalog(int busyMilliseconds, int atLeast, int below)
    {
        using var database = new TestDatabase();

        // without the write-ahead log a reader that holds a snapshot keeps every writer out
        using var reader = database.Open(new LuiCatalogOptions { WriteAheadLog = false });
        var blocking = new BlockingSimilarity();
        var tags = reader.Create("tags", index => index.SingleWord().Similarity(blocking));
        tags.Upsert(1, "vegan");

        using var writer = database.Open(new LuiCatalogOptions
        {
            WriteAheadLog = false,
            BusyTimeout = TimeSpan.FromMilliseconds(busyMilliseconds),
        });
        var written = writer.Index("tags");

        var reading = new Thread(() => tags.Search("vegan")) { IsBackground = true };
        reading.Start();
        blocking.Entered.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue("the reader holds its snapshot in the ranking");

        var watch = Stopwatch.StartNew();
        try
        {
            Should.Throw<SqliteException>(() => written.Upsert(2, "vegetarian"))
                .SqliteErrorCode.ShouldBe(5, "SQLITE_BUSY — the lock of the reader");
        }
        finally
        {
            watch.Stop();
            blocking.Release.Set();
            reading.Join();
        }

        output.WriteLine($"[busy] BusyTimeout = {busyMilliseconds} ms → the write waited {watch.ElapsedMilliseconds} ms");

        // issue #70: the provider retried on its own for 30 s, and the write waited 31 s whatever the option said
        watch.ElapsedMilliseconds.ShouldBeGreaterThanOrEqualTo(atLeast - 50, "the write waited for the lock");
        watch.ElapsedMilliseconds.ShouldBeLessThan(below, "the write waited the busy timeout of the catalog, not the 30 s of the provider");
    }

    [Fact]
    public void A_statement_that_runs_longer_than_a_second_is_not_interrupted()
    {
        // the timeout of the command bounds the retries of a busy command, not the time a statement runs — a sync of a
        // million rows runs for minutes on the connections of the catalog
        using var database = new TestDatabase();
        using var catalog = database.Open(new LuiCatalogOptions { BusyTimeout = TimeSpan.FromSeconds(1) });
        using var connection = catalog.Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            WITH RECURSIVE counter(x) AS (SELECT 1 UNION ALL SELECT x + 1 FROM counter WHERE x < 30000000)
            SELECT count(*) FROM counter;
            """;

        var watch = Stopwatch.StartNew();
        var count = (long)command.ExecuteScalar()!;
        watch.Stop();

        output.WriteLine($"[busy] a statement of {watch.ElapsedMilliseconds} ms ran to its end");
        count.ShouldBe(30_000_000);
        watch.Elapsed.ShouldBeGreaterThan(TimeSpan.FromSeconds(1), "the statement ran longer than the timeout of the command");
    }

    /// <summary>Holds a query in its ranking — and with it the snapshot of the file — until the test lets it go.</summary>
    private sealed class BlockingSimilarity : ILuiSimilarity
    {
        internal ManualResetEventSlim Entered { get; } = new();

        internal ManualResetEventSlim Release { get; } = new();

        public double Similarity(string query, string text)
        {
            Entered.Set();
            Release.Wait(TimeSpan.FromSeconds(60));
            return 1;
        }
    }
}
