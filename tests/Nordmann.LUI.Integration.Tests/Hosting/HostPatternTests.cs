using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
        var initializer = host.Services.GetServices<IHostedService>().OfType<SearchInitializer>().Single();

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
            var initializer = second.Services.GetServices<IHostedService>().OfType<SearchInitializer>().Single();
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
            builder.Services.AddHostedService<SearchInitializer>();
        }

        return builder.Build();
    }

    /// <summary>The initializer of the docs — the application's own background start — and a gate before its first step.</summary>
    private sealed class SearchInitializer(LuiCatalog catalog, ILogger<SearchInitializer> logger, Gate gate) : BackgroundService
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
