#pragma warning disable MA0048 // the generic form of a name that has a non-generic file: <Name>OfKey.cs (ADR-0073)
using Microsoft.Data.Sqlite;
using Nordmann.LUI.Indexes;
using Nordmann.LUI.Storage;

namespace Nordmann.LUI;

/// <summary>
/// The handle of a <c>Linked</c> index (ADR-0073): the read side and what keeps the index current. The content lives in a
/// table of the application; LUI keeps no copy of the row, and the write path belongs to the application — it tells the
/// index with <see cref="Refresh"/> and <see cref="Sync"/>, or through triggers it created on purpose. It comes from the
/// catalog: <c>catalog.Create(definition)</c> or <c>catalog.Index(definition)</c>.
/// </summary>
/// <typeparam name="TKey">The type of the keys — <see cref="long"/> or <see cref="int"/>: the <c>INTEGER PRIMARY KEY</c> of the table (text keys come with #267).</typeparam>
public sealed class LuiLinkedIndex<TKey> : LuiIndex<TKey>
    where TKey : notnull
{
    private readonly LinkedIndex _index;

    internal LuiLinkedIndex(Database database, LinkedIndex index)
        : base(database, index) => _index = index;

    /// <summary>
    /// Takes the whole table into the index. The options are accepted: the batches of a sync come with issue #271, and the
    /// options take effect then.
    /// </summary>
    /// <param name="options">The options of the sync; <see langword="null"/> for the defaults.</param>
    /// <param name="cancellationToken">Checked while the table is read.</param>
    /// <returns>How many rows were indexed, and how many were left out.</returns>
    /// <exception cref="LuiIndexNotFoundException">The index was dropped.</exception>
    /// <exception cref="LuiLinkedTableException">The table of the application is not what the link says.</exception>
    public LuiLinkedSyncReport Sync(LuiSyncOptions? options = null, CancellationToken cancellationToken = default)
    {
        _ = options;
        try
        {
            return _index.Sync(cancellationToken);
        }
        catch (Exception exception) when (IsDropped(exception))
        {
            throw Dropped();
        }
    }

    /// <summary>Takes one row of the table into the index.</summary>
    /// <param name="key">The key of the row.</param>
    /// <param name="transaction">A transaction of the application, or <see langword="null"/> for the own one of the call.</param>
    /// <returns><see langword="true"/> when the row was taken in.</returns>
    /// <exception cref="LuiIndexNotFoundException">The index was dropped.</exception>
    public bool Refresh(TKey key, SqliteTransaction? transaction = null) =>
        RefreshMany([key], transaction) == 1;

    /// <summary>Takes many rows of the table into the index.</summary>
    /// <param name="keys">The keys of the rows; a key that comes twice counts once.</param>
    /// <param name="transaction">A transaction of the application, or <see langword="null"/> for the own one of the call.</param>
    /// <param name="cancellationToken">Checked between the rows.</param>
    /// <returns>How many rows were taken in.</returns>
    /// <exception cref="LuiIndexNotFoundException">The index was dropped.</exception>
    public int RefreshMany(
        IEnumerable<TKey> keys,
        SqliteTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        try
        {
            return _index.RefreshMany(keys.Select(key => KeyConversion.ToKey(key).ToInt64()), transaction, cancellationToken);
        }
        catch (Exception exception) when (IsDropped(exception))
        {
            throw Dropped();
        }
    }

    /// <summary>Applies the changes a trigger collected — the outbox.</summary>
    /// <param name="cancellationToken">Checked between the batches.</param>
    /// <returns>How many rows were brought in.</returns>
    /// <exception cref="LuiIndexNotFoundException">The index was dropped.</exception>
    public int ApplyPending(CancellationToken cancellationToken = default)
    {
        try
        {
            return _index.ApplyPending(cancellationToken);
        }
        catch (Exception exception) when (IsDropped(exception))
        {
            throw Dropped();
        }
    }

    /// <summary>How many changes are waiting in the outbox.</summary>
    /// <returns>The number.</returns>
    /// <exception cref="LuiIndexNotFoundException">The index was dropped.</exception>
    public long Pending()
    {
        try
        {
            return _index.Pending();
        }
        catch (Exception exception) when (IsDropped(exception))
        {
            throw Dropped();
        }
    }

    /// <summary>
    /// The SQL of the triggers, for the application that wants to install them itself. It is <b>pure</b>: the script is built
    /// from the definition, no database is read — so it also answers on a handle whose index was dropped, where every other
    /// operation throws <see cref="LuiIndexNotFoundException"/>.
    /// </summary>
    /// <returns>The script.</returns>
    public string TriggerScript() => _index.TriggerScript();

    /// <summary>Creates the triggers on the table of the application — only when the application asks for it.</summary>
    /// <exception cref="LuiIndexNotFoundException">The index was dropped.</exception>
    public void CreateTriggers()
    {
        try
        {
            _index.CreateTriggers();
        }
        catch (Exception exception) when (IsDropped(exception))
        {
            throw Dropped();
        }
    }

    /// <summary>Drops the triggers again.</summary>
    /// <exception cref="LuiIndexNotFoundException">The index was dropped.</exception>
    public void DropTriggers()
    {
        try
        {
            _index.DropTriggers();
        }
        catch (Exception exception) when (IsDropped(exception))
        {
            throw Dropped();
        }
    }

    /// <summary>Whether the triggers on the table are there: <c>None</c>, <c>Current</c> or <c>Stale</c>.</summary>
    /// <returns>The state.</returns>
    /// <exception cref="LuiIndexNotFoundException">The index was dropped.</exception>
    public LuiTriggerState Triggers()
    {
        try
        {
            var state = _index.Triggers();
            return state == LuiTriggerState.None && !IndexStore.Exists(Database, Name) ? throw Dropped() : state;
        }
        catch (Exception exception) when (IsDropped(exception))
        {
            throw Dropped();
        }
    }
}
#pragma warning restore MA0048
