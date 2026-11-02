using System.Runtime.InteropServices;

namespace Nordmann.LUI;

/// <summary>One hit of a query (LUI-Spezifikation §8.3).</summary>
/// <typeparam name="TKey">The key of the application.</typeparam>
/// <typeparam name="TPayload">The payload that comes back with the hit.</typeparam>
/// <param name="Key">The key of the entry.</param>
/// <param name="Text">The text of the entry, as it was stored.</param>
/// <param name="Payload">The payload of the entry; the default when the index stores none.</param>
/// <param name="Weight">The weight of the entry.</param>
/// <param name="Priority">The priority of the entry.</param>
/// <param name="Similarity">How similar the input is to the text, from the word where the match begins; 0 … 1.</param>
/// <param name="AtWordStart">Whether the input stands at the beginning of a word of the text (§9.1).</param>
/// <param name="Highlight">Reserved; <see langword="null"/> until the highlighting is built.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct LuiHit<TKey, TPayload>(
    TKey Key,
    string Text,
    TPayload? Payload,
    int Weight,
    Priority Priority,
    double Similarity,
    bool AtWordStart,
    Highlight? Highlight);
