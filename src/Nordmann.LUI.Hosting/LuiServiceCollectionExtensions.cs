using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nordmann.LUI.Hosting;

namespace Nordmann.LUI;

/// <summary>
/// LUI in a container of <c>Microsoft.Extensions.DependencyInjection</c> (LUI-Spezifikation §12.7, ADR-0017, package
/// <c>Nordmann.LUI.Hosting</c>, ADR-0018): one catalog
/// as a singleton, every index as a keyed singleton under its name, and two hosted services — one ensures the indexes when
/// the host starts, one applies the outbox of the linked indexes that ask for it.
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
    /// <returns>The builder that adds the indexes.</returns>
    /// <exception cref="InvalidOperationException">LUI is registered already.</exception>
    public static LuiServicesBuilder AddLui(this IServiceCollection services, string connectionString, LuiCatalogOptions? options = null)
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
    /// <returns>The builder that adds the indexes.</returns>
    /// <exception cref="InvalidOperationException">LUI is registered already.</exception>
    public static LuiServicesBuilder AddLui(
        this IServiceCollection services,
        Func<IServiceProvider, string> connectionString,
        Func<IServiceProvider, LuiCatalogOptions>? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(connectionString);

        if (services.Any(service => service.ServiceType == typeof(LuiRegistrations)))
        {
            throw new InvalidOperationException(
                "LUI is registered already: AddLui is called once per container, and every index is added to its builder.");
        }

        var registrations = new LuiRegistrations();
        services.AddSingleton(registrations);
        services.AddSingleton(provider => new CatalogSettings(options?.Invoke(provider) ?? new LuiCatalogOptions()));
        services.AddSingleton(provider => LuiCatalog.Open(connectionString(provider), provider.GetRequiredService<CatalogSettings>().Options));
        // The hosted services resolve the catalog before they are built: the container disposes in the reverse order of
        // creation, so it disposes them — and cancels what runs in their background — before the catalog, which waits
        // for running operations. A catalog created after them would wait for a warm-up that nothing cancels when the
        // host is disposed without a stop (review of main after #23, findings 1 and 3).
        services.AddHostedService(provider =>
        {
            provider.GetRequiredService<LuiCatalog>();
            return new LuiStartup(provider, registrations, Logger<LuiStartup>(provider));
        });
        services.AddHostedService(provider =>
        {
            provider.GetRequiredService<LuiCatalog>();
            return new LuiPendingApplier(
                provider, registrations, provider.GetRequiredService<CatalogSettings>().Options.TimeProvider, Logger<LuiPendingApplier>(provider));
        });

        return new LuiServicesBuilder(services, registrations);
    }

    /// <summary>The logger of the container, or none: an application without logging still runs LUI.</summary>
    private static ILogger<T> Logger<T>(IServiceProvider provider) =>
        provider.GetService<ILogger<T>>() ?? NullLogger<T>.Instance;
}
