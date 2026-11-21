using Nordmann.LUI.Normalization;

namespace Nordmann.LUI.Typos;

/// <summary>
/// The typo dictionaries of a catalog: one per index, however many times the index is created (review of pull request
/// 10, S1) — two instances would build 194 MB each and not see each other's writes.
/// </summary>
internal sealed class TypoDictionaries
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, (string Fingerprint, TypoDictionary Dictionary)> _byIndex = new(StringComparer.Ordinal);

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
        lock (_gate)
        {
            if (_byIndex.TryGetValue(indexName, out var known))
            {
                if (string.Equals(known.Fingerprint, fingerprint, StringComparison.Ordinal))
                {
                    return known.Dictionary;
                }

                // a dictionary that was registered without a fingerprint (a test makes one first, with a source of its own) belongs
                // to the first index that asks for its name
                if (known.Fingerprint.Length == 0)
                {
                    _byIndex[indexName] = (fingerprint, known.Dictionary);
                    return known.Dictionary;
                }
            }

            var created = new TypoDictionary(normalization, texts, distance);
            _byIndex[indexName] = (fingerprint, created);
            return created;
        }
    }

    /// <summary>Whether the typo dictionary of an index exists and is built.</summary>
    internal bool IsBuilt(string indexName) => TryGet(indexName, out var dictionary) && dictionary.IsBuilt;

    /// <summary>Forgets the typo dictionary of an index that was dropped: the next index of that name starts empty.</summary>
    internal void Forget(string indexName)
    {
        lock (_gate)
        {
            _ = _byIndex.Remove(indexName);
        }
    }

    private bool TryGet(string indexName, out TypoDictionary dictionary)
    {
        lock (_gate)
        {
            var found = _byIndex.TryGetValue(indexName, out var known);
            dictionary = known.Dictionary;
            return found;
        }
    }
}
