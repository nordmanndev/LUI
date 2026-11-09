using Shouldly;

namespace Nordmann.LUI.Integration.Tests.Catalog;

/// <summary>
/// The catalog on a file that may not be written (ADR-0033): the connection string says so, and the catalog reads and
/// searches — and refuses every write with a <see cref="LuiReadOnlyException"/>. The file decides what may happen,
/// not the index and not a switch in the options.
/// </summary>
public sealed class ReadOnlyCatalogTests
{
    [Fact]
    public void A_read_only_catalog_reads_and_searches()
    {
        using var database = new TestDatabase();
        using (var writer = database.Open())
        {
            writer.Create("tags", index => index.SingleWord()).Upsert(1, "vegan");
            writer.Create("titles", index => index.MultiWord()).Upsert(1, "Gedeckter Apfelkuchen");
        }

        using var reader = ReadOnly(database);

        var tags = reader.Index("tags");
        tags.Search("vegan").Hits.ShouldHaveSingleItem().Text.ShouldBe("vegan");
        tags.Count().ShouldBe(1);

        var titles = reader.Index("titles");
        titles.Search("apfelkuchen").Hits.ShouldHaveSingleItem().Text.ShouldBe("Gedeckter Apfelkuchen");
    }

    [Fact]
    public void A_read_only_catalog_writes_nothing()
    {
        using var database = new TestDatabase();
        using (var writer = database.Open(new LuiCatalogOptions { WriteAheadLog = false }))
        {
            writer.Create("tags", index => index.SingleWord()).Upsert(1, "vegan");
        }

        // the file is in the mode "delete": a read-only catalog may not switch it to write-ahead logging
        var mode = JournalMode(database);
        var tables = database.Scalar<long>("SELECT count(*) FROM sqlite_master;");

        using (var reader = ReadOnly(database))
        {
            reader.Index("tags").Search("vegan").Hits.ShouldHaveSingleItem();
        }

        JournalMode(database).ShouldBe(mode, "the read path does not switch the file to write-ahead logging");
        File.Exists(database.FilePath + "-wal").ShouldBeFalse("the read path leaves no write-ahead log behind");
        database.Scalar<long>("SELECT count(*) FROM sqlite_master;").ShouldBe(tables, "no table was created");
    }

    [Fact]
    public void A_read_only_catalog_leaves_the_meta_table_alone()
    {
        using var database = new TestDatabase();
        using (var writer = database.Open())
        {
            writer.Create("tags", index => index.SingleWord());
        }

        var rows = database.Scalar<long>("SELECT count(*) FROM lui_meta;");

        using (var reader = ReadOnly(database))
        {
            reader.Index("tags").ShouldNotBeNull();
        }

        database.Scalar<long>("SELECT count(*) FROM lui_meta;").ShouldBe(rows, "no row of lui_meta was written");
    }

    [Fact]
    public void A_write_in_a_read_only_catalog_is_refused()
    {
        using var database = new TestDatabase();
        using (var writer = database.Open())
        {
            writer.Create("tags", index => index.SingleWord()).Upsert(1, "vegan");
        }

        using var reader = ReadOnly(database);
        var index = reader.Index("tags");

        var exception = Should.Throw<LuiReadOnlyException>(() => index.Upsert(2, "vegetarisch"));

        exception.Operation.ShouldBe("writing an entry");
        exception.Message.ShouldContain("Mode=ReadOnly");
        index.Count().ShouldBe(1, "the entry was not written");
    }

    [Fact]
    public void Creating_an_index_in_a_read_only_catalog_is_refused()
    {
        using var database = new TestDatabase();
        using (var writer = database.Open())
        {
            writer.Create("tags", index => index.SingleWord());
        }

        using var reader = ReadOnly(database);

        var exception = Should.Throw<LuiReadOnlyException>(() => reader.Create("other", index => index.SingleWord()));

        exception.Operation.ShouldBe("creating an index");
        database.Scalar<long>("SELECT count(*) FROM lui_meta;").ShouldBe(1, "the index was not created");
    }

    [Fact]
    public void Dropping_an_index_in_a_read_only_catalog_is_refused()
    {
        using var database = new TestDatabase();
        using (var writer = database.Open())
        {
            writer.Create("tags", index => index.SingleWord());
        }

        using var reader = ReadOnly(database);

        var exception = Should.Throw<LuiReadOnlyException>(() => reader.DropIndex("tags"));

        exception.Operation.ShouldBe("dropping an index");
        database.Scalar<long>("SELECT count(*) FROM lui_meta;").ShouldBe(1, "the index is still there");
    }

    [Fact]
    public void Syncing_an_index_in_a_read_only_catalog_is_refused()
    {
        using var database = new TestDatabase();
        using (var writer = database.Open())
        {
            writer.Create("tags", index => index.SingleWord());
        }

        using var reader = ReadOnly(database);
        var index = reader.Index("tags");

        var exception = Should.Throw<LuiReadOnlyException>(
            () => index.Sync([new LuiEntry(1, "vegan")]));

        exception.Operation.ShouldBe("syncing an index");
        index.Count().ShouldBe(0, "nothing was indexed");
    }

    [Fact]
    public void A_read_only_catalog_without_the_meta_table_says_so()
    {
        using var database = new TestDatabase();
        using (var connection = database.Connect())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TABLE something (id INTEGER);";
            command.ExecuteNonQuery();
        }

        var exception = Should.Throw<LuiReadOnlyException>(() => ReadOnly(database));

        exception.Message.ShouldContain("lui_meta");
    }

    /// <summary>The catalog of the file that may not be written — the same file, a connection string that says so.</summary>
    private static LuiCatalog ReadOnly(TestDatabase database) =>
        LuiCatalog.Open($"Data Source={database.FilePath};Mode=ReadOnly");

    /// <summary>The mode of the write-ahead log of the file: "delete" or "wal".</summary>
    private static string JournalMode(TestDatabase database)
    {
        using var connection = database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";
        return (string)command.ExecuteScalar()!;
    }
}
