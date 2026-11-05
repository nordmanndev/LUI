using System.Runtime.InteropServices;

namespace Nordmann.LUI;

/// <summary>
/// One hit of the untyped facade (ADR-0033): the key as the text it is stored as, the text, and the payload as the
/// raw JSON — the JSON that was stored, exactly as it was written.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct LuiDocumentHit
{
    /// <summary>The key of the entry, as the text it is stored as: <c>"42"</c>, not <c>42</c>.</summary>
    public required string Key { get; init; }

    /// <summary>The text of the entry, as it was stored.</summary>
    public required string Text { get; init; }

    /// <summary>
    /// The payload of the entry as the raw JSON; <see langword="null"/> when the index stores none. For a linked
    /// index it is the JSON object of the columns the payload was built from.
    /// </summary>
    public string? Payload { get; init; }

    /// <summary>The weight of the entry.</summary>
    public int Weight { get; init; }

    /// <summary>The priority of the entry.</summary>
    public LuiPriority Priority { get; init; }

    /// <summary>How similar the input is to the text, from the word where the match begins; 0 … 1.</summary>
    public double Similarity { get; init; }

    /// <summary>Whether the input stands at the beginning of a word of the text (§9.1).</summary>
    public bool AtWordStart { get; init; }
}
