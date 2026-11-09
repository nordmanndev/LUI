using Shouldly;

namespace Nordmann.LUI.Integration.Tests.Catalog;

/// <summary>
/// <c>LuiCatalog</c>: opening, the meta table, and the lifetime
/// (LUI-Spezifikation §7.1, §7.5, §15).
/// </summary>
public sealed class CatalogTests
{
    [Fact]
    public void Open_creates_the_meta_table()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();

        database.Scalar<long>("SELECT count(*) FROM sqlite_master WHERE name = 'lui_meta'").ShouldBe(1);
    }

    [Fact]
    public void Open_puts_the_database_into_wal_mode()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();

        database.Scalar<string>("PRAGMA journal_mode").ShouldBe("wal");
    }

    [Fact]
    public void Open_twice_on_the_same_database_is_fine()
    {
        using var database = new TestDatabase();
        using var first = database.Open();
        using var second = database.Open();

        first.ConnectionString.ShouldBe(second.ConnectionString);
    }

    [Fact]
    public void The_sqlite_that_is_loaded_is_new_enough()
    {
        using var database = new TestDatabase();

        Version.Parse(database.Scalar<string>("SELECT sqlite_version()"))
            .ShouldBeGreaterThanOrEqualTo(LuiCatalog.MinimumSqliteVersion);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Recipes")]
    [InlineData("1recipes")]
    [InlineData("re-cipes")]
    [InlineData("rezepte; drop table lui_meta")]
    [InlineData("a123456789012345678901234567890123456789012")]
    public void A_name_that_does_not_match_the_pattern_is_refused(string name)
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();

        var exception = Should.Throw<ArgumentException>(() => catalog.Create(name, index => index.SingleWord()));
        exception.Message.ShouldContain("^[a-z][a-z0-9_]{0,40}$");
    }

    [Theory]
    [InlineData("meta")]
    [InlineData("tags_fts")]
    [InlineData("tags_fts_data")]
    [InlineData("tags_rowid")]
    [InlineData("tags_p1")]
    [InlineData("tags_p2")]
    public void A_name_that_could_collide_with_the_objects_of_another_index_is_refused(string name)
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();

        Should.Throw<ArgumentException>(() => catalog.Create(name, index => index.SingleWord())).Message.ShouldContain("reserved");
    }

    [Theory]
    [InlineData("a")]
    [InlineData("recipes")]
    [InlineData("my_tags_2")]
    [InlineData("a1234567890123456789012345678901234567890")]
    public void A_name_that_matches_the_pattern_is_accepted(string name)
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();

        Should.NotThrow(() => catalog.Create(name, index => index.SingleWord()));
    }

    [Fact]
    public void A_kind_of_key_that_is_neither_number_nor_text_is_refused()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();

        Should.Throw<ArgumentOutOfRangeException>(
            () => catalog.Create("tags", index => index.SingleWord().WithKey((LuiKeyKind)2)));
    }

    [Fact]
    public void An_operation_after_dispose_throws_object_disposed()
    {
        using var database = new TestDatabase();
        var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        tags.Upsert(1, "vegan", 5);

        catalog.Dispose();

        Should.Throw<ObjectDisposedException>(() => tags.Query("veg").Execute());
        Should.Throw<ObjectDisposedException>(() => tags.Upsert(2, "vegetarisch", 5));
        Should.Throw<ObjectDisposedException>(() => catalog.Index("other"));
    }

    [Fact]
    public void Dispose_is_idempotent()
    {
        using var database = new TestDatabase();
        var catalog = database.Open();

        catalog.Dispose();

        Should.NotThrow(catalog.Dispose);
    }

    [Fact]
    public async Task Dispose_waits_for_a_query_that_is_running()
    {
        using var database = new TestDatabase();
        var catalog = database.Open();
        var blocking = new BlockingSimilarity();
        var tags = catalog.Create("tags", index => index.SingleWord().Similarity(blocking));
        tags.Upsert(new(1, "vegan") { Weight = 5, Payload = new Chip("vegan") });

        var query = Task.Run(() => tags.Query("vegan").Execute());
        blocking.Entered.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue("the query reached the ranking");

        var dispose = Task.Run(catalog.Dispose);
        (await Task.WhenAny(dispose, Task.Delay(300))).ShouldNotBe(dispose, "Dispose has to wait for the query");

        blocking.Release.Set();
        var result = await query;
        await dispose;

        result.Hits.Count.ShouldBe(1);
    }

    [Fact]
    public void The_clock_of_the_catalog_decides_created_at()
    {
        using var database = new TestDatabase();
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero));
        using var catalog = database.Open(new LuiCatalogOptions { TimeProvider = clock });

        catalog.Create("tags", index => index.SingleWord());

        database.Scalar<string>("SELECT created_at FROM lui_meta WHERE index_name = 'tags'")
            .ShouldStartWith("2026-11-02T12:00:00");
    }

    // review of pull request 6, B6: the journal mode
    [Fact]
    public void A_database_can_stay_in_its_journal_mode()
    {
        using var database = new TestDatabase();
        using (var connection = database.Connect())
        {
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode = DELETE; CREATE TABLE app(id INTEGER PRIMARY KEY);";
            command.ExecuteNonQuery();
        }

        using var catalog = database.Open(new LuiCatalogOptions { WriteAheadLog = false });
        var tags = catalog.Create("tags", index => index.SingleWord());
        tags.Upsert(1, "vegan", 5);

        database.Scalar<string>("PRAGMA journal_mode").ShouldBe("delete");
        tags.Query("vegan").Execute().Hits.Count.ShouldBe(1);
    }

    // review of pull request 6, B6: the journal mode
    [Fact]
    public void A_database_in_rollback_mode_is_switched_to_wal_by_default()
    {
        using var database = new TestDatabase();
        using (var connection = database.Connect())
        {
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode = DELETE; CREATE TABLE app(id INTEGER PRIMARY KEY);";
            command.ExecuteNonQuery();
        }

        using var catalog = database.Open();

        database.Scalar<string>("PRAGMA journal_mode").ShouldBe("wal");
    }

    // review of pull request 6, the version check
    [Fact]
    public void A_sqlite_that_is_too_old_is_named_precisely()
    {
        var exception = Should.Throw<LuiEnvironmentException>(() => LuiCatalog.EnsureSqliteVersion("3.44.0"));
        exception.Message.ShouldContain("3.45");
        exception.Message.ShouldContain("3.44.0");

        Should.NotThrow(() => LuiCatalog.EnsureSqliteVersion("3.45.0"));
        Should.NotThrow(() => LuiCatalog.EnsureSqliteVersion("3.53.3"));
    }

    // the shape of the API before 0.1.0 (ADR-0013), the options
    [Fact]
    public void A_catalog_option_out_of_range_fails_where_it_is_set()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new LuiCatalogOptions { CacheSizeMegabytes = 0 })
            .Message.ShouldContain("CacheSizeMegabytes");
        Should.Throw<ArgumentOutOfRangeException>(() => new LuiCatalogOptions { MemoryMapMegabytes = -1 })
            .Message.ShouldContain("MemoryMapMegabytes");
        Should.Throw<ArgumentOutOfRangeException>(() => new LuiCatalogOptions { BusyTimeout = TimeSpan.FromSeconds(-1) })
            .Message.ShouldContain("BusyTimeout");
        Should.Throw<ArgumentNullException>(() => new LuiCatalogOptions { TimeProvider = null! });

        Should.NotThrow(() => new LuiCatalogOptions { CacheSizeMegabytes = null, MemoryMapMegabytes = 1 });
    }

    // the shape of the API before 0.1.0 (ADR-0013), the options
    [Fact]
    public void The_exception_of_a_wrong_value_names_the_property_as_its_parameter()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new LuiQueryOptions { MaxResults = 0 }).ParamName.ShouldBe("MaxResults");
        Should.Throw<ArgumentOutOfRangeException>(() => new LuiQueryOptions { Candidates = 0 }).ParamName.ShouldBe("Candidates");
        Should.Throw<ArgumentOutOfRangeException>(() => new LuiQueryOptions { Rank = (LuiRankingOrder)42 }).ParamName.ShouldBe("Rank");
        Should.Throw<ArgumentOutOfRangeException>(() => new LuiCatalogOptions { CacheSizeMegabytes = 0 }).ParamName.ShouldBe("CacheSizeMegabytes");
        Should.Throw<ArgumentOutOfRangeException>(() => new LuiCatalogOptions { BusyTimeout = TimeSpan.FromSeconds(-1) }).ParamName.ShouldBe("BusyTimeout");
        Should.Throw<ArgumentNullException>(() => new LuiCatalogOptions { TimeProvider = null! }).ParamName.ShouldBe("TimeProvider");
        Should.Throw<ArgumentOutOfRangeException>(() => new LuiEntry(1, "a") { Weight = -1 }).ParamName.ShouldBe("Weight");
        Should.Throw<ArgumentOutOfRangeException>(() => new LuiEntry(1, "a") { Priority = (LuiPriority)7 }).ParamName.ShouldBe("Priority");
        Should.Throw<ArgumentException>(() => new LuiEntry(1, string.Empty)).ParamName.ShouldBe("Text");
    }

    // the shape of the API before 0.1.0 (ADR-0013), the options
    [Fact]
    public void The_catalog_options_reach_the_connections()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open(new LuiCatalogOptions { CacheSizeMegabytes = 3, BusyTimeout = TimeSpan.FromMilliseconds(1234) });
        using var connection = catalog.Database.OpenConnection();
        using var command = connection.CreateCommand();

        command.CommandText = "PRAGMA cache_size;";
        ((long)command.ExecuteScalar()!).ShouldBe(-3 * 1024);
        command.CommandText = "PRAGMA busy_timeout;";
        ((long)command.ExecuteScalar()!).ShouldBe(1234);
    }

    // the shape of the API before 0.1.0 (ADR-0013), the builder and the interfaces
    [Fact]
    public void The_exceptions_of_the_library_can_be_caught_as_one_and_still_as_an_InvalidOperationException()
    {
        // Review of 2026-11-04, A.8: a caller that separates LUI's conditions from its own errors had no type for it
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create("words", index => index.SingleWord());

        var mismatch = Record.Exception(() => catalog.Create("words", index => index.MultiWord()));

        mismatch.ShouldBeOfType<LuiDefinitionMismatchException>();
        mismatch.ShouldBeAssignableTo<LuiException>();
        mismatch.ShouldBeAssignableTo<InvalidOperationException>("a catch that exists keeps catching it");
        new LuiEnvironmentException("too old").ShouldBeAssignableTo<LuiException>();
        new LuiLinkedTableException("no such table").ShouldBeAssignableTo<LuiException>();
    }

    // the shape of the API before 0.1.0 (ADR-0013), the builder and the interfaces
    [Fact]
    public void A_dropped_index_is_gone_and_a_drop_of_a_missing_one_says_so()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create("tags", index => index.SingleWord()).Upsert(1, "vegan", 5);

        catalog.DropIndex("tags").ShouldBeTrue();
        catalog.DropIndex("tags").ShouldBeFalse();
        Should.Throw<ArgumentException>(() => catalog.DropIndex("Not A Name"));
        database.Scalar<long>("SELECT count(*) FROM sqlite_master WHERE name LIKE 'lui_tags%'").ShouldBe(0);
    }

    /// <summary>
    /// Holds a query in its ranking until the test lets it go. The payload is not read during a query any more
    /// (ADR-0035: the hit reads it when it is asked), so the ranking is where a query is caught running.
    /// </summary>
    private sealed class BlockingSimilarity : ILuiSimilarity
    {
        internal ManualResetEventSlim Entered { get; } = new();

        internal ManualResetEventSlim Release { get; } = new();

        public double Similarity(string query, string text)
        {
            Entered.Set();
            Release.Wait(TimeSpan.FromSeconds(30));
            return 1;
        }
    }
}
