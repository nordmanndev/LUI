using System.Buffers;

namespace Nordmann.LUI.Similarity;

/// <summary>
/// Levenshtein, LUI's own — the definition of <c>F23.StringSimilarity.NormalizedLevenshtein</c>, without its
/// allocation: the library builds two arrays per comparison (about 350 B), and a query compares up to 500
/// candidates (issue #30). The number is the same, proven against the library by
/// <c>LevenshteinEquivalenceTests</c>.
/// </summary>
internal static class LevenshteinMeasure
{
    /// <summary>
    /// The similarity of two texts: one minus the distance, divided by the longer text.
    /// </summary>
    internal static double Similarity(ReadOnlySpan<char> first, ReadOnlySpan<char> second)
    {
        if (first.SequenceEqual(second))
        {
            return 1.0;
        }

        // two empty texts are equal and returned above, so the longer text is never empty here
        var longest = Math.Max(first.Length, second.Length);

        // the order of the division and the subtraction is the library's: the number has to be the same
        // to the last bit, and the order decides where the rounding happens
        return 1.0 - (Distance(first, second) / (double)longest);
    }

    /// <summary>
    /// The distance: the least number of insertions, deletions and substitutions that turn one text into
    /// the other (Wagner-Fischer with two rows).
    /// </summary>
    internal static int Distance(ReadOnlySpan<char> first, ReadOnlySpan<char> second)
    {
        if (first.Length == 0)
        {
            return second.Length;
        }

        if (second.Length == 0)
        {
            return first.Length;
        }

        var width = second.Length + 1;

        // two rows of the table; they are swapped by swapping their references, not their content
        var previous = ArrayPool<int>.Shared.Rent(width);
        var current = ArrayPool<int>.Shared.Rent(width);

        try
        {
            // the row of an empty first text: the number of the characters to delete
            for (var i = 0; i < width; i++)
            {
                previous[i] = i;
            }

            for (var i = 0; i < first.Length; i++)
            {
                FillRow(previous, current, first, second, i);
                (previous, current) = (current, previous);
            }

            return previous[second.Length];
        }
        finally
        {
            ArrayPool<int>.Shared.Return(previous);
            ArrayPool<int>.Shared.Return(current);
        }
    }

    /// <summary>One row of the table: the distances of the first text up to its i-th character.</summary>
    private static void FillRow(
        int[] previous,
        int[] current,
        ReadOnlySpan<char> first,
        ReadOnlySpan<char> second,
        int i)
    {
        current[0] = i + 1;

        for (var j = 0; j < second.Length; j++)
        {
            var cost = first[i] == second[j] ? 0 : 1;

            current[j + 1] = Math.Min(
                current[j] + 1,
                Math.Min(previous[j + 1] + 1, previous[j] + cost));
        }
    }
}
