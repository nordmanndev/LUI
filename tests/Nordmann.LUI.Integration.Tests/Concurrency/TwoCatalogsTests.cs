using Nordmann.LUI.Integration.Tests.Catalog;
using Shouldly;
using static Nordmann.LUI.Integration.Tests.Linked.LinkedTable;

namespace Nordmann.LUI.Integration.Tests.Concurrency;

/// <summary>
/// Several processes on one file (ADR-0075, issue #265). Two catalogs on one file stand in for two processes: each has its
/// own definitions and its own runtimes in memory, and the file is the only thing they share. The catalog caches definitions
/// and runtimes only — never content: what the other one writes is always visible. A change of the schema of the file —
/// SQLite's schema counter — tells a handle that its definition may be stale, and the handle never reads or writes through a
/// stale definition.
/// </summary>
public sealed class TwoCatalogsTests
{
    private static readonly LuiIndexDefinition<long> Tags = new("tags", index => index.SingleWord());

    private static readonly LuiIndexDefinition<long> TagsOfWords = new("tags", index => index.MultiWord());

    private static readonly LuiLinkedIndexDefinition<long> LinkedTags =
        new("linked_tags", index => index.SingleWord().LinkTo("tag", "id", "name", weight: "usage_count"));

    // ---- an index that another process created -----------------------------------------------------------------------

    [Fact]
    public void An_index_created_by_one_catalog_is_opened_by_the_other()
    {
        using var database = new TestDatabase();
        using var first = database.Open();
        using var second = database.Open();

        first.Create(Tags).Upsert(1, "vegan");

        // the second catalog was open before the index was there, and is not reopened
        var tags = second.Index(Tags);
        tags.Count().ShouldBe(1);
        tags.Search("veg").Hits.Select(hit => hit.Key).ShouldBe([1L]);
    }

    [Fact]
    public void What_the_other_catalog_writes_is_visible_at_once_because_content_is_never_cached()
    {
        using var database = new TestDatabase();
        using var first = database.Open();
        using var second = database.Open();
        var written = first.Create(Tags);
        var read = second.Index(Tags);

        for (var key = 1; key <= 5; key++)
        {
            written.Upsert(key, "vegan " + key);
            read.Count().ShouldBe(key);
        }

        written.Remove(3);
        read.Search("vegan").Hits.Select(hit => hit.Key).Order().ShouldBe([1L, 2L, 4L, 5L]);
    }

    // ---- an index that another process dropped or created again -------------------------------------------------------

    [Fact]
    public void After_a_drop_by_one_catalog_every_operation_of_the_old_handle_of_the_other_throws_index_not_found()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        using var first = database.Open();
        using var second = database.Open();
        first.Create(Tags);
        first.Create(LinkedTags);
        var tags = second.Index(Tags);
        var linked = second.Index(LinkedTags);
        tags.Upsert(1, "vegan");

        first.DropIndex("tags");
        first.DropIndex("linked_tags");

        Should.Throw<LuiIndexNotFoundException>(() => tags.Count()).IndexName.ShouldBe("tags");
        Should.Throw<LuiIndexNotFoundException>(() => tags.Search("veg"));
        Should.Throw<LuiIndexNotFoundException>(() => tags.Upsert(2, "vegetable"));
        Should.Throw<LuiIndexNotFoundException>(() => tags.Remove(1));
        Should.Throw<LuiIndexNotFoundException>(() => tags.Check());
        Should.Throw<LuiIndexNotFoundException>(() => linked.Count()).IndexName.ShouldBe("linked_tags");
        Should.Throw<LuiIndexNotFoundException>(() => linked.Search("veg"));
        Should.Throw<LuiIndexNotFoundException>(() => linked.Sync());

        // the handle stays dropped, and a new open call says so too
        Should.Throw<LuiIndexNotFoundException>(() => tags.Count());
        Should.Throw<LuiIndexNotFoundException>(() => second.Index(Tags));
    }

    [Fact]
    public void A_write_with_a_transaction_of_the_application_on_an_index_that_another_catalog_dropped_throws_index_not_found()
    {
        using var database = new TestDatabase();
        using var first = database.Open();
        using var second = database.Open();
        first.Create(Tags);
        var tags = second.Index(Tags);
        first.DropIndex("tags");

        using var connection = database.Connect();
        using var transaction = connection.BeginTransaction();

        Should.Throw<LuiIndexNotFoundException>(() => tags.Upsert(1, "vegan", transaction: transaction)).IndexName.ShouldBe("tags");
        Should.Throw<LuiIndexNotFoundException>(() => tags.Remove(1, transaction));
    }

    [Fact]
    public void After_a_drop_and_a_recreate_by_one_catalog_the_old_handle_of_the_other_throws()
    {
        using var database = new TestDatabase();
        using var first = database.Open();
        using var second = database.Open();
        first.Create(Tags);
        var old = second.Index(Tags);
        old.Upsert(1, "vegan");

        first.DropIndex("tags");
        first.Create(TagsOfWords);

        // another definition under the same name: a handle must never read or write through a stale definition
        var thrown = Should.Throw<LuiDefinitionMismatchException>(() => old.Count());
        thrown.IndexName.ShouldBe("tags");
        thrown.ShouldDifferIn("kind");
        Should.Throw<LuiDefinitionMismatchException>(() => old.Search("veg"));
        Should.Throw<LuiDefinitionMismatchException>(() => old.Upsert(2, "vegetable"));
        Should.Throw<LuiDefinitionMismatchException>(() => old.Remove(1));
        Should.Throw<LuiDefinitionMismatchException>(() => old.Check());
        first.Index(TagsOfWords).Count().ShouldBe(0, "nothing was written through the stale definition");
    }

    [Fact]
    public void A_new_open_after_a_recreate_sees_the_new_definition()
    {
        using var database = new TestDatabase();
        using var first = database.Open();
        using var second = database.Open();
        first.Create(Tags);
        var old = second.Index(Tags);
        _ = old.Count();

        first.DropIndex("tags");
        first.Create(TagsOfWords).Upsert(1, "vegane torte");
        Should.Throw<LuiDefinitionMismatchException>(() => old.Count());

        var current = second.Index(TagsOfWords);

        current.Kind.ShouldBe(LuiIndexKind.MultiWord);
        current.Count().ShouldBe(1);
        current.ShouldNotBeSameAs(old);
        Should.Throw<LuiDefinitionMismatchException>(() => second.Index(Tags), "the old declaration is a mismatch now").ShouldDifferIn("kind");
    }

    [Fact]
    public void An_open_call_that_meets_a_stale_map_looks_at_the_file_before_it_says_mismatch()
    {
        using var database = new TestDatabase();
        using var first = database.Open();
        using var second = database.Open();
        first.Create(Tags);
        _ = second.Index(Tags);

        // no operation of the second catalog ran since: its definitions in memory are the old ones
        first.DropIndex("tags");
        first.Create(TagsOfWords);

        second.Index(TagsOfWords).Kind.ShouldBe(LuiIndexKind.MultiWord);
    }

    [Fact]
    public void A_recreate_with_the_same_definition_does_not_stop_the_old_handle()
    {
        using var database = new TestDatabase();
        using var first = database.Open();
        using var second = database.Open();
        first.Create(Tags);
        var old = second.Index(Tags);
        old.Upsert(1, "vegan");

        first.DropIndex("tags");
        first.Create(Tags);

        old.Count().ShouldBe(0, "the index is the same declaration: the handle works on the new index");
        old.Upsert(1, "vegane torte");
        first.Index(Tags).Count().ShouldBe(1);
    }

    [Fact]
    public void The_schema_changes_of_the_application_cause_a_harmless_reload()
    {
        using var database = new TestDatabase();
        using var first = database.Open();
        using var second = database.Open();
        first.Create(Tags);
        var tags = second.Index(Tags);
        tags.Upsert(1, "vegan");

        Run(database, "CREATE TABLE somewhere_else(id INTEGER PRIMARY KEY);");
        Run(database, "CREATE INDEX somewhere_else_id ON somewhere_else(id);");

        tags.Count().ShouldBe(1);
        tags.Search("veg").Hits.Count.ShouldBe(1);
    }

    // ---- Create is atomic across processes -------------------------------------------------------------------------

    [Fact]
    public void Two_concurrent_Creates_with_the_same_definition_leave_one_index()
    {
        for (var round = 0; round < 5; round++)
        {
            using var database = new TestDatabase();
            using var first = database.Open();
            using var second = database.Open();
            using var barrier = new Barrier(2);

            var handles = new LuiEmbeddedIndex<long>[2];
            Parallel.For(0, 2, new ParallelOptions { MaxDegreeOfParallelism = 2 }, number =>
            {
                barrier.SignalAndWait();
                handles[number] = (number == 0 ? first : second).Create(Tags);
            });

            database.Scalar<long>("SELECT count(*) FROM lui_meta").ShouldBe(1, $"round {round}: one index");
            handles[0].Upsert(1, "vegan");
            handles[1].Count().ShouldBe(1, $"round {round}: both handles are the one index");
        }
    }

    [Fact]
    public void Two_concurrent_Creates_with_different_definitions_leave_the_first_and_throw_for_the_other()
    {
        for (var round = 0; round < 5; round++)
        {
            using var database = new TestDatabase();
            using var first = database.Open();
            using var second = database.Open();
            using var barrier = new Barrier(2);

            var failures = new Exception?[2];
            Parallel.For(0, 2, new ParallelOptions { MaxDegreeOfParallelism = 2 }, number =>
            {
                barrier.SignalAndWait();
                try
                {
                    _ = number == 0 ? first.Create(Tags) : second.Create(TagsOfWords);
                }
                catch (LuiDefinitionMismatchException exception)
                {
                    failures[number] = exception;
                }
            });

            failures.Count(failure => failure is not null).ShouldBe(1, $"round {round}: exactly one of the two was refused");
            database.Scalar<long>("SELECT count(*) FROM lui_meta").ShouldBe(1);
        }
    }

    [Fact]
    public void A_Create_that_finds_the_index_another_catalog_just_created_opens_it()
    {
        using var database = new TestDatabase();
        using var first = database.Open();
        using var second = database.Open();
        first.Create(Tags).Upsert(1, "vegan");

        // the second catalog does not know the index yet: Create re-reads lui_meta inside its transaction and opens it
        var tags = second.Create(Tags);

        tags.Count().ShouldBe(1);
        database.Scalar<long>("SELECT count(*) FROM lui_meta").ShouldBe(1);
    }
}
