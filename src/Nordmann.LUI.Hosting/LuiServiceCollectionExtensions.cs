using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Nordmann.LUI.Hosting;

namespace Nordmann.LUI;

/// <summary>
/// LUI in a container of <c>Microsoft.Extensions.DependencyInjection</c> (LUI-Spezifikation §12.7, ADR-0017, package
/// <c>Nordmann.LUI.Hosting</c>, ADR-0018): <b>one catalog as a singleton, and nothing else</b>.
/// <para>
/// The container does not describe indexes (ADR-0033): an index is not registered and not injected. The application asks
/// the catalog for what it needs — <c>catalog.Create(name, …)</c> in the migration, <c>catalog.Index(…)</c> in the
/// runtime path — and the index of the catalog is the one the application works with.
/// </para>
/// </summary>
public static class LuiServiceCollectionExtensions
{
    /// <summary>
    /// Registers the catalog of a database. The container opens it on first use and disposes it with itself. Once per
    /// container: LUI keeps one catalog per database.
    /// </summary>
    /// <param name="services">The services of the application.</param>
    /// <param name="connectionString">The database.</param>
    /// <param name="options">The settings of the catalog; <see langword="null"/> takes the defaults.</param>
    /// <returns>The services of the application; the catalog is in them.</returns>
    /// <exception cref="InvalidOperationException">LUI is registered already.</exception>
    public static IServiceCollection AddLui(this IServiceCollection services, string connectionString, LuiCatalogOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        return services.AddLui(_ => connectionString, options is null ? null : _ => options);
    }

    /// <summary>
    /// Registers the catalog of a database whose connection string — and settings — come from the container, for instance
    /// from the configuration: <c>AddLui(sp => sp.GetRequiredService&lt;IConfiguration&gt;().GetConnectionString("app")!)</c>.
    /// </summary>
    /// <param name="services">The services of the application.</param>
    /// <param name="connectionString">Gives the connection string when the catalog is first needed.</param>
    /// <param name="options">Gives the settings; <see langword="null"/> takes the defaults.</param>
    /// <returns>The services of the application; the catalog is in them.</returns>
    /// <exception cref="InvalidOperationException">LUI is registered already.</exception>
    public static IServiceCollection AddLui(
        this IServiceCollection services,
        Func<IServiceProvider, string> connectionString,
        Func<IServiceProvider, LuiCatalogOptions>? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(connectionString);

        if (services.Any(service => service.ServiceType == typeof(LuiCatalog)))
        {
            throw new InvalidOperationException(
                "LUI is registered already: AddLui is called once per container, and the catalog is the only thing it holds.");
        }

        services.AddSingleton(provider => new CatalogSettings(options?.Invoke(provider) ?? new LuiCatalogOptions()));
        services.AddSingleton(provider => LuiCatalog.Open(connectionString(provider), provider.GetRequiredService<CatalogSettings>().Options));

        return services;
    }

    /// <summary>
    /// Registers the warm-up of the catalog as a service of the host: the typo dictionaries of every index are built at
    /// the start of the host, and the outbox of every linked index is applied on an interval (issue #68, ADR-0043).
    /// <para>
    /// <b>Off by default</b>: without this call the host starts nothing of LUI — no dictionary is built and no outbox is
    /// applied. The library starts no thread of its own: the host runs the service, stops it and disposes it.
    /// </para>
    /// </summary>
    /// <param name="services">The services of the application.</param>
    /// <param name="outboxInterval">
    /// How long the service waits between two rounds of the outbox; <see langword="null"/> takes the default of 30 s.
    /// </param>
    /// <returns>The services of the application; the warm-up is in them.</returns>
    public static IServiceCollection AddLuiWarmup(this IServiceCollection services, TimeSpan? outboxInterval = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var interval = outboxInterval ?? LuiWarmupService.DefaultOutboxInterval;
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(outboxInterval),
                interval,
                "The interval between two rounds of the outbox must be positive.");
        }

        services.AddSingleton(provider => new LuiWarmupService(provider.GetRequiredService<LuiCatalog>(), interval));
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<LuiWarmupService>());

        return services;
    }
}
