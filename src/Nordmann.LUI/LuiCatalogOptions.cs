using Nordmann.LUI.Schema;

namespace Nordmann.LUI;

/// <summary>
/// The options of a catalog (LUI-Spezifikation §12.1, §7.5). They steer how every
/// connection of the catalog is set up. Every value is checked when it is set, so
/// the options can be bound from configuration and fail where they are wrong — the
/// exception names the property. Bound through <c>IConfiguration.Bind</c>, it arrives
/// wrapped in a <see cref="System.Reflection.TargetInvocationException"/>: the binder wraps
/// what a setter throws; the inner exception is the one that says what is wrong.
/// </summary>
public sealed record LuiCatalogOptions
{
    /// <summary>
    /// The page cache of every connection, in megabytes; at least 1. It decides whether the index lives in
    /// memory (LUI-Spezifikation, Anhang A6). Default: <see langword="null"/>, SQLite's own.
    /// </summary>
    public int? CacheSizeMegabytes
    {
        get;
        init => field = value is { } megabytes ? Setting.AtLeast(megabytes, 1, nameof(CacheSizeMegabytes)) : null;
    }

    /// <summary>
    /// The memory-mapped part of the database file, in megabytes; at least 1. Default:
    /// <see langword="null"/>, SQLite's own.
    /// </summary>
    public int? MemoryMapMegabytes
    {
        get;
        init => field = value is { } megabytes ? Setting.AtLeast(megabytes, 1, nameof(MemoryMapMegabytes)) : null;
    }

    /// <summary>
    /// How long a connection waits for a lock before SQLite reports <c>SQLITE_BUSY</c>; not negative.
    /// Default: five seconds.
    /// <para>
    /// The wait is at least about one second: below it, Microsoft.Data.Sqlite retries a busy command until one second
    /// has passed (ADR-0046). It bounds the wait for a lock, not the time a statement runs.
    /// </para>
    /// </summary>
    public TimeSpan BusyTimeout
    {
        get;
        init => field = Setting.NotNegative(value, nameof(BusyTimeout));
    } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Whether <c>LuiCatalog.Open</c> switches the database to write-ahead logging (<c>journal_mode = WAL</c>,
    /// LUI-Spezifikation §7.5). Default: yes — WAL lets many readers run beside one writer. <b>WAL is a property
    /// of the file</b>: it stays after LUI is gone, adds <c>-wal</c> and <c>-shm</c> files, and does not work on
    /// network file systems. When LUI shares the database of an application, switch it off here and keep the
    /// mode the application chose.
    /// </summary>
    public bool WriteAheadLog { get; init; } = true;

    /// <summary>
    /// The serialiser of the payloads of every embedded index of the catalog (ADR-0035): it writes a payload with the
    /// type it has and reads it with the type the caller asks for (<see cref="LuiHit.GetPayload{T}"/>). Default:
    /// <see langword="null"/> — the indexes store no payloads, and an entry that has one is refused.
    /// <see cref="LuiPayloadSerializer.Json"/> with the JSON context of the application is the usual one. A
    /// <c>Linked</c> index does not use it: it builds its payload from the columns of its table.
    /// </summary>
    public ILuiPayloadSerializer? PayloadSerializer { get; init; }

    /// <summary>The clock of the catalog (<c>created_at</c> in <c>lui_meta</c>). Default: <see cref="TimeProvider.System"/>.</summary>
    public TimeProvider TimeProvider
    {
        get;
        init => field = Setting.NotNull(value, nameof(TimeProvider));
    } = TimeProvider.System;
}
