using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace Nordmann.LUI.Integration.Tests.Hosting;

/// <summary>
/// LUI in a host (ADR-0017, ADR-0034, ADR-0017, ADR-0033): the container holds the catalog and nothing else. An
/// index is not registered and not injected — the application asks the catalog for what it needs.
/// </summary>
public sealed class DependencyInjectionTests
{
    [Fact]
    public void The_catalog_is_the_service_of_the_container()
    {
        using var database = new TestDatabase();
        using var host = Host(database);

        var catalog = host.Services.GetRequiredService<LuiCatalog>();

        catalog.ShouldNotBeNull();
        catalog.ConnectionString.ShouldBe(database.ConnectionString);
    }

    [Fact]
    public void The_container_knows_no_index_at_all()
    {
        using var database = new TestDatabase();
        using var host = Host(database);
        host.Start();

        // ADR-0033: the index is not registered and not injected — the application asks the catalog for it.
        host.Services.GetService<ILuiEmbeddedIndex>().ShouldBeNull();
        host.Services.GetService<ILuiLinkedIndex>().ShouldBeNull();
        host.Services.GetService<ILuiIndex>().ShouldBeNull();
        host.Services.GetKeyedService<ILuiEmbeddedIndex>("tags").ShouldBeNull();
    }

    [Fact]
    public void An_index_the_application_asks_for_is_not_a_service_of_the_container()
    {
        using var database = new TestDatabase();
        using var host = Host(database);
        host.Start();

        var catalog = host.Services.GetRequiredService<LuiCatalog>();
        var tags = catalog.Create("tags", index => index.SingleWord());
        tags.Upsert(1, "vegan");

        // the index lives in the catalog, not in the container
        host.Services.GetService<ILuiEmbeddedIndex>().ShouldBeNull();
        catalog.Index("tags").Count().ShouldBe(1);
    }

    [Fact]
    public void The_settings_of_the_container_are_the_ones_it_was_given()
    {
        // N4: AddLui keeps its settings under a type of its own
        using var database = new TestDatabase();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var host = Host(
            database,
            new LuiCatalogOptions { TimeProvider = clock },
            builder => builder.Services.AddSingleton(new LuiCatalogOptions { TimeProvider = TimeProvider.System }));

        var catalog = host.Services.GetRequiredService<LuiCatalog>();

        catalog.TimeProvider.ShouldBe(clock, "the catalog of the container has the clock of the container");
    }

    [Fact]
    public async Task The_catalog_goes_with_the_host()
    {
        using var database = new TestDatabase();
        LuiCatalog catalog;
        using (var host = Host(database))
        {
            await host.StartAsync();
            catalog = host.Services.GetRequiredService<LuiCatalog>();
            await host.StopAsync();
        }

        Should.Throw<ObjectDisposedException>(() => catalog.Index("words"));
    }

    [Fact]
    public void The_registration_refuses_what_cannot_work()
    {
        var services = new ServiceCollection();
        services.AddLui("Data Source=unused.db");

        Should.Throw<InvalidOperationException>(() => services.AddLui("Data Source=other.db"));
        Should.Throw<ArgumentException>(() => new ServiceCollection().AddLui(" "));
        Should.Throw<ArgumentNullException>(() => new ServiceCollection().AddLui((string)null!));
    }

    private static IHost Host(
        TestDatabase database,
        LuiCatalogOptions? options = null,
        Action<HostApplicationBuilder>? more = null)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Services.AddLui(database.ConnectionString, options);
        more?.Invoke(builder);
        return builder.Build();
    }
}
