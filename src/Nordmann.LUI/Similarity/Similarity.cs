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

    /// <summary>
    /// The Sørensen-Dice coefficient over the <b>sets</b> of bigrams of two texts, with runs of white space
    /// read as one space — the definition of <c>F23.StringSimilarity.SorensenDice(2)</c>, without its
    /// allocation: the library builds a dictionary per comparison (about 6.5 KB), and a query compares
    /// up to 500 candidates (ADR-0011).
    /// </summary>
    private sealed class NgramDiceSimilarity : ITextSimilarity
    {
        /// <summary>Bigram sets up to this size are handled on the stack.</summary>
        private const int StackBigrams = 256;

        public string Name => nameof(NgramDice);

        public double Similarity(string query, string text)
        {
            ArgumentNullException.ThrowIfNull(query);
            ArgumentNullException.ThrowIfNull(text);

            var first = ToBigrams(query, stackalloc uint[StackBigrams], out var firstRented);
            var second = ToBigrams(text, stackalloc uint[StackBigrams], out var secondRented);

            try
            {
                // a text shorter than a bigram has none: two equal texts are identical, any other pair
                // has nothing in common
                if (first.IsEmpty || second.IsEmpty)
                {
                    return string.Equals(query, text, StringComparison.Ordinal) ? 1.0 : 0.0;
                }

                var common = 0;
                int i = 0, j = 0;
                while (i < first.Length && j < second.Length)
                {
                    if (first[i] == second[j])
                    {
                        common++;
                        i++;
                        j++;
                    }
                    else if (first[i] < second[j])
                    {
                        i++;
                    }
                    else
                    {
                        j++;
                    }
                }

                return 2.0 * common / (first.Length + second.Length);
            }
            finally
            {
                Return(firstRented);
                Return(secondRented);
            }
        }

        private static void Return(uint[]? rented)
        {
            if (rented is not null)
            {
                System.Buffers.ArrayPool<uint>.Shared.Return(rented);
            }
        }

        /// <summary>The distinct bigrams of a text, sorted; white space runs count as one space.</summary>
        private static Span<uint> ToBigrams(string text, Span<uint> stack, out uint[]? rented)
        {
            rented = null;
            var buffer = stack;
            if (text.Length > stack.Length)
            {
                rented = System.Buffers.ArrayPool<uint>.Shared.Rent(text.Length);
                buffer = rented;
            }

            var count = 0;
            var previous = '\0';
            var hasPrevious = false;
            foreach (var raw in text)
            {
                var c = char.IsWhiteSpace(raw) ? ' ' : raw;
                if (c == ' ' && hasPrevious && previous == ' ')
                {
                    continue;
                }

                if (hasPrevious)
                {
                    buffer[count++] = ((uint)previous << 16) | c;
                }

                previous = c;
                hasPrevious = true;
            }

            var bigrams = buffer[..count];
            bigrams.Sort();

            // distinct
            var distinct = 0;
            for (var k = 0; k < bigrams.Length; k++)
            {
                if (k == 0 || bigrams[k] != bigrams[k - 1])
                {
                    bigrams[distinct++] = bigrams[k];
                }
            }

            return bigrams[..distinct];
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
