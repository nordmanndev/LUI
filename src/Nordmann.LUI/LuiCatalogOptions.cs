namespace Nordmann.LUI;

/// <summary>
/// The options of a catalog (LUI-Spezifikation §12.1, §7.5). They steer how
/// every connection of the catalog is set up.
/// </summary>
public sealed class LuiCatalogOptions
{
    internal int? CacheSizeMegabytes { get; private set; }

    internal int? MemoryMapMegabytes { get; private set; }

    internal TimeSpan BusyTimeoutValue { get; private set; } = TimeSpan.FromSeconds(5);

    internal TimeProvider TimeProviderValue { get; private set; } = TimeProvider.System;

    /// <summary>
    /// The page cache of every connection. It decides whether the index lives
    /// in memory (LUI-Spezifikation, Anhang A6). Default: SQLite's own.
    /// </summary>
    /// <param name="megabytes">The size in megabytes; at least 1.</param>
    /// <returns>The options.</returns>
    public LuiCatalogOptions CacheSize(int megabytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(megabytes, 1);
        CacheSizeMegabytes = megabytes;
        return this;
    }

    /// <summary>
    /// The memory-mapped part of the database file. Default: SQLite's own.
    /// </summary>
    /// <param name="megabytes">The size in megabytes; at least 1.</param>
    /// <returns>The options.</returns>
    public LuiCatalogOptions MemoryMap(int megabytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(megabytes, 1);
        MemoryMapMegabytes = megabytes;
        return this;
    }

    /// <summary>
    /// How long a connection waits for a lock before SQLite reports
    /// <c>SQLITE_BUSY</c>. Default: five seconds.
    /// </summary>
    /// <param name="timeout">The wait; not negative.</param>
    /// <returns>The options.</returns>
    public LuiCatalogOptions BusyTimeout(TimeSpan timeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(timeout, TimeSpan.Zero);
        BusyTimeoutValue = timeout;
        return this;
    }

    /// <summary>
    /// The clock of the catalog (<c>created_at</c> in <c>lui_meta</c>).
    /// Default: <see cref="TimeProvider.System"/>.
    /// </summary>
    /// <param name="timeProvider">The clock.</param>
    /// <returns>The options.</returns>
    public LuiCatalogOptions UseTimeProvider(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        TimeProviderValue = timeProvider;
        return this;
    }
}
