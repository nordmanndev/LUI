using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Shouldly;

namespace Nordmann.LUI.Integration.Tests.Catalog;

/// <summary>
/// The read path (ADR-0033): an index that is <b>opened</b> and not created. Nothing is written, no table is
/// created, no write lock is taken — and the file of a database that is written elsewhere can be read.
/// <para>
/// There is one way into an index, its definition (ADR-0073): it names the types, and opening compares them with the file;
/// the payload comes with the hit, read by the serializer the registry of the catalog holds for its type (ADR-0072).
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

    private static readonly LuiIndexDefinition<long> Tags = new("tags", index => index.SingleWord());

    private static readonly LuiIndexDefinition<string> TextTags = new("tags", index => index.SingleWord());

    private static readonly LuiIndexDefinition<long, Chip> Chips = new("tags", index => index.SingleWord());

    private static readonly LuiIndexDefinition<long> Plain = new("plain", index => index.SingleWord());

    private static readonly LuiIndexDefinition<long> Titles = new("titles", index => index.MultiWord());

    private static readonly LuiLinkedIndexDefinition<long> LinkedTags = new("tags", index => index.SingleWord().LinkTo("tag", "id", "name"));

    private static readonly LuiLinkedIndexDefinition<long, Chip> LinkedChips = new(
        "tags",
        index => index.SingleWord()
            .LinkTo("tag", "id", "name", weight: "usage_count")
            .WithPayload(["slug", "color"], row => new Chip(row.GetString("slug"), row.GetString("color"))));

    // ---- the index has to be there, and it has to be one this library wrote ---------------------------------

    [Fact]
    public void An_index_that_does_not_exist_says_so()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();

        var exception = Should.Throw<LuiIndexNotFoundException>(() => catalog.Index(Tags));

        exception.IndexName.ShouldBe("tags");
        exception.Message.ShouldContain("tags");
        exception.Message.ShouldNotContain("no such table", Case.Insensitive);
    }

    [Fact]
    public void An_index_with_a_foreign_schema_version_says_so()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create(Tags);
        Run(database, "UPDATE lui_meta SET schema_version = 99 WHERE index_name = 'tags';");

        // LUI format 2 (ADR-0073): a file of another format is refused when the catalog is opened, before any index is
        var exception = Should.Throw<LuiFormatException>(() => database.Open());

        exception.Message.ShouldContain("99");
    }

    [Fact]
    public void An_index_with_a_foreign_fold_version_says_so()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create(Tags);
        var definition = database.Scalar<string>("SELECT definition FROM lui_meta WHERE index_name = 'tags';");
        WriteDefinition(database, "tags", definition.Replace("\"foldTable\":\"", "\"foldTable\":\"foreign-", StringComparison.Ordinal));

        using var reader = database.Open();
        var exception = Should.Throw<LuiDefinitionMismatchException>(() => reader.Index(Tags));

        exception.IndexName.ShouldBe("tags");
        exception.Message.ShouldContain("foreign-");
    }

    // ---- the read path writes nothing ---------------------------------------------------------------------------------

    [Fact]
    public void Opening_an_index_writes_nothing()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create(Tags).Upsert(1, "vegan");

        var rows = database.Scalar<long>("SELECT count(*) FROM lui_meta;");
        var tables = database.Scalar<long>("SELECT count(*) FROM sqlite_master;");
        var before = Fingerprint(database);

        catalog.Index(Tags);

        database.Scalar<long>("SELECT count(*) FROM lui_meta;").ShouldBe(rows, "no row of lui_meta was written");
        database.Scalar<long>("SELECT count(*) FROM sqlite_master;").ShouldBe(tables, "no table was created");

        if (before is not null)
        {
            Fingerprint(database).ShouldBe(before, "not a byte of the file was written");
        }
    }

    [Fact]
    [Trait("Category", "Timing")]
    public void Opening_an_index_takes_no_write_lock()
    {
        // the bounds are the busy timeout, not milliseconds (issue #78): a read path that waited for the lock would wait
        // the whole two seconds and fail; the write path waits them and fails. A loaded machine is slower, never faster —
        // the lower bound holds under any load, and the upper one leaves two seconds to open an index.
        using var database = new TestDatabase();
        using var catalog = database.Open(new LuiCatalogOptions { Pragmas = new LuiPragmas { BusyTimeout = TimeSpan.FromSeconds(2) } });
        catalog.Create(Tags).Upsert(1, "vegan");

        // a writer holds the lock of the file
        using var writer = database.Connect();
        using var transaction = writer.BeginTransaction(deferred: false);

        // the read path answers at once
        var opened = Stopwatch.StartNew();
        var index = catalog.Index(Tags);
        opened.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2), "the read path waits for no lock");

        index.Search("vegan").Hits.ShouldHaveSingleItem().Text.ShouldBe("vegan");

        // the write path waits for the lock and gives up
        var blocked = Stopwatch.StartNew();
        Should.Throw<SqliteException>(() => catalog.Create(Tags));
        blocked.Elapsed.ShouldBeGreaterThan(TimeSpan.FromSeconds(1.5), "the write path waits for the lock");
    }

    [Fact]
    public void An_index_of_a_read_only_database_can_be_opened()
    {
        using var database = new TestDatabase();
        database.PrepareWithATable();
        using (var catalog = database.Open())
        {
            catalog.Create(Tags).Upsert(1, "vegan");
        }

        using var reader = LuiCatalog.OpenReadOnly(database.FilePath);

        var index = reader.Index(Tags);
        index.Search("vegan").Hits.ShouldHaveSingleItem().Text.ShouldBe("vegan");
        index.Count().ShouldBe(1);
    }

    // ---- what the file decides, and what is the application's (ADR-0035) -------------------------------------------

    [Fact]
    public void An_opened_index_has_the_key_type_of_its_definition_and_the_file_decides_whether_it_fits()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create(TextTags).Upsert("tag-vegan", "vegan");

        var index = catalog.Index(TextTags);

        index.Search("vegan").Hits.ShouldHaveSingleItem().Key.ShouldBe("tag-vegan");
        Should.Throw<LuiDefinitionMismatchException>(() => catalog.Index(Tags)).IndexName.ShouldBe("tags");
    }

    [Fact]
    public void An_index_is_not_opened_with_a_key_of_the_other_kind_and_nothing_is_written()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create(Tags);
        tags.Upsert(1, "vegan");

        // the type of the key is the type argument of the definition: a text key does not compile on a long index, and a
        // definition with text keys is not the stored one
        var exception = Should.Throw<LuiDefinitionMismatchException>(() => catalog.Index(TextTags));

        exception.IndexName.ShouldBe("tags");
        exception.Message.ShouldContain("string");
        tags.Count().ShouldBe(1, "nothing was written");
    }

    [Fact]
    public void An_opened_index_reads_the_entries_of_the_file()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create(Chips)
            .Upsert(new LuiEntry<long, Chip>(1, "vegan", new Chip("gruen", "green")));

        var hit = catalog.Index(Chips).Search("vegan").Hits.ShouldHaveSingleItem();

        hit.Key.ShouldBe(1);
        hit.Payload.ShouldBe(new Chip("gruen", "green"));
    }

    [Fact]
    public void The_payload_is_read_by_the_serializer_of_the_catalog_that_opens_the_file()
    {
        // the bytes are in the file, the serializer is not: a catalog without a registry cannot open an index with a payload
        // type, and says what is missing (ADR-0072)
        using var database = new TestDatabase();
        using (var writer = database.Open())
        {
            writer.Create(Chips).Upsert(new LuiEntry<long, Chip>(1, "vegan", new Chip("gruen")));
        }

        using var reader = LuiCatalog.OpenOrCreate(database.FilePath);

        var exception = Should.Throw<LuiConfigurationException>(() => reader.Index(Chips));
        exception.Message.ShouldContain(nameof(LuiCatalogOptions.Payloads));
        exception.Message.ShouldContain(typeof(Chip).FullName!);
    }

    [Fact]
    public void A_payload_that_cannot_be_read_as_its_type_names_the_index_the_key_and_the_type()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var chips = catalog.Create(Chips);
        chips.Upsert(new LuiEntry<long, Chip>(7, "vegan", new Chip("gruen")));

        // bytes of another shape: the strict JSON of LUI refuses them
        Run(database, "UPDATE lui_tags SET payload = X'7B226F6C64223A317D' WHERE key = '7';");

        var exception = Should.Throw<LuiPayloadException>(() => chips.Search("vegan"));

        exception.Message.ShouldContain("tags");
        exception.Message.ShouldContain("'7'");
        exception.Message.ShouldContain(typeof(Chip).FullName!);
        exception.InnerException.ShouldBeAssignableTo<System.Text.Json.JsonException>();
    }

    [Fact]
    public void A_payload_of_an_embedded_index_of_another_shape_is_refused()
    {
        // review of PR 60, I1: the embedded path is as strict as the linked one — a chip read as a recipe card is an
        // error that names the index, the key and the type, not a card with an empty title
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var chips = catalog.Create(Chips);
        chips.Upsert(new LuiEntry<long, Chip>(1, "vegan", new Chip("#3a3", "System")));
        Run(database, "UPDATE lui_tags SET payload = CAST('{\"Title\":\"x\"}' AS BLOB) WHERE key = '1';");

        var exception = Should.Throw<LuiPayloadException>(() => chips.Search("vegan"));

        exception.IndexName.ShouldBe("tags");
        exception.Key.ShouldBe("1");
        exception.RequestedType.ShouldBe(typeof(Chip));
        exception.InnerException.ShouldBeOfType<System.Text.Json.JsonException>();
    }

    [Fact]
    public void Whatever_a_serializer_of_the_application_throws_on_the_read_comes_as_a_payload_exception()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open(new LuiCatalogOptions { Payloads = new LuiPayloadRegistry().Use(new RefusingSerializer()) });
        var chips = catalog.Create(Chips);
        chips.Upsert(new LuiEntry<long, Chip>(1, "vegan", new Chip("v")));

        var exception = Should.Throw<LuiPayloadException>(() => chips.Search("vegan"));

        exception.InnerException.ShouldBeOfType<InvalidOperationException>().Message.ShouldBe("refused");
    }

    [Fact]
    public void An_entry_of_an_index_without_a_payload_type_has_no_payload()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create(Tags).Upsert(1, "vegan");

        catalog.Index(Tags).Search("vegan").Hits.ShouldHaveSingleItem().Text.ShouldBe("vegan");

        database.Scalar<long>("SELECT count(*) FROM lui_tags WHERE payload IS NOT NULL;").ShouldBe(0, "nothing is stored as a payload");
    }

    [Fact]
    public void A_definition_with_the_payload_type_of_an_older_version_is_opened_and_its_type_ignored()
    {
        // ADR-0035: the file names no type any more. A definition that still carries `payload` and `payloadType` is the
        // same index — the two fields are read and ignored.
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create(Chips).Upsert(new LuiEntry<long, Chip>(1, "vegan", new Chip("gruen")));
        var stored = database.Scalar<string>("SELECT definition FROM lui_meta WHERE index_name = 'tags';");
        WriteDefinition(database, "tags", stored.Replace("{", "{\"payload\":true,\"payloadType\":\"Some.Old.Chip\",", StringComparison.Ordinal));

        catalog.Index(Chips).Search("vegan").Hits.ShouldHaveSingleItem().Payload.ShouldBe(new Chip("gruen"));
        Should.NotThrow(() => catalog.Create(Chips), "the definition is the same");
    }

    [Fact]
    public void A_definition_with_a_kind_of_key_this_version_does_not_know_says_so_when_it_is_opened()
    {
        // review of PR 60, M3: a key type that is none of the four of this version ("long", "int", "string", "guid") is not read as
        // numbers — the first hit would fail far from the cause, in long.Parse
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create(Tags).Upsert(1, "vegan");
        var stored = database.Scalar<string>("SELECT definition FROM lui_meta WHERE index_name = 'tags';");
        WriteDefinition(database, "tags", stored.Replace("\"keyType\":\"long\"", "\"keyType\":\"decimal\"", StringComparison.Ordinal));

        using var reader = database.Open();
        var exception = Should.Throw<LuiDefinitionMismatchException>(() => reader.Index(Tags));

        exception.IndexName.ShouldBe("tags");
        exception.Message.ShouldContain("decimal");
    }

    [Fact]
    public void An_index_is_opened_as_what_it_is()
    {
        using var database = new TestDatabase();
        Run(database, TagTable);
        using var catalog = database.Open();
        catalog.Create(Plain);
        catalog.Create(LinkedTags);

        Should.Throw<LuiDefinitionMismatchException>(() => catalog.Index(Tags)).IndexName.ShouldBe("tags");
        Should.Throw<LuiDefinitionMismatchException>(() => catalog.Index(new LuiLinkedIndexDefinition<long>("plain", index => index.SingleWord().LinkTo("tag", "id", "name")))).IndexName.ShouldBe("plain");
        Should.Throw<LuiDefinitionMismatchException>(() => catalog.Index(new LuiLinkedIndexDefinition<long, string>("plain", index => index.SingleWord().LinkTo("tag", "id", "name").WithPayload(["name"], row => row.GetString("name")))));
    }

    [Fact]
    public void An_opened_index_writes_when_the_file_allows_it()
    {
        // ADR-0033: the file decides what may happen, not the index
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create(Tags).Upsert(1, "vegan");

        var index = catalog.Index(Tags);

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
        catalog.Create(LinkedChips).Sync();

        var hit = catalog.Index(LinkedChips).Search("vegan").Hits.ShouldHaveSingleItem();

        hit.Key.ShouldBe(1, "the key of the table");
        hit.Payload.ShouldBe(new Chip("vegan", "green"), "the object of the columns");

        // the columns are in the definition: an index with payload columns is not opened without the map
        Should.Throw<LuiDefinitionMismatchException>(() => catalog.Index(LinkedTags)).Message.ShouldContain("slug");
    }

    [Fact]
    public void A_linked_index_with_a_payload_stores_the_columns_of_its_payload_in_the_definition()
    {
        using var database = new TestDatabase();
        Run(database, TagTable);
        using var catalog = database.Open();
        catalog.Create(LinkedChips);

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

        catalog.Create(LinkedChips);

        Should.NotThrow(() => catalog.Create(LinkedChips));
    }

    // ---- the defaults of an opened index ------------------------------------------------------------------------------

    [Fact]
    public void An_opened_index_measures_the_similarity_of_its_kind()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create(Tags).Upsert(1, "Apfelkuchen");
        catalog.Create(Titles).Upsert(1, "Apfelkuchen");

        var single = catalog.Index(Tags).Search("apfel").Hits.ShouldHaveSingleItem();
        var multi = catalog.Index(Titles).Search("apfel").Hits.ShouldHaveSingleItem();

        single.Similarity.ShouldBe(LuiSimilarity.JaroWinkler.Similarity("apfel", "apfelkuchen"));
        multi.Similarity.ShouldBe(LuiSimilarity.NgramDice.Similarity("apfel", "apfelkuchen"));
    }

    [Fact]
    public void The_options_of_a_query_overrule_the_similarity_and_the_typo_tolerance_of_an_opened_index()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var typoTags = new LuiIndexDefinition<long>("tags", index => index.SingleWord().TypoTolerance());
        var tags = catalog.Create(typoTags);
        tags.Upsert(1, "Apfelkuchen");

        // the application asks for the build (ADR-0042): a query never does. The read path writes nothing,
        // so the warm-up belongs to the index that writes.
        tags.WarmUpTypoDictionary();

        var index = catalog.Index(typoTags);
        index.TypoDistance.ShouldBe(LuiTypoDistance.OneError, "the distance is part of the definition");

        index.Search("apflekuchen").Hits.ShouldHaveSingleItem("the opened index forgives what its definition says");
        index.Search("apflekuchen", new LuiQueryOptions { TypoTolerance = false })
            .Hits.ShouldBeEmpty("the query asks for no correction");

        var constant = new Constant(0.25);
        index.Search("apfel", new LuiQueryOptions { Similarity = constant })
            .Hits.ShouldHaveSingleItem().Similarity.ShouldBe(0.25);
    }

    // ---- the helpers --------------------------------------------------------------------------------------------------

    /// <summary>The bytes of the file and of its write-ahead log: what a write would change.</summary>
    /// <remarks>
    /// On Windows the file cannot be read while the catalog holds it open — the handle of SQLite denies the
    /// read — so there the fingerprint is <see langword="null"/> and the claims about the bytes are not checked.
    /// The claims about the rows and the tables are checked on every platform.
    /// </remarks>
    private static string? Fingerprint(TestDatabase database) =>
        OperatingSystem.IsWindows()
            ? null
            : string.Join("|", Fingerprint(database.FilePath), Fingerprint(database.FilePath + "-wal"));

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

    /// <summary>A serializer of the application that writes and refuses to read — with an exception of its own.</summary>
    private sealed class RefusingSerializer : ILuiPayloadSerializer<Chip>
    {
        public string Format => "refusing-v1";

        public byte[] Serialize(Chip payload) => [1];

        public Chip Deserialize(ReadOnlySpan<byte> bytes) => throw new InvalidOperationException("refused");
    }
}
