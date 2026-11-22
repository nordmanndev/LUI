using Shouldly;

namespace Nordmann.LUI.Integration.Tests.Catalog;

/// <summary>
/// The LUI format version (ADR-0073, issue #270): format 2 is the format of 0.4.0, and a file of another format is rejected
/// when the catalog is <b>opened</b> — not with a misleading "another definition" when the first index is used. The format
/// is <c>lui_meta.schema_version</c>, not SQLite's own schema counter. 0.4.0 does not read the files of 0.3.0 (format 1) and
/// migrates nothing (#258).
/// </summary>
public sealed class FormatVersionTests
{
    [Fact]
    public void A_new_index_is_written_in_format_2()
    {
        using var database = new TestDatabase();
        using (var catalog = database.Open())
        {
            catalog.Create(new LuiIndexDefinition<long>("tags", index => index.SingleWord()));
        }

        database.Scalar<long>("SELECT schema_version FROM lui_meta WHERE index_name = 'tags';").ShouldBe(2);
    }

    [Theory]
    [InlineData("Open")]
    [InlineData("OpenReadOnly")]
    [InlineData("OpenOrCreate")]
    public void A_file_of_format_1_is_rejected_on_open_with_the_message(string way)
    {
        using var database = AFileOfFormat(1);

        var exception = Should.Throw<LuiFormatException>(() => OpenWay(way, database.FilePath));

        exception.Message.ShouldBe(
            $"The file '{Path.GetFileName(database.FilePath)}' was written by LUI 0.3.0 (format 1); this version reads format 2. Recreate the indexes.");
        exception.FoundFormat.ShouldBe(1);
        exception.SupportedFormat.ShouldBe(2);
        exception.Path.ShouldBe(Path.GetFullPath(database.FilePath));
        exception.ShouldBeAssignableTo<LuiException>();
        exception.ShouldNotBeAssignableTo<LuiDefinitionMismatchException>();
    }

    [Theory]
    [InlineData("Open", 3)]
    [InlineData("OpenReadOnly", 3)]
    [InlineData("OpenOrCreate", 99)]
    public void A_file_of_a_newer_format_is_rejected_on_open(string way, int format)
    {
        using var database = AFileOfFormat(format);

        var exception = Should.Throw<LuiFormatException>(() => OpenWay(way, database.FilePath));

        exception.Message.ShouldBe(
            $"The file '{Path.GetFileName(database.FilePath)}' was written by a newer version of LUI (format {format}); this version reads format 2. "
            + "Open it with the version that wrote it, or recreate the indexes.");
        exception.FoundFormat.ShouldBe(format);
        exception.SupportedFormat.ShouldBe(2);
    }

    [Fact]
    public void A_file_with_one_index_of_another_format_is_rejected_as_a_whole()
    {
        using var database = new TestDatabase();
        using (var catalog = database.Open())
        {
            catalog.Create(
                new LuiIndexDefinition<long>("first", index => index.SingleWord()),
                new LuiIndexDefinition<long>("second", index => index.SingleWord()));
        }

        Run(database, "UPDATE lui_meta SET schema_version = 1 WHERE index_name = 'second';");

        Should.Throw<LuiFormatException>(() => LuiCatalog.Open(database.FilePath)).FoundFormat.ShouldBe(1);
    }

    [Fact]
    public void The_rejection_writes_nothing_and_leaves_the_file_as_it_was()
    {
        using var database = AFileOfFormat(1);
        var before = database.Scalar<string>("SELECT group_concat(index_name || ':' || schema_version || ':' || definition, '|') FROM lui_meta;");

        Should.Throw<LuiFormatException>(() => LuiCatalog.OpenOrCreate(database.FilePath));
        Should.Throw<LuiFormatException>(() => LuiCatalog.Open(database.FilePath));

        File.Exists(database.FilePath).ShouldBeTrue("a file that LUI did not create is not deleted");
        database.Scalar<string>("SELECT group_concat(index_name || ':' || schema_version || ':' || definition, '|') FROM lui_meta;").ShouldBe(before);
    }

    [Fact]
    public void The_format_is_checked_when_the_catalog_opens_not_when_an_index_is_used()
    {
        using var database = AFileOfFormat(1);

        // the call that used to say "another definition" for the first index is not reached: the catalog does not open
        Should.Throw<LuiFormatException>(() => LuiCatalog.Open(database.FilePath));
    }

    [Fact]
    public void A_catalog_without_an_index_has_no_format_to_reject_and_a_file_of_the_current_format_opens()
    {
        using var database = new TestDatabase();
        database.Open().Dispose();

        Should.NotThrow(() => LuiCatalog.Open(database.FilePath).Dispose());

        using (var catalog = database.Open())
        {
            catalog.Create(new LuiIndexDefinition<long>("tags", index => index.SingleWord()));
        }

        Should.NotThrow(() => LuiCatalog.Open(database.FilePath).Dispose());
        Should.NotThrow(() => LuiCatalog.OpenReadOnly(database.FilePath).Dispose());
        Should.NotThrow(() => LuiCatalog.OpenOrCreate(database.FilePath).Dispose());
    }

    [Fact]
    public void An_index_of_another_format_that_appears_after_the_open_is_still_a_mismatch_of_that_index()
    {
        // the check at open is the front door; an index that is read from a file that changed under an open catalog keeps its own check.
        // A change of a definition comes with a change of the schema of the file — here the table of another version, created
        // beside — and the catalog notices it at the schema counter (ADR-0075): the handle's first operation throws
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create(new LuiIndexDefinition<long>("tags", index => index.SingleWord()));
        Run(database, "UPDATE lui_meta SET schema_version = 1 WHERE index_name = 'tags'; CREATE TABLE written_by_another_version(id INTEGER PRIMARY KEY);");

        var exception = Should.Throw<LuiDefinitionMismatchException>(
            () => catalog.Index(new LuiIndexDefinition<long>("tags", index => index.SingleWord())).Count());

        exception.IndexName.ShouldBe("tags");
        exception.Message.ShouldContain("format 1");
        exception.Message.ShouldContain("format 2");
        exception.Message.ShouldNotContain("schema version", Case.Insensitive);
    }

    private static LuiCatalog OpenWay(string way, string path) => way switch
    {
        "Open" => LuiCatalog.Open(path),
        "OpenReadOnly" => LuiCatalog.OpenReadOnly(path),
        _ => LuiCatalog.OpenOrCreate(path),
    };

    /// <summary>A file with an index whose row says <paramref name="format"/> — the one place that writes <c>lui_meta</c> by hand, to stand for a file of another version.</summary>
    private static TestDatabase AFileOfFormat(int format)
    {
        var database = new TestDatabase();
        using (var catalog = database.Open())
        {
            catalog.Create(new LuiIndexDefinition<long>("tags", index => index.SingleWord())).Upsert(1, "vegan");
        }

        Run(database, $"UPDATE lui_meta SET schema_version = {format};");
        return database;
    }

    private static void Run(TestDatabase database, string sql)
    {
        using var connection = database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
