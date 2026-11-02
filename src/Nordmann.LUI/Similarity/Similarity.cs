using F23.StringSimilarity;

namespace Nordmann.LUI.Similarity;

/// <summary>
/// The similarity measures LUI ships (LUI-Spezifikation §11), implemented on
/// <c>F23.StringSimilarity</c>. All of them are stateless and may be shared
/// between threads.
/// </summary>
#pragma warning disable MA0049 // the specification names the type Similarity (§11, §12.5): Similarity.JaroWinkler
public static class Similarity
#pragma warning restore MA0049
{
    /// <summary>Jaro-Winkler — the default of <see cref="IndexKind.SingleWord"/>.</summary>
    public static ITextSimilarity JaroWinkler { get; } = new JaroWinklerSimilarity();

    /// <summary>Dice coefficient over bigrams — the default of <see cref="IndexKind.MultiWord"/>.</summary>
    public static ITextSimilarity NgramDice { get; } = new NgramDiceSimilarity();

    /// <summary>Damerau-Levenshtein, normalised by the longer text — for codes and numbers.</summary>
    public static ITextSimilarity DamerauLevenshtein { get; } = new DamerauLevenshteinSimilarity();

    /// <summary>Levenshtein, normalised by the longer text.</summary>
    public static ITextSimilarity Levenshtein { get; } = new LevenshteinSimilarity();

    /// <summary>The default measure of an index kind (LUI-Spezifikation §4).</summary>
    internal static ITextSimilarity DefaultFor(IndexKind kind) =>
        kind == IndexKind.MultiWord ? NgramDice : JaroWinkler;

    private sealed class JaroWinklerSimilarity : ITextSimilarity
    {
        private readonly JaroWinkler _measure = new();

        public string Name => nameof(JaroWinkler);

        public double Similarity(string query, string text) => _measure.Similarity(query, text);
    }

    private sealed class NgramDiceSimilarity : ITextSimilarity
    {
        private readonly SorensenDice _measure = new(2);

        public string Name => nameof(NgramDice);

        public double Similarity(string query, string text)
        {
            // a text shorter than a bigram has no bigrams: the measure is 0/0 — two equal texts are
            // identical, any other pair has nothing in common
            if (query.Length < 2 || text.Length < 2)
            {
                return string.Equals(query, text, StringComparison.Ordinal) ? 1.0 : 0.0;
            }

            var value = _measure.Similarity(query, text);
            return double.IsNaN(value) ? 0.0 : value;
        }
    }

    private sealed class DamerauLevenshteinSimilarity : ITextSimilarity
    {
        private readonly Damerau _measure = new();

        public string Name => nameof(DamerauLevenshtein);

        public double Similarity(string query, string text)
        {
            var longest = Math.Max(query.Length, text.Length);
            return longest == 0 ? 1.0 : 1.0 - (_measure.Distance(query, text) / longest);
        }
    }

    private sealed class LevenshteinSimilarity : ITextSimilarity
    {
        private readonly NormalizedLevenshtein _measure = new();

        public string Name => nameof(Levenshtein);

        public double Similarity(string query, string text) => _measure.Similarity(query, text);
    }
}
