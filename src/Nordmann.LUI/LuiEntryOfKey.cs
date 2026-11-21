#pragma warning disable MA0048 // the generic form of a name that has a non-generic file: <Name>OfKey.cs (ADR-0073)
using Nordmann.LUI.Schema;

namespace Nordmann.LUI;

/// <summary>
/// What is written into an embedded index through a definition (ADR-0073): a key, a text, a weight and a priority.
/// It is a record — two entries with the same values are equal. The payload variants come with issue #269.
/// </summary>
/// <param name="Key">The key of the entry.</param>
/// <param name="Text">The text of the entry — what the search finds. It may not be empty.</param>
/// <typeparam name="TKey">The type of the keys of the index.</typeparam>
public sealed record LuiEntry<TKey>(TKey Key, string Text)
    where TKey : notnull
{
    /// <summary>The text of the entry — what the search finds. It may not be empty.</summary>
    public string Text
    {
        get;
        init => field = Setting.NotEmpty(value, nameof(Text));
    } = Setting.NotEmpty(Text, nameof(Text));

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
    internal LuiEntry ToEntry() =>
        new(KeyConversion.ToKey(Key), Text) { Weight = Weight, Priority = Priority };
}
#pragma warning restore MA0048
