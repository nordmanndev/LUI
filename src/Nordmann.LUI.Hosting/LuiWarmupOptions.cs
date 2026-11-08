namespace Nordmann.LUI;

/// <summary>
/// The settings of the warm-up of the host (ADR-0043): which typo dictionaries are built at the start, and how often the
/// outbox of the linked indexes is applied. <c>AddLuiWarmup(options => …)</c> sets them once, at the registration.
/// </summary>
public sealed class LuiWarmupOptions
{
    /// <summary>
    /// Whether the start of the host builds typo dictionaries at all. <see langword="false"/> leaves only the outbox to the
    /// service — for an application whose queries correct no typos. Default: <see langword="true"/>.
    /// </summary>
    public bool WarmUpTypoDictionaries { get; set; } = true;

    /// <summary>
    /// The names of the indexes whose typo dictionary the start of the host builds; <b>empty builds the dictionary of
    /// every index</b> of the catalog. A dictionary costs memory — 138 MB for 685,829 words (ADR-0021) —, so an index
    /// whose queries correct no typos does not need one. A name the catalog does not know fails the start of the host
    /// with a <see cref="LuiIndexNotFoundException"/>.
    /// </summary>
    public ICollection<string> TypoDictionaryIndexes { get; } = new List<string>();

    /// <summary>How long the service waits between two rounds of the outbox; positive. Default: 30 seconds.</summary>
    public TimeSpan OutboxInterval { get; set; } = TimeSpan.FromSeconds(30);
}
