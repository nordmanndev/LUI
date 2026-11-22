using Nordmann.LUI.Schema;

namespace Nordmann.LUI;

/// <summary>
/// How an index is synced (ADR-0016, ADR-0046, ADR-0077): an <c>Embedded</c> index with a source, a <c>Linked</c> index with its table. A record with
/// <c>init</c> properties, bindable from configuration; every value is checked where it is set.
/// </summary>
public sealed record LuiSyncOptions
{
    /// <summary>The largest batch: 1,000,000 entries.</summary>
    public const int MaxBatchSize = 1_000_000;

    /// <summary>
    /// The entries per batch — for a linked index the <b>rows of the table</b> —, each written in a transaction of its own: 1 … 1,000,000, default 10,000. A batch holds
    /// SQLite's one write lock (ADR-0015, ADR-0046) while it runs — about 0.7 s for 10,000 entries —, and other writers get a chance
    /// between the batches. A chance, not a bound: SQLite has no queue, and a writer with a short busy timeout can lose
    /// several batches in a row; give them a generous busy timeout (30 seconds). A first load into a file no one else writes is about 30 % faster with 100,000, and faster still with 1,000,000:
    /// three million entries in 172.5 s against 190 s with 100,000 (measured, ADR-0016, ADR-0020).
    /// </summary>
    public int BatchSize
    {
        get;
        init => field = Setting.InRange(value, 1, MaxBatchSize, nameof(BatchSize));
    } = 10_000;

    /// <summary>
    /// Told after every batch what the sync has done so far; <see langword="null"/> for no reports. A linked sync tells <c>Read</c>,
    /// <c>Batches</c> and <c>Removed</c> — it does not compare, so there is no <c>Added</c>, <c>Changed</c> or <c>Unchanged</c> — and, when it
    /// is through, <c>Merged</c>. It is called on the thread of the sync, outside of every lock.
    /// </summary>
    public IProgress<LuiSyncReport>? Progress { get; init; }
}
