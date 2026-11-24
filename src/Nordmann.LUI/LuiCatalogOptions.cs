using Nordmann.LUI.Schema;

namespace Nordmann.LUI;

/// <summary>
/// The options of a catalog (docs/api.md, ADR-0046, ADR-0066). They steer how every connection of the catalog is set up.
/// <para>
/// The settings of the connections are typed and live in <see cref="LuiPragmas"/>: what the application may set is
/// named, bounded and checked — <see cref="Pragmas"/> is checked <b>when the catalog is opened</b>, before a connection
/// is opened, so a file that is not written is not created either.
/// </para>
/// <para>
/// The values are not checked where they are set: a value out of range is refused when the catalog is opened, with an
/// <see cref="ArgumentOutOfRangeException"/> that names the property — so the options can be bound from configuration
/// without the binder wrapping anything.
/// </para>
/// </summary>
public sealed record LuiCatalogOptions
{
    /// <summary>
    /// The settings of the connections of the catalog (ADR-0066): how long they wait for the lock of the file, how they
    /// write, and how large their page cache is. Default: <see cref="LuiPragmas"/>'s own defaults.
    /// </summary>
    public LuiPragmas Pragmas { get; init; } = new();

    /// <summary>
    /// The serializers of the payloads of the embedded indexes of the catalog, by payload type (ADR-0072):
    /// <c>new LuiPayloadRegistry().Use(AppJsonContext.Default)</c>. An index with a payload —
    /// <c>LuiIndexDefinition&lt;TKey, TPayload&gt;</c> — needs its type registered, and a type that is not is refused with a
    /// <see cref="LuiConfigurationException"/> when the index is created or opened. The registry is frozen when the catalog is
    /// opened: a call on it afterwards changes nothing for that catalog. Default: <see langword="null"/> — the catalog has
    /// no payload type registered, and an index with a payload cannot be created. A <c>Linked</c> index does not use it: it
    /// builds its payload from the columns of its table.
    /// </summary>
    public LuiPayloadRegistry? Payloads { get; init; }

    /// <summary>
    /// The clock of the catalog: <c>created_at</c> in <c>lui_meta</c>, and the lease of a linked sync (ADR-0077) — its expiry is a point in
    /// time in UTC, read from this clock. Default: <see cref="TimeProvider.System"/>.
    /// </summary>
    public TimeProvider TimeProvider
    {
        get;
        init => field = Setting.NotNull(value, nameof(TimeProvider));
    } = TimeProvider.System;
}
