using System.Runtime.InteropServices;

namespace Nordmann.LUI;

/// <summary>One hit of a query (LUI-Spezifikation §8.3).</summary>
/// <typeparam name="TKey">The key of the application.</typeparam>
/// <typeparam name="TPayload">The payload that comes back with the hit.</typeparam>
[StructLayout(LayoutKind.Auto)]
public readonly record struct LuiHit<TKey, TPayload>
{
    /// <summary>The key of the entry.</summary>
    public required TKey Key { get; init; }

    /// <summary>The text of the entry, as it was stored.</summary>
    public required string Text { get; init; }

    /// <summary>The payload of the entry; the default when the index or the entry has none.</summary>
    public TPayload? Payload { get; init; }

    /// <summary>The weight of the entry.</summary>
    public int Weight { get; init; }

    /// <summary>The priority of the entry.</summary>
    public LuiPriority Priority { get; init; }

    /// <summary>How similar the input is to the text, from the word where the match begins; 0 … 1.</summary>
    public double Similarity { get; init; }

    /// <summary>Whether the input stands at the beginning of a word of the text (§9.1).</summary>
    public bool AtWordStart { get; init; }
}
