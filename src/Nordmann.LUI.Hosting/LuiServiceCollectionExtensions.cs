using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nordmann.LUI.Hosting;

namespace Nordmann.LUI;

/// <summary>
/// LUI in a container of <c>Microsoft.Extensions.DependencyInjection</c> (ADR-0017, ADR-0034, ADR-0017, package
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
    /// Registers the catalog of the application (ADR-0066): the container opens it on first use and disposes it with
    /// itself. Once per container: LUI keeps one catalog per database.
    /// <para>
    /// <b>The way the catalog is opened is the caller's</b>: <c>services.AddLui(_ =&gt; LuiCatalog.OpenOrCreate("app.db"))</c>.
    /// The container does not mirror the ways to open a catalog — it holds the catalog and nothing else, and the
    /// connection settings of the catalog travel with the call that opens it.
    /// </para>
    /// </summary>
    /// <param name="services">The services of the application.</param>
    /// <param name="open">Opens the catalog when it is first needed.</param>
    /// <returns>The services of the application; the catalog is in them.</returns>
    /// <exception cref="InvalidOperationException">LUI is registered already.</exception>
    public static IServiceCollection AddLui(this IServiceCollection services, Func<IServiceProvider, LuiCatalog> open)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(open);

        if (services.Any(service => service.ServiceType == typeof(LuiCatalog)))
        {
            throw new InvalidOperationException(
                "LUI is registered already: AddLui is called once per container, and the catalog is the only thing it holds.");
        }

        services.AddSingleton(open);

        return services;
    }

    /// <summary>
    /// Registers the warm-up of the catalog as a service of the host (issue #68, ADR-0043): the outbox of every linked
    /// index is applied and the typo dictionaries are built at the start of the host, and the outbox is applied again on
    /// an interval. What it did, and a round that failed, it tells the logger of the host (ADR-0045).
    /// <para>
    /// <b>Off by default</b>: without this call the host starts nothing of LUI — no dictionary is built and no outbox is
    /// applied. The library starts no thread of its own: the host runs the service, stops it and disposes it.
    /// </para>
    /// <para>
    /// The start of the host waits for the dictionaries — so that the first query already corrects a typo. A dictionary
    /// costs seconds and memory (194 MB for 685,739 words, ADR-0021): name the indexes that correct typos in
    /// <see cref="LuiWarmupOptions.TypoDictionaryIndexes"/>, or none, and every index gets one.
    /// </para>
    /// </summary>
    /// <param name="services">The services of the application.</param>
    /// <param name="configure">Sets the settings of the warm-up; <see langword="null"/> takes the defaults.</param>
    /// <returns>The services of the application; the warm-up is in them.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The interval of the outbox is not positive.</exception>
    /// <exception cref="InvalidOperationException">The warm-up is registered already.</exception>
    public static IServiceCollection AddLuiWarmup(this IServiceCollection services, Action<LuiWarmupOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.Any(service => service.ServiceType == typeof(LuiWarmupService)))
        {
            throw new InvalidOperationException(
                "The warm-up of LUI is registered already: AddLuiWarmup is called once per container.");
        }

        var options = new LuiWarmupOptions();
        configure?.Invoke(options);

        if (options.OutboxInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(configure),
                options.OutboxInterval,
                "The interval between two rounds of the outbox must be positive.");
        }

        services.AddSingleton(provider => new LuiWarmupService(
            provider.GetRequiredService<LuiCatalog>(),
            options,
            provider.GetService<ILogger<LuiWarmupService>>() ?? NullLogger<LuiWarmupService>.Instance));
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<LuiWarmupService>());

        return services;
    }

    /// <summary>
    /// Registers the one background loop LUI needs (ADR-0076): the outbox of the linked indexes is applied every <paramref name="interval"/>, and the loop stops with the host.
    /// See <see cref="AddLuiOutbox(IServiceCollection, Action{LuiOutboxOptions})"/> for what it does when a round fails.
    /// </summary>
    /// <param name="services">The services of the application.</param>
    /// <param name="interval">The time between two rounds; positive.</param>
    /// <returns>The services of the application; the outbox loop is in them.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The interval is not positive.</exception>
    /// <exception cref="InvalidOperationException">The outbox is registered already.</exception>
    public static IServiceCollection AddLuiOutbox(this IServiceCollection services, TimeSpan interval) =>
        AddOutbox(services, new LuiOutboxOptions { Interval = interval }, nameof(interval));

    /// <summary>
    /// Registers the one background loop LUI needs (ADR-0076): it calls <c>catalog.ApplyPending()</c> every <see cref="LuiOutboxOptions.Interval"/> (30 seconds by default)
    /// and stops with the host. <b>It does nothing at the start</b> — creating the triggers, the first sync and the typo dictionaries are <c>catalog.Initialize()</c>, which
    /// the application calls; LUI offers no hosted service that does.
    /// <para>
    /// <b>A failing round never stops the host:</b> it is logged through the logger of the host (a warning, with the exception), the next round waits with exponential
    /// backoff up to <see cref="LuiOutboxOptions.MaxBackoff"/> (5 minutes by default), and the first round that works goes back to the interval. Every process with the
    /// registration runs its own loop; that is safe — the outbox is applied by one writer at a time — and documented. Register it only where there are linked indexes with
    /// triggers: <b>on a catalog opened with <c>OpenReadOnly</c> the host does not start</b> ("a read-only catalog cannot apply the outbox").
    /// </para>
    /// </summary>
    /// <param name="services">The services of the application.</param>
    /// <param name="configure">Sets the settings of the loop.</param>
    /// <returns>The services of the application; the outbox loop is in them.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The interval or the maximum backoff is not positive.</exception>
    /// <exception cref="InvalidOperationException">The outbox is registered already.</exception>
    public static IServiceCollection AddLuiOutbox(this IServiceCollection services, Action<LuiOutboxOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new LuiOutboxOptions();
        configure(options);
        return AddOutbox(services, options, nameof(configure));
    }

    private static IServiceCollection AddOutbox(IServiceCollection services, LuiOutboxOptions options, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.Any(service => service.ServiceType == typeof(LuiOutboxService)))
        {
            throw new InvalidOperationException(
                "The outbox of LUI is registered already: AddLuiOutbox is called once per container.");
        }

        if (options.Interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(parameterName, options.Interval, "The interval between two rounds of the outbox must be positive.");
        }

        if (options.MaxBackoff <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(parameterName, options.MaxBackoff, "The longest wait between two rounds of the outbox, while rounds fail, must be positive.");
        }

        // the settings as they stand now: a later change of the object the application holds changes nothing
        var settings = new LuiOutboxOptions { Interval = options.Interval, MaxBackoff = options.MaxBackoff };
        services.AddSingleton(provider => new LuiOutboxService(
            provider.GetRequiredService<LuiCatalog>(),
            settings,
            provider.GetService<TimeProvider>() ?? TimeProvider.System,
            provider.GetService<ILogger<LuiOutboxService>>() ?? NullLogger<LuiOutboxService>.Instance));
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<LuiOutboxService>());

        return services;
    }
}
