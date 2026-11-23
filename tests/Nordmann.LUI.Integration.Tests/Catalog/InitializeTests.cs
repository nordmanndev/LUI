using Shouldly;
using static Nordmann.LUI.Integration.Tests.Linked.LinkedTable;

namespace Nordmann.LUI.Integration.Tests.Catalog;

/// <summary>
/// <c>catalog.Initialize()</c> (ADR-0076, issue #266): the one explicit, synchronous start call. It does everything an index needs at the
/// start of a process, in LUI's order and driven by the state of each index — the triggers of every linked index, then the first sync of
/// every linked index that needs one, then the outbox, then the typo dictionaries one after the other — and a second call does nothing.
/// A failing index does not stop the others; a cancelled run keeps what it finished; a read-only catalog builds its dictionaries and
/// leaves the file steps to the writing process, which <c>LuiIndexInfo.NeedsInitialization</c> reports.
/// <para>
/// The steps are observed from inside: <c>LuiInitializeOptions.Progress</c> is told right before every step, on the thread of the call —
/// a test that wants something to happen between two steps does it there, and nothing waits for a clock.
/// </para>
/// </summary>
public sealed class InitializeTests
{
    private static readonly LuiIndexDefinition<long> Recipes = new("recipes", index => index.SingleWord().TypoTolerance());

    private static readonly LuiLinkedIndexDefinition<long> Chefs =
        new("chefs", index => index.SingleWord().LinkTo("tag", "id", "name", weight: "usage_count"));

    private static readonly LuiLinkedIndexDefinition<long> Cooks =
        new("cooks", index => index.SingleWord().TypoTolerance().LinkTo("tag", "id", "name", weight: "usage_count"));

    private static readonly LuiIndexDefinition<long> Tags = new("tags", index => index.SingleWord());

    private static readonly LuiIndexDefinition<long> Missing = new("missing", index => index.SingleWord());

    /// <summary>
    /// A file with three indexes, written by a catalog that is gone again: an embedded index with typo tolerance and content, a linked
    /// one over a table of five rows, and an embedded one without. Nothing is initialized — the process that opens it next is the start.
    /// </summary>
    private static TestDatabase World(bool withCooks = false)
    {
        var database = new TestDatabase();
        Run(database, Table);
        Insert(database, (1, "kuchen", 5), (2, "kuchenteig", 4), (3, "torte", 3), (4, "tortenboden", 2), (5, "keks", 1));
        using var setup = database.Open();
        var recipes = setup.Create(Recipes);
        recipes.Upsert(1, "apfelkuchen");
        recipes.Upsert(2, "marmorkuchen");
        setup.Create(Chefs, Tags);
        if (withCooks)
        {
            setup.Create(Cooks);
        }

        return database;
    }

    // ---- the defaults: every index ready, in LUI's order -----------------------------------------------------------

    [Fact]
    public void Initialize_with_defaults_brings_three_indexes_to_ready_in_order()
    {
        using var database = World();
        using var catalog = database.Open();
        Info(catalog, "chefs").NeedsInitialization
            .ShouldBe(LuiInitializationNeeds.Triggers | LuiInitializationNeeds.InitialSync, "before the call a linked index needs both file steps");
        var steps = new Steps();

        // a write that comes after the first batch of the sync, for a row that was synced already: the trigger notes it in the outbox,
        // and only the outbox step brings it into the index
        steps.When(progress => progress.Sync?.Batches == 1, () => Run(database, "UPDATE tag SET name = 'sellerie' WHERE id = 1;"));

        var report = catalog.Initialize(new LuiInitializeOptions { Progress = steps, Sync = new LuiSyncOptions { BatchSize = 2 } });

        steps.Told.ShouldBe(
            [
                ("chefs", LuiInitializeStep.Triggers),
                ("chefs", LuiInitializeStep.InitialSync),
                ("chefs", LuiInitializeStep.Outbox),
                ("recipes", LuiInitializeStep.TypoDictionary),
            ],
            "exactly the steps the state of each index needs, in the order of the issue: triggers, first sync, outbox, dictionaries last");

        var chefs = Line(report, "chefs");
        chefs.TriggersInstalled.ShouldBeTrue();
        chefs.Sync.ShouldNotBeNull().Indexed.ShouldBe(5);
        chefs.Sync.Batches.ShouldBe(3);
        chefs.PendingApplied.ShouldBe(1);
        chefs.TypoDictionaryBuilt.ShouldBeFalse("the index forgives no typo");
        chefs.Failed.ShouldBeFalse();
        chefs.Skipped.ShouldBe(LuiInitializationNeeds.None);

        var recipes = Line(report, "recipes");
        recipes.TypoDictionaryBuilt.ShouldBeTrue();
        recipes.TriggersInstalled.ShouldBeFalse();
        recipes.Sync.ShouldBeNull();
        recipes.PendingApplied.ShouldBe(0);

        var tags = Line(report, "tags");
        (tags.TriggersInstalled, tags.Sync, tags.PendingApplied, tags.TypoDictionaryBuilt, tags.Failed).ShouldBe((false, null, 0, false, false), "an index that needs nothing gets nothing");

        report.Indexes.Select(line => line.Name).ShouldBe(["chefs", "recipes", "tags"], "one line per index, in the order of the names");
        report.Failed.ShouldBeFalse();
        report.Ready.ShouldBeTrue();
        report.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.Zero);

        catalog.Indexes().ShouldAllBe(index => index.NeedsInitialization == LuiInitializationNeeds.None);
        catalog.Index(Chefs).Triggers().ShouldBe(LuiTriggerState.Current);
        catalog.Index(Chefs).Search("sellerie").Hits.Select(hit => hit.Key).ShouldBe([1L], "the row of the outbox is in the index");
        catalog.Index(Recipes).TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.Current);
    }

    [Fact]
    public void The_steps_run_across_the_indexes_not_index_by_index()
    {
        using var database = World(withCooks: true);
        using var catalog = database.Open();
        var steps = new Steps();

        catalog.Initialize(new LuiInitializeOptions { Progress = steps });

        steps.Told.ShouldBe(
            [
                ("chefs", LuiInitializeStep.Triggers),
                ("cooks", LuiInitializeStep.Triggers),
                ("chefs", LuiInitializeStep.InitialSync),
                ("cooks", LuiInitializeStep.InitialSync),
                ("cooks", LuiInitializeStep.TypoDictionary),
                ("recipes", LuiInitializeStep.TypoDictionary),
            ],
            "the triggers of every index, then the syncs of every index, the dictionaries last — one step across the indexes at a time");
    }

    [Fact]
    public void The_triggers_come_before_the_first_sync_so_that_a_row_written_between_the_two_is_not_lost()
    {
        using var database = World();
        using var catalog = database.Open();
        var steps = new Steps();

        // a row is written right before every file step: before the triggers it is read by the sync; between the triggers and the sync
        // the trigger notes it. With the sync first, the row written before the triggers is in neither — and the flag is false by then
        var next = 100;
        steps.When(
            progress => progress.Sync is null && progress.Step is LuiInitializeStep.Triggers or LuiInitializeStep.InitialSync,
            () => Run(database, $"INSERT INTO tag(id, name, usage_count) VALUES ({next}, 'zwischenrow{next++}', 1);"));

        catalog.Initialize(new LuiInitializeOptions { Progress = steps });

        var chefs = catalog.Index(Chefs);
        chefs.Search("zwischenrow100").Hits.ShouldHaveSingleItem("written before the triggers: the sync reads it");
        chefs.Search("zwischenrow101").Hits.ShouldHaveSingleItem("written between the triggers and the sync: noted by the trigger and read by the sync");
        steps.Told.Select(told => told.Step).ShouldBe([LuiInitializeStep.Triggers, LuiInitializeStep.InitialSync, LuiInitializeStep.TypoDictionary]);
    }

    [Fact]
    public void A_second_Initialize_does_nothing()
    {
        using var database = World(withCooks: true);
        using var catalog = database.Open();
        catalog.Initialize();

        var steps = new Steps();
        var generation = database.Scalar<long>("SELECT sync_generation FROM lui_meta WHERE index_name = 'chefs';");
        var report = catalog.Initialize(new LuiInitializeOptions { Progress = steps });

        steps.Told.ShouldBeEmpty("a step that is not needed is not started — not even announced");
        report.Indexes.ShouldAllBe(line => !line.TriggersInstalled && line.Sync == null && line.PendingApplied == 0 && !line.TypoDictionaryBuilt && !line.Failed && line.Skipped == LuiInitializationNeeds.None);
        database.Scalar<long>("SELECT sync_generation FROM lui_meta WHERE index_name = 'chefs';").ShouldBe(generation, "no sync ran");

        // and not after a reopen of the catalog either, but for the dictionaries, which live in the memory of the process
        using var second = database.Open();
        var stepsOfTheSecond = new Steps();
        second.Initialize(new LuiInitializeOptions { Progress = stepsOfTheSecond });
        stepsOfTheSecond.Told.ShouldBe([("cooks", LuiInitializeStep.TypoDictionary), ("recipes", LuiInitializeStep.TypoDictionary)], "every process builds its own dictionaries, and nothing else");
    }

    // ---- the options ------------------------------------------------------------------------------------------------

    [Fact]
    public void Each_option_switches_its_step_off()
    {
        // InstallTriggers
        using (var database = World())
        {
            using var catalog = database.Open();
            var report = catalog.Initialize(new LuiInitializeOptions { InstallTriggers = false });
            Line(report, "chefs").TriggersInstalled.ShouldBeFalse();
            catalog.Index(Chefs).Triggers().ShouldBe(LuiTriggerState.None);
            catalog.Index(Chefs).NeedsInitialSync.ShouldBeFalse("the other steps still run");
            Line(report, "chefs").Skipped.ShouldBe(LuiInitializationNeeds.Triggers, "what was switched off stays a need");
        }

        // InitialSync
        using (var database = World())
        {
            using var catalog = database.Open();
            var report = catalog.Initialize(new LuiInitializeOptions { InitialSync = false });
            Line(report, "chefs").Sync.ShouldBeNull();
            catalog.Index(Chefs).NeedsInitialSync.ShouldBeTrue();
            catalog.Index(Chefs).Count().ShouldBe(0);
            catalog.Index(Chefs).Triggers().ShouldBe(LuiTriggerState.Current, "the other steps still run");
            Line(report, "chefs").Skipped.ShouldBe(LuiInitializationNeeds.InitialSync);
        }

        // ApplyPending
        using (var database = World())
        {
            using var catalog = database.Open();
            catalog.Initialize();
            Insert(database, (10, "kuchenform", 1));

            var report = catalog.Initialize(new LuiInitializeOptions { ApplyPending = false });
            Line(report, "chefs").PendingApplied.ShouldBe(0);
            catalog.Index(Chefs).Pending().ShouldBe(1, "the outbox waits");

            Line(catalog.Initialize(), "chefs").PendingApplied.ShouldBe(1);
            catalog.Index(Chefs).Pending().ShouldBe(0);
        }

        // WarmUpTypoDictionaries
        using (var database = World())
        {
            using var catalog = database.Open();
            var report = catalog.Initialize(new LuiInitializeOptions { WarmUpTypoDictionaries = false });
            Line(report, "recipes").TypoDictionaryBuilt.ShouldBeFalse();
            catalog.Index(Recipes).TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.NotBuilt);
            Line(report, "recipes").Skipped.ShouldBe(LuiInitializationNeeds.TypoDictionary);
        }
    }

    [Fact]
    public void Indexes_limits_the_run()
    {
        using var database = World(withCooks: true);
        using var catalog = database.Open();
        var steps = new Steps();

        var report = catalog.Initialize(new LuiInitializeOptions { Indexes = [Recipes, Cooks], Progress = steps });

        report.Indexes.Select(line => line.Name).ShouldBe(["cooks", "recipes"], "the selected indexes, and no other");
        steps.Told.Select(told => told.IndexName).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ShouldBe(["cooks", "recipes"]);
        catalog.Index(Chefs).Triggers().ShouldBe(LuiTriggerState.None, "chefs was not selected");
        catalog.Index(Chefs).NeedsInitialSync.ShouldBeTrue();
        Info(catalog, "chefs").NeedsInitialization.ShouldBe(LuiInitializationNeeds.Triggers | LuiInitializationNeeds.InitialSync);

        // in parts: the rest comes with the next call
        catalog.Initialize(new LuiInitializeOptions { Indexes = [Chefs] });
        catalog.Indexes().ShouldAllBe(index => index.NeedsInitialization == LuiInitializationNeeds.None);
    }

    [Fact]
    public void A_definition_of_a_missing_index_throws_before_any_step()
    {
        using var database = World();
        using var catalog = database.Open();
        var steps = new Steps();

        var failure = Should.Throw<LuiIndexNotFoundException>(() => catalog.Initialize(new LuiInitializeOptions { Indexes = [Recipes, Chefs, Missing], Progress = steps }));

        failure.IndexName.ShouldBe("missing");
        steps.Told.ShouldBeEmpty("no step ran, not even for the indexes that exist");
        catalog.Index(Chefs).Triggers().ShouldBe(LuiTriggerState.None);
        catalog.Index(Recipes).TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.NotBuilt);
    }

    [Fact]
    public void Without_a_list_every_index_of_the_file_is_initialized_from_its_stored_definition()
    {
        // a tool that has no definitions: the catalog knows only what the file stores
        using var database = World(withCooks: true);
        using var tool = LuiCatalog.Open(database.FilePath);

        tool.Initialize().Indexes.Select(line => line.Name).ShouldBe(["chefs", "cooks", "recipes", "tags"]);

        tool.Indexes().ShouldAllBe(index => index.NeedsInitialization == LuiInitializationNeeds.None);
    }

    // ---- failures ---------------------------------------------------------------------------------------------------

    [Fact]
    public void One_failing_index_does_not_stop_the_others()
    {
        using var database = World(withCooks: true);
        using var catalog = database.Open();

        // the application's table is gone: the triggers of both linked indexes cannot be created
        Run(database, "DROP TABLE tag;");
        var steps = new Steps();

        var failure = Should.Throw<LuiInitializeException>(() => catalog.Initialize(new LuiInitializeOptions { Progress = steps }));

        failure.Errors.Keys.Order(StringComparer.Ordinal).ToArray().ShouldBe(["chefs", "cooks"], "every failing index, by name");
        failure.Errors.Values.ShouldAllBe(error => error != null);
        failure.Message.ShouldContain("'chefs'");
        failure.Message.ShouldContain("'cooks'");
        failure.Message.ShouldContain("at " + nameof(LuiInitializeStep.Triggers), Case.Sensitive); // the message names the failing step
        failure.Report.Failed.ShouldBeTrue();
        failure.Report.Ready.ShouldBeFalse();

        var chefs = Line(failure.Report, "chefs");
        chefs.Failed.ShouldBeTrue();
        chefs.FailedStep.ShouldBe(LuiInitializeStep.Triggers);
        chefs.Sync.ShouldBeNull("the later steps of a failed index are skipped");
        chefs.Skipped.ShouldNotBe(LuiInitializationNeeds.None);

        steps.Told.ShouldNotContain(told => told.Step == LuiInitializeStep.InitialSync, "no sync of an index whose triggers failed");
        Line(failure.Report, "recipes").TypoDictionaryBuilt.ShouldBeTrue("the other indexes go on");
        catalog.Index(Recipes).TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.Current);

        // the failed index still says what is missing
        var info = Info(catalog, "chefs");
        info.NeedsInitialization.HasFlag(LuiInitializationNeeds.Triggers).ShouldBeTrue();
        info.NeedsInitialization.HasFlag(LuiInitializationNeeds.InitialSync).ShouldBeTrue();
    }

    [Fact]
    public void A_failing_index_with_a_typo_dictionary_of_its_own_gets_none_and_does_not_block_the_next()
    {
        using var database = World(withCooks: true);
        using var catalog = database.Open();
        Run(database, "DROP TABLE tag;");

        var failure = Should.Throw<LuiInitializeException>(() => catalog.Initialize());

        Line(failure.Report, "cooks").TypoDictionaryBuilt.ShouldBeFalse("a failed index gets no later step");
        Line(failure.Report, "recipes").TypoDictionaryBuilt.ShouldBeTrue();
    }

    // ---- cancellation -----------------------------------------------------------------------------------------------

    [Fact]
    public void A_run_cancelled_during_a_sync_keeps_what_it_finished()
    {
        using var database = World();
        Run(database, "WITH RECURSIVE n(i) AS (SELECT 10 UNION ALL SELECT i + 1 FROM n WHERE i < 59) INSERT INTO tag(id, name, usage_count) SELECT i, 'kuchen' || i, 1 FROM n;");
        using var catalog = database.Open();
        using var cancellation = new CancellationTokenSource();
        var steps = new Steps();
        steps.When(progress => progress.Sync?.Batches == 2, cancellation.Cancel);

        Should.Throw<OperationCanceledException>(() => catalog.Initialize(
            new LuiInitializeOptions { Progress = steps, Sync = new LuiSyncOptions { BatchSize = 10 } },
            cancellation.Token));

        var chefs = catalog.Index(Chefs);
        chefs.Triggers().ShouldBe(LuiTriggerState.Current, "the finished step stays");
        chefs.Count().ShouldBe(20, "the two batches that were committed stay");
        chefs.NeedsInitialSync.ShouldBeTrue();
        catalog.Index(Recipes).TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.NotBuilt, "the dictionaries come last, and the run did not get there");
        database.Scalar<long>("SELECT count(*) FROM lui_meta WHERE sync_lease_owner IS NOT NULL;").ShouldBe(0, "the lease is released");

        var second = new Steps();
        catalog.Initialize(new LuiInitializeOptions { Progress = second, Sync = new LuiSyncOptions { BatchSize = 10 } });

        second.Told.Select(told => told.Step).Distinct().ShouldBe([LuiInitializeStep.InitialSync, LuiInitializeStep.TypoDictionary], "the next call continues by state: the triggers are there");
        chefs.Count().ShouldBe(55);
        catalog.Indexes().ShouldAllBe(index => index.NeedsInitialization == LuiInitializationNeeds.None);
    }

    [Fact]
    public void A_token_that_is_cancelled_before_the_call_starts_no_step()
    {
        using var database = World();
        using var catalog = database.Open();
        var steps = new Steps();

        Should.Throw<OperationCanceledException>(() => catalog.Initialize(new LuiInitializeOptions { Progress = steps }, new CancellationToken(canceled: true)));

        steps.Told.ShouldBeEmpty();
        catalog.Index(Chefs).Triggers().ShouldBe(LuiTriggerState.None);
    }

    // ---- the dictionaries ---------------------------------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Timing")]
    public void The_dictionaries_are_built_one_after_the_other()
    {
        // a dictionary costs memory: the peak is that of the largest, not the sum — so no two builds overlap. The build reads the texts of
        // its index while it builds, so a read that is open is a build that runs; the seam holds every build for a moment, long enough for a
        // second one to show itself if it was started beside it. Load only makes a build later: it can hide an overlap, never make one up.
        using var database = new TestDatabase();
        using (var setup = database.Open())
        {
            foreach (var name in new[] { "alpha", "beta", "gamma" })
            {
                setup.Create(new LuiIndexDefinition<long>(name, index => index.SingleWord().TypoTolerance())).Upsert(1, "apfelkuchen");
            }
        }

        using var catalog = database.Open();
        var active = 0;
        var most = 0;
        var builds = 0;
        using var overlapped = new ManualResetEventSlim();
        catalog.Database.TypoTextsReading = began =>
        {
            if (!began)
            {
                Interlocked.Decrement(ref active);
                return;
            }

            var now = Interlocked.Increment(ref active);
            Interlocked.Increment(ref builds);
            InterlockedMax(ref most, now);
            if (now > 1)
            {
                overlapped.Set();
            }

            // the hold: a build that runs alone waits the whole time, one that runs beside another is let go by it
            overlapped.Wait(TimeSpan.FromMilliseconds(250));
        };

        catalog.Initialize();

        builds.ShouldBe(3, "one build per index");
        most.ShouldBe(1, "no two builds overlap");
    }

    [Fact]
    public void A_dictionary_waits_for_the_first_sync_of_its_index_and_knows_the_words_of_it()
    {
        using var database = World(withCooks: true);
        using var catalog = database.Open();
        var steps = new Steps();

        catalog.Initialize(new LuiInitializeOptions { Progress = steps });

        steps.Told.ToList().FindIndex(told => told is ("cooks", LuiInitializeStep.TypoDictionary))
            .ShouldBeGreaterThan(steps.Told.ToList().FindIndex(told => told is ("cooks", LuiInitializeStep.InitialSync)), "the dictionary of a linked index comes after its sync");

        // the words of the sync are in the dictionary: a typo of one of them is corrected
        var result = catalog.Index(Cooks).Search("tortenbodn");
        result.CorrectionSkipped.ShouldBeFalse();
        result.Hits.Select(hit => hit.Key).ShouldContain(4L);
    }

    [Fact]
    public void A_dictionary_waits_for_the_file_steps_of_its_index_also_when_an_option_switched_them_off()
    {
        // the rule is about the state of the index and not about the intention of the call: an index with a file step left gets no dictionary yet —
        // it would miss the words of a sync still to come — and the reason stays. The next call, with the steps on, builds it
        using (var database = World(withCooks: true))
        {
            using var catalog = database.Open();

            var report = catalog.Initialize(new LuiInitializeOptions { InitialSync = false, Indexes = [Cooks, Recipes] });

            Line(report, "cooks").TypoDictionaryBuilt.ShouldBeFalse("its first sync is still due");
            Line(report, "cooks").Skipped.ShouldBe(LuiInitializationNeeds.InitialSync | LuiInitializationNeeds.TypoDictionary);
            Line(report, "recipes").TypoDictionaryBuilt.ShouldBeTrue("an embedded index has no file step");

            catalog.Initialize(new LuiInitializeOptions { Indexes = [Cooks] });
            catalog.Index(Cooks).TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.Current);
        }

        using (var database = World(withCooks: true))
        {
            using var catalog = database.Open();

            var report = catalog.Initialize(new LuiInitializeOptions { InstallTriggers = false, Indexes = [Cooks] });

            Line(report, "cooks").Sync.ShouldNotBeNull("the sync is not what was switched off");
            Line(report, "cooks").TypoDictionaryBuilt.ShouldBeFalse("its triggers are missing");
            Line(report, "cooks").Skipped.ShouldBe(LuiInitializationNeeds.Triggers | LuiInitializationNeeds.TypoDictionary);
        }
    }

    // ---- a read-only catalog ----------------------------------------------------------------------------------------

    [Fact]
    public void On_a_read_only_catalog_only_the_dictionaries_are_built()
    {
        using var database = World(withCooks: true);
        using var reader = LuiCatalog.OpenReadOnly(database.FilePath);
        reader.IsReadOnly.ShouldBeTrue();
        var steps = new Steps();

        var report = reader.Initialize(new LuiInitializeOptions { Progress = steps });

        steps.Told.ShouldBe([("cooks", LuiInitializeStep.TypoDictionary), ("recipes", LuiInitializeStep.TypoDictionary)], "the file steps cannot be done and are left to the writing process");
        report.Failed.ShouldBeFalse("a read-only catalog does not fail for what it cannot do");
        Line(report, "recipes").TypoDictionaryBuilt.ShouldBeTrue();
        Line(report, "cooks").TypoDictionaryBuilt.ShouldBeTrue("also a linked index: the writing process does the sync, this one holds the words it has");
        Line(report, "chefs").Skipped.ShouldBe(LuiInitializationNeeds.Triggers | LuiInitializationNeeds.InitialSync);

        // a reader can tell "not ready yet" from "ready": its readiness looks only at the dictionary
        var infos = reader.Indexes();
        infos.Single(index => string.Equals(index.Name, "chefs", StringComparison.Ordinal)).NeedsInitialization.ShouldBe(LuiInitializationNeeds.Triggers | LuiInitializationNeeds.InitialSync);
        infos.ShouldAllBe(index => !index.NeedsInitialization.HasFlag(LuiInitializationNeeds.TypoDictionary));

        // the writer of the file did none of it
        using var writer = database.Open();
        writer.Index(Chefs).Triggers().ShouldBe(LuiTriggerState.None);
        writer.Index(Chefs).NeedsInitialSync.ShouldBeTrue();
    }

    // ---- forgetting is visible ---------------------------------------------------------------------------------------

    [Fact]
    public void Without_Initialize_the_needs_are_reported_and_nothing_was_done()
    {
        using var database = World();
        using var catalog = database.Open();

        var infos = catalog.Indexes().ToDictionary(index => index.Name, index => index.NeedsInitialization, StringComparer.Ordinal);

        infos["recipes"].ShouldBe(LuiInitializationNeeds.TypoDictionary, "a typo-tolerant index never corrects without its dictionary");
        infos["chefs"].ShouldBe(LuiInitializationNeeds.InitialSync | LuiInitializationNeeds.Triggers);
        infos["tags"].ShouldBe(LuiInitializationNeeds.None);

        // LUI did none of the steps on its own: opening the handles and searching starts nothing
        var recipes = catalog.Index(Recipes);
        var result = recipes.Search("apfelkuchn");
        result.CorrectionSkipped.ShouldBeTrue("a query never builds the dictionary (ADR-0042)");
        recipes.TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.NotBuilt);
        var chefs = catalog.Index(Chefs);
        chefs.Search("kuchen").Hits.ShouldBeEmpty();
        chefs.Triggers().ShouldBe(LuiTriggerState.None);
        chefs.NeedsInitialSync.ShouldBeTrue();
        Info(catalog, "chefs").NeedsInitialization.ShouldBe(LuiInitializationNeeds.InitialSync | LuiInitializationNeeds.Triggers);
    }

    [Fact]
    public void NeedsInitialization_reads_the_state_of_the_index_from_the_file_and_names_it_in_text()
    {
        using var database = World();
        var clock = new FixedClock();
        using var catalog = database.Open(new LuiCatalogOptions { TimeProvider = clock });
        var chefs = catalog.Index(Chefs);
        LuiInitializationNeeds Needs() => Info(catalog, "chefs").NeedsInitialization;

        Needs().ShouldBe(LuiInitializationNeeds.Triggers | LuiInitializationNeeds.InitialSync);

        chefs.CreateTriggers();
        Needs().ShouldBe(LuiInitializationNeeds.InitialSync);

        // a sync that another process runs: its lease is held and has not expired
        Run(database, $"UPDATE lui_meta SET sync_lease_owner = 'another', sync_lease_expires = {clock.GetUtcNow().AddSeconds(30).ToUnixTimeMilliseconds()} WHERE index_name = 'chefs';");
        Needs().ShouldBe(LuiInitializationNeeds.InitialSyncRunningElsewhere, "running elsewhere instead of due: the call would not run it");

        // a lease whose owner stopped renewing expires — the next sync takes it over, so the sync is due again
        clock.Advance(TimeSpan.FromSeconds(31));
        Needs().ShouldBe(LuiInitializationNeeds.InitialSync);

        // triggers that differ from the ones LUI writes
        Run(database, "DROP TRIGGER lui_chefs_ai;");
        Needs().ShouldBe(LuiInitializationNeeds.InitialSync | LuiInitializationNeeds.StaleTriggers);

        chefs.CreateTriggers();
        chefs.Sync();
        Needs().ShouldBe(LuiInitializationNeeds.None, "a complete sync clears the flag on a file that another catalog reads");

        // a lease that is held on an index that is synced already is no need: the next sync is an update, not the first
        Run(database, $"UPDATE lui_meta SET sync_lease_owner = 'another', sync_lease_expires = {clock.GetUtcNow().AddSeconds(30).ToUnixTimeMilliseconds()} WHERE index_name = 'chefs';");
        Needs().ShouldBe(LuiInitializationNeeds.None);

        (LuiInitializationNeeds.Triggers | LuiInitializationNeeds.InitialSync).ToString().ShouldBe("Triggers, InitialSync", "readable in a log");
        LuiInitializationNeeds.None.ToString().ShouldBe("None");
    }

    [Fact]
    public void A_sync_that_another_process_runs_is_skipped_and_reported_and_blocks_the_dictionary_of_that_index()
    {
        using var database = World(withCooks: true);
        var clock = new FixedClock();
        using var catalog = database.Open(new LuiCatalogOptions { TimeProvider = clock });
        catalog.Index(Chefs).CreateTriggers();
        Run(database, $"UPDATE lui_meta SET sync_lease_owner = 'another', sync_lease_expires = {clock.GetUtcNow().AddSeconds(30).ToUnixTimeMilliseconds()} WHERE index_name IN ('chefs', 'cooks');");
        var steps = new Steps();

        var report = catalog.Initialize(new LuiInitializeOptions { Progress = steps });

        report.Failed.ShouldBeFalse("a held lease is no failure");
        steps.Told.ShouldNotContain(told => told.Step == LuiInitializeStep.InitialSync);
        Line(report, "chefs").Skipped.ShouldBe(LuiInitializationNeeds.InitialSyncRunningElsewhere);
        var cooks = Line(report, "cooks");
        cooks.Skipped.ShouldBe(LuiInitializationNeeds.InitialSyncRunningElsewhere | LuiInitializationNeeds.TypoDictionary, "its dictionary would miss the words of the sync that is still to come: the reason stays");
        cooks.TypoDictionaryBuilt.ShouldBeFalse();
        report.Ready.ShouldBeFalse();
        Line(report, "recipes").TypoDictionaryBuilt.ShouldBeTrue();

        // the other process is done (its lease runs out): the next call completes
        clock.Advance(TimeSpan.FromSeconds(31));
        catalog.Initialize();
        catalog.Indexes().ShouldAllBe(index => index.NeedsInitialization == LuiInitializationNeeds.None);
    }

    [Fact]
    public void A_lease_that_is_taken_between_the_read_and_the_take_is_running_elsewhere_and_no_failure()
    {
        // the real race of two processes that start together: both read "the lease is free", one takes it first, the other loses the take and gets a
        // LuiSyncInProgressException from the sync. That is the answer "running elsewhere" — not a failure of the index, which would stop a host
        using var database = World(withCooks: true);
        var clock = new FixedClock();
        using var catalog = database.Open(new LuiCatalogOptions { TimeProvider = clock });
        var steps = new Steps();
        steps.When(
            progress => progress.Step == LuiInitializeStep.InitialSync && progress.Sync is null && string.Equals(progress.IndexName, "cooks", StringComparison.Ordinal),
            () => Run(database, $"UPDATE lui_meta SET sync_lease_owner = 'the-other-process', sync_lease_expires = {clock.GetUtcNow().AddSeconds(30).ToUnixTimeMilliseconds()} WHERE index_name = 'cooks';"));

        var report = catalog.Initialize(new LuiInitializeOptions { Progress = steps });

        report.Failed.ShouldBeFalse("a lease that was lost in the race is no failure");
        Line(report, "cooks").Sync.ShouldBeNull();
        Line(report, "cooks").TypoDictionaryBuilt.ShouldBeFalse("its sync is still to come");
        Line(report, "cooks").Skipped.ShouldBe(LuiInitializationNeeds.InitialSyncRunningElsewhere | LuiInitializationNeeds.TypoDictionary);
        Line(report, "chefs").Sync.ShouldNotBeNull("the other indexes went on");
    }

    [Fact]
    public void Indexes_costs_a_bounded_number_of_statements_per_linked_index()
    {
        // a readiness probe calls Indexes() every few seconds: the state of a linked index is the guard's counter, the triggers, the flag and the lease
        // in one snapshot — a handful of statements. The bound has little room (the begin of the snapshot counts; measured 13 for two linked indexes, bound 13), and a Count() or a scan
        // of the table creeping in would not fit it
        using var database = World(withCooks: true);
        using var catalog = database.Open();
        _ = catalog.Indexes();
        var commands = new Nordmann.LUI.Storage.CommandCounter();
        catalog.Database.CommandCounter = commands;

        _ = catalog.Indexes();

        const int linked = 2;
        commands.Count.ShouldBeLessThanOrEqualTo(1 + (linked * 6), "one statement for the schema counter, and at most six for each linked index (measured: 13 for two)");
    }

    // ---- the plumbing of the tests --------------------------------------------------------------------------------------

    private static LuiInitializeIndexReport Line(LuiInitializeReport report, string name) =>
        report.Indexes.Single(line => string.Equals(line.Name, name, StringComparison.Ordinal));

    private static LuiIndexInfo Info(LuiCatalog catalog, string name) =>
        catalog.Indexes().Single(index => string.Equals(index.Name, name, StringComparison.Ordinal));

    private static void InterlockedMax(ref int target, int value)
    {
        int seen;
        do
        {
            seen = Volatile.Read(ref target);
        }
        while (value > seen && Interlocked.CompareExchange(ref target, value, seen) != seen);
    }

    /// <summary>What <c>Initialize</c> told, on the thread of the call: <c>Progress&lt;T&gt;</c> would post it to the pool.</summary>
    internal sealed class Steps : IProgress<LuiInitializeProgress>
    {
        private readonly List<(Func<LuiInitializeProgress, bool> Condition, Action Then)> _hooks = [];

        /// <summary>The steps that were announced — the reports of the sync are no steps.</summary>
        internal List<(string IndexName, LuiInitializeStep Step)> Told { get; } = [];

        /// <summary>Runs <paramref name="then"/> each time something that matches <paramref name="condition"/> is told.</summary>
        internal void When(Func<LuiInitializeProgress, bool> condition, Action then) => _hooks.Add((condition, then));

        public void Report(LuiInitializeProgress value)
        {
            if (value.Sync is null)
            {
                Told.Add((value.IndexName, value.Step));
            }

            foreach (var (condition, then) in _hooks.ToArray())
            {
                if (condition(value))
                {
                    then();
                }
            }
        }
    }

    /// <summary>A clock that stands still until the test moves it.</summary>
    private sealed class FixedClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 23, 9, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
