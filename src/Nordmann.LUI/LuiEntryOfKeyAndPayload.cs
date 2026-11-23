#pragma warning disable MA0048 // the generic form of a name that has a non-generic file: <Name>OfKeyAndPayload.cs (ADR-0073)
using Nordmann.LUI.Schema;

namespace Nordmann.LUI;

/// <summary>
/// What is written into an embedded index with a payload (ADR-0073): a key, a text, <b>a payload</b>, a weight and a priority.
/// It is a record — two entries with the same values are equal — and it cannot exist without a payload: the constructor
/// takes it, a <see langword="null"/> throws, and <see cref="Payload"/> has no <see langword="init"/>, so
/// <c>entry with { … }</c> cannot make it <see langword="null"/>. It does not derive from <see cref="LuiEntry{TKey}"/>.
/// </summary>
/// <typeparam name="TKey">The type of the keys of the index.</typeparam>
/// <typeparam name="TPayload">The type of the payload of the index.</typeparam>
public sealed record LuiEntry<TKey, TPayload>
    where TKey : notnull
    where TPayload : notnull
{
    /// <summary>Creates an entry.</summary>
    /// <param name="key">The key of the entry.</param>
    /// <param name="text">The text of the entry — what the search finds. It may not be empty.</param>
    /// <param name="payload">The payload; it is written with the serializer of the catalog. It may not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="payload"/> is <see langword="null"/>.</exception>
    public LuiEntry(TKey key, string text, TPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        Key = key;
        Text = Setting.NotEmpty(text, nameof(text));
        Payload = payload;
    }

    /// <summary>The key of the entry.</summary>
    public TKey Key { get; }

    /// <summary>The text of the entry — what the search finds. It may not be empty.</summary>
    public string Text { get; }

    /// <summary>The payload of the entry; never <see langword="null"/>, and not settable after the construction.</summary>
    public TPayload Payload { get; }

    /// <summary>The weight, 0 to 536,870,911: the popularity that orders the candidates. The default is 0.</summary>
    public int Weight
    {
        get;
        init => field = Setting.InRange(value, 0, RowId.MaxWeight, nameof(Weight));
    }

    /// <summary>The priority; the default is <see cref="LuiPriority.Normal"/>.</summary>
    public LuiPriority Priority
    {
        get;
        init => field = Setting.Defined(value, nameof(Priority));
    } = LuiPriority.Normal;

    /// <summary>The entry the index below the handle writes.</summary>
    internal RawEntry ToEntry() =>
        new(KeyConversion.ToText(Key), Text, Weight, Priority, Payload);
}
#pragma warning restore MA0048
