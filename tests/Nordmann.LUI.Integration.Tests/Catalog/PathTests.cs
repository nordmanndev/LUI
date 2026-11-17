using Microsoft.Data.Sqlite;
using Shouldly;

namespace Nordmann.LUI.Integration.Tests.Catalog;

/// <summary>
/// The catalogue takes a path, not a connection string (ADR-0066): the application names a file, and LUI builds every
/// connection itself — pooling on, a private cache. The keywords of the provider are LUI's, not the application's.
/// </summary>
public sealed class PathTests
{
    [Fact]
    public void A_bare_path_opens_the_catalogue()
    {
        using var database = new TestDatabase();

        using var catalog = LuiCatalog.Open(database.FilePath);

        catalog.Create("tags", index => index.SingleWord()).Upsert(1, "vegan");
        catalog.Index("tags").Search("vegan").Hits.ShouldHaveSingleItem().Text.ShouldBe("vegan");
    }

    [Theory]
    [InlineData("")]
    [InlineData(":memory:")]
    [InlineData("file:app.db?mode=ro")]
    public void A_path_that_is_no_file_path_is_refused(string path)
    {
        var exception = Should.Throw<ArgumentException>(() => LuiCatalog.Open(path));

        exception.ParamName.ShouldBe("path", "the parameter is the path of the file");
    }

    [Fact]
    public void The_connections_lui_builds_pool_and_keep_their_cache_private()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();

        var plan = new SqliteConnectionStringBuilder(catalog.Database.ConnectionString);

        plan.Pooling.ShouldBeTrue("the pooling is LUI's, not the application's");
        plan.Cache.ShouldBe(
            SqliteCacheMode.Private,
            "a shared cache puts the connections of a process into SQLite's shared-cache mode, with table locks");
    }
}
