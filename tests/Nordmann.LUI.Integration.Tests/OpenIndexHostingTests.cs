using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;

namespace Nordmann.LUI.Integration.Tests;

/// <summary>
/// The container opens an index instead of creating it (ADR-0033): the index has to exist, and the file is never
/// written — no table, no definition, no write lock.
/// </summary>
public sealed class OpenIndexHostingTests
{
    [Fact]
    public void A_container_that_opens_an_index_does_not_create_it()
    {
        using var database = new TestDatabase();
        using var host = Host(database, lui => lui.AddOpenIndex("words"));

        Should.Throw<LuiIndexNotFoundException>(host.Start).IndexName.ShouldBe("words");
        IndexTables(database).ShouldBe(0, "no table of the index was created");
    }

    [Fact]
    public void A_container_that_opens_an_existing_index_queries_it()
    {
        using var database = new TestDatabase();
        using (var catalog = database.Open())
        {
            catalog.Index("words").SingleWord().Create().Upsert(1, "vegan");
        }

        using var host = Host(database, lui => lui.AddOpenIndex("words"));
        host.Start();

        var index = host.Services.GetRequiredKeyedService<ILuiDocumentIndex>("words");
        index.Count().ShouldBe(1);
        index.Search("veg").ShouldHaveSingleItem().Text.ShouldBe("vegan");
    }

    [Fact]
    public void The_opened_index_gives_the_key_as_the_text_it_is_stored_as()
    {
        using var database = new TestDatabase();
        using (var catalog = database.Open())
        {
            catalog.Index("words").SingleWord().Create().Upsert(42, "vegan");
        }

        using var host = Host(database, lui => lui.AddOpenIndex("words"));
        host.Start();

        var hit = host.Services.GetRequiredKeyedService<ILuiDocumentIndex>("words").Search("veg").ShouldHaveSingleItem();
        hit.Key.ShouldBe("42", "the key of the entry, as the text it is stored as");
        hit.Payload.ShouldBeNull("the index stores no payload");
    }

    [Fact]
    public void An_opened_index_in_the_container_refuses_to_write()
    {
        using var database = new TestDatabase();
        using (var catalog = database.Open())
        {
            catalog.Index("words").SingleWord().Create().Upsert(1, "vegan");
        }

        using var host = Host(database, lui => lui.AddOpenIndex<long, LuiNoPayload>("words"));
        host.Start();

        var index = host.Services.GetRequiredKeyedService<ILuiEmbeddedIndex<long, LuiNoPayload>>("words");
        Should.Throw<NotSupportedException>(() => index.Upsert(2, "vegetarisch"), "an opened index reads and does not write");
        index.Search("veg").Hits.ShouldHaveSingleItem().Text.ShouldBe("vegan");
    }

    [Fact]
    public void An_opened_index_corrects_no_typo()
    {
        using var database = new TestDatabase();
        using (var catalog = database.Open())
        {
            catalog.Index("words").SingleWord().TypoTolerance().Create().Upsert(1, "kartoffel");
        }

        using var host = Host(database, lui => lui.AddOpenIndex("words"));
        host.Start();

        var index = host.Services.GetRequiredKeyedService<ILuiDocumentIndex>("words");
        index.TypoTolerance.ShouldBeFalse("an opened index corrects no typo");
        index.Search("kartofel").ShouldBeEmpty("nothing corrects the typo");
    }

    [Fact]
    public void A_name_can_be_registered_only_once()
    {
        var services = new ServiceCollection();
        var lui = services.AddLui("Data Source=unused.db");

        lui.AddOpenIndex("words");
        Should.Throw<ArgumentException>(() => lui.AddOpenIndex("words"));
        Should.Throw<ArgumentException>(() => lui.AddIndex("words", index => index.SingleWord()));
    }

    private static IHost Host(TestDatabase database, Action<LuiServicesBuilder> indexes)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        indexes(builder.Services.AddLui(database.ConnectionString));
        return builder.Build();
    }

    private static long IndexTables(TestDatabase database) =>
        database.Scalar<long>("SELECT count(*) FROM sqlite_master WHERE name LIKE 'lui_words%';");
}
