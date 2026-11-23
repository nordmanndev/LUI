namespace Nordmann.LUI;

/// <summary>
/// Where the typo dictionary of an index stands (ADR-0012, ADR-0025) — a value for a health check, and the
/// question whether a correction is possible right now.
/// </summary>
public enum LuiTypoDictionaryState
{
    /// <summary>
    /// Not built — in this process: a query never builds it (ADR-0042), <c>Initialize()</c> or <c>WarmUpTypoDictionary()</c> does. An index that forgives
    /// typos and has no dictionary answers without corrections and says so (<see cref="LuiResult{TKey}.CorrectionSkipped"/>).
    /// </summary>
    NotBuilt,

    /// <summary>
    /// Being built. A query that needs a correction does not wait for it: it answers without the correction and says so
    /// (<see cref="LuiResult{TKey}.CorrectionSkipped"/>). A rebuild of a built dictionary keeps the old one
    /// in use until the new one is ready.
    /// </summary>
    Building,

    /// <summary>
    /// Built. It learns the words of every write through LUI at once and <b>only grows</b>: a word that was removed or
    /// changed away stays until <c>RebuildTypoDictionary()</c> or a restart. That costs little, because the index is the
    /// filter — a correction towards a word that is gone finds no candidate and falls away.
    /// </summary>
    Current,
}
