using Shouldly;

namespace Nordmann.LUI.Integration.Tests.Embedded;

/// <summary>
/// The schema and the definition of an <c>Embedded</c> single-word index (ADR-0006, ADR-0011, docs/api.md): what
/// <c>Ensure</c> creates, what the definition holds, and what refuses an index that is not the one asked for.
/// </summary>
public sealed class EmbeddedIndexTests
{
    [Fact]
    public void Ensure_creates_the_tables_the_indexes_the_search_index_and_the_triggers()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();

        catalog.Create(new LuiIndexDefinition<long>("recipes", index => index.SingleWord()));

        var tables = Names(database, "type = 'table'");
        tables.Contains("lui_recipes", StringComparer.Ordinal).ShouldBeTrue();
        tables.Contains("lui_recipes_fts", StringComparer.Ordinal).ShouldBeTrue();
        Names(database, "type = 'index' AND tbl_name = 'lui_recipes' AND name NOT LIKE 'sqlite_%'")
            .ShouldBe(["lui_recipes_p1", "lui_recipes_p2", "lui_recipes_rowid"]);
        Names(database, "type = 'trigger'").ShouldBe(["lui_recipes_ad", "lui_recipes_ai", "lui_recipes_au"]);
    }

    [Fact]
    public void Ensure_writes_the_definition_into_the_meta_table()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();

        catalog.Create(new LuiIndexDefinition<long>("recipes", index => index.SingleWord()));

        database.Scalar<string>("SELECT kind FROM lui_meta WHERE index_name = 'recipes'").ShouldBe("SingleWord");
        database.Scalar<string>("SELECT source FROM lui_meta WHERE index_name = 'recipes'").ShouldBe("Embedded");
        database.Scalar<string>("SELECT definition FROM lui_meta WHERE index_name = 'recipes'")
            .ShouldContain("\"germanTransliteration\":true");
    }

    [Fact]
    public void Ensure_is_idempotent_and_keeps_the_entries()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var first = catalog.Create(new LuiIndexDefinition<long>("tags", index => index.SingleWord()));
        first.Upsert(1, "vegan", 5);

        var second = catalog.Create(new LuiIndexDefinition<long>("tags", index => index.SingleWord()));

        second.Count().ShouldBe(1);
        database.Scalar<long>("SELECT count(*) FROM lui_meta").ShouldBe(1);
    }

    [Fact]
    public void Ensure_with_another_definition_fails_and_names_both()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create(new LuiIndexDefinition<long>("tags", index => index.SingleWord()));

        var exception = Should.Throw<LuiDefinitionMismatchException>(
            () => catalog.Create(new LuiIndexDefinition<long>("tags", index => index.SingleWord().GermanTransliteration(false))));

        exception.IndexName.ShouldBe("tags");
        exception.Stored.ShouldContain("\"germanTransliteration\":true");
        exception.Requested.ShouldContain("\"germanTransliteration\":false");
        exception.Message.ShouldContain("stored:");
        exception.Message.ShouldContain("requested:");
    }

    [Fact]
    public void A_definition_without_a_payload_type_names_no_payload()
    {
        // ADR-0072: the type of a payload is part of the stored definition — only where the definition declares one; an index
        // without a payload type stores no payload and its definition names none
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create(new LuiIndexDefinition<long>("tags", index => index.SingleWord())).Upsert(new LuiEntry<long>(1, "vegan"));

        Should.NotThrow(() => catalog.Create(new LuiIndexDefinition<long>("tags", index => index.SingleWord())));

        var definition = database.Scalar<string>("SELECT definition FROM lui_meta WHERE index_name = 'tags';");
        definition.ShouldNotContain("payload", Case.Insensitive);
        definition.ShouldNotContain(nameof(Chip));
    }

    [Fact]
    public void A_key_of_another_type_changes_the_definition()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create(new LuiIndexDefinition<long>("tags", index => index.SingleWord()));

        Should.Throw<LuiDefinitionMismatchException>(
            () => catalog.Create(new LuiIndexDefinition<string>("tags", index => index.SingleWord())));
    }

    [Fact]
    public void An_index_stored_with_another_fold_table_is_refused()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create(new LuiIndexDefinition<long>("tags", index => index.SingleWord()));

        using (var connection = database.Connect())
        {
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE lui_meta SET definition = replace(definition, '\"foldTable\":\"', '\"foldTable\":\"old-')";
            command.ExecuteNonQuery();
        }

        var exception = Should.Throw<LuiDefinitionMismatchException>(
            () => catalog.Create(new LuiIndexDefinition<long>("tags", index => index.SingleWord())));
        exception.Stored.ShouldContain("old-");
    }

    [Fact]
    public void Drop_removes_everything_and_the_way_out_of_a_mismatch()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create(new LuiIndexDefinition<long>("tags", index => index.SingleWord())).Upsert(1, "vegan", 5);

        catalog.DropIndex("tags").ShouldBeTrue();

        database.Scalar<long>("SELECT count(*) FROM sqlite_master WHERE name LIKE 'lui_tags%'").ShouldBe(0);
        database.Scalar<long>("SELECT count(*) FROM lui_meta").ShouldBe(0);
        catalog.DropIndex("tags").ShouldBeFalse();

        // and the index can be ensured again, with another definition
        Should.NotThrow(() => catalog.Create(new LuiIndexDefinition<long>("tags", index => index.SingleWord().GermanTransliteration(false))));
    }

    [Fact]
    public void Indexes_are_independent_of_each_other()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create(new LuiIndexDefinition<long>("tags", index => index.SingleWord()));
        var words = catalog.Create(new LuiIndexDefinition<long>("words", index => index.SingleWord()));

        tags.Upsert(1, "vegan", 5);
        words.Upsert(1, "apfelkuchen", 5);

        tags.Query("vegan").Execute().Hits.Count.ShouldBe(1);
        tags.Query("apfel").Execute().Hits.ShouldBeEmpty();
        words.Query("apfel").Execute().Hits.Count.ShouldBe(1);
    }

    [Fact]
    public void The_entries_survive_a_new_catalog_on_the_same_file()
    {
        using var database = new TestDatabase();
        using (var catalog = database.Open())
        {
            catalog.Create(new LuiIndexDefinition<long>("tags", index => index.SingleWord())).Upsert(1, "vegan", 5);
        }

        using var again = database.Open();
        var tags = again.Create(new LuiIndexDefinition<long>("tags", index => index.SingleWord()));

        tags.Query("vegan").Execute().Hits.ShouldHaveSingleItem().Key.ShouldBe(1);
    }

    private static List<string> Names(TestDatabase database, string condition)
    {
        using var connection = database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT name FROM sqlite_master WHERE {condition} ORDER BY name;";

        var names = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }
}
