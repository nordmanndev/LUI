using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Shouldly;

namespace Nordmann.LUI.Integration.Tests;

/// <summary>
/// The read path (ADR-0033): an index that is <b>opened</b> and not created. Nothing is written, no table is
/// created, no write lock is taken — and the file of a database that is written elsewhere can be read.
/// <para>
/// There is one way into an index, and it names no type (ADR-0035): the kind of the key comes from the file, the payload
/// is read where it is used, with the serialiser of the catalog.
/// </para>
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

        var exception = Should.Throw<LuiIndexNotFoundException>(() => catalog.Index("tags"));

        exception.IndexName.ShouldBe("tags");
        exception.Message.ShouldContain("tags");
        exception.Message.ShouldNotContain("no such table", Case.Insensitive);
    }

    [Fact]
    public void An_index_with_a_foreign_schema_version_says_so()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create("tags", index => index.SingleWord());
        Run(database, "UPDATE lui_meta SET schema_version = 99 WHERE index_name = 'tags';");

        var exception = Should.Throw<LuiDefinitionMismatchException>(() => catalog.Index("tags"));

        exception.IndexName.ShouldBe("tags");
        exception.Message.ShouldContain("99");
    }

    [Fact]
    public void An_index_with_a_foreign_fold_version_says_so()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create("tags", index => index.SingleWord());
        var definition = database.Scalar<string>("SELECT definition FROM lui_meta WHERE index_name = 'tags';");
        WriteDefinition(database, "tags", definition.Replace("\"foldTable\":\"", "\"foldTable\":\"foreign-", StringComparison.Ordinal));

        var exception = Should.Throw<LuiDefinitionMismatchException>(() => catalog.Index("tags"));

        exception.IndexName.ShouldBe("tags");
        exception.Message.ShouldContain("foreign-");
    }

    // ---- the read path writes nothing ---------------------------------------------------------------------------------

    [Fact]
    public void Opening_an_index_writes_nothing()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create("tags", index => index.SingleWord()).Upsert(1, "vegan");

        var rows = database.Scalar<long>("SELECT count(*) FROM lui_meta;");
        var tables = database.Scalar<long>("SELECT count(*) FROM sqlite_master;");
        var before = Fingerprint(database);

        catalog.Index("tags");

        database.Scalar<long>("SELECT count(*) FROM lui_meta;").ShouldBe(rows, "no row of lui_meta was written");
        database.Scalar<long>("SELECT count(*) FROM sqlite_master;").ShouldBe(tables, "no table was created");
        Fingerprint(database).ShouldBe(before, "not a byte of the file was written");
    }

    [Fact]
    public void Opening_an_index_takes_no_write_lock()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open(new LuiCatalogOptions { BusyTimeout = TimeSpan.FromMilliseconds(500) });
        catalog.Create("tags", index => index.SingleWord()).Upsert(1, "vegan");

        // a writer holds the lock of the file
        using var writer = database.Connect();
        using var transaction = writer.BeginTransaction(deferred: false);

        // the read path answers at once
        var opened = Stopwatch.StartNew();
        var index = catalog.Index("tags");
        opened.Elapsed.ShouldBeLessThan(TimeSpan.FromMilliseconds(500), "the read path waits for no lock");

        index.Search("vegan").Hits.ShouldHaveSingleItem().Text.ShouldBe("vegan");

        // the write path waits for the lock and gives up
        var blocked = Stopwatch.StartNew();
        Should.Throw<SqliteException>(() => catalog.Create("tags", index => index.SingleWord()));
        blocked.Elapsed.ShouldBeGreaterThan(TimeSpan.FromMilliseconds(400), "the write path waits for the lock");
    }

    [Fact]
    public void An_index_of_a_read_only_database_can_be_opened()
    {
        using var database = new TestDatabase();
        using (var catalog = database.Open(new LuiCatalogOptions { WriteAheadLog = false }))
        {
            catalog.Create("tags", index => index.SingleWord()).Upsert(1, "vegan");
        }

        using var reader = LuiCatalog.Open($"Data Source={database.FilePath};Mode=ReadOnly", new LuiCatalogOptions { WriteAheadLog = false });

        var index = reader.Index("tags");
        index.Search("vegan").Hits.ShouldHaveSingleItem().Text.ShouldBe("vegan");
        index.Count().ShouldBe(1);
    }

    // ---- what the file decides, and what is the application's (ADR-0035) -------------------------------------------

    [Fact]
    public void An_opened_index_takes_the_kind_of_its_keys_from_the_file()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create("tags", index => index.SingleWord().WithKey(LuiKeyKind.Text)).Upsert("tag-vegan", "vegan");

        var index = catalog.Index("tags");

        index.KeyKind.ShouldBe(LuiKeyKind.Text);
        index.Search("vegan").Hits.ShouldHaveSingleItem().Key.ShouldBe((LuiKey)"tag-vegan");
    }

    [Fact]
    public void An_opened_index_refuses_a_key_of_the_other_kind_on_the_write()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create("tags", index => index.SingleWord()).Upsert(1, "vegan");
        var index = catalog.Index("tags");

        var exception = Should.Throw<ArgumentException>(() => index.Upsert("tag-vegan", "vegan"));

        exception.Message.ShouldContain("tags");
        exception.Message.ShouldContain("numbers");
        exception.Message.ShouldContain("tag-vegan");
        Should.Throw<ArgumentException>(() => index.Remove("1"), "the text \"1\" is not the number 1");
        index.Count().ShouldBe(1, "nothing was written");
    }

    [Fact]
    public void An_opened_index_reads_the_entries_of_the_file()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create("tags", index => index.SingleWord())
            .Upsert(new LuiEntry(1, "vegan") { Payload = new Chip("gruen", "green") });

        var hit = catalog.Index("tags").Search("vegan").Hits.ShouldHaveSingleItem();

        hit.Key.ShouldBe((LuiKey)1);
        hit.HasPayload.ShouldBeTrue();
        hit.GetPayload<Chip>().ShouldBe(new Chip("gruen", "green"));
    }

    [Fact]
    public void The_payload_is_read_by_the_serialiser_of_the_catalog_that_opens_the_file()
    {
        // the bytes are in the file, the serialiser is not: a catalog without one opens the index and finds the hits,
        // and says what is missing when the payload is asked for
        using var database = new TestDatabase();
        using (var writer = database.Open())
        {
            writer.Create("tags", index => index.SingleWord()).Upsert(new LuiEntry(1, "vegan") { Payload = new Chip("gruen") });
        }

        using var reader = LuiCatalog.Open(database.ConnectionString);
        var hit = reader.Index("tags").Search("vegan").Hits.ShouldHaveSingleItem();

        hit.HasPayload.ShouldBeTrue();
        var exception = Should.Throw<LuiPayloadException>(() => hit.GetPayload<Chip>());
        exception.IndexName.ShouldBe("tags");
        exception.Key.ShouldBe((LuiKey)1);
        exception.RequestedType.ShouldBe(typeof(Chip));
        exception.Message.ShouldContain(nameof(LuiCatalogOptions.PayloadSerializer));
    }

    [Fact]
    public void A_payload_that_cannot_be_read_as_the_type_asked_for_names_the_index_the_key_and_the_type()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create("tags", index => index.SingleWord()).Upsert(new LuiEntry(7, "vegan") { Payload = new Chip("gruen") });
        var hit = catalog.Index("tags").Search("vegan").Hits.ShouldHaveSingleItem();

        var exception = Should.Throw<LuiPayloadException>(() => hit.GetPayload<Uri>());

        exception.Message.ShouldContain("tags");
        exception.Message.ShouldContain("'7'");
        exception.Message.ShouldContain("System.Uri");
        exception.InnerException.ShouldBeOfType<NotSupportedException>();
    }

    [Fact]
    public void A_payload_of_an_embedded_index_read_as_a_type_of_another_shape_is_refused()
    {
        // review of PR 60, I1: the embedded path is as strict as the linked one — a chip read as a recipe card is an
        // error that names the index, the key and the type, not a card with an empty title
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create("tags", index => index.SingleWord()).Upsert(new LuiEntry(1, "vegan") { Payload = new Chip("#3a3", "System") });
        var hit = catalog.Index("tags").Search("vegan").Hits.ShouldHaveSingleItem();

        var exception = Should.Throw<LuiPayloadException>(() => hit.GetPayload<RecipeCard>());

        exception.IndexName.ShouldBe("tags");
        exception.Key.ShouldBe((LuiKey)1);
        exception.RequestedType.ShouldBe(typeof(RecipeCard));
        exception.InnerException.ShouldBeOfType<System.Text.Json.JsonException>();
        Should.Throw<LuiPayloadException>(() => hit.TryGetPayload<RecipeCard>(out _), "the entry has a payload: false would say it had none");
    }

    [Fact]
    public void Whatever_a_serialiser_of_the_application_throws_on_the_read_comes_as_a_payload_exception()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open(new LuiCatalogOptions { PayloadSerializer = new RefusingSerializer() });
        catalog.Create("tags", index => index.SingleWord()).Upsert(new LuiEntry(1, "vegan") { Payload = new Chip("v") });
        var hit = catalog.Index("tags").Search("vegan").Hits.ShouldHaveSingleItem();

        var exception = Should.Throw<LuiPayloadException>(() => hit.GetPayload<Chip>());

        exception.InnerException.ShouldBeOfType<InvalidOperationException>().Message.ShouldBe("refused");
    }

    [Fact]
    public void An_entry_without_a_payload_has_none_and_says_so()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create("tags", index => index.SingleWord()).Upsert(1, "vegan");

        var hit = catalog.Index("tags").Search("vegan").Hits.ShouldHaveSingleItem();

        hit.HasPayload.ShouldBeFalse();
        hit.TryGetPayload<Chip>(out var payload).ShouldBeFalse();
        payload.ShouldBeNull();
        hit.GetPayload<Chip>().ShouldBeNull();
    }

    [Fact]
    public void A_definition_with_the_payload_type_of_an_older_version_is_opened_and_its_type_ignored()
    {
        // ADR-0035: the file names no type any more. A definition that still carries `payload` and `payloadType` is the
        // same index — the two fields are read and ignored.
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create("tags", index => index.SingleWord()).Upsert(new LuiEntry(1, "vegan") { Payload = new Chip("gruen") });
        var stored = database.Scalar<string>("SELECT definition FROM lui_meta WHERE index_name = 'tags';");
        WriteDefinition(database, "tags", stored.Replace("{", "{\"payload\":true,\"payloadType\":\"Some.Old.Chip\",", StringComparison.Ordinal));

        catalog.Index("tags").Search("vegan").Hits.ShouldHaveSingleItem().GetPayload<Chip>().ShouldBe(new Chip("gruen"));
        Should.NotThrow(() => catalog.Create("tags", index => index.SingleWord()), "the definition is the same");
    }

    [Fact]
    public void A_definition_with_a_kind_of_key_this_version_does_not_know_says_so_when_it_is_opened()
    {
        // review of PR 60, M3: a key type that is neither "long" nor "string" is not read as numbers — the first hit
        // would fail far from the cause, in long.Parse
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create("tags", index => index.SingleWord()).Upsert(1, "vegan");
        var stored = database.Scalar<string>("SELECT definition FROM lui_meta WHERE index_name = 'tags';");
        WriteDefinition(database, "tags", stored.Replace("\"keyType\":\"long\"", "\"keyType\":\"int\"", StringComparison.Ordinal));

        var exception = Should.Throw<LuiDefinitionMismatchException>(() => catalog.Index("tags"));

        exception.IndexName.ShouldBe("tags");
        exception.Message.ShouldContain("int");
    }

    [Fact]
    public void An_index_is_opened_as_what_it_is()
    {
        using var database = new TestDatabase();
        Run(database, TagTable);
        using var catalog = database.Open();
        catalog.Create("plain", index => index.SingleWord());
        catalog.Create("tags", index => index.SingleWord().LinkTo("tag", "id", "name"));

        Should.Throw<LuiDefinitionMismatchException>(() => catalog.Index("tags")).IndexName.ShouldBe("tags");
        Should.Throw<LuiDefinitionMismatchException>(() => catalog.LinkedIndex("plain")).IndexName.ShouldBe("plain");
        Should.Throw<LuiDefinitionMismatchException>(() => catalog.LinkedIndex("plain", row => row.GetString("name")));
    }

    [Fact]
    public void An_opened_index_writes_when_the_file_allows_it()
    {
        // ADR-0033: the file decides what may happen, not the index
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create("tags", index => index.SingleWord()).Upsert(1, "vegan");

        var index = catalog.Index("tags");

        index.Upsert(2, "vegetarisch");
        index.Count().ShouldBe(2);
    }

    // ---- a linked index -----------------------------------------------------------------------------------------------

    [Fact]
    public void A_linked_index_with_a_payload_can_be_opened()
    {
        using var database = new TestDatabase();
        Run(database, TagTable);
        Insert(database, (1, "vegan", 5, "vegan", "green"));
        using var catalog = database.Open();
        catalog.Create("tags", index => index.SingleWord()
            .LinkTo("tag", "id", "name", weight: "usage_count")
            .WithPayload(["slug", "color"], row => new Chip(row.GetString("slug"), row.GetString("color"))))
            .Sync();

        var hit = catalog.LinkedIndex("tags", row => new Chip(row.GetString("slug"), row.GetString("color")))
            .Search("vegan").Hits.ShouldHaveSingleItem();

        hit.Key.ShouldBe((LuiKey)1, "the key of the table");
        hit.GetPayload<Chip>().ShouldBe(new Chip("vegan", "green"), "the object of the columns");

        // the map built a Chip: a hit read as something else says what it is
        Should.Throw<LuiPayloadException>(() => hit.GetPayload<Uri>()).Message.ShouldContain(typeof(Chip).FullName!);

        // the columns are in the definition: an index with payload columns is not opened without the map
        Should.Throw<LuiDefinitionMismatchException>(() => catalog.LinkedIndex("tags")).Message.ShouldContain("slug");
    }

    [Fact]
    public void A_linked_index_with_a_payload_stores_the_columns_of_its_payload_in_the_definition()
    {
        using var database = new TestDatabase();
        Run(database, TagTable);
        using var catalog = database.Open();
        catalog.Create("tags", index => index.SingleWord()
            .LinkTo("tag", "id", "name")
            .WithPayload(["slug", "color"], row => new Chip(row.GetString("slug"), row.GetString("color"))));

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

        catalog.Create("tags", index => index.SingleWord()
            .LinkTo("tag", "id", "name")
            .WithPayload(["slug", "color"], row => new Chip(row.GetString("slug"), row.GetString("color"))));

        Should.NotThrow(() => catalog.Create("tags", index => index.SingleWord()
            .LinkTo("tag", "id", "name")
            .WithPayload(["slug", "color"], row => new Chip(row.GetString("slug"), row.GetString("color")))));
    }

    // ---- the defaults of an opened index ------------------------------------------------------------------------------

    [Fact]
    public void An_opened_index_measures_the_similarity_of_its_kind()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create("tags", index => index.SingleWord()).Upsert(1, "Apfelkuchen");
        catalog.Create("titles", index => index.MultiWord()).Upsert(1, "Apfelkuchen");

        var single = catalog.Index("tags").Search("apfel").Hits.ShouldHaveSingleItem();
        var multi = catalog.Index("titles").Search("apfel").Hits.ShouldHaveSingleItem();

        single.Similarity.ShouldBe(LuiSimilarity.JaroWinkler.Similarity("apfel", "apfelkuchen"));
        multi.Similarity.ShouldBe(LuiSimilarity.NgramDice.Similarity("apfel", "apfelkuchen"));
    }

    [Fact]
    public void The_options_of_a_query_overrule_the_similarity_and_the_typo_tolerance_of_an_opened_index()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        tags.Upsert(1, "Apfelkuchen");

        // the application asks for the build (ADR-0042): a query never does. The read path writes nothing,
        // so the warm-up belongs to the index that writes.
        tags.WarmUpTypoDictionary();

        var index = catalog.Index("tags");
        index.TypoTolerance.ShouldBeFalse("an opened index corrects no typo");

        index.Search("apflekuchen").Hits.ShouldBeEmpty("nothing corrects the typo");
        index.Search("apflekuchen", new LuiQueryOptions { TypoTolerance = true })
            .Hits.ShouldHaveSingleItem("the query asks for the correction");

        var constant = new Constant(0.25);
        index.Search("apfel", new LuiQueryOptions { Similarity = constant })
            .Hits.ShouldHaveSingleItem().Similarity.ShouldBe(0.25);
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

    /// <summary>A serialiser of the application that writes and refuses to read — with an exception of its own.</summary>
    private sealed class RefusingSerializer : ILuiPayloadSerializer
    {
        public byte[] Serialize(object payload) => [1];

        public T Deserialize<T>(ReadOnlySpan<byte> bytes) => throw new InvalidOperationException("refused");
    }
}
