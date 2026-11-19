using Shouldly;

namespace Nordmann.LUI.Integration.Tests.Catalog;

/// <summary>
/// What one write costs in memory (issue #237). Since 0.3.0 the catalogue holds no connection (ADR-0070): every write takes
/// one from the pool of the provider. A connection of the pool has its settings already — the three pragmas stay with the
/// physical connection — and applying them again on every write costs a command and two statements, about a kilobyte each
/// time: 422 B per <c>UpdateWeight</c> in 0.2.0 became 2,430 B. Without them it is about 1,500 B (<c>Upsert</c>: 2,700 B) —
/// the rest of the difference to 0.2.0, about a kilobyte, is the price of taking a connection for every write (ADR-0070).
/// <para>
/// The allocation of a write is the same on every machine, so it can be held in a test: the average over two thousand writes
/// after a warm-up of three hundred. The bounds sit above the measured values and below what the pragmas cost.
/// </para>
/// </summary>
public sealed class WriteAllocationTests
{
    private const int Warmup = 300;
    private const int Writes = 2000;

    /// <summary>The bound of <c>UpdateWeight</c>: the checkout of a connection, a transaction and one statement.</summary>
    private const double UpdateWeightBytes = 1_800;

    /// <summary>The bound of <c>Upsert</c> — one statement more, and the entry.</summary>
    private const double UpsertBytes = 3_200;

    [Fact]
    public void A_write_takes_a_connection_of_the_pool_and_does_not_apply_its_pragmas_again()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Create("labels", i => i.SingleWord());
        for (var key = 1; key <= 2_000; key++)
        {
            index.Upsert(key, "wort" + key, key);
        }

        var updateWeight = Measure(step => index.UpdateWeight(1 + (step % 1000), 5 + step));
        var upsertTheSame = Measure(step => index.Upsert(1 + (step % 1000), "wort" + (1 + (step % 1000)), 7 + step));

        (updateWeight <= UpdateWeightBytes).ShouldBeTrue($"UpdateWeight allocates {updateWeight:F0} B per write — the bound is {UpdateWeightBytes:F0} B (0.2.0: 422 B)");
        (upsertTheSame <= UpsertBytes).ShouldBeTrue($"Upsert allocates {upsertTheSame:F0} B per write — the bound is {UpsertBytes:F0} B (0.2.0: 869 B)");
    }

    [Fact]
    public void A_connection_that_comes_out_of_the_pool_has_the_pragmas_of_its_catalogue()
    {
        // the pragmas are applied to a physical connection once — the one that comes later out of the pool has them
        var options = new LuiCatalogOptions
        {
            Pragmas = new LuiPragmas { BusyTimeout = TimeSpan.FromSeconds(7), Synchronous = LuiSynchronous.Full, CacheSizeMegabytes = 16 }
        };

        using var database = new TestDatabase();
        using var catalog = database.Open(options);
        var index = catalog.Create("labels", i => i.SingleWord());

        for (var round = 0; round < 5; round++)
        {
            index.Upsert(round + 1, "wort" + round, round);
            using var connection = catalog.Database.OpenConnection();
            ReadPragma(connection, "busy_timeout").ShouldBe(7000, $"round {round}");
            ReadPragma(connection, "synchronous").ShouldBe(2, $"round {round}: FULL");
            ReadPragma(connection, "cache_size").ShouldBe(-16 * 1024, $"round {round}");
        }
    }

    private static double Measure(Action<int> write)
    {
        for (var step = 0; step < Warmup; step++)
        {
            write(step);
        }

        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var step = 0; step < Writes; step++)
        {
            write(step);
        }

        return (GC.GetAllocatedBytesForCurrentThread() - before) / (double)Writes;
    }

    private static long ReadPragma(Microsoft.Data.Sqlite.SqliteConnection connection, string name)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {name};";
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }
}
