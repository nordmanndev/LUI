namespace Nordmann.LUI;

/// <summary>
/// The class of an entry, ahead of its weight. A smaller number is a higher
/// priority: an entry of a higher priority is a candidate for every input it
/// matches, before any entry of a lower one (docs/api.md, E10).
/// </summary>
public enum LuiPriority
{
    /// <summary>Reserved entries, e.g. system tags such as „vegan".</summary>
    System = 0,

    /// <summary>Above the elevated entries.</summary>
    High = 1,

    /// <summary>Above the normal entries.</summary>
    Elevated = 2,

    /// <summary>The default.</summary>
    Normal = 3,
}
