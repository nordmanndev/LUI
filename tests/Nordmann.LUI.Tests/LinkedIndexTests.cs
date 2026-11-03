using Microsoft.Data.Sqlite;
using Shouldly;

namespace Nordmann.LUI.Tests;

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

        tags.Sync().ShouldBe(2);

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
        tags.Search("kuchen").Hits.ShouldHaveSingleItem("the index did not hear of the change");

        tags.Sync().ShouldBe(2);

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
    public void A_key_at_the_end_of_the_range_comes_back_as_it_is_and_one_beyond_it_is_refused()
    {
        // review of pull request 14, N6: the key is read back as text
        using var database = new TestDatabase();
        Run(database, Table);
        Insert(database, (4_294_967_295, "vegan", 5));
        using var catalog = database.Open();
        var tags = Tags(catalog);

        tags.Sync().ShouldBe(1);
        tags.Search("vegan").Hits.ShouldHaveSingleItem().Key.ShouldBe(4_294_967_295L);

        Insert(database, (4_294_967_296, "veganer", 5));
        Should.Throw<ArgumentOutOfRangeException>(() => tags.Refresh(4_294_967_296));
        Should.Throw<LuiLinkedTableException>(() => tags.Sync()).Message.ShouldContain("Nothing was changed");
        tags.Count().ShouldBe(1);
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
    public void With_the_triggers_every_write_to_the_table_reaches_the_index_and_without_them_none()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        using var catalog = database.Open();
        var tags = Tags(catalog);

        tags.CreateTriggers();
        tags.CreateTriggers();   // a second call replaces them

        Run(database, "INSERT INTO tag(id, name, usage_count) VALUES (1, 'vegan', 5), (2, 'kuchen', 3);");
        tags.Search("vegan").Hits.ShouldHaveSingleItem();
        Run(database, "UPDATE tag SET usage_count = 99 WHERE id = 2;");
        tags.Search("kuchen").Hits.ShouldHaveSingleItem().Weight.ShouldBe(99);
        Run(database, "UPDATE tag SET name = 'torte' WHERE id = 2;");
        tags.Search("kuchen").Hits.ShouldBeEmpty();
        tags.Search("torte").Hits.ShouldHaveSingleItem();
        Run(database, "UPDATE tag SET id = 3 WHERE id = 2;");
        tags.Search("torte").Hits.ShouldHaveSingleItem().Key.ShouldBe(3);
        Run(database, "DELETE FROM tag WHERE id = 1;");
        tags.Search("vegan").Hits.ShouldBeEmpty();
        tags.Count().ShouldBe(1);

        tags.DropTriggers();
        tags.DropTriggers();     // and a second removal finds nothing to remove

        database.Scalar<long>("SELECT count(*) FROM sqlite_master WHERE type = 'trigger'").ShouldBe(0);
        Run(database, "INSERT INTO tag(id, name) VALUES (4, 'tofu');");
        tags.Search("tofu").Hits.ShouldBeEmpty();
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

        catalog.DropIndex("tags").ShouldBeTrue();

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

        tags.Sync().ShouldBe(1);
        tags.Search("veg").Hits.ShouldHaveSingleItem().Weight.ShouldBe(5);
        tags.Optimize();
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
