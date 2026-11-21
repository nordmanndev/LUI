#pragma warning disable MA0048 // the generic form of a name that has a non-generic file: <Name>OfKey.cs (ADR-0073)
using Microsoft.Data.Sqlite;
using Nordmann.LUI.Indexes;
using Nordmann.LUI.Storage;

namespace Nordmann.LUI;

/// <summary>
/// The read side of an index that was opened through a definition (ADR-0073): what both sources have — the name, the kind,
/// the count, the search, the upkeep of the typo dictionary and the check. Use it where a method only reads;
/// <see cref="LuiEmbeddedIndex{TKey}"/> and <see cref="LuiLinkedIndex{TKey}"/> are for the code that writes or syncs.
/// <para>
/// A handle is a <b>view</b>: it holds no connection and is not disposable, it is thread-safe like the catalog, and
/// it is a class with reference equality — an application tests its search against a real catalog, not against a mock.
/// An operation on a handle whose index was dropped throws <see cref="LuiIndexNotFoundException"/>; on a disposed
/// catalog, <see cref="ObjectDisposedException"/>. Only the catalog creates handles: <c>catalog.Index(definition)</c>.
/// </para>
/// </summary>
/// <typeparam name="TKey">The type of the keys — <see cref="long"/> in this version.</typeparam>
public abstract class LuiIndex<TKey>
    where TKey : notnull
{
    private readonly Database _database;
    private readonly IMaintainedIndex _inner;

    private protected LuiIndex(Database database, IMaintainedIndex inner)
    {
        _database = database;
        _inner = inner;
    }

    /// <summary>The name of the index.</summary>
    public string Name => _inner.Name;

    /// <summary>Single word or multi word — the kind of the index.</summary>
    public LuiIndexKind Kind => _inner.Kind;

    /// <summary>How many errors the index forgives: <c>None</c>, <c>OneError</c> or <c>TwoErrors</c>. It belongs to the index; a query switches the correction on or off.</summary>
    public LuiTypoDistance TypoDistance => _inner.TypoDistance;

    /// <summary>The state of the typo dictionary: <c>NotBuilt</c>, <c>Building</c> or <c>Current</c>.</summary>
    public LuiTypoDictionaryState TypoDictionaryState => _inner.TypoDictionaryState;

    /// <summary>The number of the entries.</summary>
    /// <returns>The count.</returns>
    /// <exception cref="LuiIndexNotFoundException">The index was dropped.</exception>
    public long Count()
    {
        try
        {
            return _inner.Count();
        }
        catch (Exception exception) when (IsDropped(exception))
        {
            throw Dropped();
        }
    }

    /// <summary>A query that is configured step by step and then executed.</summary>
    /// <param name="text">What the user typed.</param>
    /// <returns>The query.</returns>
    public LuiQuery<TKey> Query(string text) => new(this, text);

    /// <summary>A query in one call.</summary>
    /// <param name="text">What the user typed.</param>
    /// <param name="options">The settings of the query; <see langword="null"/> for the defaults.</param>
    /// <param name="cancellationToken">Checked while the query runs.</param>
    /// <returns>The answer, with typed keys.</returns>
    /// <exception cref="LuiIndexNotFoundException">The index was dropped.</exception>
    public LuiResult<TKey> Search(string text, LuiQueryOptions? options = null, CancellationToken cancellationToken = default)
    {
        try
        {
            return LuiResult<TKey>.From(_inner.Search(text, options, cancellationToken));
        }
        catch (Exception exception) when (IsDropped(exception))
        {
            throw Dropped();
        }
    }

    /// <summary>Builds the typo dictionary before a query needs it.</summary>
    /// <param name="cancellationToken">Checked while the dictionary is built.</param>
    /// <exception cref="LuiIndexNotFoundException">The index was dropped.</exception>
    public void WarmUpTypoDictionary(CancellationToken cancellationToken = default)
    {
        try
        {
            _inner.WarmUpTypoDictionary(cancellationToken);
        }
        catch (Exception exception) when (IsDropped(exception))
        {
            throw Dropped();
        }
    }

    /// <summary>Builds the typo dictionary again, from scratch.</summary>
    /// <param name="cancellationToken">Checked while the dictionary is built.</param>
    /// <exception cref="LuiIndexNotFoundException">The index was dropped.</exception>
    public void RebuildTypoDictionary(CancellationToken cancellationToken = default)
    {
        try
        {
            _inner.RebuildTypoDictionary(cancellationToken);
        }
        catch (Exception exception) when (IsDropped(exception))
        {
            throw Dropped();
        }
    }

    /// <summary>Lets SQLite optimize the search index of this index.</summary>
    /// <exception cref="LuiIndexNotFoundException">The index was dropped.</exception>
    public void Optimize()
    {
        try
        {
            _inner.Optimize();
        }
        catch (Exception exception) when (IsDropped(exception))
        {
            throw Dropped();
        }
    }

    /// <summary>
    /// Compares the index with itself — an embedded one: the schema, the counts, the fold table; a linked one: with its
    /// table. The report carries no keys; it does not read payloads and calls no map.
    /// </summary>
    /// <returns>The report.</returns>
    /// <exception cref="LuiIndexNotFoundException">The index was dropped.</exception>
    public LuiIntegrityReport Check()
    {
        try
        {
            return StillThere(_inner.Check());
        }
        catch (Exception exception) when (IsDropped(exception))
        {
            throw Dropped();
        }
    }

    /// <summary>The database the handle belongs to.</summary>
    internal Database Database => _database;

    /// <summary>Whether an error means that the index is gone: a table of it is missing (or the transaction of the application says so) and the definition is no longer in <c>lui_meta</c>.</summary>
    /// <param name="exception">What the operation threw.</param>
    /// <returns><see langword="true"/> when the index was dropped.</returns>
    internal bool IsDropped(Exception exception) =>
        (exception is LuiLinkedTableException
            || exception is ArgumentException { ParamName: "transaction" } // a write with the transaction of the application: its check names the missing table
            || exception is SqliteException { SqliteErrorCode: 1 } sqlite && sqlite.Message.Contains("no such table", StringComparison.Ordinal))
        && !IndexStore.Exists(_database, Name);

    /// <summary>A report that says the index is broken may say it because the index is gone: the check reports a missing table, it does not throw.</summary>
    /// <param name="report">The report of the check.</param>
    /// <returns>The report, when the index exists.</returns>
    /// <exception cref="LuiIndexNotFoundException">The index was dropped.</exception>
    internal LuiIntegrityReport StillThere(LuiIntegrityReport report) =>
        report.IsHealthy || IndexStore.Exists(_database, Name) ? report : throw Dropped();

    /// <summary>The exception of an operation on a handle whose index was dropped.</summary>
    /// <returns>The exception.</returns>
    internal LuiIndexNotFoundException Dropped() => new(Name);
}
#pragma warning restore MA0048
