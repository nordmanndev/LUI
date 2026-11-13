using Shouldly;
using static Nordmann.LUI.Integration.Tests.Linked.LinkedTable;

namespace Nordmann.LUI.Integration.Tests.Linked;

/// <summary>
/// The triggers of a <c>Linked</c> index — only on request (ADR-0015): the script, the outbox they write, and how a
/// refresh, a sync and a drop deal with it.
/// </summary>
public sealed class LinkedTriggerTests
{
    // ---- triggers, only on request ----------------------------------------------------------------------------------

    [Fact]
    public void The_trigger_script_is_text_and_changes_nothing()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        using var catalog = database.Open();
        var tags = Tags(catalog);

        var script = tags.TriggerScript();

        script.Split("CREATE TRIGGER").Length.ShouldBe(4);
        script.ShouldContain("AFTER INSERT ON \"tag\"");
        database.Scalar<long>("SELECT count(*) FROM sqlite_master WHERE type = 'trigger'").ShouldBe(0);
    }

    [Fact]
    public void With_the_triggers_every_write_to_the_table_is_noted_and_applied_and_without_them_none()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        using var catalog = database.Open();
        var tags = Tags(catalog);

        tags.CreateTriggers();
        tags.CreateTriggers();   // a second call replaces them

        Run(database, "INSERT INTO tag(id, name, usage_count) VALUES (1, 'vegan', 5), (2, 'kuchen', 3);");
        tags.Pending().ShouldBe(2);
        tags.Search("vegan").Hits.ShouldBeEmpty("a query does not apply the outbox, a read stays a read");
        tags.ApplyPending().ShouldBe(2);
        tags.Pending().ShouldBe(0);
        tags.Search("vegan").Hits.ShouldHaveSingleItem();

        Run(database, "UPDATE tag SET usage_count = 99 WHERE id = 2;");
        tags.ApplyPending();
        tags.Search("kuchen").Hits.ShouldHaveSingleItem().Weight.ShouldBe(99);
        Run(database, "UPDATE tag SET name = 'torte' WHERE id = 2;");
        tags.ApplyPending();
        tags.Search("kuchen").Hits.ShouldBeEmpty();
        tags.Search("torte").Hits.ShouldHaveSingleItem();
        Run(database, "UPDATE tag SET id = 3 WHERE id = 2;");
        tags.ApplyPending().ShouldBe(2, "the old key and the new one");
        tags.Search("torte").Hits.ShouldHaveSingleItem().Key.ShouldBe(3);
        Run(database, "DELETE FROM tag WHERE id = 1;");
        tags.ApplyPending();
        tags.Search("vegan").Hits.ShouldBeEmpty();
        tags.Count().ShouldBe(1);

        tags.DropTriggers();
        tags.DropTriggers();     // and a second removal finds nothing to remove

        database.Scalar<long>("SELECT count(*) FROM sqlite_master WHERE type = 'trigger'").ShouldBe(0);
        Run(database, "INSERT INTO tag(id, name) VALUES (4, 'tofu');");
        tags.Pending().ShouldBe(0);
        tags.ApplyPending().ShouldBe(0);
        tags.Search("tofu").Hits.ShouldBeEmpty();
    }

    [Fact]
    public void The_triggers_write_only_the_outbox()
    {
        // review of pull request 16, M1: a trigger that writes FTS5 binds every writer of the table to LUI's SQLite
        using var database = new TestDatabase();
        Run(database, Table);
        using var catalog = database.Open();
        var script = Tags(catalog).TriggerScript();

        script.ShouldNotContain("_fts");
        script.ShouldNotContain("replace(");
        script.Split("INSERT OR IGNORE INTO lui_tags_pending(doc_id)").Length.ShouldBe(5, "one key per insert and delete, two per update");
    }

    [Fact]
    public void A_refresh_and_a_sync_take_their_rows_out_of_the_outbox()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        using var catalog = database.Open();
        var tags = Tags(catalog);
        tags.CreateTriggers();
        Run(database, "INSERT INTO tag(id, name) VALUES (1, 'vegan'), (2, 'tofu'), (3, 'zimt');");

        tags.Refresh(1);
        tags.Pending().ShouldBe(2);

        tags.Sync();
        tags.Pending().ShouldBe(0);
        tags.Count().ShouldBe(3);
    }

    [Fact]
    public void Apply_pending_works_through_more_rows_than_one_batch()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        using var catalog = database.Open();
        var tags = Tags(catalog);
        tags.CreateTriggers();
        Run(database, "WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n WHERE i < 2500) INSERT INTO tag(id, name) SELECT i, 'kuchen' || i FROM n;");

        tags.ApplyPending().ShouldBe(2500);

        tags.Count().ShouldBe(2500);
        tags.Pending().ShouldBe(0);
    }

    [Fact]
    public void The_triggers_of_the_script_work_when_the_application_runs_it_itself()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        using var catalog = database.Open();
        var tags = Tags(catalog);

        Run(database, tags.TriggerScript());
        Run(database, "INSERT INTO tag(id, name, usage_count) VALUES (1, 'vegan', 5);");
        tags.ApplyPending();

        tags.Search("vegan").Hits.ShouldHaveSingleItem().Key.ShouldBe(1);
    }

    // ---- review of pull request 16 ------------------------------------------------------------------------------------

    [Fact]
    public void A_drop_of_an_index_with_triggers_is_refused_unless_the_triggers_go_too()
    {
        // M3: a migration of the application may count on the triggers; their removal is not done silently
        using var database = new TestDatabase();
        Run(database, Table);
        using var catalog = database.Open();
        var tags = Tags(catalog);
        Run(database, tags.TriggerScript());

        Should.Throw<InvalidOperationException>(() => catalog.DropIndex("tags")).Message.ShouldContain("dropTriggers: true");
        database.Scalar<long>("SELECT count(*) FROM sqlite_master WHERE type = 'trigger'").ShouldBe(3);
        Run(database, "INSERT INTO tag(id, name) VALUES (1, 'vegan');");
        tags.ApplyPending();
        tags.Search("vegan").Hits.ShouldHaveSingleItem("the index and its triggers are still there");

        catalog.DropIndex("tags", dropTriggers: true).ShouldBeTrue();
        database.Scalar<long>("SELECT count(*) FROM sqlite_master WHERE type = 'trigger'").ShouldBe(0);
        Should.NotThrow(() => Run(database, "INSERT INTO tag(id, name) VALUES (2, 'tofu');"));

        // after a new ensure the index is empty and without triggers, and says so
        var again = Tags(catalog);
        again.Count().ShouldBe(0);
        again.Triggers().ShouldBe(LuiTriggerState.None);
    }

    [Fact]
    public void The_state_of_the_triggers_tells_none_current_and_stale_apart()
    {
        // M3: a migration's triggers after an update of LUI or a change of the table
        using var database = new TestDatabase();
        Run(database, Table);
        using var catalog = database.Open();
        var tags = Tags(catalog);

        tags.Triggers().ShouldBe(LuiTriggerState.None);

        Run(database, tags.TriggerScript().Replace(" ", "  ", StringComparison.Ordinal));
        tags.Triggers().ShouldBe(LuiTriggerState.Current, "white space does not make a trigger another one");

        Run(database, "DROP TRIGGER lui_tags_ad;");
        tags.Triggers().ShouldBe(LuiTriggerState.Stale);

        tags.CreateTriggers();
        tags.Triggers().ShouldBe(LuiTriggerState.Current);

        // an index whose name begins like this one's has triggers of its own
        catalog.CreateLinked("tags_all", index => index.SingleWord().LinkTo("tag", "id", "name")).CreateTriggers();
        tags.Triggers().ShouldBe(LuiTriggerState.Current);
    }

    // ---- the re-review of pull request 16 ------------------------------------------------------------------------------

    [Fact]
    public void Rows_noted_before_the_triggers_were_dropped_stay_in_the_outbox()
    {
        // F3: they changed; ApplyPending or Sync brings them in
        using var database = new TestDatabase();
        Run(database, Table);
        using var catalog = database.Open();
        var tags = Tags(catalog);
        tags.CreateTriggers();
        Run(database, "INSERT INTO tag(id, name) VALUES (1, 'vegan'), (2, 'tofu');");

        tags.DropTriggers();

        tags.Triggers().ShouldBe(LuiTriggerState.None);
        tags.Pending().ShouldBe(2);
        tags.ApplyPending().ShouldBe(2);
        tags.Search("vegan").Hits.ShouldHaveSingleItem();
    }
}
