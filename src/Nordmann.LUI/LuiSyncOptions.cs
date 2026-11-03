using Nordmann.LUI.Schema;

namespace Nordmann.LUI;

/// <summary>
/// How an <c>Owned</c> index is synced with a source (LUI-Spezifikation §12.4, §14, ADR-0016). A record with
/// <c>init</c> properties, bindable from configuration; every value is checked where it is set.
/// </summary>
public sealed record LuiSyncOptions
{
    /// <summary>The largest batch: 1,000,000 entries.</summary>
    public const int MaxBatchSize = 1_000_000;

    /// <summary>
    /// The entries per batch, each written in a transaction of its own: 1 … 1,000,000, default 10,000. A batch holds
    /// SQLite's one write lock (§15) while it runs — about 0.7 s for 10,000 entries —, and other writers get a chance
    /// between the batches. A chance, not a bound: SQLite has no queue, and a writer with a short busy timeout can lose
    /// several batches in a row; give them a few seconds. A first load into a file no one else writes is about 30 % faster with 100,000 (measured, ADR-0016).
    /// </summary>
    public int BatchSize
    {
        get;
        init => field = Setting.InRange(value, 1, MaxBatchSize, nameof(BatchSize));
    } = 10_000;

    /// <summary>Told after every batch what the sync has done so far; <see langword="null"/> for no reports.</summary>
    public IProgress<LuiSyncReport>? Progress { get; init; }
}
