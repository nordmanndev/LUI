using Shouldly;
using static Nordmann.LUI.Integration.Tests.Linked.LinkedTable;

namespace Nordmann.LUI.Integration.Tests.Linked;

/// <summary>
/// The state of a linked index (#272, ADR-0077): whether its first complete <c>Sync()</c> is still to be done. It is stored with the
/// index in <c>lui_meta</c>, read from the file on every call — another process can complete the sync without a change of the
/// SQLite schema — and cleared in the transaction that commits a complete sync; a sync that is cancelled or fails leaves it.
/// </summary>
public sealed class LinkedStateTests
{
    private static readonly LuiLinkedIndexDefinition<long> LinkedTags =
        new("tags", index => index.SingleWord().LinkTo("tag", "id", "name", weight: "usage_count"));

    private static readonly LuiLinkedIndexDefinition<long, string> LinkedSlugs =
        new("slugs", index => index.SingleWord().LinkTo("tag", "id", "name").WithPayload(["slug"], row => row.GetString("slug")));

    [Fact]
    public void NeedsInitialSync_is_true_after_Create()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Insert(database, (1, "vegan", 5));
        using var catalog = database.Open();

        var tags = catalog.Create(LinkedTags);
        var slugs = catalog.Create(LinkedSlugs);

        tags.NeedsInitialSync.ShouldBeTrue();
        slugs.NeedsInitialSync.ShouldBeTrue();
        tags.Count().ShouldBe(0, "Create syncs nothing");
        tags.Triggers().ShouldBe(LuiTriggerState.None, "Create installs nothing");
        Stored(database, "tags").ShouldBe(1);
    }

    [Fact]
    public void An_embedded_index_has_no_initial_sync_and_the_flag_is_no_part_of_a_stored_definition()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        using var catalog = database.Open();
        _ = catalog.Create(new LuiIndexDefinition<long>("embedded", index => index.SingleWord()));
        _ = catalog.Create(LinkedTags);

        Stored(database, "embedded").ShouldBe(0);
        database.Scalar<string>("SELECT definition FROM lui_meta WHERE index_name = 'embedded'").ShouldNotContain("nitial");
        database.Scalar<string>("SELECT definition FROM lui_meta WHERE index_name = 'tags'").ShouldNotContain("nitial");
    }

    [Fact]
    public void It_stays_true_after_a_cancelled_sync()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Insert(database, (1, "vegan", 5), (2, "kuchen", 3));
        using var catalog = database.Open();
        var tags = catalog.Create(LinkedTags);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Should.Throw<OperationCanceledException>(() => tags.Sync(cancellationToken: cancelled.Token));

        tags.NeedsInitialSync.ShouldBeTrue();
        tags.Count().ShouldBe(0, "a sync that is cancelled before its first batch has written nothing");
    }

    [Fact]
    public void It_stays_true_after_a_failing_sync()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Insert(database, (1, "vegan", 5));
        using var catalog = database.Open();
        var tags = catalog.Create(LinkedTags);
        Run(database, "ALTER TABLE tag RENAME COLUMN name TO title;");

        Should.Throw<LuiLinkedTableException>(() => tags.Sync()).Message.ShouldContain("'tags'");

        tags.NeedsInitialSync.ShouldBeTrue();
    }

    [Fact]
    public void It_becomes_false_after_a_complete_sync()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Insert(database, (1, "vegan", 5));
        using var catalog = database.Open();
        var tags = catalog.Create(LinkedTags);
        var slugs = catalog.Create(LinkedSlugs);

        tags.Sync().Indexed.ShouldBe(1);
        slugs.Sync();

        tags.NeedsInitialSync.ShouldBeFalse();
        slugs.NeedsInitialSync.ShouldBeFalse();
        Stored(database, "tags").ShouldBe(0);
    }

    [Fact]
    public void The_old_handle_shares_the_state_its_sync_clears_the_flag_and_its_triggers_are_idempotent()
    {
        // ADR-0077, decision 6: LuiLinkedIndex<long> has no property, but its Sync() and CreateTriggers() are the same implementation
        using var database = new TestDatabase();
        Run(database, Table);
        Insert(database, (1, "vegan", 5));
        using var catalog = database.Open();
        var old = Tags(catalog);
        var typed = catalog.Index(LinkedTags);
        typed.NeedsInitialSync.ShouldBeTrue();

        old.CreateTriggers();
        old.CreateTriggers();
        old.Triggers().ShouldBe(LuiTriggerState.Current);
        old.Sync();

        typed.NeedsInitialSync.ShouldBeFalse();
        Stored(database, "tags").ShouldBe(0);
    }

    [Fact]
    public void A_sync_of_an_empty_table_is_a_complete_sync()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        using var catalog = database.Open();
        var tags = catalog.Create(LinkedTags);

        tags.Sync().Indexed.ShouldBe(0);

        tags.NeedsInitialSync.ShouldBeFalse();
    }

    [Fact]
    public void Nothing_but_a_complete_sync_clears_it_and_a_failing_second_sync_does_not_set_it_again()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Insert(database, (1, "vegan", 5));
        using var catalog = database.Open();
        var tags = catalog.Create(LinkedTags);

        tags.CreateTriggers();
        tags.Refresh(1);
        tags.ApplyPending();
        tags.Optimize();
        tags.Check();
        tags.NeedsInitialSync.ShouldBeTrue("triggers, a refresh and the outbox are no first sync");

        tags.Sync();
        Run(database, "ALTER TABLE tag RENAME COLUMN name TO title;");
        Should.Throw<LuiLinkedTableException>(() => tags.Sync());

        tags.NeedsInitialSync.ShouldBeFalse("the first sync was complete; a later failure does not take it back");
    }

    [Fact]
    public void It_survives_a_reopen()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Insert(database, (1, "vegan", 5));
        using (var first = database.Open())
        {
            _ = first.Create(LinkedTags);
        }

        using (var second = database.Open())
        {
            var tags = second.Create(LinkedTags);
            tags.NeedsInitialSync.ShouldBeTrue("opening it again with Create does not touch the state");
            tags.Sync();
        }

        using var third = database.Open();
        third.Create(LinkedTags).NeedsInitialSync.ShouldBeFalse("and a Create of an index that is synced does not set it again");
        third.Index(LinkedTags).NeedsInitialSync.ShouldBeFalse();
    }

    [Fact]
    public void A_second_catalog_sees_false_right_after_the_first_completed_the_sync()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Insert(database, (1, "vegan", 5));
        using var first = database.Open();
        using var second = database.Open();
        var tags = first.Create(LinkedTags);
        var seen = second.Index(LinkedTags);
        seen.NeedsInitialSync.ShouldBeTrue();

        tags.Sync();

        // no reopen, no operation of the second catalog in between: the sync changed no schema, the flag is read from the file
        seen.NeedsInitialSync.ShouldBeFalse();
        second.Index(LinkedTags).NeedsInitialSync.ShouldBeFalse();
    }

    [Fact]
    public void A_read_only_catalog_reads_the_state()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        using (var writer = database.Open())
        {
            _ = writer.Create(LinkedTags);
        }

        using var reader = LuiCatalog.OpenReadOnly(database.FilePath);
        reader.Index(LinkedTags).NeedsInitialSync.ShouldBeTrue();
    }

    [Fact]
    public void The_state_goes_through_the_guard_of_the_handle()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        using var catalog = database.Open();
        using var other = database.Open();
        var tags = catalog.Create(LinkedTags);
        tags.Sync();
        tags.NeedsInitialSync.ShouldBeFalse();

        // dropped and created again: a new index, to be synced (the same definition is not a stale one)
        other.DropIndex("tags").ShouldBeTrue();
        _ = other.Create(LinkedTags);
        catalog.Index(LinkedTags).NeedsInitialSync.ShouldBeTrue();
        tags.NeedsInitialSync.ShouldBeTrue("the old handle of the same definition reads the new row, not a cached answer");

        other.DropIndex("tags").ShouldBeTrue();
        Should.Throw<LuiIndexNotFoundException>(() => tags.NeedsInitialSync).IndexName.ShouldBe("tags");
        Should.Throw<LuiIndexNotFoundException>(() => catalog.Index(LinkedTags)).IndexName.ShouldBe("tags");
    }

    [Fact]
    public void The_state_of_an_index_that_was_created_again_with_another_definition_is_a_mismatch()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        using var catalog = database.Open();
        using var other = database.Open();
        var tags = catalog.Create(LinkedTags);
        other.DropIndex("tags").ShouldBeTrue();
        _ = other.Create(new LuiLinkedIndexDefinition<long>("tags", index => index.SingleWord().LinkTo("tag", "id", "slug")));

        Should.Throw<LuiDefinitionMismatchException>(() => tags.NeedsInitialSync).IndexName.ShouldBe("tags");
    }

    private static long Stored(TestDatabase database, string name) =>
        database.Scalar<long>($"SELECT needs_initial_sync FROM lui_meta WHERE index_name = '{name}'");
}
