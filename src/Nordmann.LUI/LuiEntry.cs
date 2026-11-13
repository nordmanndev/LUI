using Nordmann.LUI.Schema;

namespace Nordmann.LUI;

/// <summary>
/// One entry of an index, as it is written: an upsert stores <b>all of it</b> —
/// text, weight, priority and payload — in place of the entry with the same key
/// (ADR-0016). The values are checked when they are set.
/// </summary>
/// <param name="Key">The key of the application: a number or a text, of the kind the index has.</param>
/// <param name="Text">What is searched and shown; not empty.</param>
public sealed record LuiEntry(LuiKey Key, string Text)
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
    /// Sent back with the hit (ADR-0035): any object the serialiser of the catalog can write, written with the type it
    /// has. <see langword="null"/> means "no payload".
    /// </summary>
    public object? Payload { get; init; }
}
