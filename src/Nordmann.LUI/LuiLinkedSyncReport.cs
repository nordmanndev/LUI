namespace Nordmann.LUI;

/// <summary>
/// What a sync of a <c>Linked</c> index did (ADR-0015, ADR-0077): what it indexed, what it left out and why, and how it worked — the
/// batches it committed, the entries it removed at the end, and the longest time it held the write lock of the file.
/// </summary>
public sealed record LuiLinkedSyncReport
{
    /// <summary>The entries in the index now, when the sync is through.</summary>
    public long Indexed { get; init; }

    /// <summary>The rows left out because their text is <c>NULL</c> or empty.</summary>
    public long WithoutText { get; init; }

    /// <summary>The rows left out because their key is outside 0 … 4,294,967,295 (0 … 2,147,483,647 for an <c>int</c> index) — it cannot be a document id of the index.</summary>
    public long KeyOutOfRange { get; init; }

    /// <summary>The batches of the table that were written, each in a transaction of its own. A table without a row has none.</summary>
    public int Batches { get; init; }

    /// <summary>
    /// The entries that were removed at the end because their row is not in the table any more — they carried the generation of an
    /// earlier sync, not of this one.
    /// </summary>
    public long Removed { get; init; }

    /// <summary>
    /// The longest time the sync held the write lock of the file in one transaction — a batch, a step of the removal or of the merge, the
    /// lease — from the checkpoint that precedes it to its commit. One transaction keeps another writer out at most this long; a writer can
    /// wait for several in a row, for SQLite has no queue.
    /// </summary>
    public TimeSpan LongestLock { get; init; }
}
