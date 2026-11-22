#pragma warning disable MA0048 // the generic form of a name that has a non-generic file: <Name>OfKeyAndPayload.cs (ADR-0073)
using Microsoft.Data.Sqlite;
using Nordmann.LUI.Indexes;
using Nordmann.LUI.Storage;

namespace Nordmann.LUI;

/// <summary>
/// The handle of an <c>Embedded</c> index <b>with a payload</b> (ADR-0073): the read side and the write path. The content lives
/// in a table of the library, and the application writes through this handle. It comes from the catalog —
/// <c>catalog.Create(definition)</c> or <c>catalog.Index(definition)</c> — and a catalog that was opened with
/// <c>OpenReadOnly</c> refuses every write of it with a <see cref="LuiReadOnlyException"/>.
/// <para>
/// <b>Every write carries a payload</b>: there is no <c>Upsert(key, text, weight)</c>, no <see cref="LuiEntry{TKey}"/> and no
/// payload-less <c>Sync</c> on this handle, so an entry without a payload cannot reach the index — and with
/// <c>TPayload = int</c>, <c>Upsert(1, "x", 5)</c> means the payload <c>5</c>, never a weight. The payload is serialized with the
/// serializer that the <see cref="LuiCatalogOptions.Payloads"/> registry of the catalog holds for <typeparamref name="TPayload"/> — found
/// once, when the index is created or opened (ADR-0072), so a write never looks it up and never fails for want of one.
/// </para>
/// </summary>
/// <typeparam name="TKey">The type of the keys — <see cref="long"/>, <see cref="int"/>, <see cref="string"/> or <see cref="Guid"/> (ADR-0073).</typeparam>
/// <typeparam name="TPayload">The type of the payload of the index.</typeparam>
public sealed class LuiEmbeddedIndex<TKey, TPayload> : LuiIndex<TKey, TPayload>
    where TKey : notnull
    where TPayload : notnull
{
    private readonly EmbeddedIndex _index;

    internal LuiEmbeddedIndex(Database database, EmbeddedIndex index)
        : base(database, index)
    {
        _index = index;
    }

    /// <summary>Writes one entry: a new one, or the new text, payload, weight and priority of the entry with that key.</summary>
    /// <param name="key">The key of the entry.</param>
    /// <param name="text">The text — what the search finds.</param>
    /// <param name="payload">The payload, written with the serializer of its type; never <see langword="null"/>.</param>
    /// <param name="weight">The weight, 0 to 536,870,911.</param>
    /// <param name="priority">The priority.</param>
    /// <param name="transaction">A transaction of the application, or <see langword="null"/> for the own one of the call.</param>
    /// <exception cref="ArgumentNullException"><paramref name="payload"/> is <see langword="null"/>.</exception>
    /// <exception cref="LuiIndexNotFoundException">The index was dropped.</exception>
    /// <exception cref="LuiReadOnlyException">The file may not be written.</exception>
    public void Upsert(
        TKey key,
        string text,
        TPayload payload,
        int weight = 0,
        LuiPriority priority = LuiPriority.Normal,
        SqliteTransaction? transaction = null) =>
        Upsert(new LuiEntry<TKey, TPayload>(key, text, payload) { Weight = weight, Priority = priority }, transaction);

    /// <summary>Writes one entry.</summary>
    /// <param name="entry">The entry.</param>
    /// <param name="transaction">A transaction of the application, or <see langword="null"/> for the own one of the call.</param>
    /// <exception cref="LuiIndexNotFoundException">The index was dropped.</exception>
    /// <exception cref="LuiReadOnlyException">The file may not be written.</exception>
    public void Upsert(LuiEntry<TKey, TPayload> entry, SqliteTransaction? transaction = null)
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
        IEnumerable<LuiEntry<TKey, TPayload>> entries,
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
        IEnumerable<LuiEntry<TKey, TPayload>> entries,
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
        IAsyncEnumerable<LuiEntry<TKey, TPayload>> entries,
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
    public LuiIntegrityReport Check(IEnumerable<LuiEntry<TKey, TPayload>> entries, CancellationToken cancellationToken = default)
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

    /// <summary>
    /// Reads every stored payload and lists the entries whose payload cannot be read (ADR-0074) — <b>a full read of the index</b>, for
    /// maintenance and not for every probe: <see cref="LuiIndex{TKey, TPayload}.Check()"/> stays the cheap structural check and reads no
    /// payload. What cannot be read is the serializer's verdict: with <c>LuiPayloadSerializer.Json</c> a payload of an old shape does not
    /// read. An entry on the list is written again — <c>Upsert</c>, <c>UpsertMany</c> or <c>Sync</c> — and is gone from the next check.
    /// The index is read in one snapshot, on a connection that only reads; a read-only catalog may call it.
    /// </summary>
    /// <param name="cancellationToken">Checked between the entries.</param>
    /// <returns>The entries that cannot be read, by key; healthy when there are none.</returns>
    /// <exception cref="LuiIndexNotFoundException">The index was dropped.</exception>
    /// <exception cref="LuiDefinitionMismatchException">The index is another one than the definition of the handle.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public LuiPayloadCheck<TKey> CheckPayloads(CancellationToken cancellationToken = default)
    {
        try
        {
            return new LuiPayloadCheck<TKey>
            {
                Unreadable =
                [
                    .. _index.UnreadablePayloads<TPayload>(cancellationToken)
                        .Select(entry => new LuiUnreadablePayload<TKey> { Key = KeyConversion.FromKey<TKey>(entry.Key), Message = entry.Message }),
                ],
            };
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

    private static IEnumerable<LuiEntry> Convert(IEnumerable<LuiEntry<TKey, TPayload>> entries) =>
        entries.Select(entry => (entry ?? throw new ArgumentNullException(nameof(entries))).ToEntry());
}
#pragma warning restore MA0048
