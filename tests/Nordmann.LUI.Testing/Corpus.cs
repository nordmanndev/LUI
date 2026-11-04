namespace Nordmann.LUI.Testing;

/// <summary>
/// A generated corpus of the measurements (LUI-Spezifikation §16, Anhang A; ADR-0020): the same entries, the same inputs
/// and the same typos for a given seed — the budget tests and the benchmarks measure one corpus, and a run can be
/// repeated. The key of an entry is its position, 1 … n.
/// </summary>
public sealed class Corpus
{
    /// <summary>The largest weight of an entry (2²⁹ − 1).</summary>
    private const int MaxWeight = 536_870_911;

    private readonly string[] _texts;
    private readonly int[] _weights;

    private Corpus(CorpusKind kind, string[] texts, int[] weights)
    {
        Kind = kind;
        _texts = texts;
        _weights = weights;
    }

    /// <summary>What the entries are.</summary>
    public CorpusKind Kind { get; }

    /// <summary>The texts; the text of the key <c>k</c> is <c>Texts[k - 1]</c>.</summary>
    public IReadOnlyList<string> Texts => _texts;

    /// <summary>The weights, in the order of the texts.</summary>
    public IReadOnlyList<int> Weights => _weights;

    /// <summary>The number of entries.</summary>
    public int Count => _texts.Length;

    /// <summary>Generates a corpus out of the words of a word list.</summary>
    /// <param name="words">The words — the German word list, or the sample of the repository.</param>
    /// <param name="entries">How many entries.</param>
    /// <param name="kind">Single words or titles.</param>
    /// <param name="weights">How the weights are spread.</param>
    /// <param name="seed">The seed: the same seed gives the same corpus.</param>
    /// <returns>The corpus.</returns>
    public static Corpus Generate(
        IReadOnlyList<string> words,
        int entries,
        CorpusKind kind,
        WeightDistribution weights = WeightDistribution.LongTail,
        int seed = 42)
    {
        ArgumentNullException.ThrowIfNull(words);
        ArgumentOutOfRangeException.ThrowIfLessThan(entries, 1);
        if (words.Count == 0)
        {
            throw new ArgumentException("The word list is empty.", nameof(words));
        }

        var random = new Random(seed);
        var texts = new string[entries];
        var weightsOf = new int[entries];
        for (var i = 0; i < entries; i++)
        {
            texts[i] = kind == CorpusKind.MultiWord
                ? Title(words, random)
                : i < words.Count
                    ? words[i]
                    : Capitalize(words[random.Next(words.Count)]) + words[random.Next(words.Count)].ToLowerInvariant();
            weightsOf[i] = Weight(weights, random);
        }

        return new Corpus(kind, texts, weightsOf);
    }

    /// <summary>The entries, in the order of their keys — as a source of a sync reads them.</summary>
    public IEnumerable<LuiEntry<long, LuiNoPayload>> Entries()
    {
        for (var i = 0; i < _texts.Length; i++)
        {
            yield return new LuiEntry<long, LuiNoPayload>(i + 1, _texts[i]) { Weight = _weights[i] };
        }
    }

    /// <summary>
    /// What users type (§16: inputs of 1 … 8 characters): the beginning of an entry, or a piece out of its middle; for
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
    /// Inputs with one typo (§10, §16 "Abfrage mit Tippfehler-Korrektur"): the beginning of an entry of 5 … 9 letters with
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

    private static int Weight(WeightDistribution distribution, Random random)
    {
        if (distribution == WeightDistribution.Sparse && random.NextDouble() < 0.5)
        {
            return 0;
        }

        // a long tail: most entries are unpopular, a few are very popular
        return (int)Math.Min(MaxWeight, 1 + (long)(random.NextDouble() * random.NextDouble() * random.NextDouble() * 1_000_000));
    }

    private static string Title(IReadOnlyList<string> words, Random random) =>
        string.Join(' ', Enumerable.Range(0, random.Next(2, 5)).Select(_ => Capitalize(words[random.Next(words.Count)])));

    private static string Capitalize(string word) => char.ToUpperInvariant(word[0]) + word[1..];
}
