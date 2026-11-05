using System.Buffers;

namespace Nordmann.LUI.Similarity;

/// <summary>
/// Jaro-Winkler, LUI's own — the definition of <c>F23.StringSimilarity.JaroWinkler</c>, without its
/// allocation: the library builds four arrays per comparison (about 200 B), and a query compares up to 500
/// candidates (ADR-0011, issue #30). The number is the same, proven against the library by
/// <c>JaroWinklerEquivalenceTests</c> — including its quirks: the measure is computed in single precision,
/// the common prefix is not capped at four characters and its weight shrinks for long texts.
/// </summary>
internal sealed class JaroWinklerMeasure
{
    /// <summary>The value of the Jaro similarity above which the Winkler bonus is added.</summary>
    private const double Threshold = 0.7;

    /// <summary>The weight of a character of the common prefix — at most, and less for long texts.</summary>
    private const double WinklerCoefficient = 0.1;

    /// <summary>Match arrays up to this length are handled on the stack.</summary>
    private const int StackMatches = 256;

    /// <summary>The similarity of two texts: the Jaro similarity, raised by the common prefix.</summary>
    internal double Similarity(ReadOnlySpan<char> first, ReadOnlySpan<char> second)
    {
        if (first.SequenceEqual(second))
        {
            return 1.0;
        }

        if (first.Length == 0 || second.Length == 0)
        {
            return 0.0;
        }

        // the longer text is the one that is searched, the shorter one the one that is matched
        ReadOnlySpan<char> max = first.Length > second.Length ? first : second;
        ReadOnlySpan<char> min = first.Length > second.Length ? second : first;

        Span<int> matchIndexes = stackalloc int[StackMatches];
        Span<bool> matchFlags = stackalloc bool[StackMatches];
        int[]? rentedIndexes = null;
        bool[]? rentedFlags = null;

        try
        {
            if (max.Length > StackMatches || min.Length > StackMatches)
            {
                rentedIndexes = ArrayPool<int>.Shared.Rent(min.Length);
                rentedFlags = ArrayPool<bool>.Shared.Rent(max.Length);
                matchIndexes = rentedIndexes;
                matchFlags = rentedFlags;
                matchFlags.Clear();
            }

            var matches = Match(min, max, matchIndexes, matchFlags);
            if (matches == 0)
            {
                return 0.0;
            }

            return Measure(first, second, min, max, matchIndexes, matchFlags, matches);
        }
        finally
        {
            if (rentedIndexes is not null)
            {
                ArrayPool<int>.Shared.Return(rentedIndexes);
            }

            if (rentedFlags is not null)
            {
                ArrayPool<bool>.Shared.Return(rentedFlags);
            }
        }
    }

    /// <summary>The Jaro similarity of the two texts, raised by the common prefix.</summary>
    private static double Measure(
        ReadOnlySpan<char> first,
        ReadOnlySpan<char> second,
        ReadOnlySpan<char> min,
        ReadOnlySpan<char> max,
        ReadOnlySpan<int> matchIndexes,
        ReadOnlySpan<bool> matchFlags,
        int matches)
    {
        var transpositions = Transpositions(min, max, matchIndexes, matchFlags, matches) / 2;

        // The measure of the library is computed in single precision — the same number, the same rounding.
        // The divisor is an integer there, so the division happens in single precision as well; a double
        // divisor would give a number that differs in the eighth digit.
        float m = matches;
        double jaro = ((m / first.Length + m / second.Length + (m - transpositions) / m)) / 3;
        if (jaro <= Threshold)
        {
            return jaro;
        }

        var prefix = Prefix(first, second, min.Length);
        return jaro + (Math.Min(WinklerCoefficient, 1.0 / max.Length) * prefix * (1 - jaro));
    }

    /// <summary>
    /// The matches of the shorter text in the longer one: how far a match may be from its counterpart
    /// (half the longer text, minus one), and where each match was found.
    /// </summary>
    private static int Match(ReadOnlySpan<char> min, ReadOnlySpan<char> max, Span<int> matchIndexes, Span<bool> matchFlags)
    {
        var range = Math.Max(max.Length / 2 - 1, 0);
        var matches = 0;

        for (var mi = 0; mi < min.Length; mi++)
        {
            matchIndexes[mi] = -1;
            var c1 = min[mi];
            var start = Math.Max(mi - range, 0);
            var end = Math.Min(mi + range + 1, max.Length);
            for (var xi = start; xi < end; xi++)
            {
                if (!matchFlags[xi] && c1 == max[xi])
                {
                    matchIndexes[mi] = xi;
                    matchFlags[xi] = true;
                    matches++;
                    break;
                }
            }
        }

        return matches;
    }

    /// <summary>
    /// The transpositions: the matched characters that stand in a different order in the two texts. Each
    /// transposition counts twice, as in the library.
    /// </summary>
    private static int Transpositions(ReadOnlySpan<char> min, ReadOnlySpan<char> max, ReadOnlySpan<int> matchIndexes, ReadOnlySpan<bool> matchFlags, int matches)
    {
        var transpositions = 0;
        var mi = 0;
        var xi = 0;

        for (var k = 0; k < matches; k++)
        {
            while (matchIndexes[mi] == -1)
            {
                mi++;
            }

            while (!matchFlags[xi])
            {
                xi++;
            }

            if (min[mi] != max[xi])
            {
                transpositions++;
            }

            mi++;
            xi++;
        }

        return transpositions;
    }

    /// <summary>The common prefix of the two texts — as long as it is, not capped at four characters.</summary>
    private static int Prefix(ReadOnlySpan<char> first, ReadOnlySpan<char> second, int length)
    {
        var prefix = 0;
        for (var i = 0; i < length; i++)
        {
            if (first[i] != second[i])
            {
                break;
            }

            prefix++;
        }

        return prefix;
    }
}
