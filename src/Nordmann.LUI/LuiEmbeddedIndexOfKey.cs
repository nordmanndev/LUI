#pragma warning disable MA0048 // the generic form of a name that has a non-generic file: <Name>OfKey.cs (ADR-0073)
using Microsoft.Data.Sqlite;
using Nordmann.LUI.Indexes;
using Nordmann.LUI.Storage;

namespace Nordmann.LUI;

/// <summary>
/// The handle of an <c>Embedded</c> index (ADR-0073): the read side and the write path. The content lives in a table of the
/// library, and the application writes through this handle. It comes from the catalog —
/// <c>catalog.Create(definition)</c> or <c>catalog.Index(definition)</c> — and a catalog that was opened with
/// <c>OpenReadOnly</c> refuses every write of it with a <see cref="LuiReadOnlyException"/>.
/// </summary>
/// <typeparam name="TKey">The type of the keys — <see cref="long"/> in this version.</typeparam>
public sealed class LuiEmbeddedIndex<TKey> : LuiIndex<TKey>
    where TKey : notnull
{
    private readonly EmbeddedIndex _index;

    internal LuiEmbeddedIndex(Database database, EmbeddedIndex index)
        : base(database, index) => _index = index;

    /// <summary>Writes one entry: a new one, or the new text, weight and priority of the entry with that key.</summary>
    /// <param name="key">The key of the entry.</param>
    /// <param name="text">The text — what the search finds.</param>
    /// <param name="weight">The weight, 0 to 536,870,911.</param>
    /// <param name="priority">The priority.</param>
    /// <param name="transaction">A transaction of the application, or <see langword="null"/> for the own one of the call.</param>
    /// <exception cref="LuiIndexNotFoundException">The index was dropped.</exception>
    /// <exception cref="LuiReadOnlyException">The file may not be written.</exception>
    public void Upsert(
        TKey key,
        string text,
        int weight = 0,
        LuiPriority priority = LuiPriority.Normal,
        SqliteTransaction? transaction = null) =>
        Upsert(new LuiEntry<TKey>(key, text) { Weight = weight, Priority = priority }, transaction);

    /// <summary>Writes one entry.</summary>
    /// <param name="entry">The entry.</param>
    /// <param name="transaction">A transaction of the application, or <see langword="null"/> for the own one of the call.</param>
    /// <exception cref="LuiIndexNotFoundException">The index was dropped.</exception>
    /// <exception cref="LuiReadOnlyException">The file may not be written.</exception>
    public void Upsert(LuiEntry<TKey> entry, SqliteTransaction? transaction = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        try
        {
            _index.Upsert(entry.ToEntry(), transaction);
        }
        catch (Exception exception) when (IsDropped(exception))
        {
            throw Dropped();
        }
    }

    /// <summary>Writes many entries in one transaction.</summary>
    /// <param name="entries">The entries.</param>
    /// <param name="transaction">A transaction of the application, or <see langword="null"/> for the own one of the call.</param>
    /// <param name="cancellationToken">Checked between the entries.</param>
    /// <returns>How many were written.</returns>
    /// <exception cref="LuiIndexNotFoundException">The index was dropped.</exception>
    /// <exception cref="LuiReadOnlyException">The file may not be written.</exception>
    public int UpsertMany(
        IEnumerable<LuiEntry<TKey>> entries,
        SqliteTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        try
        {
            return _index.UpsertMany(Convert(entries), transaction, cancellationToken);
        }
        catch (Exception exception) when (IsDropped(exception))
        {
            throw Dropped();
        }
    }

    /// <summary>
    /// Makes the index equal to the given entries — <b>the bulk path</b>: what is new is added, what changed is changed,
    /// what the entries no longer have is removed.
    /// </summary>
    /// <param name="entries">The entries the index should hold.</param>
    /// <param name="options">The options of the sync; <see langword="null"/> for the defaults.</param>
    /// <param name="cancellationToken">Checked between the batches.</param>
    /// <returns>What was read, added, changed, unchanged and removed.</returns>
    /// <exception cref="LuiIndexNotFoundException">The index was dropped.</exception>
    /// <exception cref="LuiReadOnlyException">The file may not be written.</exception>
    public LuiSyncReport Sync(
        IEnumerable<LuiEntry<TKey>> entries,
        LuiSyncOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        try
        {
            return _index.Sync(Convert(entries), options, cancellationToken);
        }
        catch (Exception exception) when (IsDropped(exception))
        {
            throw Dropped();
        }
    }

    /// <summary>
    /// The same from a stream: the source may be asynchronous, the writes to SQLite are not — <b>the call blocks until the
    /// sync is done</b>, as the whole API is synchronous. Run it on a thread that may block.
    /// </summary>
    /// <param name="entries">The entries the index should hold.</param>
    /// <param name="options">The options of the sync; <see langword="null"/> for the defaults.</param>
    /// <param name="cancellationToken">Checked between the entries.</param>
    /// <returns>The same report as <c>Sync(IEnumerable&lt;…&gt;)</c> gives for the same entries.</returns>
    /// <exception cref="LuiIndexNotFoundException">The index was dropped.</exception>
    /// <exception cref="LuiReadOnlyException">The file may not be written.</exception>
    public LuiSyncReport Sync(
        IAsyncEnumerable<LuiEntry<TKey>> entries,
        LuiSyncOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);

        // the source may be asynchronous, the writes to SQLite are not (ADR-0016): the entries are awaited one by one
        return Sync(entries.ToBlockingEnumerable(cancellationToken), options, cancellationToken);
    }

    /// <summary>Removes an entry.</summary>
    /// <param name="key">The key of the entry.</param>
    /// <param name="transaction">A transaction of the application, or <see langword="null"/> for the own one of the call.</param>
    /// <returns><see langword="true"/> when there was such an entry.</returns>
    /// <exception cref="LuiIndexNotFoundException">The index was dropped.</exception>
    /// <exception cref="LuiReadOnlyException">The file may not be written.</exception>
    public bool Remove(TKey key, SqliteTransaction? transaction = null)
    {
        try
        {
            return _index.Remove(KeyConversion.ToKey(key), transaction);
        }
        catch (Exception exception) when (IsDropped(exception))
        {
            throw Dropped();
        }
    }

    /// <summary>Changes the weight of an entry.</summary>
    /// <param name="key">The key of the entry.</param>
    /// <param name="weight">The weight, 0 to 536,870,911.</param>
    /// <param name="transaction">A transaction of the application, or <see langword="null"/> for the own one of the call.</param>
    /// <returns><see langword="false"/> when there is no such entry.</returns>
    /// <exception cref="LuiIndexNotFoundException">The index was dropped.</exception>
    /// <exception cref="LuiReadOnlyException">The file may not be written.</exception>
    public bool UpdateWeight(TKey key, int weight, SqliteTransaction? transaction = null)
    {
        try
        {
            return _index.UpdateWeight(KeyConversion.ToKey(key), weight, transaction);
        }
        catch (Exception exception) when (IsDropped(exception))
        {
            throw Dropped();
        }
    }

    /// <summary>Changes the priority of an entry.</summary>
    /// <param name="key">The key of the entry.</param>
    /// <param name="priority">The priority.</param>
    /// <param name="transaction">A transaction of the application, or <see langword="null"/> for the own one of the call.</param>
    /// <returns><see langword="false"/> when there is no such entry.</returns>
    /// <exception cref="LuiIndexNotFoundException">The index was dropped.</exception>
    /// <exception cref="LuiReadOnlyException">The file may not be written.</exception>
    public bool UpdatePriority(TKey key, LuiPriority priority, SqliteTransaction? transaction = null)
    {
        try
        {
            return _index.UpdatePriority(KeyConversion.ToKey(key), priority, transaction);
        }
        catch (Exception exception) when (IsDropped(exception))
        {
            throw Dropped();
        }
    }

    /// <summary>Compares the index with a set of entries: what is missing, what is extra, what differs.</summary>
    /// <param name="entries">The entries the index should hold.</param>
    /// <param name="cancellationToken">Checked between the entries.</param>
    /// <returns>The report.</returns>
    /// <exception cref="LuiIndexNotFoundException">The index was dropped.</exception>
    public LuiIntegrityReport Check(IEnumerable<LuiEntry<TKey>> entries, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        try
        {
            return StillThere(_index.Check(Convert(entries), cancellationToken));
        }
        catch (Exception exception) when (IsDropped(exception))
        {
            throw Dropped();
        }
    }

    /// <summary>Rebuilds the search index from the content table.</summary>
    /// <exception cref="LuiIndexNotFoundException">The index was dropped.</exception>
    /// <exception cref="LuiReadOnlyException">The file may not be written.</exception>
    public void Rebuild()
    {
        try
        {
            _index.Rebuild();
        }
        catch (Exception exception) when (IsDropped(exception))
        {
            throw Dropped();
        }
    }

    private static IEnumerable<LuiEntry> Convert(IEnumerable<LuiEntry<TKey>> entries) =>
        entries.Select(entry => (entry ?? throw new ArgumentNullException(nameof(entries))).ToEntry());
}
#pragma warning restore MA0048
