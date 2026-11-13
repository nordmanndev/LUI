namespace Nordmann.LUI;

/// <summary>
/// What a sync of an <c>Embedded</c> index with a source did (ADR-0016, ADR-0016): every entry of the source
/// was added, changed or found as it was; what the source does not have was removed. While a sync runs, the same record
/// reports its progress after every batch.
/// </summary>
public sealed record LuiSyncReport
{
    /// <summary>The entries read from the source — a key that comes twice is counted twice.</summary>
    public long Read { get; init; }

    /// <summary>The entries that were not in the index.</summary>
    public long Added { get; init; }

    /// <summary>
    /// The entries whose text, weight, priority or payload was different. A key that comes twice in the source, in two
    /// batches, counts as a change the second time — the last entry wins, even in an index that was empty.
    /// </summary>
    public long Changed { get; init; }

    /// <summary>The entries that were in the index as they are in the source: not written.</summary>
    public long Unchanged { get; init; }

    /// <summary>The entries of the index the source does not have: removed at the end.</summary>
    public long Removed { get; init; }

    /// <summary>The batches committed.</summary>
    public int Batches { get; init; }

    /// <summary>Whether the search index was merged at the end — it is, when the sync changed anything.</summary>
    public bool Merged { get; init; }
}
