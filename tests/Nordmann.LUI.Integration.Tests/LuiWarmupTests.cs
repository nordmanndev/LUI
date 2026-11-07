using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;

namespace Nordmann.LUI.Integration.Tests;

/// <summary>
/// The warm-up as a service of the host (issue #68, ADR-0043): <c>AddLuiWarmup()</c> warms the typo dictionaries at
/// the start of the host and applies the outbox of a linked index on an interval — and only when the application asks
/// for it. The library starts nothing on its own: the host runs the service, and the service lives as long as the host.
/// </summary>
public sealed class LuiWarmupTests
{
    private const string Table = """
        CREATE TABLE tag (
            id          INTEGER PRIMARY KEY,
            name        TEXT,
            usage_count INTEGER NOT NULL DEFAULT 0);
        """;

    [Fact]
    public async Task The_host_warms_the_dictionary_of_every_index_at_its_start()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        tags.Upsert(1, "vegan");

        using var host = Host(database, warmup: true);
        await host.StartAsync();

        // the dictionary belongs to the index of the catalog — the one of the host, not the one of the test
        host.Services.GetRequiredService<LuiCatalog>()
            .Index("tags")
            .TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.Current);
    }

    [Fact]
    public async Task The_host_applies_the_outbox_of_a_linked_index()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord().LinkTo("tag", "id", "name"));
        tags.CreateTriggers();
        Insert(database, (1, "vegan", 0));
        tags.Pending().ShouldBe(1, "the trigger noted the row in the outbox");

        using var host = Host(database, warmup: true);
        await host.StartAsync();

        var tagsOfTheHost = host.Services.GetRequiredService<LuiCatalog>().LinkedIndex("tags");
        tagsOfTheHost.Pending().ShouldBe(0, "the outbox is empty — the warm-up brought the row in");
        tagsOfTheHost.Search("vegan").Hits.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Without_the_registration_nothing_happens()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        tags.Upsert(1, "vegan");
        var linked = catalog.Create("linked", index => index.SingleWord().LinkTo("tag", "id", "name"));
        linked.CreateTriggers();
        Insert(database, (1, "vegan", 0));

        using var host = Host(database, warmup: false);
        await host.StartAsync();

        var catalogOfTheHost = host.Services.GetRequiredService<LuiCatalog>();
        catalogOfTheHost.Index("tags").TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.NotBuilt);
        catalogOfTheHost.LinkedIndex("linked").Pending().ShouldBe(1, "the outbox waits for the application");
    }

    private static IHost Host(TestDatabase database, bool warmup)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Services.AddLui(database.ConnectionString);
        if (warmup)
        {
            builder.Services.AddLuiWarmup();
        }

        return builder.Build();
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
