using Microsoft.Data.Sqlite;
using Shouldly;

namespace Nordmann.LUI.Integration.Tests.Catalog;

/// <summary>
/// The connection of the read path (ADR-0067): it is read-only — whichever way the catalogue was opened — it carries the
/// three pragmas of a connection and no journal mode, and it goes back to the pool of the provider with no transaction
/// open. SQLite says that it cannot write, not a convention.
/// </summary>
public sealed class ReadConnectionTests
{
    public enum Way
    {
        Open,
        OpenOrCreate,
        OpenReadOnly,
    }

    [Theory]
    [InlineData(Way.Open)]
    [InlineData(Way.OpenOrCreate)]
    [InlineData(Way.OpenReadOnly)]
    public void The_connection_of_the_read_path_is_read_only_whichever_way_the_catalogue_was_opened(Way way)
    {
        using var database = new TestDatabase();
        MakeCatalogue(database);
        using var catalog = OpenWith(database, way);

        using var connection = catalog.Database.OpenReadConnection();

        var exception = Should.Throw<SqliteException>(() => Execute(connection, "CREATE TABLE written_by_a_read(x INTEGER);"));
        exception.SqliteErrorCode.ShouldBe(8, "SQLITE_READONLY: the mode of the connection says so");
        Scalar(connection, "PRAGMA query_only;").ShouldBe(0, "it is the mode of the connection, not a pragma that refuses");
    }

    [Fact]
    public void A_query_that_wanted_to_write_fails_with_SQLITE_READONLY()
    {
        using var database = new TestDatabase();
        MakeCatalogue(database);
        using var catalog = LuiCatalog.Open(database.FilePath);

        using var connection = catalog.Database.OpenReadConnection();

        Should.Throw<SqliteException>(() => Execute(connection, "DELETE FROM lui_meta;")).SqliteErrorCode.ShouldBe(8);
        database.Scalar<long>("SELECT count(*) FROM lui_meta;").ShouldBe(1, "nothing was written");
    }

    [Fact]
    public void The_pragmas_of_the_read_connection_run_without_an_error_and_read_back_and_carry_no_journal_mode()
    {
        using var database = new TestDatabase();
        MakeCatalogue(database);
        var options = new LuiCatalogOptions
        {
            Pragmas = new LuiPragmas
            {
                BusyTimeout = TimeSpan.FromSeconds(2),
                Synchronous = LuiSynchronous.Full,
                CacheSizeMegabytes = 64,
            },
        };
        using var catalog = LuiCatalog.Open(database.FilePath, options);

        using var connection = catalog.Database.OpenReadConnection();

        Scalar(connection, "PRAGMA busy_timeout;").ShouldBe(2000);
        Scalar(connection, "PRAGMA synchronous;").ShouldBe(2);
        Scalar(connection, "PRAGMA cache_size;").ShouldBe(-64L * 1024);
        catalog.Database.Pragmas().Contains("journal_mode", StringComparison.OrdinalIgnoreCase).ShouldBeFalse();
    }

    [Fact]
    public void A_query_ends_its_transaction_before_the_connection_goes_back_to_the_pool()
    {
        using var database = new TestDatabase();
        MakeCatalogue(database);
        using var catalog = LuiCatalog.Open(database.FilePath);
        var tags = catalog.Index("tags");

        tags.Search("vegan").Hits.ShouldHaveSingleItem();

        // the connection the query used is the one the pool hands out next: it must be able to begin a transaction
        using var connection = catalog.Database.OpenReadConnection();
        using var transaction = connection.BeginTransaction(deferred: true);
        transaction.Rollback();
    }

    // a guard, not a proof of the change: SQLite falls back to read-only on a file without rights in the mode ReadWrite as
    // well — what this keeps is that the read path answers from such a file whichever way the catalogue was opened
    [Fact]
    public void A_file_that_may_not_be_written_answers_a_query()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows has no Unix modes.");
        Assert.SkipWhen(Environment.IsPrivilegedProcess, "A privileged process is not bound by the rights of a file.");

        using var database = new TestDatabase();
        MakeCatalogue(database, journalMode: "DELETE");

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(database.FilePath, UnixFileMode.UserRead);
        }

        try
        {
            using var catalog = LuiCatalog.Open(database.FilePath);

            catalog.Index("tags").Search("vegan").Hits.ShouldHaveSingleItem();
            using var connection = catalog.Database.OpenReadConnection();
            Should.Throw<SqliteException>(() => Execute(connection, "DELETE FROM lui_meta;")).SqliteErrorCode.ShouldBe(8);
        }
        finally
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(database.FilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
    }

    [Fact]
    public void A_catalogue_that_wrote_and_was_read_leaves_its_file_whole_when_it_is_disposed()
    {
        // the writer has to be the LAST connection to close: only a connection that may write checkpoints the write-ahead log
        // into the file and removes the -wal and -shm. If the read connections close after it, the data stays in the -wal
        // and the file alone is an empty database — a copy of it is no catalogue.
        using var database = new TestDatabase();
        var copy = database.FilePath + ".copy";

        using (var catalog = LuiCatalog.OpenOrCreate(database.FilePath))
        {
            var tags = catalog.Create("tags", index => index.SingleWord());
            for (var i = 1; i <= 50; i++)
            {
                tags.Upsert(i, $"tag{i}");
            }

            tags.Search("tag").Hits.ShouldNotBeEmpty();
        }

        try
        {
            File.Exists(database.FilePath + "-wal").ShouldBeFalse("the writer closed last, and checkpointed");
            File.Copy(database.FilePath, copy);

            using var reader = LuiCatalog.OpenReadOnly(copy);

            reader.Index("tags").Count().ShouldBe(50, "the file alone holds what was written");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(copy);
            File.Delete(copy + "-wal");
            File.Delete(copy + "-shm");
        }
    }

    [Fact]
    public void The_catalogue_holds_no_connection_and_the_read_path_no_pool_and_no_dictionary_of_statements()
    {
        // ADR-0067 and ADR-0070: the pool of the provider is the pool. The catalogue holds no connection at all — not of the
        // read path, not of the writer — and no cache of statements.
        const System.Reflection.BindingFlags all =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static
            | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        var library = typeof(LuiCatalog).Assembly;
        string[] heldByTheRead = ["Nordmann.LUI.Querying.QueryRunner", "Nordmann.LUI.Querying.CandidateReader", "Nordmann.LUI.Querying.HitReader", "Nordmann.LUI.Querying.TypoCorrection"];

        foreach (var name in heldByTheRead)
        {
            var type = library.GetType(name).ShouldNotBeNull(name);
            type.GetFields(all).Where(field => typeof(SqliteConnection).IsAssignableFrom(field.FieldType)).ShouldBeEmpty(name + " holds no connection");
            type.GetFields(all).Where(field => typeof(System.Collections.IDictionary).IsAssignableFrom(field.FieldType)).ShouldBeEmpty(name + " keeps no dictionary of statements");
        }

        var database = library.GetType("Nordmann.LUI.Storage.Database").ShouldNotBeNull();
        database.GetFields(all).Where(field => typeof(SqliteConnection).IsAssignableFrom(field.FieldType)).Select(field => field.Name)
            .ShouldBeEmpty("the catalogue holds no connection: every operation takes one from the pool of the provider and gives it back");
        database.GetFields(all).Where(field => typeof(System.Collections.IDictionary).IsAssignableFrom(field.FieldType)).ShouldBeEmpty();
    }

    private static LuiCatalog OpenWith(TestDatabase database, Way way) => way switch
    {
        Way.Open => LuiCatalog.Open(database.FilePath),
        Way.OpenOrCreate => LuiCatalog.OpenOrCreate(database.FilePath),
        _ => LuiCatalog.OpenReadOnly(database.FilePath),
    };

    private static void MakeCatalogue(TestDatabase database, string journalMode = "WAL")
    {
        using (var catalog = LuiCatalog.OpenOrCreate(database.FilePath))
        {
            catalog.Create("tags", index => index.SingleWord()).Upsert(1, "vegan");
        }

        SqliteConnection.ClearAllPools();
        if (!string.Equals(journalMode, "WAL", StringComparison.Ordinal))
        {
            using var connection = database.Connect();
            Execute(connection, $"PRAGMA journal_mode = {journalMode};");
            SqliteConnection.ClearAllPools();
        }
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static long Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }
}
