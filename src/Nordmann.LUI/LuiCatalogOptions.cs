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
/// Bound through <c>IConfiguration.Bind</c>, the options arrive wrapped in a
/// <see cref="System.Reflection.TargetInvocationException"/>: the binder wraps what a setter throws; the inner exception
/// is the one that says what is wrong.
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
