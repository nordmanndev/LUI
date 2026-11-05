using System.Buffers;
using Nordmann.LUI.Similarity;

namespace Nordmann.LUI;

/// <summary>
/// The similarity measures LUI ships (LUI-Spezifikation §11) — all of them LUI's own now: the
/// definitions of <c>F23.StringSimilarity</c>, without its allocation (issue #30, ADR-0032). All of
/// them are stateless and may be shared between threads.
/// </summary>
public static class LuiSimilarity
{
    /// <summary>
    /// Jaro-Winkler — the default of <see cref="LuiIndexKind.SingleWord"/>. LUI's own implementation:
    /// the definition of the library it replaces, without its allocation (issue #30).
    /// </summary>
    public static ILuiSimilarity JaroWinkler { get; } = new JaroWinklerSimilarity();

    /// <summary>Dice coefficient over bigrams — the default of <see cref="LuiIndexKind.MultiWord"/>.</summary>
    public static ILuiSimilarity NgramDice { get; } = new NgramDiceSimilarity();

    /// <summary>Damerau-Levenshtein, normalised by the longer text — for codes and numbers.</summary>
    public static ILuiSimilarity DamerauLevenshtein { get; } = new DamerauLevenshteinSimilarity();

    /// <summary>Levenshtein, normalised by the longer text.</summary>
    public static ILuiSimilarity Levenshtein { get; } = new LevenshteinSimilarity();


    /// <summary>
    /// Jaro-Winkler, LUI's own — the definition of <c>F23.StringSimilarity.JaroWinkler</c>, without its
    /// allocation: the library builds four arrays per comparison (about 200 B), and a query compares up to
    /// 500 candidates (ADR-0011, issue #30). The number is the same, proven against the library by
    /// <c>JaroWinklerEquivalenceTests</c>.
    /// </summary>
    private sealed class JaroWinklerSimilarity : ILuiSimilarity, ISpanSimilarity
    {
        private readonly JaroWinklerMeasure _measure = new();

        public double Similarity(string query, string text)
        {
            ArgumentNullException.ThrowIfNull(query);
            ArgumentNullException.ThrowIfNull(text);

            return _measure.Similarity(query.AsSpan(), text.AsSpan());
        }

        double ISpanSimilarity.Similarity(ReadOnlySpan<char> query, ReadOnlySpan<char> text) =>
            _measure.Similarity(query, text);
    }

    /// <summary>
    /// Dice coefficient over bigrams — the default of <see cref="LuiIndexKind.MultiWord"/>. The definition of
    /// <c>F23.StringSimilarity.SorensenDice(2)</c>, without its allocation: the library builds a dictionary
    /// per comparison (about 6.5 KB), and a query compares up to 500 candidates (ADR-0011).
    /// </summary>
    private sealed class NgramDiceSimilarity : ILuiSimilarity, ISpanSimilarity
    {
        /// <summary>Bigram sets up to this size are handled on the stack.</summary>
        private const int StackBigrams = 256;


        public double Similarity(string query, string text)
        {
            ArgumentNullException.ThrowIfNull(query);
            ArgumentNullException.ThrowIfNull(text);

            return Similarity(query.AsSpan(), text.AsSpan());
        }

        double ISpanSimilarity.Similarity(ReadOnlySpan<char> query, ReadOnlySpan<char> text) =>
            Similarity(query, text);

        private static double Similarity(ReadOnlySpan<char> query, ReadOnlySpan<char> text)
        {
            var first = ToBigrams(query, stackalloc uint[StackBigrams], out var firstRented);
            var second = ToBigrams(text, stackalloc uint[StackBigrams], out var secondRented);

            try
            {
                // a text shorter than a bigram has none: two equal texts are identical, any other pair
                // has nothing in common
                if (first.IsEmpty || second.IsEmpty)
                {
                    return query.SequenceEqual(text) ? 1.0 : 0.0;
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
                ArrayPool<uint>.Shared.Return(rented);
            }
        }

        /// <summary>The distinct bigrams of a text, sorted; white space runs count as one space.</summary>
        private static Span<uint> ToBigrams(ReadOnlySpan<char> text, Span<uint> stack, out uint[]? rented)
        {
            rented = null;
            var buffer = stack;
            if (text.Length > stack.Length)
            {
                rented = ArrayPool<uint>.Shared.Rent(text.Length);
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

    private sealed class DamerauLevenshteinSimilarity : ILuiSimilarity, ISpanSimilarity
    {
        public double Similarity(string query, string text) => DamerauMeasure.Similarity(query, text);

        double ISpanSimilarity.Similarity(ReadOnlySpan<char> query, ReadOnlySpan<char> text) =>
            DamerauMeasure.Similarity(query, text);
    }

    private sealed class LevenshteinSimilarity : ILuiSimilarity, ISpanSimilarity
    {
        public double Similarity(string query, string text) => LevenshteinMeasure.Similarity(query, text);

        double ISpanSimilarity.Similarity(ReadOnlySpan<char> query, ReadOnlySpan<char> text) =>
            LevenshteinMeasure.Similarity(query, text);
    }
}
