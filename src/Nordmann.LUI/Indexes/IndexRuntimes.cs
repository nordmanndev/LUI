using System.Collections.Concurrent;
using Nordmann.LUI.Querying;
using Nordmann.LUI.Schema;
using Nordmann.LUI.Storage;
using Nordmann.LUI.Typos;

namespace Nordmann.LUI.Indexes;

/// <summary>
/// The runtimes of the indexes of a catalog (ADR-0075): one per index name and <b>closed definition type</b> — the key and
/// the payload type of the handle — for the stored definition it was built from (an index that was created again with
/// another definition is another runtime), and per the code the definition brings: its similarity and the map of its payload are
/// runtime only (ADR-0073), so two definitions of one index that differ in them each have their own runtime. A runtime whose
/// guard failed is replaced by a new one at the next open call.
/// </summary>
internal sealed class IndexRuntimes(Database database, TypoDictionaries typos, ILuiPayloadSerializer? payload, StoredIndexes stored)
{
    private readonly ConcurrentDictionary<Key, IndexRuntime> _runtimes = new();
    private readonly Lock _gate = new();

    /// <summary>The runtime of an index that is stored: the one that is cached, or a new one.</summary>
    /// <param name="resolved">The definition after its first use.</param>
    /// <param name="definitionType">The closed type of the definition.</param>
    /// <param name="entry">What <c>lui_meta</c> holds about the index.</param>
    /// <param name="counter">The schema counter the entry was read at.</param>
    /// <returns>The runtime.</returns>
    internal IndexRuntime Get(ResolvedIndex resolved, Type definitionType, IndexStoreEntry entry, long counter)
    {
        var description = resolved.Description;
        var key = new Key(resolved.Name, definitionType, entry.Definition, description.Options.Similarity, description.PayloadMap);
        if (_runtimes.TryGetValue(key, out var existing) && !existing.Guard.HasFailed)
        {
            return existing;
        }

        lock (_gate)
        {
            if (_runtimes.TryGetValue(key, out existing) && !existing.Guard.HasFailed)
            {
                return existing;
            }

            var runtime = Build(resolved, entry, counter);
            _runtimes[key] = runtime;
            return runtime;
        }
    }

    /// <summary>The index was dropped by this catalog: every handle of its runtimes is stale from now on, and the runtimes are forgotten.</summary>
    /// <param name="name">The name of the index.</param>
    internal void Drop(string name)
    {
        foreach (var pair in _runtimes.Where(pair => string.Equals(pair.Key.Name, name, StringComparison.Ordinal)))
        {
            pair.Value.Guard.Dropped();
            _ = _runtimes.TryRemove(pair);
        }
    }

    private IndexRuntime Build(ResolvedIndex resolved, IndexStoreEntry entry, long counter)
    {
        var name = resolved.Name;
        var description = resolved.Description;
        var guard = new IndexGuard(stored, name, entry.Definition, counter);

        // the stored definition is what the index is built from, the similarity and the map are the code of the definition; an
        // opened index takes the distance of its stored definition (ADR-0054)
        if (description.Source == LuiIndexSource.Embedded)
        {
            var source = new EmbeddedSource(name, entry.Definition);
            return new IndexRuntime(
                new EmbeddedIndex(
                    database,
                    typos,
                    source,
                    payload,
                    description.Options.Similarity ?? source.Rules.DefaultSimilarity,
                    typoDistance: entry.Definition.TypoDistance,
                    guard),
                guard);
        }

        var linked = new LinkedSource(name, entry.Definition);
        PayloadReader reader = description.PayloadMap is { } map
            ? new ColumnPayload(name, entry.Definition.Link?.PayloadColumns ?? [], map)
            : new NoPayload(name);
        return new IndexRuntime(
            new LinkedIndex(
                database,
                typos,
                linked,
                reader,
                description.Options.Similarity ?? linked.Rules.DefaultSimilarity,
                typoDistance: entry.Definition.TypoDistance,
                guard),
            guard);
    }

    private readonly record struct Key(string Name, Type DefinitionType, IndexDefinition Stored, ILuiSimilarity? Similarity, Delegate? Map);
}
