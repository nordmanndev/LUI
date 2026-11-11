using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace Nordmann.LUI.Integration.Tests.Hosting;

/// <summary>
/// The warm-up as a service of the host (issue #68, ADR-0043): <c>AddLuiWarmup()</c> warms the typo dictionaries at
/// the start of the host and applies the outbox of a linked index on an interval — and only when the application asks
/// for it. The library starts nothing on its own: the host runs the service, and the service lives as long as the host.
/// </summary>
public sealed class LuiWarmupTests
{
    private const string Table = """
        CREATE TABLE tag (
            id          INTEGER PRIMARY KEY,
            name        TEXT,
            usage_count INTEGER NOT NULL DEFAULT 0);
        """;

    [Fact]
    public async Task The_host_warms_the_dictionary_of_every_index_at_its_start()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        tags.Upsert(1, "vegan");

        using var host = Host(database, warmup: true);
        await host.StartAsync();

        // the dictionary belongs to the index of the catalog — the one of the host, not the one of the test
        host.Services.GetRequiredService<LuiCatalog>()
            .Index("tags")
            .TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.Current);
    }

    [Fact]
    public async Task The_host_applies_the_outbox_of_a_linked_index()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord().LinkTo("tag", "id", "name"));
        tags.CreateTriggers();
        Insert(database, (1, "vegan", 0));
        tags.Pending().ShouldBe(1, "the trigger noted the row in the outbox");

        using var host = Host(database, warmup: true);
        await host.StartAsync();

        var tagsOfTheHost = host.Services.GetRequiredService<LuiCatalog>().LinkedIndex("tags");
        tagsOfTheHost.Pending().ShouldBe(0, "the outbox is empty — the warm-up brought the row in");
        tagsOfTheHost.Search("vegan").Hits.ShouldHaveSingleItem();
    }

    [Fact]
    [Trait("Category", "Timing")]
    public async Task The_rounds_of_the_outbox_run_on_the_interval()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord().LinkTo("tag", "id", "name"));
        tags.CreateTriggers();
        Insert(database, (1, "vegan", 0));

        using var host = Host(database, warmup: true, configure: options => options.OutboxInterval = TimeSpan.FromMilliseconds(50));
        await host.StartAsync();

        // a row that comes after the start waits for the next round — the interval is the promise of the service
        Insert(database, (2, "vegetarian", 0));
        var tagsOfTheHost = host.Services.GetRequiredService<LuiCatalog>().LinkedIndex("tags");

        await WaitUntil(() => tagsOfTheHost.Pending() == 0);
        tagsOfTheHost.Search("vegetarian").Hits.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Without_the_registration_nothing_happens()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        tags.Upsert(1, "vegan");
        var linked = catalog.Create("linked", index => index.SingleWord().LinkTo("tag", "id", "name"));
        linked.CreateTriggers();
        Insert(database, (1, "vegan", 0));

        using var host = Host(database, warmup: false);
        await host.StartAsync();

        var catalogOfTheHost = host.Services.GetRequiredService<LuiCatalog>();
        catalogOfTheHost.Index("tags").TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.NotBuilt);
        catalogOfTheHost.LinkedIndex("linked").Pending().ShouldBe(1, "the outbox waits for the application");
    }

    [Fact]
    public async Task The_host_builds_only_the_dictionaries_it_is_told_to()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create("tags", index => index.SingleWord()).Upsert(1, "vegan");
        catalog.Create("names", index => index.SingleWord()).Upsert(1, "Vera");

        // a dictionary costs memory — the index whose queries correct no typos does not get one (ADR-0043)
        using var host = Host(database, warmup: true, configure: options => options.TypoDictionaryIndexes.Add("tags"));
        await host.StartAsync();

        var catalogOfTheHost = host.Services.GetRequiredService<LuiCatalog>();
        catalogOfTheHost.Index("tags").TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.Current);
        catalogOfTheHost.Index("names").TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.NotBuilt);
    }

    [Fact]
    public async Task Without_the_dictionaries_the_host_still_applies_the_outbox()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord().LinkTo("tag", "id", "name"));
        tags.CreateTriggers();
        Insert(database, (1, "vegan", 0));

        using var host = Host(database, warmup: true, configure: options => options.WarmUpTypoDictionaries = false);
        await host.StartAsync();

        var tagsOfTheHost = host.Services.GetRequiredService<LuiCatalog>().LinkedIndex("tags");
        tagsOfTheHost.Pending().ShouldBe(0, "the outbox is the service's either way");
        tagsOfTheHost.TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.NotBuilt);
    }

    [Fact]
    public async Task A_name_the_catalog_does_not_know_fails_the_start_of_the_host()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create("tags", index => index.SingleWord()).Upsert(1, "vegan");

        using var host = Host(database, warmup: true, configure: options => options.TypoDictionaryIndexes.Add("tgas"));

        var failure = await Should.ThrowAsync<LuiIndexNotFoundException>(() => host.StartAsync());
        failure.Message.ShouldContain("tgas");
    }

    [Fact]
    [Trait("Category", "Timing")]
    public async Task A_round_that_fails_is_told_to_the_logger_of_the_host()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord().LinkTo("tag", "id", "name"));
        tags.CreateTriggers();

        var logger = new RecordingLoggerProvider();
        using var host = Host(
            database,
            warmup: true,
            configure: options =>
            {
                options.WarmUpTypoDictionaries = false;
                options.OutboxInterval = TimeSpan.FromMilliseconds(50);
            },
            logger: logger);
        await host.StartAsync();

        // a row in the outbox whose table is gone: the round cannot read it and fails — every time (ADR-0045)
        Insert(database, (1, "vegan", 0));
        Run(database, "ALTER TABLE tag RENAME TO tag_gone;");

        await WaitUntil(() => logger.Entries.Any(entry => entry.Level == LogLevel.Warning));
        var warning = logger.Entries.First(entry => entry.Level == LogLevel.Warning);
        warning.Event.Id.ShouldBe(3);
        warning.Exception.ShouldNotBeNull("the logger gets the failure itself, not only a sentence about it");
        warning.Message.ShouldContain("the rows stay in the outbox");

        tags.Pending().ShouldBe(1, "the row waits in the outbox for a round that can read it");
    }

    [Fact]
    [Trait("Category", "Timing")]
    public async Task After_the_host_stops_no_round_of_the_outbox_begins()
    {
        // issue #106 (the coverage of #80, G3): StopAsync ran in no test — the tests disposed the host without stopping it
        using var database = new TestDatabase();
        Run(database, Table);
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord().LinkTo("tag", "id", "name"));
        tags.CreateTriggers();

        using var host = Host(database, warmup: true, configure: options =>
        {
            options.WarmUpTypoDictionaries = false;
            options.OutboxInterval = TimeSpan.FromMilliseconds(50);
        });
        await host.StartAsync(TestContext.Current.CancellationToken);
        await host.StopAsync(TestContext.Current.CancellationToken);

        // the host is stopped, not disposed: its catalog is still open, so a timer that still ran would apply the row
        Insert(database, (1, "vegan", 0));

        // twenty intervals. Load only makes a round later: it can hide a round that should not run, never make one up —
        // the claim is "none", and a mutation that keeps the timer is caught on any machine that is not stalled for a second
        await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        tags.Pending().ShouldBe(1, "no round ran after the stop");
    }

    [Fact]
    [Trait("Category", "Timing")]
    public async Task A_round_that_waits_for_the_write_lock_is_not_joined_by_the_next_one()
    {
        // issue #106 (G3): the guard against overlapping rounds was not tested. A round waits for the write lock as long
        // as the busy timeout (1 s) and fails; the timer ticks every 20 ms. With the guard, one round at a time waits, so
        // at most one fails per second. Without it, every tick starts a round of its own — about fifty per second.
        using var database = new TestDatabase();
        Run(database, Table);
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord().LinkTo("tag", "id", "name"));
        tags.CreateTriggers();

        var logger = new RecordingLoggerProvider();
        using var host = Host(
            database,
            warmup: true,
            configure: options =>
            {
                options.WarmUpTypoDictionaries = false;
                options.OutboxInterval = TimeSpan.FromMilliseconds(20);
            },
            logger: logger,
            catalogOptions: new LuiCatalogOptions { BusyTimeout = TimeSpan.FromSeconds(1) });
        await host.StartAsync(TestContext.Current.CancellationToken);

        using var blocker = database.Connect();
        HoldTheWriteLockWithARowInTheOutbox(blocker, tags);

        // three busy timeouts. A lower bound and an upper bound, both safe under load: a round fails no sooner than its
        // busy timeout, so three seconds hold at most three failures in a row — and at least one, because the first
        // round begins within a tick and fails a second later
        await Task.Delay(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        var failed = logger.Entries.Count(entry => entry.Level == LogLevel.Warning);
        Run(blocker, "COMMIT;");

        failed.ShouldBeInRange(1, 4, "one round at a time waits for the lock");
        await WaitUntil(() => tags.Pending() == 0);
    }

    [Fact]
    public void The_warm_up_is_registered_once_and_with_a_positive_interval()
    {
        var services = new ServiceCollection();
        services.AddLuiWarmup();

        Should.Throw<InvalidOperationException>(() => services.AddLuiWarmup());
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new ServiceCollection().AddLuiWarmup(options => options.OutboxInterval = TimeSpan.Zero));
    }

    private static IHost Host(
        TestDatabase database,
        bool warmup,
        Action<LuiWarmupOptions>? configure = null,
        RecordingLoggerProvider? logger = null,
        LuiCatalogOptions? catalogOptions = null)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Services.AddLui(database.ConnectionString, catalogOptions);
        if (warmup)
        {
            builder.Services.AddLuiWarmup(configure);
        }

        if (logger is not null)
        {
            builder.Logging.AddProvider(logger);
        }

        return builder.Build();
    }

    /// <summary>
    /// Waits until the claim of the service holds. The interval of the service is 50 ms; the deadline of thirty seconds
    /// only stops a hang — a claim that holds returns at once, and a loaded runner gets its time (issue #78).
    /// </summary>
    /// <param name="claim">What must come true.</param>
    private static async Task WaitUntil(Func<bool> claim)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!claim())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The claim of the service did not come true in thirty seconds.");
            }

            await Task.Delay(20, TestContext.Current.CancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Commits a row the trigger notes in the outbox and takes the write lock at once, in one statement. A round that
    /// slips in between and applies the row leaves the outbox empty: then the lock is let go and the next row tried, so
    /// that the test always holds the lock over a row that waits.
    /// </summary>
    private static void HoldTheWriteLockWithARowInTheOutbox(SqliteConnection blocker, ILuiLinkedIndex tags)
    {
        for (var id = 1; id <= 10; id++)
        {
            Run(blocker, $"INSERT INTO tag(id, name, usage_count) VALUES ({id}, 'vegan{id}', 0); BEGIN IMMEDIATE;");
            if (tags.Pending() > 0)
            {
                return;
            }

            Run(blocker, "COMMIT;");
        }

        throw new InvalidOperationException("Every row was applied before the lock was taken.");
    }

    private static void Run(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void Run(TestDatabase database, string sql)
    {
        using var connection = database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void Insert(TestDatabase database, params (long Id, string? Name, int Weight)[] rows)
    {
        using var connection = database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO tag(id, name, usage_count) VALUES ($id, $name, $weight);";
        var id = command.Parameters.Add("$id", SqliteType.Integer);
        var name = command.Parameters.Add("$name", SqliteType.Text);
        var weight = command.Parameters.Add("$weight", SqliteType.Integer);
        foreach (var row in rows)
        {
            id.Value = row.Id;
            name.Value = (object?)row.Name ?? DBNull.Value;
            weight.Value = row.Weight;
            command.ExecuteNonQuery();
        }
    }
}
