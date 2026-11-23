using Nordmann.LUI.Integration.Tests.Catalog;
using Shouldly;
using static Nordmann.LUI.Integration.Tests.Linked.LinkedTable;

namespace Nordmann.LUI.Integration.Tests.Concurrency;

/// <summary>
/// <c>Initialize()</c> in two processes on one file (ADR-0076, ADR-0077, issue #266). Two catalogs on one file stand in for two processes.
/// The first sync of a linked index is guarded by the lease of #271: exactly one of the two runs it, and the other skips it, reports
/// <c>InitialSyncRunningElsewhere</c> and does not wait and does not fail. The typo dictionaries live in the memory of each process, so
/// each builds its own — that is expected work, not a repetition.
/// <para>
/// Both interleavings are driven from inside — the progress of the sync of the first catalog is where the second one is called — so no
/// test waits for a clock or a race.
/// </para>
/// </summary>
public sealed class TwoCatalogsInitializeTests
{
    private static readonly LuiIndexDefinition<long> Recipes = new("recipes", index => index.SingleWord().TypoTolerance());

    private static readonly LuiLinkedIndexDefinition<long> Cooks =
        new("cooks", index => index.SingleWord().TypoTolerance().LinkTo("tag", "id", "name", weight: "usage_count"));

    private static readonly LuiLinkedIndexDefinition<long> Chefs =
        new("chefs", index => index.SingleWord().LinkTo("tag", "id", "name", weight: "usage_count"));

    private static TestDatabase World()
    {
        var database = new TestDatabase();
        Run(database, Table);
        Run(database, "WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n WHERE i < 30) INSERT INTO tag(id, name, usage_count) SELECT i, 'kuchen' || i, i % 7 FROM n;");
        using var setup = database.Open();
        setup.Create(Recipes).Upsert(1, "apfelkuchen");
        setup.Create(Cooks, Chefs);
        return database;
    }

    private static LuiInitializeIndexReport Line(LuiInitializeReport report, string name) =>
        report.Indexes.Single(line => string.Equals(line.Name, name, StringComparison.Ordinal));

    [Fact]
    public void Exactly_one_of_two_catalogs_runs_the_first_sync_the_other_reports_it_running_elsewhere()
    {
        using var database = World();
        using var first = database.Open();
        using var second = database.Open();
        LuiInitializeReport? ofTheSecond = null;
        var progress = new InitializeTests.Steps();

        // while the first catalog is in its first sync (it holds the lease), the second one initializes the same file
        progress.When(
            told => told.Step == LuiInitializeStep.InitialSync && string.Equals(told.IndexName, "cooks", StringComparison.Ordinal) && told.Sync?.Batches == 1 && ofTheSecond is null,
            () => ofTheSecond = second.Initialize());

        var ofTheFirst = first.Initialize(new LuiInitializeOptions { Progress = progress, Sync = new LuiSyncOptions { BatchSize = 10 } });

        ofTheSecond.ShouldNotBeNull();
        ofTheSecond.Failed.ShouldBeFalse("a lease that is held is no failure, and the call does not wait for it");

        // the second skipped the sync of the index the first was syncing, and its dictionary: the words of the sync are still to come
        var cooksOfTheSecond = Line(ofTheSecond, "cooks");
        cooksOfTheSecond.Sync.ShouldBeNull();
        cooksOfTheSecond.TypoDictionaryBuilt.ShouldBeFalse();
        cooksOfTheSecond.Skipped.ShouldBe(LuiInitializationNeeds.InitialSyncRunningElsewhere | LuiInitializationNeeds.TypoDictionary);

        // the sync of chefs was done by the first one already, so there was nothing to run; every process builds its own dictionaries of the others
        Line(ofTheSecond, "chefs").Sync.ShouldBeNull();
        Line(ofTheSecond, "recipes").TypoDictionaryBuilt.ShouldBeTrue();

        // the first one did the sync of cooks, then built its dictionary
        var cooksOfTheFirst = Line(ofTheFirst, "cooks");
        cooksOfTheFirst.Sync.ShouldNotBeNull().Indexed.ShouldBe(30);
        cooksOfTheFirst.TypoDictionaryBuilt.ShouldBeTrue();
        Line(ofTheFirst, "recipes").TypoDictionaryBuilt.ShouldBeTrue();

        // a later call of the second completes: the sync is done, only its dictionary is left
        var later = second.Initialize();
        later.Ready.ShouldBeTrue("a repeat after the other process finished is ready: Initialize() is safe to repeat");
        Line(later, "cooks").Sync.ShouldBeNull("the first sync is done: it is not run again");
        Line(later, "cooks").TypoDictionaryBuilt.ShouldBeTrue();
        second.Indexes().ShouldAllBe(index => index.NeedsInitialization == LuiInitializationNeeds.None);
        first.Indexes().ShouldAllBe(index => index.NeedsInitialization == LuiInitializationNeeds.None);

        database.Scalar<long>("SELECT sync_generation FROM lui_meta WHERE index_name = 'cooks';").ShouldBe(1, "one sync ran, not two");
    }

    [Fact]
    public void A_sync_that_the_other_catalog_completed_after_the_state_was_read_is_not_run_again()
    {
        // the second catalog has read "a first sync is due" — and the first one completes it before the second asks for the lease. The
        // check of the flag and the take of the lease are one step inside the lease's transaction: the second finds nothing to do
        using var database = World();
        using var first = database.Open();
        using var second = database.Open();
        var progress = new InitializeTests.Steps();
        var generations = new List<long>();

        // told right before the sync call of the second catalog, after it read its state
        progress.When(
            told => told.Step == LuiInitializeStep.InitialSync && string.Equals(told.IndexName, "chefs", StringComparison.Ordinal) && told.Sync is null,
            () =>
            {
                first.Initialize(new LuiInitializeOptions { Indexes = [Chefs] });
                generations.Add(database.Scalar<long>("SELECT sync_generation FROM lui_meta WHERE index_name = 'chefs';"));
            });

        var report = second.Initialize(new LuiInitializeOptions { Progress = progress, Indexes = [Chefs] });

        generations.ShouldBe([1L], "the first catalog completed the first sync in the middle of the call of the second");
        report.Failed.ShouldBeFalse();
        report.Indexes.Single().Sync.ShouldBeNull("nothing was due any more when the lease was taken");
        database.Scalar<long>("SELECT sync_generation FROM lui_meta WHERE index_name = 'chefs';").ShouldBe(1, "no second sync");
        database.Scalar<long>("SELECT count(*) FROM lui_meta WHERE sync_lease_owner IS NOT NULL;").ShouldBe(0);
        second.Index(Chefs).NeedsInitialSync.ShouldBeFalse();
    }
}
