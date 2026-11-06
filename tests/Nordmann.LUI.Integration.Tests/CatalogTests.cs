using Shouldly;

namespace Nordmann.LUI.Integration.Tests;

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
    public void A_key_that_is_neither_long_nor_string_is_refused()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();

        Should.Throw<NotSupportedException>(
            () => catalog.Create("tags", index => index.SingleWord().WithKey<Guid>()));
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
        Should.Throw<ObjectDisposedException>(() => catalog.Index<long, LuiNoPayload>("other"));
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
        var blocking = new BlockingSerializer();
        var tags = catalog.Create("tags", index => index.SingleWord().WithPayload(blocking));
        tags.Upsert(new(1, "vegan") { Weight = 5, Payload = new Chip("vegan") });

        var query = Task.Run(() => tags.Query("vegan").Execute());
        blocking.Entered.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue("the query reached the payload");

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

    private sealed class BlockingSerializer : ILuiPayloadSerializer<Chip>
    {
        internal ManualResetEventSlim Entered { get; } = new();

        internal ManualResetEventSlim Release { get; } = new();

        public byte[] Serialize(Chip payload) => System.Text.Encoding.UTF8.GetBytes(payload.Slug);

        public Chip Deserialize(ReadOnlySpan<byte> bytes)
        {
            Entered.Set();
            Release.Wait(TimeSpan.FromSeconds(30));
            return new Chip(System.Text.Encoding.UTF8.GetString(bytes));
        }
    }
}
