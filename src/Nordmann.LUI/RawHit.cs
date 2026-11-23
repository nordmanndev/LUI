using System.Runtime.InteropServices;
using Nordmann.LUI.Querying;

namespace Nordmann.LUI;

/// <summary>
/// A hit as a query hands it to the handle (ADR-0073, ADR-0074): the key as the file stores it, and the stored payload — the bytes of an
/// embedded index, or the object a linked map built — that the handle reads in its type. The handle makes the typed hit of it.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly struct RawHit
{
    /// <summary>The key as the file stores it.</summary>
    internal required string Key { get; init; }

    /// <summary>The text of the entry.</summary>
    internal required string Text { get; init; }

    /// <summary>The weight of the entry.</summary>
    internal int Weight { get; init; }

    /// <summary>The priority of the entry.</summary>
    internal LuiPriority Priority { get; init; }

    /// <summary>How near the text was to the query, 0 to 1.</summary>
    internal double Similarity { get; init; }

    /// <summary>Whether the query matched at the beginning of a word.</summary>
    internal bool AtWordStart { get; init; }

    /// <summary>The payload as it is stored: the bytes of an embedded index, the object of the map of a linked one, or <see langword="null"/>.</summary>
    internal object? StoredPayload { get; init; }

    /// <summary>What reads <see cref="StoredPayload"/>.</summary>
    internal PayloadSource? PayloadSource { get; init; }

    /// <summary>Reads the payload in the type of the handle; a hit without a payload fails with a <see cref="LuiPayloadException"/>.</summary>
    /// <typeparam name="T">The payload type of the index.</typeparam>
    /// <returns>The payload.</returns>
    internal T RequirePayload<T>() =>
        (PayloadSource ?? throw new InvalidOperationException("The hit does not come from a query."))
            .Require<T>(StoredPayload, Key);
}
