using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Shouldly;

using Nordmann.LUI.Integration.Tests.Linked;

namespace Nordmann.LUI.Integration.Tests.Catalog;

/// <summary>
/// A catalogue is one file (issue #183): an <c>Embedded</c> and a <c>Linked</c> index live side by side in it, with the
/// table of the application beside the tables of LUI — and the journal mode of the file is the one the rules of ADR-0066 and
/// ADR-0068 give, in either order the application and LUI set the file up in.
/// </summary>
public sealed class OneFileTests
{
    [Fact]
    public void An_embedded_and_a_linked_index_answer_in_one_catalogue_and_the_file_holds_both_and_the_table_of_the_application()
    {
        using var database = new TestDatabase();
        Application(database);
        LinkedTable.Insert(database, (1, "vegan", 3), (2, "veggie", 1));

        using (var catalog = LuiCatalog.OpenOrCreate(database.FilePath))
        {
            catalog.Create(new LuiIndexDefinition<long>("labels", index => index.SingleWord())).Upsert(1, "vegetarisch");
            var tags = LinkedTable.Tags(catalog);
            tags.Sync();

            catalog.Index(new LuiIndexDefinition<long>("labels", index => index.SingleWord())).Search("vegetar").Hits.ShouldHaveSingleItem();
            tags.Search("veg").Hits.Count.ShouldBe(2, "the linked index answers from the table of the application");
        }

        SqliteConnection.ClearAllPools();

        var tables = TablesOf(database);
        tables.Contains("lui_meta", StringComparer.Ordinal).ShouldBeTrue("the meta table of the catalogue");
        tables.ShouldContain(name => name.StartsWith("lui_labels", StringComparison.Ordinal), "the tables of the embedded index");
        tables.ShouldContain(name => name.StartsWith("lui_tags", StringComparison.Ordinal), "the tables of the linked index");
        tables.Contains("tag", StringComparer.Ordinal).ShouldBeTrue("the table of the application, beside the tables of LUI");
        tables.Contains("lui_labels", StringComparer.Ordinal).ShouldBeTrue("the embedded index owns its content table");

        Directory.GetFiles(Path.GetDirectoryName(database.FilePath)!, Path.GetFileName(database.FilePath) + "*")
            .Where(path => !path.EndsWith("-wal", StringComparison.Ordinal) && !path.EndsWith("-shm", StringComparison.Ordinal))
            .ShouldHaveSingleItem("a catalogue is one file");
    }

    [Fact]
    public void LUI_sets_up_a_new_file_in_write_ahead_logging_and_the_application_that_comes_after_does_not_change_it()
    {
        using var database = new TestDatabase();

        using (var catalog = LuiCatalog.OpenOrCreate(database.FilePath))
        {
            catalog.Create(new LuiIndexDefinition<long>("labels", index => index.SingleWord())).Upsert(1, "vegan");
        }

        SqliteConnection.ClearAllPools();
        database.Scalar<string>("PRAGMA journal_mode").ShouldBe("wal", "OpenOrCreate set up a file with an empty schema");

        Application(database);
        SqliteConnection.ClearAllPools();

        database.Scalar<string>("PRAGMA journal_mode").ShouldBe("wal", "the table of the application does not change the mode");
    }

    [Fact]
    public void A_file_that_is_already_there_keeps_its_mode_even_if_it_is_empty()
    {
        // ADR-0066, section 4 (2026-11-18): WAL only when OpenOrCreate creates the file itself — not when the schema is empty
        using var database = new TestDatabase();
        File.WriteAllBytes(database.FilePath, []);

        using (var catalog = LuiCatalog.OpenOrCreate(database.FilePath))
        {
            catalog.Create(new LuiIndexDefinition<long>("labels", index => index.SingleWord())).Upsert(1, "vegan");
        }

        SqliteConnection.ClearAllPools();
        database.Scalar<string>("PRAGMA journal_mode").ShouldBe("delete", "an empty file that is there has the mode it has — the rollback journal");
    }

    [Fact]
    public void A_file_that_the_application_set_up_first_keeps_its_mode_when_LUI_comes_after()
    {
        using var database = new TestDatabase();
        Application(database);
        SqliteConnection.ClearAllPools();
        database.Scalar<string>("PRAGMA journal_mode").ShouldBe("delete", "the application's own file, the default mode");

        using (var catalog = LuiCatalog.OpenOrCreate(database.FilePath))
        {
            catalog.Create(new LuiIndexDefinition<long>("labels", index => index.SingleWord())).Upsert(1, "vegan");
            LinkedTable.Tags(catalog);
        }

        SqliteConnection.ClearAllPools();
        database.Scalar<string>("PRAGMA journal_mode").ShouldBe("delete", "a file with tables keeps its mode");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_later_Open_writes_nothing_and_does_not_change_the_journal_mode_of_the_file_of_the_application(bool applicationFirst)
    {
        using var database = new TestDatabase();
        if (applicationFirst)
        {
            Application(database);
        }

        using (var catalog = LuiCatalog.OpenOrCreate(database.FilePath))
        {
            catalog.Create(new LuiIndexDefinition<long>("labels", index => index.SingleWord())).Upsert(1, "vegan");
        }

        if (!applicationFirst)
        {
            Application(database);
        }

        SqliteConnection.ClearAllPools();
        var mode = database.Scalar<string>("PRAGMA journal_mode");
        SqliteConnection.ClearAllPools(); // Windows: a pooled connection keeps the file open, and the hash could not read it
        var before = Hash(database);

        using (var catalog = LuiCatalog.Open(database.FilePath))
        {
            catalog.Index(new LuiIndexDefinition<long>("labels", index => index.SingleWord())).Search("vegan").Hits.ShouldHaveSingleItem();
        }

        SqliteConnection.ClearAllPools();
        Hash(database).ShouldBe(before, "Open writes nothing");
        database.Scalar<string>("PRAGMA journal_mode").ShouldBe(mode, "Open does not switch the mode of the file of the application");
        SqliteConnection.ClearAllPools();
    }

    /// <summary>The application sets up its table.</summary>
    private static void Application(TestDatabase database)
    {
        using var connection = database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = LinkedTable.Table;
        command.ExecuteNonQuery();
    }

    private static List<string> TablesOf(TestDatabase database)
    {
        using var connection = database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name;";
        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static string Hash(TestDatabase database) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(database.FilePath)));
}
