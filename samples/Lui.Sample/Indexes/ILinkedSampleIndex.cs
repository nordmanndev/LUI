using Nordmann.LUI.Sample.Api;

namespace Nordmann.LUI.Sample.Indexes;

/// <summary>
/// An index over a table of the application: the page writes to the table, as the application would, and the index
/// follows — through the triggers the application created and the outbox they fill, or through <c>Refresh</c>.
/// </summary>
internal interface ILinkedSampleIndex : ISampleIndex
{
    /// <summary>Adds or changes a row of the application's table; returns its id.</summary>
    long UpsertRow(TagRowRequest request);

    /// <summary>Deletes a row of the application's table.</summary>
    bool DeleteRow(long id);

    /// <summary>Tells the index that rows changed (<c>RefreshMany</c>); returns how many were taken.</summary>
    int Refresh(IReadOnlyCollection<long> keys, CancellationToken cancellationToken);

    /// <summary>Applies the outbox now (<c>ApplyPending</c>); returns how many keys were applied.</summary>
    int ApplyPending(CancellationToken cancellationToken);

    /// <summary>Creates (<c>CreateTriggers</c>) or drops (<c>DropTriggers</c>) the triggers — the application's call.</summary>
    void SetTriggers(bool create);
}
