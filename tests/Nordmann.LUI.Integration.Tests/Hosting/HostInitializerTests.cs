using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace Nordmann.LUI.Integration.Tests.Hosting;

/// <summary>
/// The start of the search in a host (ADR-0076, issue #266; the tests of the warm-up service of issue #68, ADR-0043, that
/// <c>catalog.Initialize()</c> replaced): the application's own initializer calls <c>Initialize()</c> — it warms the typo
/// dictionaries, applies the outbox of a linked index and brings the first sync in —, and without that call nothing happens: the
/// library starts nothing on its own. The rounds of the outbox on an interval are <c>AddLuiOutbox</c> (<c>OutboxTests</c>).
/// </summary>
public sealed class HostInitializerTests
{
    private const string Table = """
        CREATE TABLE tag (
            id          INTEGER PRIMARY KEY,
            name        TEXT,
            usage_count INTEGER NOT NULL DEFAULT 0);
        """;

    private static readonly LuiIndexDefinition<long> TypoTags = new("tags", index => index.SingleWord().TypoTolerance());

    private static readonly LuiLinkedIndexDefinition<long> LinkedTags = new("tags", index => index.SingleWord().LinkTo("tag", "id", "name"));

    [Fact]
    public async Task The_initializer_warms_the_dictionary_of_every_index_at_its_start()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create(TypoTags);
        tags.Upsert(1, "vegan");

        using var host = Host(database, new LuiInitializeOptions());
        await host.StartAsync(TestContext.Current.CancellationToken);

        // the dictionary belongs to the index of the catalog — the one of the host, not the one of the test
        host.Services.GetRequiredService<LuiCatalog>().Index(TypoTags).TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.Current);
    }

    [Fact]
    public async Task The_initializer_applies_the_outbox_of_a_linked_index()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        using var catalog = database.Open();
        var tags = catalog.Create(LinkedTags);
        tags.CreateTriggers();
        Insert(database, (1, "vegan", 0));
        tags.Pending().ShouldBe(1, "the trigger noted the row in the outbox");

        using var host = Host(database, new LuiInitializeOptions { InitialSync = false });
        await host.StartAsync(TestContext.Current.CancellationToken);

        var tagsOfTheHost = host.Services.GetRequiredService<LuiCatalog>().Index(LinkedTags);
        tagsOfTheHost.Pending().ShouldBe(0, "the outbox is empty — the initializer brought the row in");
        tagsOfTheHost.Search("vegan").Hits.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Without_an_initializer_nothing_happens()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        using var catalog = database.Open();
        var tags = catalog.Create(new LuiIndexDefinition<long>("tags", index => index.SingleWord().TypoTolerance()));
        tags.Upsert(1, "vegan");
        var linked = catalog.Create(new LuiLinkedIndexDefinition<long>("linked", index => index.SingleWord().LinkTo("tag", "id", "name")));
        linked.CreateTriggers();
        Insert(database, (1, "vegan", 0));

        using var host = Host(database, options: null);
        await host.StartAsync(TestContext.Current.CancellationToken);

        var catalogOfTheHost = host.Services.GetRequiredService<LuiCatalog>();
        catalogOfTheHost.Index(TypoTags).TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.NotBuilt);
        catalogOfTheHost.Index(new LuiLinkedIndexDefinition<long>("linked", index => index.SingleWord().LinkTo("tag", "id", "name"))).Pending().ShouldBe(1, "the outbox waits for the application");
    }

    [Fact]
    public async Task The_initializer_builds_only_the_dictionaries_of_the_indexes_it_is_told_to()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var names = new LuiIndexDefinition<long>("names", index => index.SingleWord().TypoTolerance());

        // an index that forgives nothing has no dictionary at all — not even when the initializer is told to build it
        var plain = new LuiIndexDefinition<long>("plain", index => index.SingleWord());
        catalog.Create(TypoTags).Upsert(1, "vegan");
        catalog.Create(names).Upsert(1, "Vera");
        catalog.Create(plain).Upsert(1, "nothing");

        // a dictionary costs memory — the index whose queries correct no typos does not get one (ADR-0043)
        using var host = Host(database, new LuiInitializeOptions { Indexes = [TypoTags, plain] });
        await host.StartAsync(TestContext.Current.CancellationToken);

        var catalogOfTheHost = host.Services.GetRequiredService<LuiCatalog>();
        catalogOfTheHost.Index(TypoTags).TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.Current);
        catalogOfTheHost.Index(names).TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.NotBuilt);
        catalogOfTheHost.Index(plain).TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.NotBuilt, "a distance of None builds no dictionary");
    }

    [Fact]
    public async Task Without_the_dictionaries_the_initializer_still_applies_the_outbox()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        using var catalog = database.Open();
        var tags = catalog.Create(LinkedTags);
        tags.CreateTriggers();
        Insert(database, (1, "vegan", 0));

        using var host = Host(database, new LuiInitializeOptions { WarmUpTypoDictionaries = false });
        await host.StartAsync(TestContext.Current.CancellationToken);

        var tagsOfTheHost = host.Services.GetRequiredService<LuiCatalog>().Index(LinkedTags);
        tagsOfTheHost.Pending().ShouldBe(0, "the outbox is the initializer's either way");
        tagsOfTheHost.TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.NotBuilt);
    }

    [Fact]
    public async Task An_index_the_catalog_does_not_have_fails_the_start_of_the_host()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create(new LuiIndexDefinition<long>("tags", index => index.SingleWord())).Upsert(1, "vegan");

        using var host = Host(database, new LuiInitializeOptions { Indexes = [new LuiIndexDefinition<long>("tgas", index => index.SingleWord())] });

        var failure = await Should.ThrowAsync<LuiIndexNotFoundException>(() => host.StartAsync(TestContext.Current.CancellationToken));
        failure.Message.ShouldContain("tgas");
    }

    private static IHost Host(TestDatabase database, LuiInitializeOptions? options)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Services.AddLui(_ => LuiCatalog.OpenOrCreate(database.FilePath));
        if (options is not null)
        {
            builder.Services.AddSingleton(options);
            builder.Services.AddHostedService<Initializer>();
        }

        return builder.Build();
    }

    /// <summary>The application's own initializer: it calls <c>Initialize()</c> when the host starts, and a failure stops the start.</summary>
    private sealed class Initializer(LuiCatalog catalog, LuiInitializeOptions options) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.Run(() => catalog.Initialize(options, cancellationToken), cancellationToken);

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static void Run(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void Run(TestDatabase database, string sql)
    {
        using var connection = database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void Insert(TestDatabase database, params (long Id, string? Name, int Weight)[] rows)
    {
        using var connection = database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO tag(id, name, usage_count) VALUES ($id, $name, $weight);";
        var id = command.Parameters.Add("$id", SqliteType.Integer);
        var name = command.Parameters.Add("$name", SqliteType.Text);
        var weight = command.Parameters.Add("$weight", SqliteType.Integer);
        foreach (var row in rows)
        {
            id.Value = row.Id;
            name.Value = (object?)row.Name ?? DBNull.Value;
            weight.Value = row.Weight;
            command.ExecuteNonQuery();
        }
    }
}
