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
    /// index did not hear of. The search index is written in the order of the rowid and merged at the end. A row
    /// without a text, or with a key outside 0 … 4,294,967,295, is left out and counted — never refused.
    /// <para>
    /// SQLite has one writer per file (§15): other writers wait while a sync runs, <b>up to their busy timeout</b> — a
    /// million rows take about half a minute, so a writer with a shorter timeout gets <c>SQLITE_BUSY</c>. Run a large
    /// sync when writes are rare, or give the writers a longer timeout.
    /// </para>
    /// </summary>
    /// <param name="cancellationToken">Checked between chunks of rows; a cancelled sync changes nothing.</param>
    /// <returns>What was indexed, and what was left out.</returns>
    LuiLinkedSyncReport Sync(CancellationToken cancellationToken = default);

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

    /// <summary><see cref="Refresh"/> for many rows, in one transaction; a key that comes twice is refreshed once.</summary>
    /// <param name="keys">The primary keys of the rows.</param>
    /// <param name="transaction">The transaction of the application, or <see langword="null"/> for one of its own.</param>
    /// <param name="cancellationToken">Checked between the rows; LUI's own transaction is rolled back.</param>
    /// <returns>How many of the rows are in the index now.</returns>
    int RefreshMany(IEnumerable<long> keys, SqliteTransaction? transaction = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks the index against itself and against the application's table (§12.6, §14) and changes nothing: the stored
    /// definition is the one of this instance, the search index passes FTS5's integrity check, the application's table
    /// still fits, and the entries agree with its rows. The integrity check reads the whole search index under SQLite's
    /// write lock (about 2 s for a million entries); the comparison with the table runs after it, in a snapshot of its own,
    /// without the lock. For an index with triggers, the rows they noted count until <see cref="ApplyPending"/> — a health
    /// check between two of them asks <see cref="Pending"/>.
    /// </summary>
    /// <returns>What was found; <see cref="LuiIntegrityReport.IsHealthy"/> when nothing.</returns>
    LuiIntegrityReport Check();

    /// <summary>
    /// Builds the typo dictionary now, if it is not built yet — so that the first typo does not pay for it
    /// (about 5 seconds and 140 MB for 685,000 words). Meant for the start of an application; it builds the
    /// dictionary even when this instance has the typo tolerance off. Nothing happens if it exists.
    /// </summary>
    /// <param name="cancellationToken">Checked while the entries are read, and while it waits for a build that runs.</param>
    void WarmUpTypoDictionary(CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds the typo dictionary anew from the entries (§10.1). The dictionary is built on first use and
    /// grows with every write through LUI: only new word beginnings, so the frequencies of the old ones stay as
    /// they were; a removal does not shrink it, because SymSpell cannot forget — a correction towards a removed
    /// word finds nothing. A rebuild gives the sums of the weights as they are now and forgets the removed
    /// words; it also takes in what was written past the library. It reads every entry (seconds for a large
    /// index); writes that arrive meanwhile are not lost, and queries use the old dictionary until the new one is
    /// ready. Nothing rebuilds it on its own (ADR-0025): call it in a quiet phase if removed words should go.
    /// </summary>
    /// <param name="cancellationToken">Checked while the entries are read, and while it waits for a build that runs.</param>
    void RebuildTypoDictionary(CancellationToken cancellationToken = default);

    /// <summary>
    /// Merges the segments of the search index. Many single refreshes leave deletion marks that a query has to step
    /// over until the segments are merged; <see cref="Sync"/> merges at its end.
    /// </summary>
    void Optimize();

    /// <summary>
    /// Brings the rows the triggers noted in step with the index — in batches of a thousand, each in a transaction of
    /// LUI's own, on its checked connection. The triggers only note the key of a changed row (<see cref="CreateTriggers"/>);
    /// until this runs, the index answers as before the change — a query never applies them, a read stays a read.
    /// Call it after a burst of writes, on a timer (the application does that — the container of LUI holds the catalog
    /// and nothing else, ADR-0033), or before a query that has to see them.
    /// <para>
    /// Run it regularly: a row that changed and is not applied yet still stands in the index with its old text, and a query
    /// reads it as a candidate before it finds that the text no longer matches. Such a row does not count against the
    /// budget of <c>Candidates</c> — the read goes on past it, up to another budget of them (ADR-0017); with more of them
    /// among the best, the answer is truncated and an entry that does match can be missed. <see cref="Pending"/> tells how
    /// far behind the index is; it is a value for a health check.
    /// </para>
    /// </summary>
    /// <param name="cancellationToken">Checked between the rows; the batch that is running is rolled back.</param>
    /// <returns>How many noted rows were brought in step.</returns>
    int ApplyPending(CancellationToken cancellationToken = default);

    /// <summary>
    /// How many rows the triggers noted that <see cref="ApplyPending"/> has not brought in step yet — a value for a health
    /// check: it grows with every changed row until the next <c>ApplyPending</c> (at most to the number of rows of the
    /// table, a key is noted once).
    /// </summary>
    /// <returns>The number of keys in the outbox.</returns>
    long Pending();

    /// <summary>
    /// The SQL of three triggers on the application's table that note every changed row for the index — for the
    /// application's own migration. Nothing is changed by asking for it. Run it in a transaction of the migration.
    /// </summary>
    /// <returns>The <c>CREATE TRIGGER</c> statements.</returns>
    string TriggerScript();

    /// <summary>
    /// Creates the triggers of <see cref="TriggerScript"/> on the application's table — only when it is called. They note
    /// the key of every row that any code inserts, changes or deletes, in a plain table of LUI's (an outbox), in the same
    /// transaction as the write; <see cref="ApplyPending"/> brings those rows into the index.
    /// <para>
    /// The triggers are plain SQL — an <c>INSERT OR IGNORE</c> of a key, no FTS5, no normalisation, no check — so every
    /// program that writes the table, with any SQLite, still can, and a write of the application never fails because of
    /// the index (review of pull request 16, M1). The price: the index is as current as the last <c>ApplyPending</c>.
    /// </para>
    /// </summary>
    void CreateTriggers();

    /// <summary>
    /// Removes the triggers of <see cref="TriggerScript"/>, if they are there. Rows they noted stay in the outbox — they
    /// changed — and <see cref="ApplyPending"/> or <see cref="Sync"/> brings them in (review of pull request 16, F3).
    /// </summary>
    void DropTriggers();

    /// <summary>
    /// Whether the triggers of this index are on the table, and whether they are the ones <see cref="TriggerScript"/>
    /// gives now: <see cref="LuiTriggerState.Stale"/> after an update of LUI or a change of the table, when the triggers
    /// a migration created no longer write what the index reads. Recreate them with <see cref="CreateTriggers"/>, or put
    /// the new script into a migration.
    /// </summary>
    /// <returns>The state of the triggers.</returns>
    LuiTriggerState Triggers();
}
