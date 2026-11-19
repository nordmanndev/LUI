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
        using (var connection = catalog.Database.OpenConnection())
        using (var refuse = connection.CreateCommand())
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

    [Fact]
    public void OpenReadOnly_and_OpenOrCreate_on_a_file_that_is_no_database_say_so_and_leave_it_alone()
    {
        using var database = new TestDatabase();
        File.WriteAllText(database.FilePath, "this is not a SQLite database, and it is long enough to have a header of its own");
        var before = Hash(database);

        var readOnly = Should.Throw<LuiCatalogNotFoundException>(() => LuiCatalog.OpenReadOnly(database.FilePath));
        var create = Should.Throw<LuiCatalogNotFoundException>(() => LuiCatalog.OpenOrCreate(database.FilePath));

        readOnly.Message.ShouldContain("no SQLite database");
        create.Message.ShouldContain("no SQLite database");
        create.Message.ShouldContain("cannot set it up", customMessage: "OpenOrCreate does not send the caller to OpenOrCreate");
        Hash(database).ShouldBe(before);
    }

    [Fact]
    public void Open_and_OpenReadOnly_on_a_catalogue_in_write_ahead_logging_leave_the_main_file_alone()
    {
        using var database = new TestDatabase();
        MakeCatalogue(database, inDeleteMode: false);
        var before = Hash(database);

        using (LuiCatalog.Open(database.FilePath))
        {
        }

        using (var reader = LuiCatalog.OpenReadOnly(database.FilePath))
        {
            reader.Index("tags").Search("vegan").Hits.ShouldHaveSingleItem();
        }

        Hash(database).ShouldBe(before);
        database.Scalar<string>("PRAGMA journal_mode").ShouldBe("wal");
    }

    [Fact]
    public void Every_write_of_a_catalogue_opened_with_OpenReadOnly_is_refused_by_SQLite_and_translated()
    {
        using var database = new TestDatabase();
        MakeCatalogue(database, inDeleteMode: false);
        using (var connection = database.Connect())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TABLE tag(id INTEGER PRIMARY KEY, name TEXT NOT NULL); INSERT INTO tag VALUES (1, 'vegan');";
            command.ExecuteNonQuery();
        }

        using (var writer = LuiCatalog.OpenOrCreate(database.FilePath))
        {
            writer.CreateLinked("linked", index => index.SingleWord().LinkTo("tag", "id", "name"));
        }

        SqliteConnection.ClearAllPools();

        using var reader = LuiCatalog.OpenReadOnly(database.FilePath);
        var tags = reader.Index("tags");
        var linked = reader.LinkedIndex("linked");
        var writes = new Dictionary<string, Action>(StringComparer.Ordinal)
        {
            ["Upsert"] = () => tags.Upsert(2, "vegetarisch"),
            ["UpsertMany"] = () => tags.UpsertMany([new LuiEntry(3, "vollkorn")]),
            ["Sync"] = () => tags.Sync([new LuiEntry(4, "bio")]),
            ["Remove"] = () => tags.Remove(1),
            ["UpdateWeight"] = () => tags.UpdateWeight(1, 5),
            ["UpdatePriority"] = () => tags.UpdatePriority(1, LuiPriority.High),
            ["Rebuild"] = tags.Rebuild,
            ["Optimize"] = tags.Optimize,
            ["Check"] = () => tags.Check(),
            ["Create"] = () => reader.Create("other", index => index.SingleWord()),
            ["DropIndex"] = () => reader.DropIndex("tags"),
            ["linked Sync"] = () => linked.Sync(),
            ["linked Optimize"] = linked.Optimize,
            ["linked Check"] = () => linked.Check(),
        };

        foreach (var (name, write) in writes)
        {
            var exception = Should.Throw<LuiReadOnlyException>(write, name);

            exception.InnerException.ShouldBeOfType<SqliteException>(name).SqliteErrorCode.ShouldBe(8, name);
            exception.Message.ShouldContain("OpenReadOnly", customMessage: name);
        }
    }

    [Fact]
    public void A_transaction_of_the_application_is_refused_by_every_write_that_takes_one()
    {
        using var database = new TestDatabase();
        MakeCatalogue(database, inDeleteMode: false);

        using var reader = LuiCatalog.OpenReadOnly(database.FilePath);
        var index = reader.Index("tags");
        using var connection = database.Connect();
        using var transaction = connection.BeginTransaction();

        Should.Throw<LuiReadOnlyException>(() => index.UpsertMany([new LuiEntry(2, "vegetarisch")], transaction));
        Should.Throw<LuiReadOnlyException>(() => index.Remove(1, transaction));
        Should.Throw<LuiReadOnlyException>(() => index.UpdateWeight(1, 5, transaction));
        Should.Throw<LuiReadOnlyException>(() => index.UpdatePriority(1, LuiPriority.High, transaction));

        transaction.Rollback();
    }

    [Fact]
    public void A_catalogue_in_write_ahead_logging_in_a_directory_that_may_not_be_written_is_refused_when_it_is_opened()
    {
        using var directory = ReadOnlyDirectory(withCatalogue: true);

        var readOnly = Should.Throw<LuiReadOnlyException>(() => LuiCatalog.OpenReadOnly(directory.FilePath));
        var open = Should.Throw<LuiReadOnlyException>(() => LuiCatalog.Open(directory.FilePath));

        readOnly.InnerException.ShouldBeOfType<SqliteException>().SqliteErrorCode.ShouldBe(8);
        readOnly.Message.ShouldContain("'-wal' and '-shm'");
        readOnly.Message.ShouldContain(Path.GetDirectoryName(directory.FilePath)!, customMessage: "the message names the directory");
        open.Message.ShouldContain(Path.GetFileName(directory.FilePath));
    }

    [Fact]
    public void OpenOrCreate_in_a_directory_that_may_not_be_written_says_so()
    {
        using var directory = ReadOnlyDirectory(withCatalogue: false);

        var exception = Should.Throw<LuiReadOnlyException>(() => LuiCatalog.OpenOrCreate(directory.FilePath));

        exception.Message.ShouldContain("may not write to the directory");
        File.Exists(directory.FilePath).ShouldBeFalse();
    }

    [Fact]
    public void A_directory_that_may_not_be_searched_is_a_problem_of_rights_not_a_missing_file()
    {
        using var directory = ReadOnlyDirectory(withCatalogue: true, mode: UnixFileMode.None);

        var exception = Should.Throw<SqliteException>(() => LuiCatalog.Open(directory.FilePath));

        exception.SqliteErrorCode.ShouldBe(14);
    }

    /// <summary>
    /// A directory whose rights are taken away, with the file of a catalogue in it — what a replica or a read-only medium
    /// looks like to a process. Rights bind only a process that is not <c>root</c>, and only where there are Unix modes.
    /// </summary>
    private static RightsDirectory ReadOnlyDirectory(bool withCatalogue, UnixFileMode mode = UnixFileMode.UserRead | UnixFileMode.UserExecute)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows has no Unix modes: the rights of a directory work otherwise there.");
        Assert.SkipWhen(Environment.IsPrivilegedProcess, "A privileged process is not bound by the rights of a directory.");

        return new RightsDirectory(withCatalogue, mode);
    }

    private sealed class RightsDirectory : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"lui-rights-{Guid.NewGuid():N}");

        public RightsDirectory(bool withCatalogue, UnixFileMode mode)
        {
            Directory.CreateDirectory(_directory);
            FilePath = Path.Combine(_directory, "app.db");

            if (withCatalogue)
            {
                using (var catalog = LuiCatalog.OpenOrCreate(FilePath))
                {
                    catalog.Create("tags", index => index.SingleWord()).Upsert(1, "vegan");
                }

                SqliteConnection.ClearAllPools();
            }

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(_directory, mode);
            }
        }

        public string FilePath { get; }

        public void Dispose()
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(_directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            SqliteConnection.ClearAllPools();
            Directory.Delete(_directory, recursive: true);
        }
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
