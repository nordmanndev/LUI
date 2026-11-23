using System.Runtime.InteropServices;
using Nordmann.LUI.Querying;

namespace Nordmann.LUI;

/// <summary>
/// One hit of a query (ADR-0008). The hit knows no payload type (ADR-0035): the payload is read where it is
/// used, with the type the caller asks for — <c>hit.GetPayload&lt;TagChip&gt;()</c> — and not before.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct LuiHit
{
    /// <summary>The key of the entry.</summary>
    public required LuiKey Key { get; init; }

    /// <summary>The text of the entry, as it was stored.</summary>
    public required string Text { get; init; }

    /// <summary>The weight of the entry.</summary>
    public int Weight { get; init; }

    /// <summary>The priority of the entry.</summary>
    public LuiPriority Priority { get; init; }

    /// <summary>How similar the input is to the text, from the word where the match begins; 0 … 1.</summary>
    public double Similarity { get; init; }

    /// <summary>Whether the input stands at the beginning of a word of the text (ADR-0008).</summary>
    public bool AtWordStart { get; init; }

    /// <summary>The payload as it came out of the file: the bytes, or what the map of a linked index built.</summary>
    internal object? StoredPayload { get; init; }

    /// <summary>Where the payload came from: the index and its serialiser.</summary>
    internal PayloadSource? PayloadSource { get; init; }

    /// <summary>
    /// Whether two hits are the same (review of PR 60, M4): the key, the text, the weight, the priority, the similarity,
    /// the word start — and the stored payload by its <b>content</b>, the bytes of an embedded index or what the map of a
    /// linked one built. Where the payload came from is no part of it: two instances of the same index give equal hits.
    /// </summary>
    /// <param name="other">The other hit.</param>
    /// <returns><see langword="true"/> when they are the same.</returns>
    public bool Equals(LuiHit other) =>
        Key == other.Key
        && string.Equals(Text, other.Text, StringComparison.Ordinal)
        && Weight == other.Weight
        && Priority == other.Priority
        && Similarity.Equals(other.Similarity)
        && AtWordStart == other.AtWordStart
        && SamePayload(StoredPayload, other.StoredPayload);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Key);
        hash.Add(Text, StringComparer.Ordinal);
        hash.Add(Weight);
        hash.Add(Priority);
        hash.Add(Similarity);
        hash.Add(AtWordStart);
        if (StoredPayload is byte[] bytes)
        {
            hash.AddBytes(bytes);
        }
        else
        {
            hash.Add(StoredPayload);
        }

        return hash.ToHashCode();
    }

    /// <summary>The payload for the hit of a handle with a payload type: set when the query returns, and a failure fails the query (ADR-0074).</summary>
    /// <typeparam name="T">The payload type of the handle.</typeparam>
    /// <returns>The payload.</returns>
    /// <exception cref="LuiPayloadException">The payload is missing or cannot be read.</exception>
    internal T RequirePayload<T>() =>
        (PayloadSource ?? throw new InvalidOperationException("The hit does not come from a query."))
            .Require<T>(StoredPayload, Key);

    private static bool SamePayload(object? one, object? other) =>
        one is byte[] bytes && other is byte[] otherBytes
            ? bytes.AsSpan().SequenceEqual(otherBytes)
            : Equals(one, other);
}
