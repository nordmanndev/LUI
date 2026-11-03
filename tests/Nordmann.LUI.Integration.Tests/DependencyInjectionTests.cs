using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace Nordmann.LUI.Integration.Tests;

/// <summary>
/// LUI in a host (LUI-Spezifikation §12.7, ADR-0017): the catalog and the indexes as services, keyed by name; the indexes
/// ensured on start; the outbox of a linked index applied on a timer; the catalog disposed with the host.
/// </summary>
public sealed class DependencyInjectionTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    public void Every_index_is_a_keyed_service_under_its_name_and_ensured_when_the_host_starts()
    {
        using var database = new TestDatabase();
        using var host = Host(database, lui => lui
            .AddIndex("words", index => index.SingleWord())
            .AddIndex("titles", index => index.MultiWord().WithPayload(ChipJsonContext.Default.Chip)));

        Meta(database).ShouldBe(0);
        host.Start();
        Meta(database).ShouldBe(2);

        var owned = host.Services.GetRequiredKeyedService<ILuiOwnedIndex<long, LuiNoPayload>>("words");
        var read = host.Services.GetRequiredKeyedService<ILuiIndex<long, LuiNoPayload>>("words");
        read.ShouldBeSameAs(owned);
        host.Services.GetRequiredKeyedService<ILuiIndex<long, Chip>>("titles").Kind.ShouldBe(LuiIndexKind.MultiWord);

        owned.Upsert(1, "vegan");
        read.Search("veg").Hits.ShouldHaveSingleItem().Text.ShouldBe("vegan");
    }

    [Fact]
    public void A_definition_that_does_not_match_stops_the_start_not_the_first_request()
    {
        using var database = new TestDatabase();
        using (var catalog = database.Open())
        {
            catalog.Index("words").MultiWord().Ensure();
        }

        using var host = Host(database, lui => lui.AddIndex("words", index => index.SingleWord()));

        Should.Throw<LuiDefinitionMismatchException>(host.Start);
    }

    [Fact]
    public void Without_ensure_on_start_an_index_is_ensured_when_it_is_first_resolved()
    {
        using var database = new TestDatabase();
        using var host = Host(database, lui => lui.AddIndex("words", index => index.SingleWord()).EnsureOnStart(false));

        host.Start();
        Meta(database).ShouldBe(0);

        host.Services.GetRequiredKeyedService<ILuiIndex<long, LuiNoPayload>>("words").Count().ShouldBe(0);
        Meta(database).ShouldBe(1);
    }

    [Fact]
    public void A_linked_index_is_a_keyed_service_and_its_outbox_is_applied_on_its_interval()
    {
        using var database = new TestDatabase();
        Run(database, "CREATE TABLE tag (id INTEGER PRIMARY KEY, name TEXT, usage_count INTEGER NOT NULL DEFAULT 0);");
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var host = Host(
            database,
            lui => lui.AddIndex("tags", index => index.SingleWord().LinkTo("tag", "id", "name"), applyPendingEvery: TimeSpan.FromSeconds(5)),
            new LuiCatalogOptions { TimeProvider = clock });
        host.Start();

        var tags = host.Services.GetRequiredKeyedService<ILuiLinkedIndex<LuiNoPayload>>("tags");
        host.Services.GetRequiredKeyedService<ILuiIndex<long, LuiNoPayload>>("tags").ShouldBeSameAs(tags);
        tags.CreateTriggers();
        Run(database, "INSERT INTO tag(id, name) VALUES (1, 'vegan');");
        tags.Pending().ShouldBe(1);

        Eventually(() =>
        {
            clock.Advance(TimeSpan.FromSeconds(5));
            return tags.Pending() == 0;
        }).ShouldBeTrue("the outbox is applied on the timer");
        tags.Search("vegan").Hits.ShouldHaveSingleItem();
    }

    [Fact]
    public void A_round_of_the_outbox_that_fails_does_not_end_the_loop()
    {
        using var database = new TestDatabase();
        Run(database, "CREATE TABLE tag (id INTEGER PRIMARY KEY, name TEXT, usage_count INTEGER NOT NULL DEFAULT 0);");
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var log = new CapturedLog();
        using var host = Host(
            database,
            lui => lui.AddIndex("tags", index => index.SingleWord().LinkTo("tag", "id", "name"), applyPendingEvery: TimeSpan.FromSeconds(1)),
            new LuiCatalogOptions { TimeProvider = clock },
            builder => builder.Logging.SetMinimumLevel(LogLevel.Debug).AddProvider(log));
        host.Start();
        var tags = host.Services.GetRequiredKeyedService<ILuiLinkedIndex<LuiNoPayload>>("tags");
        tags.CreateTriggers();
        Run(database, "INSERT INTO tag(id, name) VALUES (1, 'vegan'); ALTER TABLE tag RENAME COLUMN name TO title;");

        // rounds fail while the column is gone …
        Eventually(() =>
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            return log.Count(LogLevel.Debug, "rounds in a row") >= 2;
        }).ShouldBeTrue("three rounds fail");

        tags.Pending().ShouldBe(1);

        // … and the next one succeeds once it is back
        Run(database, "ALTER TABLE tag RENAME COLUMN title TO name;");
        Eventually(() =>
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            return tags.Pending() == 0;
        }).ShouldBeTrue("the loop goes on after a failed round");

        // a failure that lasts is one error, not one per round (review of pull request 23, N3)
        log.Count(LogLevel.Error, "could not be applied").ShouldBe(1);
        Eventually(() => log.Count(LogLevel.Information, "applied again") == 1).ShouldBeTrue();
    }

    [Fact]
    public void Options_the_application_registers_for_itself_do_not_change_the_catalog_of_the_container()
    {
        // N4: AddLui keeps its settings under a type of its own
        using var database = new TestDatabase();
        Run(database, "CREATE TABLE tag (id INTEGER PRIMARY KEY, name TEXT, usage_count INTEGER NOT NULL DEFAULT 0);");
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var host = Host(
            database,
            lui => lui.AddIndex("tags", index => index.SingleWord().LinkTo("tag", "id", "name"), applyPendingEvery: TimeSpan.FromSeconds(5)),
            new LuiCatalogOptions { TimeProvider = clock },
            builder => builder.Services.AddSingleton(new LuiCatalogOptions { TimeProvider = TimeProvider.System }));
        host.Start();
        var tags = host.Services.GetRequiredKeyedService<ILuiLinkedIndex<LuiNoPayload>>("tags");
        tags.CreateTriggers();
        Run(database, "INSERT INTO tag(id, name) VALUES (1, 'vegan');");

        // the timer runs on the clock AddLui was given
        Eventually(() =>
        {
            clock.Advance(TimeSpan.FromSeconds(5));
            return tags.Pending() == 0;
        }).ShouldBeTrue();
    }

    [Fact]
    public void The_typo_dictionaries_of_the_indexes_with_typo_tolerance_are_built_after_the_start()
    {
        using var database = new TestDatabase();
        using (var catalog = database.Open())
        {
            catalog.Index("typos").SingleWord().TypoTolerance().Ensure().Upsert(1, "kartoffel");
        }

        using var host = Host(database, lui => lui
            .AddIndex("typos", index => index.SingleWord().TypoTolerance())
            .AddIndex("plain", index => index.SingleWord())
            .WarmUpTypoDictionariesOnStart());
        host.Start();
        var dictionaries = host.Services.GetRequiredService<LuiCatalog>().TypoDictionaries;

        Eventually(() => dictionaries.IsBuilt("typos")).ShouldBeTrue("warmed up in the background");
        dictionaries.IsBuilt("plain").ShouldBeFalse();
    }

    [Fact]
    public async Task The_catalog_goes_with_the_host()
    {
        using var database = new TestDatabase();
        LuiCatalog catalog;
        using (var host = Host(database, lui => lui.AddIndex("words", index => index.SingleWord())))
        {
            await host.StartAsync();
            catalog = host.Services.GetRequiredService<LuiCatalog>();
            await host.StopAsync();
        }

        Should.Throw<ObjectDisposedException>(() => catalog.Index("words"));
    }

    [Fact]
    public void The_registration_refuses_what_cannot_work()
    {
        var services = new ServiceCollection();
        var lui = services.AddLui("Data Source=unused.db");

        Should.Throw<InvalidOperationException>(() => services.AddLui("Data Source=other.db"));
        lui.AddIndex("words", index => index.SingleWord());
        Should.Throw<ArgumentException>(() => lui.AddIndex("words", index => index.MultiWord()));
        Should.Throw<ArgumentException>(() => lui.AddIndex(" ", index => index.SingleWord()));
        Should.Throw<ArgumentOutOfRangeException>(() =>
            lui.AddIndex("tags", index => index.SingleWord().LinkTo("tag", "id", "name"), applyPendingEvery: TimeSpan.Zero));
    }

    [Fact]
    public void A_name_that_is_not_an_index_name_stops_the_start()
    {
        // the rule of the name is the core's (LuiCatalog.Index); the hosting package knows the public API only (ADR-0018)
        using var database = new TestDatabase();
        using var host = Host(database, lui => lui.AddIndex("Not A Name", index => index.SingleWord()));

        Should.Throw<ArgumentException>(host.Start).Message.ShouldContain("[a-z]");
    }

    [Fact]
    public void An_index_says_whether_it_corrects_typos()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();

        catalog.Index("plain").SingleWord().Ensure().TypoTolerance.ShouldBeFalse();
        catalog.Index("typos").SingleWord().TypoTolerance().Ensure().TypoTolerance.ShouldBeTrue();
    }

    private static IHost Host(
        TestDatabase database,
        Action<LuiServicesBuilder> indexes,
        LuiCatalogOptions? options = null,
        Action<HostApplicationBuilder>? more = null)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        indexes(builder.Services.AddLui(database.ConnectionString, options));
        more?.Invoke(builder);
        return builder.Build();
    }

    private static long Meta(TestDatabase database) =>
        database.Scalar<long>("SELECT count(*) FROM sqlite_master WHERE name = 'lui_meta'") == 0
            ? 0
            : database.Scalar<long>("SELECT count(*) FROM lui_meta");

    private static bool Eventually(Func<bool> condition) =>
        SpinWait.SpinUntil(
            () =>
            {
                if (condition())
                {
                    return true;
                }

                Thread.Sleep(20);
                return false;
            },
            Patience);

    private static void Run(TestDatabase database, string sql)
    {
        using var connection = database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>The log lines of LUI's hosted services, for the tests that ask how often something was logged.</summary>
    private sealed class CapturedLog : ILoggerProvider
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<(LogLevel Level, string Message)> _lines = new();

        public ILogger CreateLogger(string categoryName) =>
            categoryName.StartsWith("Nordmann.LUI", StringComparison.Ordinal) ? new Logger(_lines) : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        public int Count(LogLevel level, string text) =>
            _lines.Count(line => line.Level == level && line.Message.Contains(text, StringComparison.Ordinal));

        public void Dispose()
        {
        }

        private sealed class Logger(System.Collections.Concurrent.ConcurrentQueue<(LogLevel Level, string Message)> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                lines.Enqueue((logLevel, formatter(state, exception)));
        }
    }
}
