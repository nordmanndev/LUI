using Microsoft.Data.Sqlite;

namespace Nordmann.LUI;

/// <summary>
/// An index over a table of the application (<c>Linked</c>, LUI-Spezifikation §5, §7.3, ADR-0015). It is loosely
/// coupled: LUI never changes the application's table on its own — no column, no index, and no trigger unless the
/// application asks for one. The application keeps the index current: <see cref="Sync"/> for the whole table,
/// <see cref="Refresh"/> after its own writes, or the triggers of <see cref="TriggerScript"/>, if it wants them.
/// LUI reads the text, the weight and the priority from the table itself; the key is the table's
/// <c>INTEGER PRIMARY KEY</c>.
/// </summary>
/// <typeparam name="TPayload">The payload that comes back with a hit, read from columns of the table; <see cref="LuiNoPayload"/> when there is none.</typeparam>
public interface ILuiLinkedIndex<TPayload> : ILuiIndex<long, TPayload>
{
    /// <summary>
    /// Indexes the whole table anew, in one transaction of LUI's own: the first build, and the repair after writes the
    /// index did not hear of. The search index is written in the order of the rowid and merged at the end. Writes of
    /// the application to the same file wait while it runs (§15).
    /// </summary>
    /// <param name="cancellationToken">Checked between chunks of rows; a cancelled sync changes nothing.</param>
    /// <returns>The number of entries in the index.</returns>
    long Sync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Brings the entry of one row in step with the table: after the application inserted, changed or deleted it. A row
    /// that is gone, or has no text, leaves the index. Without <paramref name="transaction"/> LUI reads the row and writes
    /// the index in a transaction of its own; with the transaction of the application both belong to it — call it after
    /// the application's own write, in the same transaction.
    /// </summary>
    /// <param name="key">The primary key of the row.</param>
    /// <param name="transaction">The transaction of the application, or <see langword="null"/> for one of its own.</param>
    /// <returns><see langword="true"/> when the row is in the index now.</returns>
    bool Refresh(long key, SqliteTransaction? transaction = null);

    /// <summary><see cref="Refresh"/> for many rows, in one transaction.</summary>
    /// <param name="keys">The primary keys of the rows.</param>
    /// <param name="transaction">The transaction of the application, or <see langword="null"/> for one of its own.</param>
    /// <param name="cancellationToken">Checked between the rows; LUI's own transaction is rolled back.</param>
    /// <returns>How many of the rows are in the index now.</returns>
    int RefreshMany(IEnumerable<long> keys, SqliteTransaction? transaction = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Merges the segments of the search index. Many single refreshes leave deletion marks that a query has to step
    /// over until the segments are merged; <see cref="Sync"/> merges at its end.
    /// </summary>
    void Optimize();

    /// <summary>
    /// The SQL of three triggers on the application's table that keep the index in step with it — for the application's
    /// own migration. Nothing is changed by asking for it.
    /// </summary>
    /// <returns>The <c>CREATE TRIGGER</c> statements.</returns>
    string TriggerScript();

    /// <summary>
    /// Creates the triggers of <see cref="TriggerScript"/> on the application's table — only when it is called. They make
    /// every write to the table, by any code, a write to the index in the same transaction.
    /// </summary>
    void CreateTriggers();

    /// <summary>Removes the triggers of <see cref="TriggerScript"/>, if they are there.</summary>
    void DropTriggers();
}
