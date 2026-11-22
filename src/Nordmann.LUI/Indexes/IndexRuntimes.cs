using Nordmann.LUI.Querying;
using Nordmann.LUI.Schema;
using Nordmann.LUI.Storage;
using Nordmann.LUI.Typos;

namespace Nordmann.LUI.Indexes;

/// <summary>
/// Builds the runtime of an index (ADR-0075): the index with its source, its rules, its query runner and its guard. The catalog
/// keeps a runtime <b>with the definition object it was built for</b> — the handle table is a <see cref="System.Runtime.CompilerServices.ConditionalWeakTable{TKey, TValue}"/> —
/// so it lives as long as the definition and not a call longer: a definition holds code (its similarity, the map of its payload)
/// that the runtime uses, a host may build a definition per request, and a runtime that outlived its definition would be one
/// per request for as long as the catalog lives. A runtime whose guard failed is built anew at the next open call.
/// </summary>
internal sealed class IndexRuntimes(Database database, TypoDictionaries typos, PayloadSerialization payload, StoredIndexes stored)
{
    /// <summary>
    /// The runtime of an index by its stored definition alone (ADR-0076): no definition object of the application, no payload type and no
    /// map. It is the runtime of <see cref="Build"/> with nothing of the application in it — the index has no payload reader, so it reads
    /// no payload and calls no map — and the same guard, so every operation of it meets the checks of ADR-0075. It is built for one call
    /// and let go: nothing of it is kept.
    /// </summary>
    /// <param name="name">The name of the index.</param>
    /// <param name="entry">What <c>lui_meta</c> holds about the index.</param>
    /// <param name="counter">The schema counter the entry was read at.</param>
    /// <returns>The runtime.</returns>
    internal IndexRuntime BuildStored(string name, IndexStoreEntry entry, long counter)
    {
        var source = string.Equals(entry.Source, EmbeddedSource.SourceName, StringComparison.Ordinal) ? LuiIndexSource.Embedded : LuiIndexSource.Linked;
        var description = new IndexDescription(source, entry.Kind, IndexOptions.Default, entry.Definition.Link);
        return Build(new ResolvedIndex(name, description, entry.Definition.KeyType), entry, counter);
    }

    /// <summary>The runtime of an index that is stored.</summary>
    /// <param name="resolved">The definition after its first use.</param>
    /// <param name="entry">What <c>lui_meta</c> holds about the index.</param>
    /// <param name="counter">The schema counter the entry was read at.</param>
    /// <param name="codec">The serializer of the declared payload type of an embedded index, found in the registry of the catalog
    /// when the definition was compared (ADR-0072); <see langword="null"/> for an index without a declared payload type, which takes the
    /// serialisation of the catalog. It lives in the runtime, so the lookup is made once per catalog and definition.</param>
    /// <returns>The runtime.</returns>
    internal IndexRuntime Build(ResolvedIndex resolved, IndexStoreEntry entry, long counter, PayloadCodec? codec = null)
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
                    codec ?? payload,
                    description.Options.Similarity ?? source.Rules.DefaultSimilarity,
                    typoDistance: entry.Definition.TypoDistance,
                    guard,

                    // a handle without a payload type has no payload (ADR-0074): the statement of its hits has no payload column,
                    // and the bytes that the old calls may have stored are never copied
                    hitPayload: codec is null ? new NoPayload(name) : null),
                guard);
        }

        var linked = new LinkedSource(name, entry.Definition);
        PayloadReader reader = description.PayloadMap is { } map
            ? new ColumnPayload(name, entry.Definition.Link?.PayloadColumns ?? [], map, entry.Definition.KeyType, description.PayloadType)
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
}
