using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using static Nordmann.LUI.Integration.Tests.Linked.LinkedTable;

namespace Nordmann.LUI.Integration.Tests.Hosting;

/// <summary>
/// The documented host pattern (ADR-0076, issue #266), in a generic host — the hosting package takes no web dependency (ADR-0018), and the
/// pattern needs none: the factory of <c>AddLui</c> only <b>opens</b> the catalog; the indexes are created, and initialized, in explicit steps
/// after <c>Build()</c>. So every configuration error stops the start at a line of <c>Program.cs</c>, and the application's own background
/// service initializes while the host is already up — the readiness check says "not ready" until <c>Initialize()</c> is done.
/// <para>
/// The background initializer is the one of the docs, with a gate: it stops at its first step until the test lets it go, so "up at once and
/// not ready yet" is a state the test holds, not one it races for. The gates are signals with a timeout of seconds — a stop for a hang, not
/// a reading of the clock.
/// </para>
/// </summary>
public sealed class HostPatternTests
{
    private static readonly LuiIndexDefinition<long> Recipes = new("recipes", index => index.SingleWord().TypoTolerance());

    private static readonly LuiLinkedIndexDefinition<long> Chefs =
        new("chefs", index => index.SingleWord().LinkTo("tag", "id", "name", weight: "usage_count"));

    private static readonly LuiIndexDefinition<long> Tags = new("tags", index => index.SingleWord());

    private static readonly TimeSpan HangStop = TimeSpan.FromSeconds(30);

    [Fact]
    public void A_broken_definition_fails_the_start_at_Create()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        using var host = Build(database);
        var catalog = host.Services.GetRequiredService<LuiCatalog>();

        // an invalid definition: the failure is at the line of Create in Program.cs, with the name of the definition — nothing is created, the host never started
        var broken = new LuiIndexDefinition<long>("Not A Valid Name", index => index.SingleWord());
        var failure = Should.Throw<LuiConfigurationException>(() => catalog.Create(Recipes, broken));
        failure.Message.ShouldContain("Not A Valid Name");
        catalog.Indexes().ShouldBeEmpty("all or none: the valid definition before it was not created either");

        // a linked definition whose table is not there
        var noTable = new LuiLinkedIndexDefinition<long>("chefs", index => index.SingleWord().LinkTo("no_such_table", "id", "name"));
        Should.Throw<LuiLinkedTableException>(() => catalog.Create(noTable));
        catalog.Indexes().ShouldBeEmpty();

        // and a mismatch with what is stored
        catalog.Create(Recipes);
        var other = new LuiIndexDefinition<long>("recipes", index => index.MultiWord());
        Should.Throw<LuiDefinitionMismatchException>(() => catalog.Create(other));
    }

    [Fact]
    public void A_failing_factory_is_not_cached_and_leaves_no_open_catalog_behind()
    {
        using var database = new TestDatabase();
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();

        // the file is not there yet: Open (not OpenOrCreate) refuses it, the container does not cache the failure, and the next resolution opens it
        builder.Services.AddLui(_ => LuiCatalog.Open(database.FilePath));
        using var host = builder.Build();
        Should.Throw<LuiCatalogNotFoundException>(() => host.Services.GetRequiredService<LuiCatalog>());
        Should.Throw<LuiCatalogNotFoundException>(() => host.Services.GetRequiredService<LuiCatalog>(), "not cached: it fails again, the same way");

        using (var creator = database.Open())
        {
            creator.Create(Tags);
        }

        host.Services.GetRequiredService<LuiCatalog>().Indexes().Select(index => index.Name).ShouldBe(["tags"]);
    }

    [Fact]
    public async Task With_the_background_initializer_ready_is_false_until_Initialize_is_done()
    {
        using var database = Written();
        var gate = new Gate();
        using var host = Build(database, gate, withInitializer: true);
        var catalog = host.Services.GetRequiredService<LuiCatalog>();
        catalog.Create(Recipes, Chefs, Tags);
        catalog.Index(Recipes).Upsert(1, "apfelkuchen");

        // the host starts at once: the initializer runs in the background and is held at its first step by the gate
        await host.StartAsync(TestContext.Current.CancellationToken);
        gate.Entered.Wait(HangStop, TestContext.Current.CancellationToken).ShouldBeTrue("the initializer began");
        var initializer = host.Services.GetServices<IHostedService>().OfType<GatedInitializer>().Single();

        Ready(catalog).ShouldBeFalse("the host is up, the search is not complete yet");
        initializer.ExecuteTask!.IsCompleted.ShouldBeFalse();
        catalog.Indexes().Single(index => string.Equals(index.Name, "chefs", StringComparison.Ordinal)).NeedsInitialization
            .ShouldBe(LuiInitializationNeeds.Triggers | LuiInitializationNeeds.InitialSync);
        catalog.Index(Recipes).Search("apfelkuchn").CorrectionSkipped.ShouldBeTrue("the search answers without corrections meanwhile");

        gate.Release.Set();
        await initializer.ExecuteTask.WaitAsync(HangStop, TestContext.Current.CancellationToken);

        Ready(catalog).ShouldBeTrue("Initialize() is done: every index has NeedsInitialization == None");
        catalog.Index(Recipes).Search("apfelkuchn").CorrectionSkipped.ShouldBeFalse();
        catalog.Index(Chefs).Search("kuchen").Hits.ShouldNotBeEmpty();
        initializer.Reported.ShouldNotBeNull().Ready.ShouldBeTrue();

        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_crash_after_Create_and_a_second_start_leave_every_index_ready()
    {
        using var database = Written();

        // the first start: Create, and the process is gone — nothing was initialized
        using (var first = Build(database))
        {
            var catalog = first.Services.GetRequiredService<LuiCatalog>();
            catalog.Create(Recipes, Chefs, Tags);
            Ready(catalog).ShouldBeFalse();
        }

        // the second start finds the indexes there — Create opens them — and initializes: ready
        using (var second = Build(database, new Gate(), withInitializer: true))
        {
            var catalog = second.Services.GetRequiredService<LuiCatalog>();
            catalog.Create(Recipes, Chefs, Tags);
            Ready(catalog).ShouldBeFalse();

            await second.StartAsync(TestContext.Current.CancellationToken);
            var initializer = second.Services.GetServices<IHostedService>().OfType<GatedInitializer>().Single();
            initializer.Gate.Release.Set();
            await initializer.ExecuteTask!.WaitAsync(HangStop, TestContext.Current.CancellationToken);

            Ready(catalog).ShouldBeTrue();
            await second.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public void A_crash_in_the_middle_of_the_first_sync_is_continued_by_the_next_start()
    {
        using var database = Written();
        using (var first = Build(database))
        {
            var catalog = first.Services.GetRequiredService<LuiCatalog>();
            catalog.Create(Recipes, Chefs, Tags);
            using var crash = new CancellationTokenSource();

            // the process stops after the first batch of the sync of 40 rows (the token of the host that stops, here: the crash)
            Should.Throw<OperationCanceledException>(() => catalog.Initialize(
                new LuiInitializeOptions
                {
                    Sync = new LuiSyncOptions { BatchSize = 10 },
                    Progress = new Inline(progress =>
                    {
                        if (progress.Sync?.Batches == 1)
                        {
                            crash.Cancel();
                        }
                    }),
                },
                crash.Token));

            Ready(catalog).ShouldBeFalse();
            catalog.Index(Chefs).Count().ShouldBe(10);
        }

        using var second = Build(database);
        var restarted = second.Services.GetRequiredService<LuiCatalog>();
        restarted.Create(Recipes, Chefs, Tags);

        restarted.Initialize();

        Ready(restarted).ShouldBeTrue();
        restarted.Index(Chefs).Count().ShouldBe(40);
    }

    [Fact]
    public async Task A_predecessor_that_was_killed_in_its_first_sync_leaves_a_lease_and_the_initializer_of_the_docs_finishes_when_it_expires()
    {
        // the real crash: a pod that is killed during its first sync does not release its lease (a cancelled run does, in its finally). The restarted pod
        // sees the lease for up to a minute — it reports InitialSyncRunningElsewhere, the readiness stays false, and the initializer repeats the call
        using var database = Written();
        var clock = new SignallingClock(new DateTimeOffset(2026, 11, 23, 9, 0, 0, TimeSpan.Zero));
        var logger = new RecordingLoggerProvider();
        using var host = BuildDocs(database, clock, logger);
        var catalog = host.Services.GetRequiredService<LuiCatalog>();
        catalog.Create(Recipes, Chefs, Tags);
        Run(database, $"UPDATE lui_meta SET sync_lease_owner = 'dead-pod', sync_lease_expires = {clock.GetUtcNow().AddSeconds(60).ToUnixTimeMilliseconds()} WHERE index_name = 'chefs';");
        var initializer = host.Services.GetServices<IHostedService>().OfType<SearchInitializer>().Single();

        await host.StartAsync(TestContext.Current.CancellationToken);
        await clock.Timers.WaitAsync(HangStop, TestContext.Current.CancellationToken);

        Ready(catalog).ShouldBeFalse("the lease of the dead pod is held");
        logger.Entries.Select(entry => entry.Message).ShouldContain(message => message.Contains("not ready yet", StringComparison.Ordinal) && message.Contains("InitialSyncRunningElsewhere", StringComparison.Ordinal));

        // the pause passes, the lease is still there: the call is repeated, and is not ready again
        clock.Advance(TimeSpan.FromSeconds(5));
        await clock.Timers.WaitAsync(HangStop, TestContext.Current.CancellationToken);
        Ready(catalog).ShouldBeFalse();
        initializer.ExecuteTask!.IsCompleted.ShouldBeFalse("the initializer goes on until the report says Ready");

        // the lease runs out: the next call takes it over, syncs, builds the dictionary — and the initializer ends
        clock.Advance(TimeSpan.FromSeconds(55));
        await initializer.ExecuteTask.WaitAsync(HangStop, TestContext.Current.CancellationToken);

        Ready(catalog).ShouldBeTrue();
        catalog.Index(Chefs).Count().ShouldBe(40);
        logger.Entries.Select(entry => entry.Message).ShouldContain(message => message.StartsWith("Search initialized", StringComparison.Ordinal));
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    [Trait("Category", "Timing")]
    public async Task A_first_call_that_fails_is_logged_and_the_initializer_of_the_docs_repeats_it_until_ready()
    {
        // a transient failure: another writer holds the write lock of the file at the start, the busy timeout (one second, the least) runs out, the
        // triggers cannot be created — Initialize() throws. The initializer logs it and tries again; it does not end, and the host does not stop.
        // (The one second is the wait of SQLite for the lock, not a claim about time: the test waits for the signals of the clock.)
        using var database = Written();
        var clock = new SignallingClock(new DateTimeOffset(2026, 11, 23, 9, 0, 0, TimeSpan.Zero));
        var logger = new RecordingLoggerProvider();
        using var host = BuildDocs(database, clock, logger, new LuiCatalogOptions { TimeProvider = clock, Pragmas = new LuiPragmas { BusyTimeout = TimeSpan.FromSeconds(1) } });
        var catalog = host.Services.GetRequiredService<LuiCatalog>();
        catalog.Create(Recipes, Chefs, Tags);
        var initializer = host.Services.GetServices<IHostedService>().OfType<SearchInitializer>().Single();

        using var blocker = database.Connect();
        using (var begin = blocker.CreateCommand())
        {
            begin.CommandText = "BEGIN IMMEDIATE;";
            begin.ExecuteNonQuery();
        }

        await host.StartAsync(TestContext.Current.CancellationToken);
        await clock.Timers.WaitAsync(HangStop, TestContext.Current.CancellationToken);

        var warning = logger.Entries.Single(entry => entry.Level == LogLevel.Warning);
        warning.Exception.ShouldBeOfType<LuiInitializeException>();
        initializer.ExecuteTask!.IsCompleted.ShouldBeFalse("a failed call does not end the initializer");
        Ready(catalog).ShouldBeFalse();

        // the other writer is done; the pause (ten seconds after the first failure) passes, and the repeated call completes
        using (var commit = blocker.CreateCommand())
        {
            commit.CommandText = "COMMIT;";
            commit.ExecuteNonQuery();
        }

        clock.Advance(TimeSpan.FromSeconds(10));
        await initializer.ExecuteTask.WaitAsync(HangStop, TestContext.Current.CancellationToken);

        Ready(catalog).ShouldBeTrue("the retry finished what the first call could not");
        catalog.Index(Chefs).Count().ShouldBe(40);
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_first_call_that_fails_before_any_step_with_a_plain_exception_is_repeated_too()
    {
        // not every failure is a LuiInitializeException: the read of the stored definitions that precedes the steps can throw the error of SQLite itself
        // (lui_meta is not there for a moment — a migration of the application). Such an exception that escapes the initializer would stop the host
        using var database = Written();
        var clock = new SignallingClock(new DateTimeOffset(2026, 11, 23, 9, 0, 0, TimeSpan.Zero));
        var logger = new RecordingLoggerProvider();
        using var host = BuildDocs(database, clock, logger);
        var catalog = host.Services.GetRequiredService<LuiCatalog>();
        catalog.Create(Recipes, Chefs, Tags);
        var initializer = host.Services.GetServices<IHostedService>().OfType<SearchInitializer>().Single();
        Run(database, "ALTER TABLE lui_meta RENAME TO lui_meta_away;");

        await host.StartAsync(TestContext.Current.CancellationToken);
        await clock.Timers.WaitAsync(HangStop, TestContext.Current.CancellationToken);

        logger.Entries.Count(entry => entry.Level == LogLevel.Warning).ShouldBe(1, "the failure is logged, and the initializer is still there");
        initializer.ExecuteTask!.IsCompleted.ShouldBeFalse();

        Run(database, "ALTER TABLE lui_meta_away RENAME TO lui_meta;");
        clock.Advance(TimeSpan.FromSeconds(10));
        await initializer.ExecuteTask.WaitAsync(HangStop, TestContext.Current.CancellationToken);

        Ready(catalog).ShouldBeTrue();
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public void The_initializer_the_tests_run_is_the_one_of_the_README_and_of_the_api_page()
    {
        // the docs show the initializer, and the package smoke test compiles the README's: this test says that the one the tests run — the one that
        // is repeated after a failure and after a sync elsewhere — is that very code, so that a page cannot teach what no test has run
        static string Normalized(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        static string ClassOf(string text, string start)
        {
            var from = text.LastIndexOf(start, StringComparison.Ordinal);
            from.ShouldBeGreaterThanOrEqualTo(0, "the page shows the initializer");
            var end = text.IndexOf("\n}\n", from, StringComparison.Ordinal);
            return Normalized(text[from..(end + 3)]);
        }

        var tests = ClassOf(Repository.NormalizedText(Path.Combine(Repository.RootPath, "tests", "Nordmann.LUI.Integration.Tests", "Hosting", "HostPatternTests.cs")).Replace("private sealed class", "sealed class", StringComparison.Ordinal).Replace("\n    ", "\n", StringComparison.Ordinal), "sealed class SearchInitializer");
        ClassOf(Repository.NormalizedText(Path.Combine(Repository.RootPath, "README.md")), "sealed class SearchInitializer").ShouldBe(tests);
        ClassOf(Repository.NormalizedText(Path.Combine(Repository.RootPath, "docs", "api.md")), "sealed class SearchInitializer").ShouldBe(tests);
    }

    /// <summary>The readiness check of the docs: not ready while any index has <c>NeedsInitialization != None</c>.</summary>
    private static bool Ready(LuiCatalog catalog) =>
        catalog.Indexes().All(index => index.NeedsInitialization == LuiInitializationNeeds.None);

    /// <summary>A file of the application with a table of 40 rows — the process that opens it next is a start.</summary>
    private static TestDatabase Written()
    {
        var database = new TestDatabase();
        Run(database, Table);
        Run(database, "WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n WHERE i < 40) INSERT INTO tag(id, name, usage_count) SELECT i, 'kuchen' || i, i % 7 FROM n;");
        return database;
    }

    private static IHost Build(TestDatabase database, Gate? gate = null, bool withInitializer = false)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Services.AddLui(_ => LuiCatalog.OpenOrCreate(database.FilePath));    // open only
        if (withInitializer)
        {
            builder.Services.AddSingleton(gate ?? new Gate());
            builder.Services.AddHostedService<GatedInitializer>();
        }

        return builder.Build();
    }

    private static IHost BuildDocs(TestDatabase database, FakeTimeProvider clock, RecordingLoggerProvider logger, LuiCatalogOptions? options = null)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.Services.AddLui(_ => LuiCatalog.OpenOrCreate(database.FilePath, options ?? new LuiCatalogOptions { TimeProvider = clock }));
        builder.Services.AddHostedService<SearchInitializer>();
        builder.Logging.AddProvider(logger);
        return builder.Build();
    }

    /// <summary>
    /// The initializer of the docs — the very class of the README and of docs/api.md, character for character (a test compares them): it repeats
    /// Initialize() until the report says Ready, also after a failure. The pause is the clock of the test.
    /// </summary>
#pragma warning disable MA0004, CA1873, CA1031 // the class is the one of the docs, character for character: it is written for an application, not for these analyzers
    private sealed class SearchInitializer(LuiCatalog catalog, ILogger<SearchInitializer> logger, TimeProvider? time = null) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var clock = time ?? TimeProvider.System;
            var pause = TimeSpan.FromSeconds(5);
            try
            {
                // Initialize() is idempotent and safe to repeat: repeat it until the report says Ready — also after a failure, which may be transient
                // (a busy file while another replica syncs), and while an index waits for a first sync that another process runs
                while (true)
                {
                    try
                    {
                        var report = await Task.Run(() => catalog.Initialize(cancellationToken: stoppingToken), stoppingToken);
                        if (report.Ready)
                        {
                            logger.LogInformation("Search initialized: {Report}", report);
                            return;
                        }

                        logger.LogInformation("Search is not ready yet: {Report}", report);
                        pause = TimeSpan.FromSeconds(5);
                    }
                    catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                    {
                        // a LuiInitializeException, or the error of SQLite itself before the first step: never let it escape — an exception that leaves
                        // ExecuteAsync stops the host. A permanent failure is logged again at every attempt
                        pause = TimeSpan.FromTicks(Math.Min(pause.Ticks * 2, TimeSpan.FromMinutes(1).Ticks));
                        logger.LogWarning(exception, "Search could not be initialized yet, trying again in {Pause}", pause);
                    }

                    await Task.Delay(pause, clock, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // the host stops
            }
        }
    }
#pragma warning restore MA0004, CA1873, CA1031

    /// <summary>A clock that tells when a timer is made — the initializer waits on one after every call that is not ready or failed — so that the test moves the clock after that and never before.</summary>
    private sealed class SignallingClock(DateTimeOffset start) : FakeTimeProvider(start)
    {
        internal SemaphoreSlim Timers { get; } = new(0);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = base.CreateTimer(callback, state, dueTime, period);
            _ = Timers.Release();
            return timer;
        }
    }

    /// <summary>A one-shot initializer with a gate before its first step — for the tests that hold "up and not ready"; the initializer of the docs is <see cref="SearchInitializer"/>.</summary>
    private sealed class GatedInitializer(LuiCatalog catalog, ILogger<GatedInitializer> logger, Gate gate) : BackgroundService
    {
        internal Gate Gate { get; } = gate;

        internal LuiInitializeReport? Reported { get; private set; }

        protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.Run(
            () =>
            {
                var report = catalog.Initialize(new LuiInitializeOptions { Progress = Gate }, stoppingToken);
                Reported = report;
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("Search initialized: {Report}", report);
                }
            },
            stoppingToken);
    }

    /// <summary>Holds the first step of <c>Initialize()</c> until the test lets it go.</summary>
    private sealed class Gate : IProgress<LuiInitializeProgress>
    {
        private int _held;

        internal ManualResetEventSlim Entered { get; } = new();

        internal ManualResetEventSlim Release { get; } = new();

        public void Report(LuiInitializeProgress value)
        {
            if (Interlocked.Exchange(ref _held, 1) != 0)
            {
                return;
            }

            Entered.Set();
            Release.Wait(HangStop).ShouldBeTrue("the test lets the initializer go");
        }
    }

    private sealed class Inline(Action<LuiInitializeProgress> action) : IProgress<LuiInitializeProgress>
    {
        public void Report(LuiInitializeProgress value) => action(value);
    }
}
