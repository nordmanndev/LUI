using System.Runtime.InteropServices;

namespace Nordmann.LUI;

/// <summary>
/// A hit of a typed view (issue #59, ADR-0044): the hit of the index, with the payload in the type of the application.
/// The index knows no type (ADR-0035) — the view is the application's, and it names the type where the payload is read.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct LuiTypedHit<TPayload>
{
    /// <summary>The hit of the index, with the payload in it.</summary>
    internal LuiHit Hit { get; init; }

    /// <summary>The key of the entry.</summary>
    public LuiKey Key => Hit.Key;

    /// <summary>The text of the entry, as it was stored.</summary>
    public string Text => Hit.Text;

    /// <summary>The weight of the entry.</summary>
    public int Weight => Hit.Weight;

    /// <summary>The priority of the entry.</summary>
    public LuiPriority Priority => Hit.Priority;

    /// <summary>How similar the input is to the text, from the word where the match begins; 0 … 1.</summary>
    public double Similarity => Hit.Similarity;

    /// <summary>Whether the input stands at the beginning of a word of the text (§9.1).</summary>
    public bool AtWordStart => Hit.AtWordStart;

    /// <summary>Whether the entry has a payload.</summary>
    public bool HasPayload => Hit.HasPayload;

    /// <summary>
    /// The payload, read as <typeparamref name="TPayload"/>; the default when the entry has none. It is deserialised on
    /// every call — keep the result when it is used twice.
    /// </summary>
    public TPayload? Payload => Hit.GetPayload<TPayload>();
}
