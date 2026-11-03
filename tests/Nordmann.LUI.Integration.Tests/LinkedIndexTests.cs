using Microsoft.Data.Sqlite;
using Shouldly;

namespace Nordmann.LUI.Integration.Tests;

/// <summary>
/// A <c>Linked</c> index over a table of the application (LUI-Spezifikation §5, §7.3, ADR-0015): loosely coupled —
/// LUI reads the table and never changes it on its own; the application keeps the index current.
/// </summary>
public sealed class LinkedIndexTests
{
    private const string Table = """
        CREATE TABLE tag (
            id          INTEGER PRIMARY KEY,
            name        TEXT,
            usage_count INTEGER NOT NULL DEFAULT 0,
            priority    INTEGER,
            slug        TEXT,
            color       TEXT);
        """;

    // ---- the table of the application is not touched ------------------------------------------------------------

    [Fact]
    public void Ensure_sync_and_refresh_leave_the_schema_of_the_application_table_as_it_was()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Insert(database, (1, "vegan", 5));
        var before = Schema(database, "tag");

        using var catalog = database.Open();
        var tags = Tags(catalog);
        tags.Sync();
        tags.Refresh(1);

        Schema(database, "tag").ShouldBe(before);
        database.Scalar<long>("SELECT count(*) FROM sqlite_master WHERE type = 'trigger'").ShouldBe(0);
        database.Scalar<long>("SELECT count(*) FROM sqlite_master WHERE type = 'index' AND tbl_name = 'tag'").ShouldBe(0);
        database.Scalar<long>("SELECT count(*) FROM sqlite_master WHERE name LIKE 'lui_tags%'").ShouldBeGreaterThan(0);
    }

    [Fact]
    public void A_linked_index_does_not_store_the_text_a_second_time()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        using var catalog = database.Open();
        Tags(catalog);

        database.Scalar<long>("SELECT count(*) FROM pragma_table_info('lui_tags') WHERE name IN ('text', 'key', 'payload')").ShouldBe(0);
    }

    [Theory]
    [InlineData("CREATE TABLE other (id INTEGER PRIMARY KEY, name TEXT);", "'tag'")]
    [InlineData("CREATE TABLE tag (id INTEGER PRIMARY KEY, title TEXT, usage_count INTEGER);", "'name'")]
    [InlineData("CREATE TABLE tag (id TEXT PRIMARY KEY, name TEXT, usage_count INTEGER);", "INTEGER PRIMARY KEY")]
    [InlineData("CREATE TABLE tag (id INTEGER, name TEXT, usage_count INTEGER);", "INTEGER PRIMARY KEY")]
    [InlineData("CREATE TABLE tag (id INTEGER, name TEXT, usage_count INTEGER, PRIMARY KEY (id, name));", "INTEGER PRIMARY KEY")]
    [InlineData("CREATE TABLE tag (id INTEGER PRIMARY KEY, name TEXT, usage_count INTEGER) WITHOUT ROWID;", "INTEGER PRIMARY KEY")]
    [InlineData("CREATE TABLE tag (id INTEGER PRIMARY KEY DESC, name TEXT, usage_count INTEGER);", "INTEGER PRIMARY KEY")]
    public void A_table_that_is_not_what_the_link_says_is_refused_and_not_changed(string table, string message)
    {
        using var database = new TestDatabase();
        Run(database, table);
        using var catalog = database.Open();

        Should.Throw<LuiLinkedTableException>(() => Tags(catalog)).Message.ShouldContain(message);
        database.Scalar<long>("SELECT count(*) FROM sqlite_master WHERE name LIKE 'lui_tags%'").ShouldBe(0);
    }

    [Theory]
    [InlineData("tag; DROP TABLE tag")]
    [InlineData("1tag")]
    [InlineData("")]
    public void A_name_that_could_be_more_than_a_name_is_refused(string name)
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();

        Should.Throw<ArgumentException>(() => catalog.Index("tags").SingleWord().LinkTo(name, "id", "name"));
        Should.Throw<ArgumentException>(() => catalog.Index("tags").SingleWord().LinkTo("tag", "id", name));
    }

    // ---- the application keeps the index current ------------------------------------------------------------------

    [Fact]
    public void An_index_starts_empty_and_a_sync_indexes_every_row_with_a_text()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Insert(database, (1, "vegan", 5), (2, "vegetarisch", 9), (3, null, 7), (4, string.Empty, 7));
        using var catalog = database.Open();
        var tags = Tags(catalog);

        tags.Count().ShouldBe(0);
        tags.Search("veg").Hits.ShouldBeEmpty();

        var result = tags.Sync();

        result.Indexed.ShouldBe(2);
        result.WithoutText.ShouldBe(2);
        result.KeyOutOfRange.ShouldBe(0);
        tags.Count().ShouldBe(2);
        // word start, then similarity, then weight (§9.2): „vegan" is nearer to „veg" than „vegetarisch"
        tags.Search("veg").Hits.Select(hit => (hit.Key, hit.Text, hit.Weight)).ShouldBe([(1L, "vegan", 5), (2L, "vegetarisch", 9)]);
    }

    [Fact]
    public void A_sync_takes_in_every_change_the_index_did_not_hear_of()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Insert(database, (1, "vegan", 5), (2, "kuchen", 9));
        using var catalog = database.Open();
        var tags = Tags(catalog);
        tags.Sync();

        Run(database, "UPDATE tag SET name = 'torte' WHERE id = 2; DELETE FROM tag WHERE id = 1; INSERT INTO tag(id, name) VALUES (3, 'tofu');");
        // the index did not hear of the change: the old text is no hit any more (S1), the new one not yet
        tags.Search("kuchen").Hits.ShouldBeEmpty();
        tags.Search("torte").Hits.ShouldBeEmpty();
        tags.Count().ShouldBe(2);

        tags.Sync().Indexed.ShouldBe(2);

        tags.Search("kuchen").Hits.ShouldBeEmpty();
        tags.Search("vegan").Hits.ShouldBeEmpty();
        tags.Search("torte").Hits.ShouldHaveSingleItem().Key.ShouldBe(2);
        tags.Search("tofu").Hits.ShouldHaveSingleItem().Key.ShouldBe(3);
    }

    [Fact]
    public void A_refresh_without_a_transaction_follows_an_insert_an_update_and_a_delete()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        using var catalog = database.Open();
        var tags = Tags(catalog);

        Insert(database, (7, "apfelkuchen", 10));
        tags.Refresh(7).ShouldBeTrue();
        tags.Search("apfelk").Hits.ShouldHaveSingleItem().Key.ShouldBe(7);

        Run(database, "UPDATE tag SET name = 'birnenkuchen', usage_count = 20 WHERE id = 7;");
        tags.Refresh(7).ShouldBeTrue();
        tags.Search("apfelk").Hits.ShouldBeEmpty();
        tags.Search("birnen").Hits.ShouldHaveSingleItem().Weight.ShouldBe(20);

        Run(database, "DELETE FROM tag WHERE id = 7;");
        tags.Refresh(7).ShouldBeFalse();
        tags.Search("birnen").Hits.ShouldBeEmpty();
        tags.Count().ShouldBe(0);
    }

    [Fact]
    public void A_refresh_in_the_transaction_of_the_application_is_part_of_it()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        using var catalog = database.Open();
        var tags = Tags(catalog);

        using (var connection = database.Connect())
        using (var transaction = connection.BeginTransaction())
        {
            Run(connection, transaction, "INSERT INTO tag(id, name, usage_count) VALUES (1, 'vegan', 5);");
            tags.Refresh(1, transaction).ShouldBeTrue();
            transaction.Rollback();
        }

        tags.Count().ShouldBe(0);
        tags.Search("vegan").Hits.ShouldBeEmpty();

        using (var connection = database.Connect())
        using (var transaction = connection.BeginTransaction())
        {
            Run(connection, transaction, "INSERT INTO tag(id, name, usage_count) VALUES (1, 'vegan', 5);");
            tags.Refresh(1, transaction);
            transaction.Commit();
        }

        tags.Search("vegan").Hits.ShouldHaveSingleItem().Key.ShouldBe(1);
    }

    [Fact]
    public void Refresh_many_counts_the_rows_that_are_in_the_index_and_a_cancellation_rolls_back()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Insert(database, (1, "apfel", 1), (2, "birne", 2), (3, "kirsche", 3));
        using var catalog = database.Open();
        var tags = Tags(catalog);

        tags.RefreshMany([1, 2, 3, 99]).ShouldBe(3);
        tags.Count().ShouldBe(3);

        Run(database, "DELETE FROM tag;");
        using var cancellation = new CancellationTokenSource();
        IEnumerable<long> Keys()
        {
            yield return 1;
            cancellation.Cancel();
            yield return 2;
        }

        Should.Throw<OperationCanceledException>(() => tags.RefreshMany(Keys(), cancellationToken: cancellation.Token));
        tags.Count().ShouldBe(3, "LUI's own transaction is rolled back");
    }

    [Fact]
    public void The_weight_and_the_priority_come_from_their_columns_and_are_brought_into_range()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Run(database, """
            INSERT INTO tag(id, name, usage_count, priority) VALUES
                (1, 'vegan',       5,         0),
                (2, 'veganer',     900,       NULL),
                (3, 'vegane',      -40,       9),
                (4, 'veganismus',  999999999, 3);
            """);
        using var catalog = database.Open();
        var tags = catalog.Index("tags").SingleWord().LinkTo("tag", "id", "name", weight: "usage_count", priority: "priority").Ensure();
        tags.Sync();

        var hits = tags.Search("vegan").Hits;

        hits[0].Key.ShouldBe(1, "the system priority comes first");
        hits[0].Priority.ShouldBe(LuiPriority.System);
        hits.Single(hit => hit.Key == 3).Weight.ShouldBe(0);
        hits.Single(hit => hit.Key == 3).Priority.ShouldBe(LuiPriority.Normal);
        hits.Single(hit => hit.Key == 4).Weight.ShouldBe(536_870_911);
    }

    // ---- what comes back -----------------------------------------------------------------------------------------

    [Fact]
    public void The_payload_is_read_from_columns_of_the_table()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Run(database, "INSERT INTO tag(id, name, usage_count, slug, color) VALUES (1, 'vegan', 5, 'vegan', '#0a0'), (2, 'veganer', 4, 'veganer', NULL);");
        using var catalog = database.Open();
        var tags = catalog.Index("tags").SingleWord()
            .LinkTo("tag", "id", "name", weight: "usage_count")
            .WithPayload(["slug", "color"], row => new Chip(row.GetString("slug"), row.IsNull("color") ? null : row.GetString("color")))
            .Ensure();
        tags.Sync();

        var hits = tags.Search("vegan").Hits;

        hits[0].Payload.ShouldBe(new Chip("vegan", "#0a0"));
        hits[1].Payload.ShouldBe(new Chip("veganer"));
    }

    [Fact]
    public void A_payload_column_that_is_not_there_is_refused_at_ensure_and_one_that_was_not_named_at_the_query()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Insert(database, (1, "vegan", 5));
        using var catalog = database.Open();

        Should.Throw<LuiLinkedTableException>(() => catalog.Index("tags").SingleWord()
                .LinkTo("tag", "id", "name").WithPayload(["shade"], row => row.GetString("shade")).Ensure())
            .Message.ShouldContain("'shade'");

        var tags = catalog.Index("tags").SingleWord().LinkTo("tag", "id", "name")
            .WithPayload(["slug"], row => row.GetString("color")).Ensure();
        tags.Sync();

        Should.Throw<ArgumentException>(() => tags.Search("vegan")).Message.ShouldContain("WithPayload");
    }

    [Fact]
    public void A_key_at_the_end_of_the_range_comes_back_as_it_is_and_one_beyond_it_is_left_out_not_refused()
    {
        // review of pull request 14, N6: the key is read back as text; review of pull request 16, M2: a key out of range
        // leaves the row out — one such row must not take the whole index with it
        using var database = new TestDatabase();
        Run(database, Table);
        Insert(database, (4_294_967_295, "vegan", 5), (1_700_000_000_000, "veganer", 5), (-1, "vegane", 5));
        using var catalog = database.Open();
        var tags = Tags(catalog);

        var result = tags.Sync();

        result.Indexed.ShouldBe(1);
        result.KeyOutOfRange.ShouldBe(2);
        tags.Search("vegan").Hits.ShouldHaveSingleItem().Key.ShouldBe(4_294_967_295L);

        // a call of the application with such a key is a mistake of the call, and says so
        Should.Throw<ArgumentOutOfRangeException>(() => tags.Refresh(1_700_000_000_000));
    }

    [Fact]
    public void With_the_triggers_a_row_with_a_key_out_of_range_is_written_and_left_out_of_the_index()
    {
        // review of pull request 16, M2: the trigger must not refuse a write of the application
        using var database = new TestDatabase();
        Run(database, Table);
        using var catalog = database.Open();
        var tags = Tags(catalog);
        tags.CreateTriggers();

        Should.NotThrow(() => Run(database, "INSERT INTO tag(id, name, usage_count) VALUES (4294967296, 'vegan', 5), (1, 'veganer', 3);"));
        tags.ApplyPending().ShouldBe(2);

        database.Scalar<long>("SELECT count(*) FROM tag").ShouldBe(2);
        tags.Pending().ShouldBe(0, "the key out of range leaves the outbox too");
        tags.Search("vegan").Hits.ShouldHaveSingleItem().Key.ShouldBe(1);
    }

    [Fact]
    public void A_multi_word_index_over_a_table_finds_the_words_in_any_order()
    {
        using var database = new TestDatabase();
        Run(database, "CREATE TABLE recipe (id INTEGER PRIMARY KEY, title TEXT NOT NULL, views INTEGER NOT NULL);");
        Run(database, "INSERT INTO recipe VALUES (1, 'Apfel Kuchen', 10), (2, 'Bratapfelkuchen', 1000), (3, 'Gedeckter Apfelkuchen', 500);");
        using var catalog = database.Open();
        var recipes = catalog.Index("recipes").MultiWord().LinkTo("recipe", "id", "title", weight: "views").Ensure();
        recipes.Sync();

        recipes.Search("kuchen apfel").Hits[0].Text.ShouldBe("Apfel Kuchen");
        recipes.Search("apfelkuch").Hits.Select(hit => hit.Key).ShouldContain(1L);
        recipes.Search("ge").Hits.ShouldHaveSingleItem().Key.ShouldBe(3, "the prefix path");
    }

    [Fact]
    public void A_linked_index_answers_as_an_owned_index_with_the_same_entries()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        var random = new Random(5);
        string[] stems = ["apfel", "kuchen", "vegan", "torte", "birne", "sahne", "zimt", "#äpfel", "straße"];
        var rows = Enumerable.Range(1, 400)
            .Select(i => ((long)i, (string?)$"{stems[random.Next(stems.Length)]}{stems[random.Next(stems.Length)]}{i % 7}", random.Next(0, 1000)))
            .ToArray();
        Insert(database, rows);
        using var catalog = database.Open();
        var linked = Tags(catalog);
        linked.Sync();
        var owned = catalog.Index("owned").SingleWord().Ensure();
        owned.UpsertMany(rows.Select(row => new LuiEntry<long, LuiNoPayload>(row.Item1, row.Item2!) { Weight = row.Item3 }));

        foreach (var input in new[] { "a", "ap", "apf", "kuchen", "nka", "veg", "#äpf", "äpfel", "strasse", "torte3", "zimtz" })
        {
            linked.Search(input).Hits.Select(hit => (hit.Key, hit.Text)).ShouldBe(
                owned.Search(input).Hits.Select(hit => (hit.Key, hit.Text)), $"input '{input}'");
        }
    }

    [Fact]
    public void The_typo_dictionary_of_a_linked_index_learns_from_the_table()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Insert(database, (1, "Apfelkuchen", 10));
        using var catalog = database.Open();
        var tags = catalog.Index("tags").SingleWord().TypoTolerance().LinkTo("tag", "id", "name", weight: "usage_count").Ensure();
        tags.Sync();

        tags.Search("apflekuchen").CorrectedQuery.ShouldBe("apfelkuchen");

        Insert(database, (2, "Himbeere", 50));
        tags.Refresh(2);
        tags.Search("himbere").Hits.ShouldHaveSingleItem().Key.ShouldBe(2);
    }

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

    // ---- the lifetime of the index -----------------------------------------------------------------------------------

    [Fact]
    public void Dropping_the_index_removes_its_tables_and_triggers_and_leaves_the_table_and_its_rows()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Insert(database, (1, "vegan", 5));
        using var catalog = database.Open();
        var tags = Tags(catalog);
        tags.Sync();
        tags.CreateTriggers();

        catalog.DropIndex("tags", dropTriggers: true).ShouldBeTrue();

        database.Scalar<long>("SELECT count(*) FROM sqlite_master WHERE name LIKE 'lui_tags%' OR type = 'trigger'").ShouldBe(0);
        database.Scalar<long>("SELECT count(*) FROM tag").ShouldBe(1);
    }

    [Fact]
    public void A_link_to_another_column_is_another_definition()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        using var catalog = database.Open();
        Tags(catalog);

        Should.Throw<LuiDefinitionMismatchException>(() => catalog.Index("tags").SingleWord().LinkTo("tag", "id", "slug").Ensure());
        Should.NotThrow(() => Tags(catalog));
    }

    [Fact]
    public void An_index_of_a_source_this_version_does_not_know_is_not_dropped_blindly()
    {
        // review of pull request 14, N2
        using var database = new TestDatabase();
        using var catalog = database.Open();
        Run(database, """
            INSERT INTO lui_meta(index_name, kind, source, definition, schema_version, created_at)
            VALUES ('future', 'SingleWord', 'Remote', '{"kind":"SingleWord","source":"Remote","keyType":"long","germanTransliteration":true,"payload":false,"foldTable":"x"}', 1, '2026-11-03');
            """);

        Should.Throw<InvalidOperationException>(() => catalog.DropIndex("future")).Message.ShouldContain("'Remote'");
    }

    [Fact]
    public void A_table_and_columns_named_as_an_orm_names_them_are_quoted()
    {
        using var database = new TestDatabase();
        Run(database, "CREATE TABLE \"Tags\" (\"Id\" INTEGER PRIMARY KEY, \"Name\" TEXT NOT NULL, \"UsageCount\" INTEGER NOT NULL);");
        Run(database, "INSERT INTO \"Tags\" VALUES (1, 'vegan', 5);");
        using var catalog = database.Open();
        var tags = catalog.Index("tags").SingleWord().LinkTo("Tags", "Id", "Name", weight: "UsageCount").Ensure();

        tags.Sync().Indexed.ShouldBe(1);
        tags.Search("veg").Hits.ShouldHaveSingleItem().Weight.ShouldBe(5);
        tags.Optimize();
        tags.Search("vegan").Hits.ShouldHaveSingleItem();
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
        catalog.Index("tags_all").SingleWord().LinkTo("tag", "id", "name").Ensure().CreateTriggers();
        tags.Triggers().ShouldBe(LuiTriggerState.Current);
    }

    [Fact]
    public void A_text_that_changed_under_the_index_is_not_a_hit()
    {
        // S1: the candidates come from the index, the text from the table — they must agree
        using var database = new TestDatabase();
        Run(database, Table);
        Insert(database, (1, "vegan", 5), (2, "veganer", 3));
        using var catalog = database.Open();
        var tags = Tags(catalog);
        tags.Sync();

        Run(database, "UPDATE tag SET name = 'zucker' WHERE id = 1;");

        tags.Search("vegan").Hits.ShouldHaveSingleItem().Key.ShouldBe(2, "„zucker\" does not match „vegan\"");
        tags.Search("ve").Hits.ShouldHaveSingleItem().Key.ShouldBe(2, "nor does it begin with „ve\"");
        tags.Search("zuck").Hits.ShouldBeEmpty("the index has not heard of the new text yet");

        tags.Refresh(1);
        tags.Search("zuck").Hits.ShouldHaveSingleItem().Key.ShouldBe(1);
    }

    [Fact]
    public void A_text_that_changed_under_a_multi_word_index_is_not_a_hit()
    {
        using var database = new TestDatabase();
        Run(database, "CREATE TABLE recipe (id INTEGER PRIMARY KEY, title TEXT NOT NULL, views INTEGER NOT NULL);");
        Run(database, "INSERT INTO recipe VALUES (1, 'Apfel Kuchen', 10), (2, 'Kuchen mit Apfel', 5);");
        using var catalog = database.Open();
        var recipes = catalog.Index("recipes").MultiWord().LinkTo("recipe", "id", "title", weight: "views").Ensure();
        recipes.Sync();

        Run(database, "UPDATE recipe SET title = 'Birnen Torte' WHERE id = 1;");

        recipes.Search("kuchen apfel").Hits.ShouldHaveSingleItem().Key.ShouldBe(2);
        recipes.Search("ap").Hits.ShouldBeEmpty();
    }

    [Fact]
    public void A_column_renamed_under_the_index_is_named_and_not_a_bare_sqlite_error()
    {
        // S4
        using var database = new TestDatabase();
        Run(database, Table);
        Insert(database, (1, "vegan", 5));
        using var catalog = database.Open();
        var tags = Tags(catalog);
        tags.Sync();

        Run(database, "ALTER TABLE tag RENAME COLUMN name TO title;");

        Should.Throw<LuiLinkedTableException>(() => tags.Search("vegan")).Message.ShouldContain("no such column");
        Should.Throw<LuiLinkedTableException>(() => tags.Refresh(1));
        Should.Throw<LuiLinkedTableException>(() => tags.Sync());
        Should.Throw<LuiLinkedTableException>(() => Tags(catalog)).Message.ShouldContain("'name'");
    }

    [Fact]
    public void A_key_that_comes_twice_is_refreshed_once_and_counted_once()
    {
        // N2
        using var database = new TestDatabase();
        Run(database, Table);
        Insert(database, (1, "vegan", 5));
        using var catalog = database.Open();
        var tags = Tags(catalog);

        tags.RefreshMany([1, 1, 1]).ShouldBe(1);
        tags.Count().ShouldBe(1);
    }

    [Fact]
    public void A_transaction_of_another_database_is_refused_with_a_clear_message()
    {
        // N5
        using var database = new TestDatabase();
        using var other = new TestDatabase();
        Run(database, Table);
        Run(other, Table);
        using var catalog = database.Open();
        var tags = Tags(catalog);

        using var connection = other.Connect();
        using var transaction = connection.BeginTransaction();

        Should.Throw<ArgumentException>(() => tags.Refresh(1, transaction)).Message.ShouldContain("another database");
    }

    [Fact]
    public void A_payload_column_named_twice_is_refused()
    {
        // N7
        using var database = new TestDatabase();
        using var catalog = database.Open();

        Should.Throw<ArgumentException>(() => catalog.Index("tags").SingleWord().LinkTo("tag", "id", "name")
            .WithPayload(["slug", "Slug"], row => row.GetString("slug")));
    }

    // ---- the re-review of pull request 16 ------------------------------------------------------------------------------

    [Fact]
    public void Stale_candidates_do_not_use_up_the_budget_up_to_a_second_budget_of_them()
    {
        // F1 (PR-008): a changed row that is not applied yet is read as a candidate of its old text and then found not to
        // match; it no longer counts against the budget, the read goes on past it — up to another budget of such rows
        using var database = new TestDatabase();
        Run(database, Table);
        Run(database, "WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n WHERE i < 1200) INSERT INTO tag(id, name, usage_count) SELECT i, 'vegan' || i, i FROM n;");
        using var catalog = database.Open();
        var tags = Tags(catalog);
        tags.Sync();
        tags.CreateTriggers();

        // the 450 most popular change their text: fewer than the budget of 500
        Run(database, "UPDATE tag SET name = 'zucker' || id WHERE id > 750;");
        var found = tags.Search("vegan");
        found.Hits.Count.ShouldBe(20);
        found.Hits.ShouldAllBe(hit => hit.Key <= 750);
        found.Hits[0].Key.ShouldBe(750);

        // 1,150 among the best: more than a second budget — the answer is empty and says it was cut off
        Run(database, "UPDATE tag SET name = 'zucker' || id WHERE id > 50;");
        var crowded = tags.Search("vegan");
        crowded.Hits.ShouldBeEmpty();
        crowded.Truncated.ShouldBeTrue();
        tags.Search("vegan", new LuiQueryOptions { Candidates = 1_150 }).Hits.Count.ShouldBe(20);

        tags.ApplyPending().ShouldBe(1_150);
        tags.Search("vegan").Hits.Count.ShouldBe(20);
        tags.Search("zucker").Hits.Count.ShouldBe(20);
    }

    [Fact]
    public void An_outdated_row_after_the_budget_does_not_make_the_answer_truncated()
    {
        // review of main after #23, finding 4: the budget was checked before the row was known to be outdated
        using var database = new TestDatabase();
        Run(database, Table);
        Run(database, "WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n WHERE i < 11) INSERT INTO tag(id, name, usage_count) SELECT i, 'vegan' || i, 100 - i FROM n;");
        using var catalog = database.Open();
        var tags = Tags(catalog);
        tags.Sync();
        Run(database, "UPDATE tag SET name = 'zucker' WHERE id = 11;");

        var result = tags.Search("vegan", new LuiQueryOptions { Candidates = 10 });

        result.Hits.Count.ShouldBe(10);
        result.Truncated.ShouldBeFalse("the row after the budget is outdated: there was nothing more");
    }

    [Fact]
    public void Without_stale_candidates_the_budget_is_what_it_was()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Run(database, "WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n WHERE i < 30) INSERT INTO tag(id, name, usage_count) SELECT i, 'vegan' || i, i FROM n;");
        using var catalog = database.Open();
        var tags = Tags(catalog);
        tags.Sync();

        var result = tags.Search("vegan", new LuiQueryOptions { Candidates = 10 });

        result.CandidatesConsidered.ShouldBe(10);
        result.Truncated.ShouldBeTrue();
    }

    [Fact]
    public void The_mismatch_of_a_linked_index_names_the_way_out_that_works()
    {
        // F2
        using var database = new TestDatabase();
        Run(database, Table);
        using var catalog = database.Open();
        Tags(catalog);

        Should.Throw<LuiDefinitionMismatchException>(() => catalog.Index("tags").SingleWord().LinkTo("tag", "id", "slug").Ensure())
            .Message.ShouldContain("dropTriggers: true");
        Should.Throw<LuiDefinitionMismatchException>(() => catalog.Index("owned").SingleWord().Ensure().Count() + catalog.Index("owned").MultiWord().Ensure().Count())
            .Message.ShouldNotContain("dropTriggers");
    }

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

    private static ILuiLinkedIndex<LuiNoPayload> Tags(LuiCatalog catalog) =>
        catalog.Index("tags").SingleWord().LinkTo("tag", "id", "name", weight: "usage_count").Ensure();

    private static void Insert(TestDatabase database, params (long Id, string? Name, int Weight)[] rows)
    {
        using var connection = database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO tag(id, name, usage_count) VALUES ($id, $name, $weight);";
        var id = command.Parameters.Add("$id", SqliteType.Integer);
        var name = command.Parameters.Add("$name", SqliteType.Text);
        var weight = command.Parameters.Add("$weight", SqliteType.Integer);
        foreach (var row in rows)
        {
            id.Value = row.Id;
            name.Value = (object?)row.Name ?? DBNull.Value;
            weight.Value = row.Weight;
            command.ExecuteNonQuery();
        }
    }

    private static string Schema(TestDatabase database, string table)
    {
        using var connection = database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT group_concat(type || ' ' || name || ' ' || coalesce(sql, ''), '\n') FROM sqlite_master WHERE tbl_name = $table;";
        command.Parameters.AddWithValue("$table", table);
        return (string)command.ExecuteScalar()!;
    }

    private static void Run(TestDatabase database, string sql)
    {
        using var connection = database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void Run(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
