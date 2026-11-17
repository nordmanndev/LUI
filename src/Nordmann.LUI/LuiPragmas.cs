namespace Nordmann.LUI;

/// <summary>
/// The settings of the connections of a catalog (ADR-0066). They belong to the catalog: an index has no connection of
/// its own to configure, and the application never holds one.
/// <para>
/// Every value is checked when the catalog is opened: a value out of range is an
/// <see cref="ArgumentOutOfRangeException"/> <b>before</b> a connection is opened, so a file that is not written is
/// not created either.
/// </para>
/// </summary>
public sealed record LuiPragmas
{
    /// <summary>
    /// How long a connection waits for the lock of the file before SQLite reports <c>SQLITE_BUSY</c>. Default: five
    /// seconds; at least one second (below it, Microsoft.Data.Sqlite retries a busy command until a second has passed,
    /// ADR-0046) and at most ten minutes. It bounds the wait for a lock, not the time a statement runs.
    /// </summary>
    public TimeSpan BusyTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How the connections write: <see cref="LuiSynchronous.Normal"/> (the default) or
    /// <see cref="LuiSynchronous.Full"/>.
    /// </summary>
    public LuiSynchronous Synchronous { get; init; } = LuiSynchronous.Normal;

    /// <summary>
    /// The page cache of every connection, in megabytes; between 1 and 1024. Default: <see langword="null"/> — SQLite's
    /// own (2 MB). It holds <b>per connection</b>: the upper bound of the read path is the number of parallel queries
    /// times this value (ADR-0067).
    /// </summary>
    public int? CacheSizeMegabytes { get; init; }
}
