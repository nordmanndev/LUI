#pragma warning disable MA0048 // the generic form of a name that has a non-generic file: <Name>OfKey.cs (ADR-0073)
using Nordmann.LUI.Schema;

namespace Nordmann.LUI;

/// <summary>
/// What is written into an embedded index through a definition (ADR-0073): a key, a text, a weight and a priority.
/// It is a record — two entries with the same values are equal. An entry with a payload is a type of its own, <see cref="LuiEntry{TKey, TPayload}"/> (issue #269).
/// </summary>
/// <typeparam name="TKey">The type of the keys of the index.</typeparam>
public sealed record LuiEntry<TKey>
    where TKey : notnull
{
    /// <summary>Creates an entry.</summary>
    /// <param name="key">The key of the entry.</param>
    /// <param name="text">The text of the entry — what the search finds. It may not be empty.</param>
    public LuiEntry(TKey key, string text)
    {
        Key = key;
        Text = Setting.NotEmpty(text, nameof(text));
    }

    /// <summary>The key of the entry.</summary>
    public TKey Key { get; }

    /// <summary>The text of the entry — what the search finds. It may not be empty.</summary>
    public string Text { get; }

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
        new(KeyConversion.ToText(Key), Text, Weight, Priority, Payload: null);
}
#pragma warning restore MA0048
