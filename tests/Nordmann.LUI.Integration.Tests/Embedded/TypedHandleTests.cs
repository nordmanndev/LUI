using Shouldly;
using static Nordmann.LUI.Integration.Tests.Linked.LinkedTable;

namespace Nordmann.LUI.Integration.Tests.Embedded;

/// <summary>
/// The typed handles of an index definition (ADR-0073, issue #261): keys are <c>long</c> and nothing else compiles,
/// the embedded handle writes, the linked handle syncs, and the API stays synchronous — also for an asynchronous source.
/// </summary>
public sealed class TypedHandleTests
{
    private static readonly LuiIndexDefinition<long> Tags = new("tags", index => index.SingleWord());

    private static readonly LuiIndexDefinition<long> Titles = new("titles", index => index.MultiWord().TypoTolerance());

    private static readonly LuiLinkedIndexDefinition<long> LinkedTags =
        new("linked_tags", index => index.SingleWord().LinkTo("tag", "id", "name", weight: "usage_count"));

    [Fact]
    public void Upsert_and_search_with_long_keys()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create(Tags);

        tags.Upsert(1, "vegan", weight: 5);
        tags.Upsert(new LuiEntry<long>(2, "vegetarisch") { Weight = 3, Priority = LuiPriority.High });
        tags.UpsertMany([new LuiEntry<long>(3, "veggie"), new LuiEntry<long>(4, "vegetable")]).ShouldBe(2);

        LuiResult<long> result = tags.Search("veg");
        result.Query.ShouldBe("veg");
        result.Hits.Select(hit => hit.Key).Order().ShouldBe([1L, 2L, 3L, 4L]);
        result.Hits.First(hit => hit.Key == 2).Priority.ShouldBe(LuiPriority.High);
        tags.Count().ShouldBe(4);
    }

    [Fact]
    public void The_handle_of_Create_and_the_handle_of_Index_see_the_same_index()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create(Tags);
        tags.Upsert(7, "vegan", 9);

        var hit = catalog.Index(Tags).Search("vegan").Hits.ShouldHaveSingleItem();
        hit.Key.ShouldBe(7);
        hit.Weight.ShouldBe(9);

        catalog.Index(Tags).Upsert(8, "veganer");
        tags.Search("vegan").Hits.Count.ShouldBe(2);
    }

    [Fact]
    public void The_fluent_query_runs_on_the_typed_handle()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var titles = catalog.Create(Titles);
        titles.UpsertMany([new LuiEntry<long>(1, "Gedeckter Apfelkuchen"), new LuiEntry<long>(2, "Apfelstrudel")]);
        titles.WarmUpTypoDictionary();

        LuiResult<long> result = titles.Query("apfel").Take(1).Candidates(100).Rank(LuiRankingOrder.WordStartThenSimilarity)
            .PriorityMode(LuiPriorityMode.First).Similarity(LuiSimilarity.NgramDice).TypoTolerance(false).TrackTimings().Execute();

        result.Hits.Count.ShouldBe(1);
        result.Timings.ShouldNotBeNull();
        titles.Query("apflekuchen").TypoTolerance().Execute().CorrectedQuery.ShouldBe("apfelkuchen");
    }

    [Fact]
    public void Hit_key_is_a_long()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create(Tags);
        tags.Upsert(42, "vegan");

        LuiHit<long> hit = tags.Search("vegan").Hits.ShouldHaveSingleItem();
        long key = hit.Key;

        key.ShouldBe(42);
        hit.Text.ShouldBe("vegan");
        hit.Similarity.ShouldBe(1.0);
        hit.AtWordStart.ShouldBeTrue();
        hit.ShouldBe(tags.Search("vegan").Hits[0], "a hit is a record: value equality");
    }

    [Fact]
    public void An_entry_checks_itself_like_the_untyped_entry()
    {
        Should.Throw<ArgumentException>(() => new LuiEntry<long>(1, string.Empty));
        Should.Throw<ArgumentNullException>(() => new LuiEntry<long>(1, null!));
        Should.Throw<ArgumentOutOfRangeException>(() => new LuiEntry<long>(1, "x") { Weight = -1 });
        Should.Throw<ArgumentOutOfRangeException>(() => new LuiEntry<long>(1, "x") { Priority = (LuiPriority)99 });
        new LuiEntry<long>(1, "x").ShouldBe(new LuiEntry<long>(1, "x"));
    }

    [Fact]
    public void The_embedded_handle_updates_removes_and_maintains()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create(Tags);
        tags.Upsert(1, "vegan", 1);

        tags.UpdateWeight(1, 9).ShouldBeTrue();
        tags.UpdateWeight(2, 9).ShouldBeFalse();
        tags.UpdatePriority(1, LuiPriority.System).ShouldBeTrue();
        tags.Search("vegan").Hits.ShouldHaveSingleItem().Weight.ShouldBe(9);
        tags.Check().IsHealthy.ShouldBeTrue();
        tags.Check([new LuiEntry<long>(1, "vegan") { Weight = 9, Priority = LuiPriority.System }]).IsHealthy.ShouldBeTrue();
        Should.NotThrow(() => tags.Rebuild());
        Should.NotThrow(() => tags.Optimize());
        Should.NotThrow(() => tags.WarmUpTypoDictionary());
        Should.NotThrow(() => tags.RebuildTypoDictionary());
        tags.TypoDistance.ShouldBe(LuiTypoDistance.None);

        tags.Remove(1).ShouldBeTrue();
        tags.Remove(1).ShouldBeFalse();
    }

    [Fact]
    public void Writes_take_part_in_a_transaction_of_the_application()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create(Tags);

        using (var connection = database.Connect())
        {
            using var transaction = connection.BeginTransaction();
            tags.Upsert(1, "vegan", transaction: transaction);
            transaction.Rollback();
        }

        tags.Count().ShouldBe(0);
    }

    [Fact]
    public void Sync_of_an_async_source_returns_the_report_of_the_same_entries()
    {
        using var database = new TestDatabase();
        using var other = new TestDatabase();
        using var catalog = database.Open();
        using var second = other.Open();
        LuiEntry<long>[] entries = [new(1, "vegan"), new(2, "vegetarisch"), new(3, "veggie")];

        var synchronous = catalog.Create(Tags).Sync(entries);
        var asynchronous = second.Create(Tags).Sync(Asynchronous(entries));

        asynchronous.ShouldBe(synchronous);
        asynchronous.Added.ShouldBe(3);
        second.Index(Tags).Count().ShouldBe(3);

        // a second run changes nothing, and removes what the source no longer has
        var again = second.Index(Tags).Sync(Asynchronous(entries.Take(2)), new LuiSyncOptions { BatchSize = 1 });
        again.Removed.ShouldBe(1);
    }

    [Fact]
    public void The_linked_handle_syncs_refreshes_and_reports_its_triggers()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Insert(database, (1, "vegan", 5), (2, "vegetarisch", 3));
        using var catalog = database.Open();
        var tags = catalog.Create(LinkedTags);

        tags.Sync(new LuiSyncOptions { BatchSize = 10 }).Indexed.ShouldBe(2);
        tags.Sync().Indexed.ShouldBe(2);
        tags.Search("veg").Hits.Select(hit => hit.Key).Order().ShouldBe([1L, 2L]);

        Insert(database, (3, "veggie", 1));
        tags.Refresh(3).ShouldBeTrue();
        tags.RefreshMany([1L, 2L, 3L]).ShouldBe(3);
        tags.Count().ShouldBe(3);

        tags.Triggers().ShouldBe(LuiTriggerState.None);
        tags.TriggerScript().ShouldNotBeNullOrEmpty();
        tags.CreateTriggers();
        tags.Triggers().ShouldBe(LuiTriggerState.Current);
        Insert(database, (4, "vegetable", 1));
        tags.Pending().ShouldBe(1);
        tags.ApplyPending().ShouldBe(1);
        tags.DropTriggers();
        tags.Triggers().ShouldBe(LuiTriggerState.None);
        tags.Check().IsHealthy.ShouldBeTrue();
        Should.NotThrow(() => tags.Optimize());
    }

    private static async IAsyncEnumerable<LuiEntry<long>> Asynchronous(IEnumerable<LuiEntry<long>> source)
    {
        foreach (var entry in source)
        {
            await Task.Yield();
            yield return entry;
        }
    }

    private static void Run(TestDatabase database, string sql)
    {
        using var connection = database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
