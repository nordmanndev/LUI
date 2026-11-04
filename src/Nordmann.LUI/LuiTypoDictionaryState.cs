namespace Nordmann.LUI;

/// <summary>
/// Where the typo dictionary of an index stands (LUI-Spezifikation §10.1, ADR-0021) — a value for a health check, and the
/// question a background service asks before it rebuilds.
/// </summary>
public enum LuiTypoDictionaryState
{
    /// <summary>Not built: the first correction, <c>WarmUpTypoDictionary()</c> or a start with warm-up builds it.</summary>
    NotBuilt,

    /// <summary>
    /// Being built. A query that needs a correction does not wait for it: it answers without the correction and says so
    /// (<see cref="LuiResult{TKey, TPayload}.CorrectionSkipped"/>). A rebuild of a built dictionary keeps the old one
    /// in use until the new one is ready.
    /// </summary>
    Building,

    /// <summary>Built and in step with the entries, as far as LUI wrote them.</summary>
    Current,

    /// <summary>
    /// Built, and behind: a sync changed or removed entries. The new words are learned, but the words that went stay and
    /// the frequencies are the old ones — a correction may point at a word that is gone and finds nothing. A rebuild
    /// (<c>RebuildTypoDictionary()</c>, or the hosting package's background service) brings it in step.
    /// </summary>
    Stale,
}
