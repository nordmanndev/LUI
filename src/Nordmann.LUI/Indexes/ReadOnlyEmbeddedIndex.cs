using Microsoft.Data.Sqlite;

namespace Nordmann.LUI.Indexes;

/// <summary>
/// An embedded index that was opened and not created (ADR-0033): it answers questions and nothing else. Every write
/// and every maintenance refuses itself — the read path writes nothing, and a service that may not write must not be
/// able to.
/// </summary>
/// <typeparam name="TKey">The key of the application.</typeparam>
/// <typeparam name="TPayload">The payload that comes back with a hit.</typeparam>
internal sealed class ReadOnlyEmbeddedIndex<TKey, TPayload>(EmbeddedIndex<TKey, TPayload> inner)
    : ILuiEmbeddedIndex<TKey, TPayload>
{
    public string Name => inner.Name;

    public LuiIndexKind Kind => inner.Kind;

    public bool TypoTolerance => inner.TypoTolerance;

    public LuiTypoDictionaryState TypoDictionaryState => inner.TypoDictionaryState;

    public long Count() => inner.Count();

    public LuiQuery<TKey, TPayload> Query(string text) => new(this, text);

    public LuiResult<TKey, TPayload> Search(
        string text,
        LuiQueryOptions? options = null,
        CancellationToken cancellationToken = default) =>
        inner.Search(text, options, cancellationToken);

    public void Upsert(LuiEntry<TKey, TPayload> entry, SqliteTransaction? transaction = null) => throw Refuses();

    public void Upsert(
        TKey key,
        string text,
        int weight = 0,
        LuiPriority priority = LuiPriority.Normal,
        SqliteTransaction? transaction = null) =>
        throw Refuses();

    public int UpsertMany(
        IEnumerable<LuiEntry<TKey, TPayload>> entries,
        SqliteTransaction? transaction = null,
        CancellationToken cancellationToken = default) =>
        throw Refuses();

    public int UpsertMany(
        IAsyncEnumerable<LuiEntry<TKey, TPayload>> entries,
        SqliteTransaction? transaction = null,
        CancellationToken cancellationToken = default) =>
        throw Refuses();

    public bool UpdateWeight(TKey key, int weight, SqliteTransaction? transaction = null) => throw Refuses();

    public bool UpdatePriority(TKey key, LuiPriority priority, SqliteTransaction? transaction = null) => throw Refuses();

    public bool Remove(TKey key, SqliteTransaction? transaction = null) => throw Refuses();

    public LuiSyncReport Sync(
        IEnumerable<LuiEntry<TKey, TPayload>> entries,
        LuiSyncOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw Refuses();

    public LuiSyncReport Sync(
        IAsyncEnumerable<LuiEntry<TKey, TPayload>> entries,
        LuiSyncOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw Refuses();

    public LuiIntegrityReport Check() => throw Refuses();

    public LuiIntegrityReport Check(
        IEnumerable<LuiEntry<TKey, TPayload>> entries,
        CancellationToken cancellationToken = default) =>
        throw Refuses();

    public void Rebuild() => throw Refuses();

    public void Optimize() => throw Refuses();

    public void WarmUpTypoDictionary(CancellationToken cancellationToken = default) => throw Refuses();

    public void RebuildTypoDictionary(CancellationToken cancellationToken = default) => throw Refuses();

    /// <summary>The refusal: the index was opened, not created — it reads and does not write.</summary>
    private NotSupportedException Refuses() =>
        new(
            $"The index '{inner.Name}' was opened, not created: it reads and does not write. Write to the index that "
            + "was created with the builder.");
}
