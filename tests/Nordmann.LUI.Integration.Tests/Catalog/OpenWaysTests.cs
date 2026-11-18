using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Shouldly;

namespace Nordmann.LUI.Integration.Tests.Catalog;

/// <summary>
/// The three ways to open a catalogue (ADR-0068): <c>Open</c> checks and writes nothing, <c>OpenOrCreate</c> sets the
/// file up, <c>OpenReadOnly</c> only reads — and a write that SQLite refuses is a <see cref="LuiReadOnlyException"/>
/// that names the reason.
/// </summary>
public sealed class OpenWaysTests
{
    [Fact]
    public void Open_on_a_catalogue_in_the_rollback_journal_writes_nothing_and_keeps_the_mode()
    {
        using var database = new TestDatabase();
        MakeCatalogue(database, inDeleteMode: true);
        var before = Hash(database);

        using (var catalog = LuiCatalog.Open(database.FilePath))
        {
            catalog.Index("tags").Search("vegan").Hits.ShouldHaveSingleItem();
        }

        Hash(database).ShouldBe(before, "Open writes nothing");
        database.Scalar<string>("PRAGMA journal_mode").ShouldBe("delete");
    }

    [Fact]
    public void Open_on_a_foreign_database_is_refused_and_leaves_it_alone()
    {
        using var database = new TestDatabase();
        MakeForeignDatabase(database);
        var before = Hash(database);

        var exception = Should.Throw<LuiCatalogNotFoundException>(() => LuiCatalog.Open(database.FilePath));

        exception.Message.ShouldContain("lui_meta", customMessage: "the message names the table that is missing");
        exception.Path.ShouldBe(Path.GetFullPath(database.FilePath));
        Hash(database).ShouldBe(before, "a file that is no catalogue is not made one by Open");
    }

    [Fact]
    public void Open_on_a_file_that_is_no_database_says_so()
    {
        using var database = new TestDatabase();
        File.WriteAllText(database.FilePath, "this is not a SQLite database, and it is long enough to have a header of its own");
        var before = Hash(database);

        var exception = Should.Throw<LuiException>(() => LuiCatalog.Open(database.FilePath));

        exception.ShouldBeOfType<LuiCatalogNotFoundException>();
        exception.Message.ShouldContain("SQLite");
        exception.InnerException.ShouldBeOfType<SqliteException>();
        Hash(database).ShouldBe(before);
    }

    [Fact]
    public void OpenReadOnly_on_a_foreign_database_is_refused_and_leaves_it_alone()
    {
        using var database = new TestDatabase();
        MakeForeignDatabase(database);
        var before = Hash(database);

        var exception = Should.Throw<LuiCatalogNotFoundException>(() => LuiCatalog.OpenReadOnly(database.FilePath));

        exception.Message.ShouldContain("lui_meta");
        Hash(database).ShouldBe(before);
    }

    [Fact]
    public void OpenOrCreate_on_the_database_of_an_application_adds_the_meta_table_and_keeps_everything_else()
    {
        using var database = new TestDatabase();
        MakeForeignDatabase(database);

        using (var catalog = LuiCatalog.OpenOrCreate(database.FilePath))
        {
            catalog.Create("tags", index => index.SingleWord()).Upsert(1, "vegan");
        }

        database.Scalar<long>("SELECT count(*) FROM sqlite_master WHERE name = 'lui_meta'").ShouldBe(1);
        database.Scalar<string>("PRAGMA journal_mode").ShouldBe("delete", "the file is the application's, and so is its mode");
        database.Scalar<long>("SELECT count(*) FROM app").ShouldBe(1, "the table of the application is untouched");
    }

    [Fact]
    public void OpenOrCreate_on_a_catalogue_changes_nothing()
    {
        using var database = new TestDatabase();
        MakeCatalogue(database, inDeleteMode: true);
        var before = Hash(database);

        using (LuiCatalog.OpenOrCreate(database.FilePath))
        {
        }

        Hash(database).ShouldBe(before);
    }

    [Fact]
    public void A_write_in_a_catalogue_opened_with_OpenReadOnly_names_the_way_it_was_opened()
    {
        using var database = new TestDatabase();
        MakeCatalogue(database, inDeleteMode: false);

        using var reader = LuiCatalog.OpenReadOnly(database.FilePath);
        var index = reader.Index("tags");

        var exception = Should.Throw<LuiReadOnlyException>(() => index.Upsert(2, "vegetarisch"));

        exception.Operation.ShouldBe("writing an entry");
        exception.Message.ShouldContain("OpenReadOnly");
        exception.Message.ShouldContain("writing an entry");
        exception.Message.ShouldContain("'tags'");
        exception.Message.ShouldContain("Open", customMessage: "the message says how to write");
        var refusal = exception.InnerException.ShouldBeOfType<SqliteException>("SQLite refused, LUI translated");
        refusal.SqliteErrorCode.ShouldBe(8);
    }

    [Fact]
    public void A_write_that_SQLite_refuses_on_an_ordinary_catalogue_names_the_file_not_OpenReadOnly()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Create("tags", i => i.SingleWord());

        // the file may not be written: SQLite refuses (query_only answers like a file without rights, and as root too)
        using (var refuse = catalog.Database.Writer.CreateCommand())
        {
            refuse.CommandText = "PRAGMA query_only = ON;";
            refuse.ExecuteNonQuery();
        }

        var exception = Should.Throw<LuiReadOnlyException>(() => index.Upsert(1, "vegan"));

        exception.Message.ShouldContain(Path.GetFileName(database.FilePath), customMessage: "the message names the file");
        exception.Message.ShouldNotContain("OpenReadOnly", customMessage: "this catalogue was not opened read-only");
        exception.InnerException.ShouldBeOfType<SqliteException>().SqliteErrorCode.ShouldBe(8);
    }

    [Fact]
    public void A_transaction_of_the_application_does_not_get_round_the_read_only_catalogue()
    {
        using var database = new TestDatabase();
        MakeCatalogue(database, inDeleteMode: false);

        using var reader = LuiCatalog.OpenReadOnly(database.FilePath);
        var index = reader.Index("tags");
        using var connection = database.Connect();
        using var transaction = connection.BeginTransaction();

        Should.Throw<LuiReadOnlyException>(() => index.Upsert(new LuiEntry(2, "vegetarisch"), transaction));

        transaction.Rollback();
    }

    /// <summary>A catalogue with one index and one entry, written and closed.</summary>
    private static void MakeCatalogue(TestDatabase database, bool inDeleteMode)
    {
        using (var catalog = LuiCatalog.OpenOrCreate(database.FilePath))
        {
            catalog.Create("tags", index => index.SingleWord()).Upsert(1, "vegan");
        }

        if (inDeleteMode)
        {
            using var connection = database.Connect();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode = DELETE;";
            command.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();
    }

    /// <summary>The database of an application: one table with one row, no <c>lui_meta</c>, the rollback journal.</summary>
    private static void MakeForeignDatabase(TestDatabase database)
    {
        using (var connection = database.Connect())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TABLE app(id INTEGER PRIMARY KEY); INSERT INTO app VALUES (1);";
            command.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();
    }

    /// <summary>The hash of the bytes of the file.</summary>
    private static string Hash(TestDatabase database) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(database.FilePath)));
}
