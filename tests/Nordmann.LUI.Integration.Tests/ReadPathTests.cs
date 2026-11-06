using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Shouldly;

namespace Nordmann.LUI.Integration.Tests;

/// <summary>
/// The read path (ADR-0033): an index that is <b>opened</b> and not created. Nothing is written, no table is
/// created, no write lock is taken — and the file of a database that is written elsewhere can be read.
/// <para>
/// The untyped facade is gone: there is one way into an index, and it is typed — the key and the payload are the
/// types of the application.
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

        var exception = Should.Throw<LuiIndexNotFoundException>(() => catalog.Index<long, LuiNoPayload>("tags"));

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

        var exception = Should.Throw<LuiDefinitionMismatchException>(() => catalog.Index<long, LuiNoPayload>("tags"));

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

        var exception = Should.Throw<LuiDefinitionMismatchException>(() => catalog.Index<long, LuiNoPayload>("tags"));

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

        catalog.Index<long, LuiNoPayload>("tags");

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
        var index = catalog.Index<long, LuiNoPayload>("tags");
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

        var index = reader.Index<long, LuiNoPayload>("tags");
        index.Search("vegan").Hits.ShouldHaveSingleItem().Text.ShouldBe("vegan");
        index.Count().ShouldBe(1);
    }

    // ---- the types of the caller against the types of the file --------------------------------------------------------

    [Fact]
    public void An_opened_index_refuses_a_key_type_the_file_does_not_have()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create("tags", index => index.SingleWord()).Upsert(1, "vegan");

        var exception = Should.Throw<LuiDefinitionMismatchException>(() => catalog.Index<string, LuiNoPayload>("tags"));

        exception.IndexName.ShouldBe("tags");
        exception.Message.ShouldContain("long");
        exception.Message.ShouldContain("string");
    }

    [Fact]
    public void An_opened_index_refuses_a_payload_type_the_file_does_not_have()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create("tags", index => index.SingleWord().WithPayload(ChipJsonContext.Default.Chip))
            .Upsert(new LuiEntry<long, Chip>(1, "vegan") { Payload = new Chip("gruen") });

        var exception = Should.Throw<LuiDefinitionMismatchException>(
            () => catalog.Index<long, OtherChip>("tags", new OtherSerializer()));

        exception.IndexName.ShouldBe("tags");
        exception.Message.ShouldContain(nameof(Chip));
        exception.Message.ShouldContain(nameof(OtherChip));
    }

    [Fact]
    public void An_opened_index_refuses_a_payload_the_file_does_not_have()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create("tags", index => index.SingleWord().WithPayload(ChipJsonContext.Default.Chip))
            .Upsert(new LuiEntry<long, Chip>(1, "vegan") { Payload = new Chip("gruen") });

        // the file stores a payload, the caller brings none
        var withoutPayload = Should.Throw<LuiDefinitionMismatchException>(
            () => catalog.Index<long, Chip>("tags"));
        withoutPayload.IndexName.ShouldBe("tags");
        withoutPayload.Message.ShouldContain("payload");

        // the file stores none, the caller brings one
        catalog.Create("plain", index => index.SingleWord()).Upsert(1, "vegan");
        var withPayload = Should.Throw<LuiDefinitionMismatchException>(
            () => catalog.Index<long, Chip>("plain", ChipJsonContext.Default.Chip));
        withPayload.IndexName.ShouldBe("plain");
        withPayload.Message.ShouldContain("payload");
    }

    [Fact]
    public void An_opened_index_reads_the_entries_of_the_file()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create("tags", index => index.SingleWord().WithPayload(ChipJsonContext.Default.Chip))
            .Upsert(new LuiEntry<long, Chip>(1, "vegan") { Payload = new Chip("gruen", "green") });

        var index = catalog.Index<long, Chip>("tags", ChipJsonContext.Default.Chip);

        var hit = index.Search("vegan").Hits.ShouldHaveSingleItem();
        hit.Key.ShouldBe(1);
        hit.Payload.ShouldBe(new Chip("gruen", "green"));
    }

    [Fact]
    public void An_opened_index_with_a_type_info_refuses_a_payload_type_the_file_does_not_have()
    {
        // ADR-0033: the type of the payload is in the definition, and the type the caller asks for is compared with it.
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create("tags", index => index.SingleWord().WithPayload(new OtherSerializer()))
            .Upsert(new LuiEntry<long, OtherChip>(1, "vegan") { Payload = new OtherChip("gruen") });

        var exception = Should.Throw<LuiDefinitionMismatchException>(
            () => catalog.Index<long, Chip>("tags", ChipJsonContext.Default.Chip));

        exception.IndexName.ShouldBe("tags");
        exception.Message.ShouldContain(nameof(Chip));
        exception.Message.ShouldContain(nameof(OtherChip));
    }

    [Fact]
    public void An_opened_index_with_a_serialiser_of_the_application_reads_the_entries_of_the_file()
    {
        // The overload with a serialiser stays for an application whose payload is not JSON — the bytes of the file are
        // the same, whichever serialiser reads them.
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create("tags", index => index.SingleWord().WithPayload(ChipJsonContext.Default.Chip))
            .Upsert(new LuiEntry<long, Chip>(1, "vegan") { Payload = new Chip("gruen", "green") });

        var index = catalog.Index<long, Chip>("tags", new ChipSerializer());

        var hit = index.Search("vegan").Hits.ShouldHaveSingleItem();
        hit.Key.ShouldBe(1);
        hit.Payload.ShouldBe(new Chip("gruen", "green"));
    }

    [Fact]
    public void An_opened_index_writes_when_the_file_allows_it()
    {
        // ADR-0033: the file decides what may happen, not the index
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create("tags", index => index.SingleWord()).Upsert(1, "vegan");

        var index = catalog.Index<long, LuiNoPayload>("tags");

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

        var hit = catalog.Index<Chip>("tags", row => new Chip(row.GetString("slug"), row.GetString("color")))
            .Search("vegan").Hits.ShouldHaveSingleItem();

        hit.Key.ShouldBe(1, "the key of the table");
        hit.Payload.ShouldBe(new Chip("vegan", "green"), "the object of the columns");
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

        var single = catalog.Index<long, LuiNoPayload>("tags").Search("apfel").Hits.ShouldHaveSingleItem();
        var multi = catalog.Index<long, LuiNoPayload>("titles").Search("apfel").Hits.ShouldHaveSingleItem();

        single.Similarity.ShouldBe(LuiSimilarity.JaroWinkler.Similarity("apfel", "apfelkuchen"));
        multi.Similarity.ShouldBe(LuiSimilarity.NgramDice.Similarity("apfel", "apfelkuchen"));
    }

    [Fact]
    public void The_options_of_a_query_overrule_the_similarity_and_the_typo_tolerance_of_an_opened_index()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create("tags", index => index.SingleWord()).Upsert(1, "Apfelkuchen");

        var index = catalog.Index<long, LuiNoPayload>("tags");
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

    /// <summary>A payload of another type than the one the index stores (ADR-0033).</summary>
    private sealed record OtherChip(string Slug);

    /// <summary>The serialiser of that payload — the application brings it, the file does not store it.</summary>
    private sealed class OtherSerializer : ILuiPayloadSerializer<OtherChip>
    {
        public byte[] Serialize(OtherChip payload) => System.Text.Encoding.UTF8.GetBytes(payload.Slug);

        public OtherChip Deserialize(ReadOnlySpan<byte> bytes) =>
            new OtherChip(System.Text.Encoding.UTF8.GetString(bytes));
    }

    /// <summary>A serialiser of the application — the way to open an index whose payload is not JSON.</summary>
    private sealed class ChipSerializer : ILuiPayloadSerializer<Chip>
    {
        public byte[] Serialize(Chip payload) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(payload);

        public Chip Deserialize(ReadOnlySpan<byte> bytes) =>
            System.Text.Json.JsonSerializer.Deserialize<Chip>(bytes)!;
    }
}
