using System.Collections.Concurrent;
using Nordmann.LUI.Normalization;

namespace Nordmann.LUI.Typos;

/// <summary>
/// The typo dictionaries of a catalog: one per index, however many times the index is created (review of pull request
/// 10, S1) — two instances would build 194 MB each and not see each other's writes.
/// </summary>
internal sealed class TypoDictionaries
{
    private readonly Lock _replace = new();
    private readonly ConcurrentDictionary<string, Entry> _byIndex = new(StringComparer.Ordinal);

    /// <summary>
    /// The typo dictionary of an index — the same object for every instance of that index <b>with that definition</b>. A dictionary
    /// is built from the texts, the normalisation and the distance of one definition: an index that was created again under the
    /// same name with another one (by another process) gets a dictionary of its own, and the old one is let go.
    /// </summary>
    /// <param name="indexName">The name of the index.</param>
    /// <param name="normalization">The normalisation of the index.</param>
    /// <param name="texts">Reads <c>(text, weight)</c> of every entry, when the dictionary is built.</param>
    /// <param name="distance">How many errors the dictionary forgives — the distance of the index.</param>
    /// <param name="fingerprint">What says which definition the dictionary is for: the stored definition, as text.</param>
    internal TypoDictionary For(
        string indexName,
        NormalizationOptions normalization,
        Func<IEnumerable<(string Text, int Weight)>> texts,
        LuiTypoDistance distance,
        string fingerprint = "")
    {
        var entry = _byIndex.GetOrAdd(
            indexName,
            static (_, state) => new Entry(state.Fingerprint, new TypoDictionary(state.Normalization, state.Texts, state.Distance)),
            (Fingerprint: fingerprint, Normalization: normalization, Texts: texts, Distance: distance));

        return string.Equals(entry.Fingerprint, fingerprint, StringComparison.Ordinal)
            ? entry.Dictionary
            : Replace(indexName, fingerprint, normalization, texts, distance);
    }

    /// <summary>
    /// The dictionary that was there is of another definition — or it was registered without a fingerprint (a test makes one first,
    /// with a source of its own), and then it belongs to the first index that asks for its name.
    /// </summary>
    private TypoDictionary Replace(
        string indexName,
        string fingerprint,
        NormalizationOptions normalization,
        Func<IEnumerable<(string Text, int Weight)>> texts,
        LuiTypoDistance distance)
    {
        BeforeReplace?.Invoke();
        lock (_replace)
        {
            // the entry that was there may have been forgotten since (the index was dropped): then it is a new one
            var found = _byIndex.TryGetValue(indexName, out var known);
            if (found && string.Equals(known!.Fingerprint, fingerprint, StringComparison.Ordinal))
            {
                return known.Dictionary;
            }

            var dictionary = found && known!.Fingerprint.Length == 0 ? known.Dictionary : new TypoDictionary(normalization, texts, distance);
            _byIndex[indexName] = new Entry(fingerprint, dictionary);
            return dictionary;
        }
    }

    /// <summary>
    /// Where the dictionary of an index <b>with this definition</b> stands — without creating one: an index nobody has opened in this
    /// process has none, and a dictionary of another definition (the index was created again under the name) is not its dictionary.
    /// </summary>
    /// <param name="indexName">The name of the index.</param>
    /// <param name="fingerprint">The stored definition, as text — what <see cref="For"/> was asked with.</param>
    /// <returns>The state of the dictionary, <see cref="LuiTypoDictionaryState.NotBuilt"/> when there is none.</returns>
    internal LuiTypoDictionaryState StateOf(string indexName, string fingerprint) =>
        _byIndex.TryGetValue(indexName, out var entry) && string.Equals(entry.Fingerprint, fingerprint, StringComparison.Ordinal)
            ? entry.Dictionary.State
            : LuiTypoDictionaryState.NotBuilt;

    /// <summary>Whether the typo dictionary of an index exists and is built.</summary>
    internal bool IsBuilt(string indexName) => _byIndex.TryGetValue(indexName, out var entry) && entry.Dictionary.IsBuilt;

    /// <summary>Forgets the typo dictionary of an index that was dropped: the next index of that name starts empty.</summary>
    internal void Forget(string indexName) => _byIndex.TryRemove(indexName, out _);

    /// <summary>A seam of the tests (ADR-0075): runs between the registry's answer and the replacement — where <c>Forget</c> may come in.</summary>
    internal Action? BeforeReplace { get; set; }

    private sealed record Entry(string Fingerprint, TypoDictionary Dictionary);
}
