namespace Nordmann.LUI.Sample.Indexes;

/// <summary>
/// An index the user fills on demand: the entries are not a list that is checked in, but what a source produces for the number
/// the user asks for (ADR-0029). The index itself is an ordinary embedded index — only the way it is loaded is different.
/// </summary>
internal interface IBulkSampleIndex : ISampleIndex
{
    /// <summary>The most entries the source can deliver, or <see langword="null"/> when it has no ceiling.</summary>
    long? Capacity { get; }

    /// <summary>
    /// Loads the index with exactly <paramref name="count"/> entries: what the source has is in the index afterwards, and
    /// what it does not have is gone.
    /// </summary>
    /// <param name="count">The number of entries.</param>
    /// <param name="progress">Told after every batch what the sync has done so far.</param>
    /// <param name="cancellationToken">Stops the fill; the batches that were committed stay.</param>
    /// <returns>What the sync did.</returns>
    LuiSyncReport Fill(int count, IProgress<LuiSyncReport>? progress, CancellationToken cancellationToken);
}
