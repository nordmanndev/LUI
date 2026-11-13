namespace Nordmann.LUI;

/// <summary>
/// What the priority of an entry does in the ranking (ADR-0013, ADR-0055).
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
