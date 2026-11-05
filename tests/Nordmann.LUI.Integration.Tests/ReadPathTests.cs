using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Shouldly;

namespace Nordmann.LUI.Integration.Tests;

/// <summary>
/// The read path (ADR-0033): an index that is <b>opened</b> and not created. Nothing is written, no table is
/// created, no write lock is taken — and the file of a database that is written elsewhere can be read.
/// </summary>
public sealed class ReadPathTests
{
    private const string TagTable = """
        CREATE TABLE tag (
            id          INTEGER PRIMARY KEY,
            name        TEXT,
            usage_count INTEGER NOT NULL DEFAULT 0,
            slug        TEXT,
            color       TEXT);
        """;

    // ---- the index has to be there, and it has to be one this library wrote ---------------------------------

    [Fact]
    public void An_index_that_does_not_exist_says_so()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();

        var exception = Should.Throw<LuiIndexNotFoundException>(() => catalog.OpenIndex("tags"));

        exception.IndexName.ShouldBe("tags");
        exception.Message.ShouldContain("tags");
        exception.Message.ShouldNotContain("no such table", Case.Insensitive);
    }

    [Fact]
    public void An_index_with_a_foreign_schema_version_says_so()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Index("tags").SingleWord().Create();
        Run(database, "UPDATE lui_meta SET schema_version = 99 WHERE index_name = 'tags';");

        var exception = Should.Throw<LuiDefinitionMismatchException>(() => catalog.OpenIndex("tags"));

        exception.IndexName.ShouldBe("tags");
        exception.Message.ShouldContain("99");
    }

    [Fact]
    public void An_index_with_a_foreign_fold_version_says_so()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Index("tags").SingleWord().Create();
        var definition = database.Scalar<string>("SELECT definition FROM lui_meta WHERE index_name = 'tags';");
        WriteDefinition(database, "tags", definition.Replace("\"foldTable\":\"", "\"foldTable\":\"foreign-", StringComparison.Ordinal));

        var exception = Should.Throw<LuiDefinitionMismatchException>(() => catalog.OpenIndex("tags"));

        exception.IndexName.ShouldBe("tags");
        exception.Message.ShouldContain("foreign-");
    }

    // ---- the read path writes nothing ---------------------------------------------------------------------------------

    [Fact]
    public void Opening_an_index_writes_nothing()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Index("tags").SingleWord().Create().Upsert(1, "vegan");

        var rows = database.Scalar<long>("SELECT count(*) FROM lui_meta;");
        var tables = database.Scalar<long>("SELECT count(*) FROM sqlite_master;");
        var before = Fingerprint(database);

        catalog.OpenIndex("tags");

        database.Scalar<long>("SELECT count(*) FROM lui_meta;").ShouldBe(rows, "no row of lui_meta was written");
        database.Scalar<long>("SELECT count(*) FROM sqlite_master;").ShouldBe(tables, "no table was created");
        Fingerprint(database).ShouldBe(before, "not a byte of the file was written");
    }

    [Fact]
    public void Opening_an_index_takes_no_write_lock()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open(new LuiCatalogOptions { BusyTimeout = TimeSpan.FromMilliseconds(500) });
        catalog.Index("tags").SingleWord().Create().Upsert(1, "vegan");

        // a writer holds the lock of the file
        using var writer = database.Connect();
        using var transaction = writer.BeginTransaction(deferred: false);

        // the read path answers at once
        var opened = Stopwatch.StartNew();
        var index = catalog.OpenIndex("tags");
        opened.Elapsed.ShouldBeLessThan(TimeSpan.FromMilliseconds(500), "the read path waits for no lock");

        index.Search("vegan").ShouldHaveSingleItem().Text.ShouldBe("vegan");

        // the write path waits for the lock and gives up
        var blocked = Stopwatch.StartNew();
        Should.Throw<SqliteException>(() => catalog.Index("tags").SingleWord().Create());
        blocked.Elapsed.ShouldBeGreaterThan(TimeSpan.FromMilliseconds(400), "the write path waits for the lock");
    }

    [Fact]
    public void An_index_of_a_read_only_database_can_be_opened()
    {
        using var database = new TestDatabase();
        using (var catalog = database.Open(new LuiCatalogOptions { WriteAheadLog = false }))
        {
            catalog.Index("tags").SingleWord().Create().Upsert(1, "vegan");
        }

        var readOnly = string.Create(CultureInfo.InvariantCulture, $"Data Source=file:{database.FilePath}?mode=ro");
        using var reader = LuiCatalog.Open(readOnly, new LuiCatalogOptions { WriteAheadLog = false });

        var index = reader.OpenIndex("tags");
        index.Search("vegan").ShouldHaveSingleItem().Text.ShouldBe("vegan");
        index.Count().ShouldBe(1);
    }

    // ---- the untyped facade ------------------------------------------------------------------------------------------

    [Fact]
    public void The_untyped_facade_returns_the_key_as_the_text_it_is_stored_as()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Index("tags").SingleWord().Create().UpsertMany(
        [
            new LuiEntry<long, LuiNoPayload>(1, "apfel"),
            new LuiEntry<long, LuiNoPayload>(2, "aprikose"),
            new LuiEntry<long, LuiNoPayload>(42, "apfelkuchen"),
        ]);

        var index = catalog.OpenIndex("tags");

        index.Name.ShouldBe("tags");
        index.Kind.ShouldBe(LuiIndexKind.SingleWord);
        index.Count().ShouldBe(3);
        index.Search("ap").Select(hit => hit.Key).OrderBy(key => key, StringComparer.Ordinal)
            .ShouldBe(["1", "2", "42"]);
    }

    [Fact]
    public void The_untyped_facade_returns_the_payload_as_the_raw_json()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Index("tags").SingleWord().WithPayload(ChipJsonContext.Default.Chip).Create()
            .Upsert(new LuiEntry<long, Chip>(1, "apfel") { Payload = new Chip("gruen", "green") });
        catalog.Index("plain").SingleWord().Create().Upsert(1, "apfel");

        var withPayload = catalog.OpenIndex("tags").Search("apfel").ShouldHaveSingleItem();
        withPayload.Payload.ShouldBe("""{"Slug":"gruen","Color":"green"}""", "the JSON that was stored, exactly as it was written");

        var withoutPayload = catalog.OpenIndex("plain").Search("apfel").ShouldHaveSingleItem();
        withoutPayload.Payload.ShouldBeNull("the index stores no payload");
    }

    [Fact]
    public void A_linked_index_with_a_payload_can_be_opened()
    {
        using var database = new TestDatabase();
        Run(database, TagTable);
        Insert(database, (1, "vegan", 5, "vegan", "green"));
        using var catalog = database.Open();
        catalog.Index("tags").SingleWord()
            .LinkTo("tag", "id", "name", weight: "usage_count")
            .WithPayload(["slug", "color"], row => new Chip(row.GetString("slug"), row.GetString("color")))
            .Create()
            .Sync();

        var hit = catalog.OpenIndex("tags").Search("vegan").ShouldHaveSingleItem();

        hit.Key.ShouldBe("1", "the key of the table, as the text it is stored as");
        hit.Payload.ShouldBe("""{"slug":"vegan","color":"green"}""", "the JSON object of the columns");
    }

    [Fact]
    public void A_linked_index_with_a_payload_stores_the_columns_of_its_payload_in_the_definition()
    {
        using var database = new TestDatabase();
        Run(database, TagTable);
        using var catalog = database.Open();
        catalog.Index("tags").SingleWord()
            .LinkTo("tag", "id", "name")
            .WithPayload(["slug", "color"], row => new Chip(row.GetString("slug"), row.GetString("color")))
            .Create();

        var definition = database.Scalar<string>("SELECT definition FROM lui_meta WHERE index_name = 'tags';");

        definition.ShouldContain("\"slug\"");
        definition.ShouldContain("\"color\"");
    }

    [Fact]
    public void A_linked_index_with_a_payload_can_be_created_again()
    {
        // the definition that comes out of lui_meta holds another list of columns than the one that was asked for —
        // and it is the same index
        using var database = new TestDatabase();
        Run(database, TagTable);
        using var catalog = database.Open();

        catalog.Index("tags").SingleWord()
            .LinkTo("tag", "id", "name")
            .WithPayload(["slug", "color"], row => new Chip(row.GetString("slug"), row.GetString("color")))
            .Create();

        Should.NotThrow(() => catalog.Index("tags").SingleWord()
            .LinkTo("tag", "id", "name")
            .WithPayload(["slug", "color"], row => new Chip(row.GetString("slug"), row.GetString("color")))
            .Create());
    }

    // ---- the typed facade ---------------------------------------------------------------------------------------------

    [Fact]
    public void The_typed_facade_refuses_a_type_the_file_does_not_have()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Index("tags").SingleWord().Create().Upsert(1, "vegan");

        var exception = Should.Throw<LuiDefinitionMismatchException>(() => catalog.OpenIndex<string, LuiNoPayload>("tags"));

        exception.IndexName.ShouldBe("tags");
        exception.Message.ShouldContain("long");
        exception.Message.ShouldContain("string");
    }

    [Fact]
    public void The_typed_facade_opens_an_index_whose_types_the_file_has_and_does_not_write()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Index("tags").SingleWord().WithPayload(ChipJsonContext.Default.Chip).Create()
            .Upsert(new LuiEntry<long, Chip>(1, "vegan") { Payload = new Chip("gruen") });

        var index = catalog.OpenIndex<long, Chip>("tags", new LuiJsonPayloadSerializer<Chip>(ChipJsonContext.Default.Chip));

        index.Search("vegan").Hits.ShouldHaveSingleItem().Key.ShouldBe(1);
        Should.Throw<NotSupportedException>(() => index.Upsert(2, "vegetarisch"), "an opened index does not write");
    }

    // ---- the defaults of an opened index ------------------------------------------------------------------------------

    [Fact]
    public void An_opened_index_measures_the_similarity_of_its_kind()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Index("tags").SingleWord().Create().Upsert(1, "Apfelkuchen");
        catalog.Index("titles").MultiWord().Create().Upsert(1, "Apfelkuchen");

        var single = catalog.OpenIndex("tags").Search("apfel").ShouldHaveSingleItem();
        var multi = catalog.OpenIndex("titles").Search("apfel").ShouldHaveSingleItem();

        single.Similarity.ShouldBe(LuiSimilarity.JaroWinkler.Similarity("apfel", "apfelkuchen"));
        multi.Similarity.ShouldBe(LuiSimilarity.NgramDice.Similarity("apfel", "apfelkuchen"));
    }

    [Fact]
    public void The_options_of_a_query_overrule_the_similarity_and_the_typo_tolerance_of_an_opened_index()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Index("tags").SingleWord().Create().Upsert(1, "Apfelkuchen");

        var index = catalog.OpenIndex("tags");
        index.TypoTolerance.ShouldBeFalse("an opened index corrects no typo");

        index.Search("apflekuchen").ShouldBeEmpty("nothing corrects the typo");
        index.Search("apflekuchen", new LuiQueryOptions { TypoTolerance = true })
            .ShouldHaveSingleItem("the query asks for the correction");

        var constant = new Constant(0.25);
        index.Search("apfel", new LuiQueryOptions { Similarity = constant })
            .ShouldHaveSingleItem().Similarity.ShouldBe(0.25);
    }

    // ---- the helpers --------------------------------------------------------------------------------------------------

    /// <summary>The bytes of the file and of its write-ahead log: what a write would change.</summary>
    private static string Fingerprint(TestDatabase database) =>
        string.Join("|", Fingerprint(database.FilePath), Fingerprint(database.FilePath + "-wal"));

    private static string Fingerprint(string path) =>
        File.Exists(path)
            ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
            : "none";

    private static void Run(TestDatabase database, string sql)
    {
        using var connection = database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void WriteDefinition(TestDatabase database, string indexName, string definition)
    {
        using var connection = database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE lui_meta SET definition = $definition WHERE index_name = $name;";
        command.Parameters.AddWithValue("$definition", definition);
        command.Parameters.AddWithValue("$name", indexName);
        command.ExecuteNonQuery();
    }

    private static void Insert(
        TestDatabase database,
        params (long Id, string Name, int Weight, string Slug, string Color)[] rows)
    {
        using var connection = database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO tag(id, name, usage_count, slug, color) VALUES ($id, $name, $weight, $slug, $color);";
        var id = command.Parameters.Add("$id", SqliteType.Integer);
        var name = command.Parameters.Add("$name", SqliteType.Text);
        var weight = command.Parameters.Add("$weight", SqliteType.Integer);
        var slug = command.Parameters.Add("$slug", SqliteType.Text);
        var color = command.Parameters.Add("$color", SqliteType.Text);
        foreach (var row in rows)
        {
            id.Value = row.Id;
            name.Value = row.Name;
            weight.Value = row.Weight;
            slug.Value = row.Slug;
            color.Value = row.Color;
            command.ExecuteNonQuery();
        }
    }

    /// <summary>A measure that always gives the same number.</summary>
    private sealed class Constant(double value) : ILuiSimilarity
    {
        public double Similarity(string query, string text) => value;
    }
}
