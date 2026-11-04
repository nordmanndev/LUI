using Microsoft.Data.Sqlite;

namespace Nordmann.LUI;

/// <summary>
/// An index whose content lives in a table of the library and is written through
/// it (<c>Embedded</c>, LUI-Spezifikation §5, §12.4). Inside a transaction of the
/// application, its rows and the entries change atomically.
/// </summary>
/// <typeparam name="TKey">The key of the application: <see cref="long"/> or <see cref="string"/>.</typeparam>
/// <typeparam name="TPayload">The payload that comes back with a hit; <see cref="LuiNoPayload"/> when there is none.</typeparam>
public interface ILuiEmbeddedIndex<TKey, TPayload> : ILuiIndex<TKey, TPayload>
{
    /// <summary>
    /// Adds an entry, or <b>replaces the whole entry</b> with the same key — text, weight, priority
    /// <i>and payload</i>: an entry without a payload clears the payload that was stored. To change only the
    /// weight or the priority use <see cref="UpdateWeight"/> and <see cref="UpdatePriority"/>. Writing an entry
    /// that is identical to the stored one changes nothing in the search index. Inside the transaction of the
    /// application, the entry is committed or rolled back with the application's rows; the typo dictionary
    /// learns its words at once (see <c>TypoTolerance</c> on the builder for what a rollback leaves behind).
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <param name="transaction">The transaction of the application, or <see langword="null"/> for one of its own.</param>
    /// <exception cref="InvalidOperationException">The entry has a payload, and the index stores none.</exception>
    void Upsert(LuiEntry<TKey, TPayload> entry, SqliteTransaction? transaction = null);

    /// <summary>
    /// Adds an entry without a payload, or replaces the whole entry with the same key — the short form of
    /// <see cref="Upsert(LuiEntry{TKey, TPayload}, SqliteTransaction?)"/>; a payload that was stored is cleared.
    /// </summary>
    /// <param name="key">The key of the application.</param>
    /// <param name="text">What is searched and shown; not empty.</param>
    /// <param name="weight">The popularity, 0 … 536,870,911.</param>
    /// <param name="priority">The class of the entry.</param>
    /// <param name="transaction">The transaction of the application, or <see langword="null"/> for one of its own.</param>
    /// <exception cref="ArgumentOutOfRangeException">The weight or the priority is out of range.</exception>
    void Upsert(
        TKey key,
        string text,
        int weight = 0,
        LuiPriority priority = LuiPriority.Normal,
        SqliteTransaction? transaction = null);

    /// <summary>
    /// Upserts many entries in one transaction — the way to load an index: a commit per entry costs far more
    /// than the entry. Without <paramref name="transaction"/> LUI opens one of its own and commits it at the
    /// end: all entries are written, or — on an error or a cancellation — none. With the transaction of the
    /// application the entries are part of it, and the application commits or rolls back; the typo dictionary
    /// learns their words at once, as with <see cref="Upsert(LuiEntry{TKey, TPayload}, SqliteTransaction?)"/>.
    /// </summary>
    /// <param name="entries">The entries; a key that comes twice is written twice, and the last one stays.</param>
    /// <param name="transaction">The transaction of the application, or <see langword="null"/> for one of its own.</param>
    /// <param name="cancellationToken">Checked between the entries.</param>
    /// <returns>
    /// The number of entries written — with the transaction of the application, written into it: they are in
    /// the index once the application commits.
    /// </returns>
    /// <exception cref="OperationCanceledException">The token was cancelled; LUI's own transaction is rolled back.</exception>
    int UpsertMany(
        IEnumerable<LuiEntry<TKey, TPayload>> entries,
        SqliteTransaction? transaction = null,
        CancellationToken cancellationToken = default);

    /// <summary>Changes the weight of an entry.</summary>
    /// <param name="key">The key of the application.</param>
    /// <param name="weight">The popularity, 0 … 536,870,911.</param>
    /// <param name="transaction">The transaction of the application, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the entry exists.</returns>
    bool UpdateWeight(TKey key, int weight, SqliteTransaction? transaction = null);

    /// <summary>Changes the priority of an entry.</summary>
    /// <param name="key">The key of the application.</param>
    /// <param name="priority">The class of the entry.</param>
    /// <param name="transaction">The transaction of the application, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the entry exists.</returns>
    bool UpdatePriority(TKey key, LuiPriority priority, SqliteTransaction? transaction = null);

    /// <summary>
    /// Brings the index in step with a source (§12.4, §14, ADR-0016): adds what is missing, changes what differs, leaves
    /// alone what is the same, and at the end removes the keys the source does not have — <b>the keys that were in the
    /// index when the sync began</b>: one the application adds while the sync runs stays.
    /// <para>
    /// It works in batches (<see cref="LuiSyncOptions.BatchSize"/>), each in a transaction of its own on the catalog's
    /// writer, so other writers get a chance between them — SQLite has no queue, give them a busy timeout of a few
    /// seconds. Each batch is written in the order of the search index, with FTS5's incremental merge off, and the search
    /// index is merged once at the end — the bulk path of ADR-0010. A sync that is cancelled or fails keeps the batches
    /// it committed: run it again, and it skips what is already there. The removal at the end happens only when the whole
    /// source was read; it runs in batches too, and a cancellation during it leaves the rest for the next run.
    /// </para>
    /// <para>
    /// The new words of a sync join a typo dictionary that is built at once. When the sync changed or removed anything, the
    /// dictionary is marked <see cref="LuiTypoDictionaryState.Stale"/> instead of built anew (ADR-0021): a removed word
    /// lingers until <see cref="RebuildTypoDictionary"/> — or the background service of the
    /// hosting package — builds it anew.
    /// </para>
    /// </summary>
    /// <param name="entries">The entries; of a key that comes twice the last one stays.</param>
    /// <param name="options">Batch size and progress; <see langword="null"/> takes the defaults.</param>
    /// <param name="cancellationToken">Checked between the entries; the batch that is running is rolled back.</param>
    /// <returns>What was added, changed, kept and removed.</returns>
    /// <exception cref="OperationCanceledException">The token was cancelled; the committed batches stay.</exception>
    LuiSyncReport Sync(
        IEnumerable<LuiEntry<TKey, TPayload>> entries,
        LuiSyncOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// <see cref="Sync(IEnumerable{LuiEntry{TKey, TPayload}}, LuiSyncOptions?, CancellationToken)"/> with a source that is
    /// asynchronous — an EF Core query against another database, a stream of a service (§13). The writes to SQLite stay
    /// synchronous: the call blocks until the sync is done. Run it on a thread that may block (a background service).
    /// </summary>
    /// <param name="entries">The entries.</param>
    /// <param name="options">Batch size and progress; <see langword="null"/> takes the defaults.</param>
    /// <param name="cancellationToken">Passed to the source, and checked between the entries.</param>
    /// <returns>What was added, changed, kept and removed.</returns>
    LuiSyncReport Sync(
        IAsyncEnumerable<LuiEntry<TKey, TPayload>> entries,
        LuiSyncOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks the index against itself (§12.6, §14) and changes nothing: the stored definition is the one of this instance,
    /// the search index passes FTS5's integrity check, and the three triggers that keep it in step with the content table
    /// are in place. The integrity check reads the whole search index under SQLite's write lock (about 2 s for a million
    /// entries). <see cref="Check(IEnumerable{LuiEntry{TKey, TPayload}}, CancellationToken)"/> compares it with a source.
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
    /// ready. <see cref="ILuiIndex{TKey, TPayload}.TypoDictionaryState"/> says when one is due.
    /// </summary>
    /// <param name="cancellationToken">Checked while the entries are read, and while it waits for a build that runs.</param>
    void RebuildTypoDictionary(CancellationToken cancellationToken = default);

    /// <summary>
    /// Compares the index with a source and changes nothing (§12.6): the entries of the source that are missing, those the
    /// source does not have, and those with another text, weight, priority or payload. A sync with the same source repairs
    /// what it finds. It reads in a snapshot of its own, beside the writers.
    /// </summary>
    /// <param name="entries">The entries the index should hold.</param>
    /// <param name="cancellationToken">Checked between the entries.</param>
    /// <returns>The counts, and a finding with a few keys for each kind of difference.</returns>
    LuiIntegrityReport Check(IEnumerable<LuiEntry<TKey, TPayload>> entries, CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds what is derived from the content table anew (§12.6): the three triggers, and the search index — FTS5 reads
    /// the content table in the order of the rowid —, then merges it. The repair for every finding of <see cref="Check()"/>
    /// below the content table. One transaction: other writers wait until it is done.
    /// </summary>
    void Rebuild();

    /// <summary>
    /// Merges the segments of the search index into one. Single writes leave small segments and deletion marks behind
    /// that a query steps over; FTS5 merges some of them as it goes, and this merges all. A sync does it at its end.
    /// </summary>
    void Optimize();

    /// <summary>Removes an entry.</summary>
    /// <param name="key">The key of the application.</param>
    /// <param name="transaction">The transaction of the application, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the entry existed.</returns>
    bool Remove(TKey key, SqliteTransaction? transaction = null);
}
