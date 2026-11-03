using Nordmann.LUI.Schema;

namespace Nordmann.LUI;

/// <summary>
/// One entry of an index, as it is written: an upsert stores <b>all of it</b> —
/// text, weight, priority and payload — in place of the entry with the same key
/// (LUI-Spezifikation §12.4). The values are checked when they are set.
/// </summary>
/// <typeparam name="TKey">The key of the application: <see cref="long"/> or <see cref="string"/>.</typeparam>
/// <typeparam name="TPayload">The payload that comes back with a hit.</typeparam>
/// <param name="Key">The key of the application.</param>
/// <param name="Text">What is searched and shown; not empty.</param>
public sealed record LuiEntry<TKey, TPayload>(TKey Key, string Text)
{
    /// <summary>What is searched and shown; not empty.</summary>
    public string Text
    {
        get;
        init => field = Setting.NotEmpty(value, nameof(Text));
    } = Setting.NotEmpty(Text, nameof(Text));

    /// <summary>The popularity, 0 … 536,870,911. Default: 0.</summary>
    public int Weight
    {
        get;
        init => field = Setting.InRange(value, 0, RowId.MaxWeight, nameof(Weight));
    }

    /// <summary>The class of the entry, ahead of its weight. Default: <see cref="LuiPriority.Normal"/>.</summary>
    public LuiPriority Priority
    {
        get;
        init => field = Setting.Defined(value, nameof(Priority));
    } = LuiPriority.Normal;

    /// <summary>
    /// Sent back with the hit; stored only if the index has a payload. <see langword="null"/> — and, for a
    /// value type, its default — means "no payload".
    /// </summary>
    public TPayload? Payload { get; init; }
}
