using System.Collections.Concurrent;
using Nordmann.LUI.Normalization;

namespace Nordmann.LUI.Typos;

/// <summary>
/// The typo dictionaries of a catalog: one per index, however many times the index is ensured (review of pull request
/// 10, S1) — two instances would build 138 MB each and not see each other's writes.
/// </summary>
internal sealed class TypoDictionaries
{
    private readonly ConcurrentDictionary<string, TypoDictionary> _byIndex = new(StringComparer.Ordinal);

    /// <summary>The typo dictionary of an index — the same object for every instance of that index.</summary>
    /// <param name="indexName">The name of the index.</param>
    /// <param name="normalization">The normalisation of the index.</param>
    /// <param name="texts">Reads <c>(text, weight)</c> of every entry, when the dictionary is built.</param>
    internal TypoDictionary For(
        string indexName,
        NormalizationOptions normalization,
        Func<IEnumerable<(string Text, int Weight)>> texts) =>
        _byIndex.GetOrAdd(
            indexName,
            static (_, state) => new TypoDictionary(state.Normalization, state.Texts),
            (Normalization: normalization, Texts: texts));

    /// <summary>Forgets the typo dictionary of an index that was dropped: the next index of that name starts empty.</summary>
    internal void Forget(string indexName) => _byIndex.TryRemove(indexName, out _);
}
