using System.Globalization;
using Microsoft.Data.Sqlite;
using Shouldly;

namespace Nordmann.LUI.Integration.Tests.Catalog;

/// <summary>
/// The settings of the connections of a catalogue (ADR-0066): a typed record, checked when the catalogue is opened, and
/// applied to every connection LUI opens — the writer and the connection of the read path.
/// <para>
/// The three pragmas of a connection are the ones that belong to it: <c>busy_timeout</c>, <c>synchronous</c> and
/// <c>cache_size</c>. The journal mode is <b>not</b> among them — it is a property of the file, and a read-only
/// connection cannot set it (SQLite error 8).
/// </para>
/// </summary>
public sealed class PragmasTests
{
    [Fact]
    public void The_busy_timeout_of_the_writer_is_the_one_the_settings_name()
    {
        using var database = new TestDatabase();
        var options = new LuiCatalogOptions
        {
            Pragmas = new LuiPragmas { BusyTimeout = TimeSpan.FromSeconds(3) },
        };

        using var catalog = database.Open(options);
        using var writer = catalog.Database.OpenConnection();

        options.Pragmas.BusyTimeout.ShouldBe(TimeSpan.FromSeconds(3));
        Scalar(writer, "PRAGMA busy_timeout;").ShouldBe(3000);
    }

    [Fact]
    public void The_synchronous_of_the_writer_is_the_one_the_settings_name()
    {
        using var database = new TestDatabase();
        var options = new LuiCatalogOptions
        {
            Pragmas = new LuiPragmas { Synchronous = LuiSynchronous.Full },
        };

        using var catalog = database.Open(options);
        using var writer = catalog.Database.OpenConnection();

        Scalar(writer, "PRAGMA synchronous;").ShouldBe(2, "FULL is the second value of SQLite");
    }

    [Fact]
    public void The_cache_size_of_the_writer_is_the_one_the_settings_name()
    {
        using var database = new TestDatabase();
        var options = new LuiCatalogOptions
        {
            Pragmas = new LuiPragmas { CacheSizeMegabytes = 512 },
        };

        using var catalog = database.Open(options);
        using var writer = catalog.Database.OpenConnection();

        // a negative cache_size is a size in KiB
        Scalar(writer, "PRAGMA cache_size;").ShouldBe(-512L * 1024);
    }

    [Fact]
    public void The_pragmas_are_read_back_on_the_connection_of_the_read_path()
    {
        using var database = new TestDatabase();
        var options = new LuiCatalogOptions
        {
            Pragmas = new LuiPragmas
            {
                BusyTimeout = TimeSpan.FromSeconds(2),
                Synchronous = LuiSynchronous.Full,
                CacheSizeMegabytes = 64,
            },
        };

        using var catalog = database.Open(options);

        using var connection = catalog.Database.OpenConnection();

        Scalar(connection, "PRAGMA busy_timeout;").ShouldBe(2000);
        Scalar(connection, "PRAGMA synchronous;").ShouldBe(2);
        Scalar(connection, "PRAGMA cache_size;").ShouldBe(-64L * 1024);
    }

    [Fact]
    public void The_pragma_statement_names_the_three_pragmas_and_no_journal_mode()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open(new LuiCatalogOptions { Pragmas = new LuiPragmas { CacheSizeMegabytes = 64 } });

        var statement = catalog.Database.Pragmas();

        statement.ShouldContain("busy_timeout");
        statement.ShouldContain("synchronous");
        statement.ShouldContain("cache_size");
        statement.Contains("journal_mode").ShouldBeFalse(
            "the journal mode is a property of the file, not of a connection");
    }

    [Theory]
    [InlineData(500)]      // below a second
    [InlineData(600_001)]  // above ten minutes
    public void A_busy_timeout_outside_its_bounds_is_refused_when_the_catalog_is_opened(int milliseconds)
    {
        using var database = new TestDatabase();
        var options = new LuiCatalogOptions
        {
            Pragmas = new LuiPragmas { BusyTimeout = TimeSpan.FromMilliseconds(milliseconds) },
        };

        Should.Throw<ArgumentOutOfRangeException>(() => database.Open(options));

        File.Exists(database.FilePath).ShouldBeFalse("the value is refused before a connection is opened");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1025)]
    public void A_cache_size_outside_its_bounds_is_refused_when_the_catalog_is_opened(int megabytes)
    {
        using var database = new TestDatabase();
        var options = new LuiCatalogOptions
        {
            Pragmas = new LuiPragmas { CacheSizeMegabytes = megabytes },
        };

        Should.Throw<ArgumentOutOfRangeException>(() => database.Open(options));

        File.Exists(database.FilePath).ShouldBeFalse("the value is refused before a connection is opened");
    }

    [Fact]
    public void Missing_settings_are_refused_when_the_catalog_is_opened()
    {
        using var database = new TestDatabase();
        var options = new LuiCatalogOptions { Pragmas = null! };

        Should.Throw<ArgumentNullException>(() => database.Open(options));

        File.Exists(database.FilePath).ShouldBeFalse("the settings are refused before the file is touched");
    }

    [Fact]
    public void Two_catalogs_on_one_file_keep_their_own_pragmas_although_they_share_a_pool()
    {
        using var database = new TestDatabase();
        using var normal = database.Open();
        using var full = database.Open(new LuiCatalogOptions { Pragmas = new LuiPragmas { Synchronous = LuiSynchronous.Full } });

        using (var connection = normal.Database.OpenConnection())
        {
            Scalar(connection, "PRAGMA synchronous;").ShouldBe(1, "NORMAL is the first value of SQLite");
        }

        using (var connection = full.Database.OpenConnection())
        {
            Scalar(connection, "PRAGMA synchronous;").ShouldBe(2);
        }

        using (var connection = normal.Database.OpenConnection())
        {
            Scalar(connection, "PRAGMA synchronous;").ShouldBe(1, "the pragmas run on every opened connection");
        }
    }

    /// <summary>Reads one value of a pragma of a connection.</summary>
    private static long Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }
}
