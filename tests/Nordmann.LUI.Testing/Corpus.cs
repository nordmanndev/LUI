namespace Nordmann.LUI.Testing;

/// <summary>
/// A corpus of the measurements (ADR-0020, Anhang A; ADR-0020; ADR-0059): the entries, their weights
/// and the kind of the index they live in. The entries come from a <b>fixed file</b> and not from a generator: a
/// corpus that changes with the scaling parameter is a different corpus, and a measurement that runs on two corpora is
/// no comparison. The key of an entry is its position, 1 … n.
/// </summary>
public sealed class Corpus
{
    private readonly string[] _texts;
    private readonly int[] _weights;

    private Corpus(string name, CorpusKind kind, string[] texts, int[] weights)
    {
        Name = name;
        Kind = kind;
        _texts = texts;
        _weights = weights;
    }

    /// <summary>The name of the corpus: the file name is its identity.</summary>
    public string Name { get; }

    /// <summary>What the entries are.</summary>
    public CorpusKind Kind { get; }

    /// <summary>The texts; the text of the key <c>k</c> is <c>Texts[k - 1]</c>.</summary>
    public IReadOnlyList<string> Texts => _texts;

    /// <summary>The weights, in the order of the texts.</summary>
    public IReadOnlyList<int> Weights => _weights;

    /// <summary>The number of entries.</summary>
    public int Count => _texts.Length;

    /// <summary>
    /// The corpus of a corpus file (ADR-0059): the entries of the file with their weights. The file carries them —
    /// the weights of a place are its population, the weights of a film are its votes, and the words and the titles
    /// carry the derived weight of their text.
    /// </summary>
    /// <param name="file">The corpus file and the prefix of it that the run takes.</param>
    /// <returns>The corpus.</returns>
    public static Corpus From(CorpusFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        return new Corpus(file.Name, file.Kind, [.. file.Texts], [.. file.Weights]);
    }

    /// <summary>The entries, in the order of their keys — as a source of a sync reads them.</summary>
    public IEnumerable<LuiEntry> Entries()
    {
        for (var i = 0; i < _texts.Length; i++)
        {
            yield return new LuiEntry(i + 1, _texts[i]) { Weight = _weights[i] };
        }
    }

    /// <summary>
    /// What users type (ADR-0020: inputs of 1 … 8 characters): the beginning of an entry, or a piece out of its middle; for
    /// titles also two words of a title, a few letters of each, in either order.
    /// </summary>
    /// <param name="count">How many inputs.</param>
    /// <param name="seed">The seed.</param>
    /// <returns>The inputs.</returns>
    public IReadOnlyList<string> Inputs(int count, int seed = 7)
    {
        var random = new Random(seed);
        var inputs = new List<string>(count);
        while (inputs.Count < count)
        {
            var text = _texts[random.Next(_texts.Length)];
            if (Kind == CorpusKind.MultiWord && random.Next(3) == 0)
            {
                var parts = text.Split(' ').Select(word => word[..Math.Min(word.Length, random.Next(3, 6))]).Take(2).ToArray();
                if (random.Next(2) == 0)
                {
                    Array.Reverse(parts);
                }

                inputs.Add(string.Join(' ', parts));
                continue;
            }

            var length = Math.Min(text.Length, random.Next(1, 9));
            var start = random.Next(3) == 0 && text.Length > length ? random.Next(text.Length - length + 1) : 0;
            var input = text.Substring(start, length).Trim();
            if (input.Length > 0)
            {
                inputs.Add(input);
            }
        }

        return inputs;
    }

    /// <summary>
    /// Inputs with one typo (ADR-0012, ADR-0020 "Abfrage mit Tippfehler-Korrektur"): the beginning of an entry of 5 … 9 letters with
    /// one edit after the first letter — the inputs a typo tolerance is there for.
    /// </summary>
    /// <param name="count">How many inputs.</param>
    /// <param name="seed">The seed.</param>
    /// <returns>The inputs.</returns>
    public IReadOnlyList<string> Typos(int count, int seed = 13)
    {
        var random = new Random(seed);
        var typos = new List<string>(count);
        while (typos.Count < count)
        {
            var word = _texts[random.Next(_texts.Length)].Split(' ')[0];
            if (word.Length < 5)
            {
                continue;
            }

            typos.Add(TypoMaker.WithOneTypo(word[..Math.Min(word.Length, random.Next(5, 10))], random).ToLowerInvariant());
        }

        return typos;
    }
}
