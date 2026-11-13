using Shouldly;
using static Nordmann.LUI.Integration.Tests.Linked.LinkedTable;

namespace Nordmann.LUI.Integration.Tests.Linked;

/// <summary>
/// A <c>Linked</c> index over a table of the application (ADR-0014, ADR-0015, ADR-0015, ADR-0015): loosely coupled —
/// LUI reads the table and never changes it on its own. The definition, the names, the lifetime of the index.
/// </summary>
public sealed class LinkedIndexTests
{
    // ---- the table of the application is not touched ------------------------------------------------------------

    [Fact]
    public void Ensure_sync_and_refresh_leave_the_schema_of_the_application_table_as_it_was()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Insert(database, (1, "vegan", 5));
        var before = LinkedTable.Schema(database, "tag");

        using var catalog = database.Open();
        var tags = Tags(catalog);
        tags.Sync();
        tags.Refresh(1);

        LinkedTable.Schema(database, "tag").ShouldBe(before);
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

        Should.Throw<ArgumentException>(() => catalog.Create("tags", index => index.SingleWord().LinkTo(name, "id", "name")));
        Should.Throw<ArgumentException>(() => catalog.Create("tags", index => index.SingleWord().LinkTo("tag", "id", name)));
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

        Should.Throw<LuiDefinitionMismatchException>(() => catalog.Create("tags", index => index.SingleWord().LinkTo("tag", "id", "slug")));
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
        var tags = catalog.Create("tags", index => index.SingleWord().LinkTo("Tags", "Id", "Name", weight: "UsageCount"));

        tags.Sync().Indexed.ShouldBe(1);
        tags.Search("veg").Hits.ShouldHaveSingleItem().Weight.ShouldBe(5);
        tags.Optimize();
        tags.Search("vegan").Hits.ShouldHaveSingleItem();
    }

    // ---- review of pull request 16 ------------------------------------------------------------------------------------

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
    public void A_payload_column_named_twice_is_refused()
    {
        // N7
        using var database = new TestDatabase();
        using var catalog = database.Open();

        Should.Throw<ArgumentException>(() => catalog.Create("tags", index => index.SingleWord().LinkTo("tag", "id", "name")
            .WithPayload(["slug", "Slug"], row => row.GetString("slug"))));
    }

    // ---- the re-review of pull request 16 ------------------------------------------------------------------------------

    [Fact]
    public void The_mismatch_of_a_linked_index_names_the_way_out_that_works()
    {
        // F2
        using var database = new TestDatabase();
        Run(database, Table);
        using var catalog = database.Open();
        Tags(catalog);

        Should.Throw<LuiDefinitionMismatchException>(() => catalog.Create("tags", index => index.SingleWord().LinkTo("tag", "id", "slug")))
            .Message.ShouldContain("dropTriggers: true");
        Should.Throw<LuiDefinitionMismatchException>(() => catalog.Create("embedded", index => index.SingleWord()).Count() + catalog.Create("embedded", index => index.MultiWord()).Count())
            .Message.ShouldNotContain("dropTriggers");
    }

    // the shape of the API before 0.1.0 (ADR-0013), the builder and the interfaces
    [Fact]
    public void A_linked_index_has_the_numbers_of_its_table_as_keys()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();

        Should.Throw<InvalidOperationException>(
            () => catalog.Create("tags", index => index.SingleWord().WithKey(LuiKeyKind.Text).LinkTo("tag", "id", "name")))
            .Message.ShouldContain("INTEGER PRIMARY KEY");

        // issue #105: KeyKind of a linked index was read in no test
        Run(database, Table);
        Tags(catalog).KeyKind.ShouldBe(LuiKeyKind.Number);
    }
}
