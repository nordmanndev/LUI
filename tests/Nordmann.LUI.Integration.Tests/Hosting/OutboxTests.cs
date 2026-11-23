using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using static Nordmann.LUI.Integration.Tests.Linked.LinkedTable;

namespace Nordmann.LUI.Integration.Tests.Hosting;

/// <summary>
/// <c>AddLuiOutbox</c> (ADR-0076, issue #266): the one background loop LUI needs — it calls <c>catalog.ApplyPending()</c> every
/// <c>Interval</c> and stops with the host. It does nothing at the start (that is <c>Initialize()</c>). A failing round never stops the host:
/// it is logged as a warning with its exception, the next round waits with exponential backoff up to <c>MaxBackoff</c>, and the first
/// successful round goes back to the interval. On a read-only catalog the registration throws when the host starts.
/// <para>
/// The loop is driven by a <see cref="FakeTimeProvider"/> that the test registers in the container: a test moves the clock and waits for
/// what the service tells — it is waiting (with the delay it waits), a round began, a round finished — never for a time. The
/// signals are counted, so a test that comes late loses none of them (issue #185: a claim must stay true).
/// </para>
/// </summary>
public sealed class OutboxTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(50);

    [Fact]
    public async Task A_failing_round_is_logged_and_retried_with_backoff()
    {
        using var world = new World(Interval, MaxBackoff);
        world.BreakTheTable();
        var service = world.Service;
        await world.Host.StartAsync(TestContext.Current.CancellationToken);

        (await service.ArmedAsync()).ShouldBe(Interval, "the first round comes after the interval, not at the start");

        // every round fails, and the wait before the next one doubles, up to the maximum: 20 s, 40 s, then 80 s is cut at 50 s, and stays there
        TimeSpan[] expected = [TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(40), MaxBackoff, MaxBackoff];
        var waited = Interval;
        for (var round = 0; round < expected.Length; round++)
        {
            world.Clock.Advance(waited);
            waited = await service.ArmedAsync();
            waited.ShouldBe(expected[round], $"after failure {round + 1} the next round waits with backoff");
        }

        var warnings = world.Logger.Entries.Where(entry => entry.Level == LogLevel.Warning).ToArray();
        warnings.Length.ShouldBe(4, "every failed round is told to the logger of the host");
        warnings.ShouldAllBe(entry => entry.Exception != null, "with the exception itself, not only a sentence about it");
        warnings[0].Message.ShouldContain("the rows stay in the outbox");
        warnings[0].Message.ShouldContain("20", Case.Sensitive); // the sentence says when the next round is
        service.RoundsFinished.ShouldBe(4);

        world.Tag.Pending().ShouldBe(1, "the row waits in the outbox for a round that can read it");
        service.Failures.ShouldBe(4);
    }

    [Fact]
    public async Task The_first_successful_round_returns_to_the_interval()
    {
        using var world = new World(Interval, MaxBackoff);
        world.BreakTheTable();
        var service = world.Service;
        await world.Host.StartAsync(TestContext.Current.CancellationToken);
        var waited = await service.ArmedAsync();

        // two failures: 20 s, then 40 s
        for (var round = 0; round < 2; round++)
        {
            world.Clock.Advance(waited);
            waited = await service.ArmedAsync();
        }

        waited.ShouldBe(TimeSpan.FromSeconds(40));
        world.MendTheTable();

        world.Clock.Advance(waited);
        (await service.ArmedAsync()).ShouldBe(Interval, "the round that succeeds goes back to the interval, not to the backoff it was in");
        service.Failures.ShouldBe(0);
        world.Tag.Pending().ShouldBe(0, "the row of the outbox was applied");
        world.Tag.Search("vegan").Hits.ShouldHaveSingleItem();
        world.Logger.Entries.Count(entry => entry.Level == LogLevel.Warning).ShouldBe(2);

        // and the next failure starts the backoff again from the interval: the first step of the ladder, not where it was
        Insert(world.Database, (2, "vegetarisch", 0));
        world.BreakTheTable();
        world.Clock.Advance(Interval);
        (await service.ArmedAsync()).ShouldBe(TimeSpan.FromSeconds(20));
    }

    [Fact]
    public async Task On_a_read_only_catalog_AddLuiOutbox_throws_at_start()
    {
        using var database = new TestDatabase();
        using (var writer = database.Open())
        {
            writer.Create(new LuiIndexDefinition<long>("tags", index => index.SingleWord())).Upsert(1, "vegan");
        }

        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Services.AddLui(_ => LuiCatalog.OpenReadOnly(database.FilePath));
        builder.Services.AddLuiOutbox(Interval);
        using var host = builder.Build();

        var failure = await Should.ThrowAsync<InvalidOperationException>(() => host.StartAsync(TestContext.Current.CancellationToken));

        failure.Message.ShouldContain("a read-only catalog cannot apply the outbox");
    }

    [Fact]
    public async Task The_service_does_nothing_at_the_start_and_applies_the_outbox_on_the_interval()
    {
        using var world = new World(Interval, MaxBackoff);
        var service = world.Service;
        world.Tag.Pending().ShouldBe(1);

        await world.Host.StartAsync(TestContext.Current.CancellationToken);
        await service.ArmedAsync();

        world.Tag.Pending().ShouldBe(1, "the start applies nothing — that is Initialize(), and the outbox waits for the first round");
        service.RoundsBegun.ShouldBe(0);
        world.Tag.Search("vegan").Hits.ShouldBeEmpty();

        world.Clock.Advance(Interval);
        _ = await service.ArmedAsync();

        service.RoundsFinished.ShouldBe(1);
        world.Tag.Pending().ShouldBe(0);
        world.Tag.Search("vegan").Hits.ShouldHaveSingleItem();
        world.Logger.Entries.Where(entry => entry.Level >= LogLevel.Warning).ShouldBeEmpty("a round that works says nothing loud");
    }

    [Fact]
    public async Task After_the_host_stops_no_round_begins()
    {
        using var world = new World(Interval, MaxBackoff);
        var service = world.Service;
        await world.Host.StartAsync(TestContext.Current.CancellationToken);
        await service.ArmedAsync();

        await world.Host.StopAsync(TestContext.Current.CancellationToken);
        world.Clock.Advance(TimeSpan.FromHours(1));

        service.RoundsBegun.ShouldBe(0, "the host stopped the loop: the clock that moves afterwards begins no round");
        world.Tag.Pending().ShouldBe(1);
    }

    [Fact]
    [Trait("Category", "Timing")]
    public async Task The_host_waits_for_a_round_that_runs_when_it_stops()
    {
        // the round waits for the write lock of the file, which the test holds, for the busy timeout of the catalog — one second. The stop of the
        // host does not leave it behind: it holds the file of the application, and the host that stopped it is gone (ADR-0043: the host runs
        // the service, stops it and disposes it). The wait for the round is a wait for its task (issue #185), not for a flag that clears early.
        using var world = new World(Interval, MaxBackoff, busyTimeout: TimeSpan.FromSeconds(1));
        var service = world.Service;
        await world.Host.StartAsync(TestContext.Current.CancellationToken);
        await service.ArmedAsync();

        using var blocker = world.Database.Connect();
        using (var begin = blocker.CreateCommand())
        {
            begin.CommandText = "BEGIN IMMEDIATE;";
            begin.ExecuteNonQuery();
        }

        world.Clock.Advance(Interval);
        await service.BeganAsync();

        await world.Host.StopAsync(TestContext.Current.CancellationToken);

        service.RoundsFinished.ShouldBe(1, "the stop returned after the round was over");
        service.RoundRunning.ShouldBeFalse();
        using var commit = blocker.CreateCommand();
        commit.CommandText = "COMMIT;";
        commit.ExecuteNonQuery();
    }

    [Fact]
    [Trait("Category", "Timing")]
    public async Task The_host_waits_for_a_round_that_runs_when_it_is_disposed()
    {
        // issue #148: the host stopped the service, and the round that was running went on — it held the file of the application, and the
        // teardown of a test could not delete it. The disposal of the host waits for the round (ADR-0043: the host runs it, the host stops it,
        // the host disposes it). The round waits for the write lock of the file, which the test holds, and gives up after the busy timeout of
        // one second; the wait asks for a round that has begun (issue #185), and the claim of that stays true.
        using var world = new World(Interval, MaxBackoff, busyTimeout: TimeSpan.FromSeconds(1));
        var service = world.Service;
        await world.Host.StartAsync(TestContext.Current.CancellationToken);
        await service.ArmedAsync();

        using var blocker = world.Database.Connect();
        RunOn(blocker, "BEGIN IMMEDIATE;");

        world.Clock.Advance(Interval);
        await service.BeganAsync();

        world.Host.Dispose();

        service.RoundRunning.ShouldBeFalse("the disposal waits for the round that runs");
        service.RoundsFinished.ShouldBe(1, "the round was over before the host was gone");
        RunOn(blocker, "COMMIT;");
    }

    [Fact]
    public void The_outbox_is_registered_once_with_an_interval_or_with_options_and_with_positive_values()
    {
        var services = new ServiceCollection();
        services.AddLuiOutbox(TimeSpan.FromSeconds(5));
        Should.Throw<InvalidOperationException>(() => services.AddLuiOutbox(TimeSpan.FromSeconds(5)), "once per container, whichever overload");
        Should.Throw<InvalidOperationException>(() => services.AddLuiOutbox(options => options.Interval = TimeSpan.FromSeconds(1)));

        Should.Throw<ArgumentOutOfRangeException>(() => new ServiceCollection().AddLuiOutbox(TimeSpan.Zero));
        Should.Throw<ArgumentOutOfRangeException>(() => new ServiceCollection().AddLuiOutbox(options => options.Interval = TimeSpan.FromSeconds(-1)));
        Should.Throw<ArgumentOutOfRangeException>(() => new ServiceCollection().AddLuiOutbox(options => options.MaxBackoff = TimeSpan.Zero));

        // a value the timer cannot wait for would stop the host at the first wait: it is refused at the registration, and the message names the option
        var tooLong = Should.Throw<ArgumentOutOfRangeException>(() => new ServiceCollection().AddLuiOutbox(options => options.MaxBackoff = TimeSpan.MaxValue));
        tooLong.Message.ShouldContain("LuiOutboxOptions.MaxBackoff");
        Should.Throw<ArgumentOutOfRangeException>(() => new ServiceCollection().AddLuiOutbox(TimeSpan.FromDays(60))).Message.ShouldContain("LuiOutboxOptions.Interval");
        Should.Throw<ArgumentOutOfRangeException>(() => new ServiceCollection().AddLuiOutbox(options => options.MaxBackoff = TimeSpan.Zero)).Message.ShouldContain("LuiOutboxOptions.MaxBackoff");
        Should.Throw<ArgumentOutOfRangeException>(() => new ServiceCollection().AddLuiOutbox(TimeSpan.Zero)).Message.ShouldContain("LuiOutboxOptions.Interval");
        Should.NotThrow(() => new ServiceCollection().AddLuiOutbox(options => options.MaxBackoff = TimeSpan.FromDays(24)));

        // the defaults of the issue
        var options = new LuiOutboxOptions();
        options.Interval.ShouldBe(TimeSpan.FromSeconds(30));
        options.MaxBackoff.ShouldBe(TimeSpan.FromMinutes(5));

        // beside the application's own initializer (ADR-0076), in a container of its own
        var both = new ServiceCollection();
        both.AddHostedService<OwnInitializer>();
        both.AddLuiOutbox(options => options.Interval = TimeSpan.FromSeconds(7));
        both.Count(service => service.ServiceType == typeof(IHostedService)).ShouldBe(2);
    }

    [Fact]
    [Trait("Category", "Timing")]
    public async Task The_claim_of_a_round_that_has_begun_stays_true_when_the_round_is_over()
    {
        // issue #185: the claim of a round that runs is momentary — the round of the test waits for the write lock of the file, which
        // the test itself holds, so it lives about a second. A waiter that comes late must still learn that a round ran: the claim of a
        // round that has begun stays true.
        using var world = new World(Interval, MaxBackoff, busyTimeout: TimeSpan.FromSeconds(1));
        var service = world.Service;
        await world.Host.StartAsync(TestContext.Current.CancellationToken);
        await service.ArmedAsync();

        using var blocker = world.Database.Connect();
        RunOn(blocker, "BEGIN IMMEDIATE;");

        world.Clock.Advance(Interval);
        await service.BeganAsync();
        await service.FinishedAsync();

        service.RoundRunning.ShouldBeFalse("the round is over");
        service.RoundsBegun.ShouldBe(1, "the claim of a round that has begun stays true");
        RunOn(blocker, "COMMIT;");
    }

    private static void RunOn(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>The application's own initializer, as far as the registration is concerned: a second hosted service in the container.</summary>
    private sealed class OwnInitializer : BackgroundService
    {
        protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
    }

    /// <summary>A host with a catalog, a linked index with a row in its outbox, the outbox service, a clock the test moves and a logger that keeps what it heard.</summary>
    private sealed class World : IDisposable
    {
        internal World(TimeSpan interval, TimeSpan maxBackoff, TimeSpan? busyTimeout = null)
        {
            Database = new TestDatabase();
            Run(Database, Table);
            using (var setup = Database.Open())
            {
                var tags = setup.Create(Chefs);
                tags.CreateTriggers();
            }

            Insert(Database, (1, "vegan", 0));
            Clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 23, 8, 0, 0, TimeSpan.Zero));
            Logger = new RecordingLoggerProvider();

            var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
            builder.Services.AddSingleton<TimeProvider>(Clock);
            builder.Services.AddLui(_ => LuiCatalog.OpenOrCreate(
                Database.FilePath,
                busyTimeout is { } timeout ? new LuiCatalogOptions { Pragmas = new LuiPragmas { BusyTimeout = timeout } } : null));
            builder.Services.AddLuiOutbox(options =>
            {
                options.Interval = interval;
                options.MaxBackoff = maxBackoff;
            });
            builder.Logging.AddProvider(Logger);
            Host = builder.Build();
            Service = Host.Services.GetServices<IHostedService>().OfType<global::Nordmann.LUI.Hosting.LuiOutboxService>().Single();
        }

        internal TestDatabase Database { get; }

        internal FakeTimeProvider Clock { get; }

        internal RecordingLoggerProvider Logger { get; }

        internal IHost Host { get; }

        internal global::Nordmann.LUI.Hosting.LuiOutboxService Service { get; }

        internal LuiLinkedIndex<long> Tag => Host.Services.GetRequiredService<LuiCatalog>().Index(Chefs);

        /// <summary>The table of the application is renamed: the outbox holds a row that no round can read — every round fails (ADR-0045).</summary>
        internal void BreakTheTable() => Run(Database, "ALTER TABLE tag RENAME TO tag_gone;");

        internal void MendTheTable() => Run(Database, "ALTER TABLE tag_gone RENAME TO tag;");

        public void Dispose()
        {
            Host.Dispose();
            Database.Dispose();
        }
    }

    private static readonly LuiLinkedIndexDefinition<long> Chefs =
        new("chefs", index => index.SingleWord().LinkTo("tag", "id", "name", weight: "usage_count"));
}
