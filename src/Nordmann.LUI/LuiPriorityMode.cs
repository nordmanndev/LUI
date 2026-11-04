namespace Nordmann.LUI;

/// <summary>
/// What the priority of an entry does in the ranking (LUI-Spezifikation §9.2).
/// </summary>
public enum LuiPriorityMode
{
    /// <summary>The default: the priority comes before every other stage.</summary>
    First,

    /// <summary>
    /// The priority only makes an entry a certain candidate; the ranking
    /// treats it like any other entry.
    /// </summary>
    CandidatesOnly,
}
