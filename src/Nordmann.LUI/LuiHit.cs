using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Nordmann.LUI.Querying;

namespace Nordmann.LUI;

/// <summary>
/// One hit of a query (LUI-Spezifikation §8.3). The hit knows no payload type (ADR-0035): the payload is read where it is
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

    /// <summary>Whether the input stands at the beginning of a word of the text (§9.1).</summary>
    public bool AtWordStart { get; init; }

    /// <summary>Whether the entry has a payload.</summary>
    public bool HasPayload => StoredPayload is not null;

    /// <summary>The payload as it came out of the file: the bytes, or what the map of a linked index built.</summary>
    internal object? StoredPayload { get; init; }

    /// <summary>Where the payload came from: the index and its serialiser.</summary>
    internal PayloadSource? PayloadSource { get; init; }

    /// <summary>
    /// The payload, read as <typeparamref name="T"/>; the default when the entry has none. It is deserialised on every
    /// call — keep the result when it is used twice.
    /// </summary>
    /// <typeparam name="T">The type of the payload — the application's to know.</typeparam>
    /// <returns>The payload, or the default of <typeparamref name="T"/>.</returns>
    /// <exception cref="LuiPayloadException">The payload cannot be read as <typeparamref name="T"/>.</exception>
    public T? GetPayload<T>() => TryGetPayload<T>(out var payload) ? payload : default;

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

    /// <summary>The payload, read as <typeparamref name="T"/>, if the entry has one.</summary>
    /// <typeparam name="T">The type of the payload — the application's to know.</typeparam>
    /// <param name="payload">The payload.</param>
    /// <returns><see langword="true"/> when the entry has a payload.</returns>
    /// <exception cref="LuiPayloadException">The payload cannot be read as <typeparamref name="T"/>.</exception>
    public bool TryGetPayload<T>([MaybeNullWhen(false)] out T payload)
    {
        if (StoredPayload is null || PayloadSource is null)
        {
            payload = default;
            return false;
        }

        payload = PayloadSource.Read<T>(StoredPayload, Key);
        return true;
    }

    private static bool SamePayload(object? one, object? other) =>
        one is byte[] bytes && other is byte[] otherBytes
            ? bytes.AsSpan().SequenceEqual(otherBytes)
            : Equals(one, other);
}
