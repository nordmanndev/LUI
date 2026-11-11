using System.Buffers;

namespace Nordmann.LUI.Similarity;

/// <summary>
/// Damerau-Levenshtein, LUI's own — the definition of <c>F23.StringSimilarity.Damerau</c> (the unrestricted
/// distance, with transpositions), without its allocation: the library builds a dictionary and a matrix per
/// comparison (about 3.6 KB), and a query compares up to 500 candidates (issue #30). The number is the same,
/// proven against the library by <c>DamerauEquivalenceTests</c>.
/// </summary>
internal static class DamerauMeasure
{
    /// <summary>Cells up to this number are handled on the stack.</summary>
    private const int StackCells = 2048;

    /// <summary>Characters up to this number are handled on the stack.</summary>
    private const int StackCharacters = 256;

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
    /// The distance: the least number of insertions, deletions, substitutions and transpositions of two
    /// adjacent characters that turn one text into the other (Lowrance-Wagner).
    /// </summary>
    internal static int Distance(ReadOnlySpan<char> first, ReadOnlySpan<char> second)
    {
        var rows = first.Length;
        var columns = second.Length;
        var width = columns + 2;

        // the table of the distances: every cell is written before it is read — the edges are filled
        // first, every other cell in the row above it or to its left
        Span<int> table = stackalloc int[StackCells];
        Span<char> sorted = stackalloc char[StackCharacters];
        Span<int> last = stackalloc int[StackCharacters];
        int[]? rentedTable = null;
        char[]? rentedCharacters = null;
        int[]? rentedLast = null;

        try
        {
            if (((rows + 2) * width) > StackCells)
            {
                rentedTable = ArrayPool<int>.Shared.Rent((rows + 2) * width);
                table = rentedTable;
            }

            if ((rows + columns) > StackCharacters)
            {
                rentedCharacters = ArrayPool<char>.Shared.Rent(rows + columns);
                rentedLast = ArrayPool<int>.Shared.Rent(rows + columns);
                sorted = rentedCharacters;
                last = rentedLast;
            }

            var known = Collect(sorted, last, first, second);
            FillEdges(table, width, rows, columns, rows + columns);

            return FillTable(table, width, first, second, known, last);
        }
        finally
        {
            if (rentedTable is not null)
            {
                ArrayPool<int>.Shared.Return(rentedTable);
            }

            if (rentedCharacters is not null)
            {
                ArrayPool<char>.Shared.Return(rentedCharacters);
            }

            if (rentedLast is not null)
            {
                ArrayPool<int>.Shared.Return(rentedLast);
            }
        }
    }

    /// <summary>
    /// The characters of both texts, sorted and without duplicates: the last place a character was seen
    /// in the first text is kept beside it (all of them start at zero).
    /// </summary>
    private static ReadOnlySpan<char> Collect(Span<char> sorted, Span<int> last, ReadOnlySpan<char> first, ReadOnlySpan<char> second)
    {
        var count = 0;
        foreach (var character in first)
        {
            sorted[count++] = character;
        }

        foreach (var character in second)
        {
            sorted[count++] = character;
        }

        sorted[..count].Sort();

        var distinct = 0;
        for (var i = 0; i < count; i++)
        {
            if (distinct == 0 || sorted[distinct - 1] != sorted[i])
            {
                sorted[distinct++] = sorted[i];
                last[distinct - 1] = 0;
            }
        }

        return sorted[..distinct];
    }

    /// <summary>The edges of the table: the distance to an empty text is its length.</summary>
    private static void FillEdges(Span<int> table, int width, int rows, int columns, int infinity)
    {
        // the corner of the table: the cell that the transposition reads when neither text has seen
        // the character yet. The library it replaces has it at zero — a fresh array — and a table
        // from the pool still holds what the last tenant wrote in it.
        table[0] = 0;

        for (var i = 0; i <= rows; i++)
        {
            table[(i + 1) * width] = infinity;
            table[(i + 1) * width + 1] = i;
        }

        for (var j = 0; j <= columns; j++)
        {
            table[j + 1] = infinity;
            table[width + j + 1] = j;
        }
    }

    /// <summary>
    /// The table of the distances. A transposition is the fourth way to reach a cell: the two characters
    /// were swapped, and the distance is the one of the cell before the transposition plus the length of
    /// the text between them.
    /// </summary>
    private static int FillTable(
        Span<int> table,
        int width,
        ReadOnlySpan<char> first,
        ReadOnlySpan<char> second,
        ReadOnlySpan<char> known,
        Span<int> last)
    {
        for (var i = 1; i <= first.Length; i++)
        {
            // the last place the character of the first text was seen in the second
            var db = 0;

            for (var j = 1; j <= second.Length; j++)
            {
                var i1 = Last(known, last, second[j - 1]);
                var j1 = db;

                var cost = 1;
                if (first[i - 1] == second[j - 1])
                {
                    cost = 0;
                    db = j;
                }

                table[(i + 1) * width + (j + 1)] = Math.Min(
                    table[i * width + j] + cost,
                    Math.Min(
                        table[(i + 1) * width + j] + 1,
                        Math.Min(
                            table[i * width + (j + 1)] + 1,
                            table[i1 * width + j1] + (i - i1 - 1) + 1 + (j - j1 - 1))));
            }

            last[IndexOf(known, first[i - 1])] = i;
        }

        return table[(first.Length + 1) * width + (second.Length + 1)];
    }

    /// <summary>The last place the character was seen in the first text; zero when it was not.</summary>
    private static int Last(ReadOnlySpan<char> known, ReadOnlySpan<int> last, char character)
    {
        var index = IndexOf(known, character);
        return index < 0 ? 0 : last[index];
    }

    /// <summary>The place of the character in the sorted characters; minus one when it is not among them.</summary>
    private static int IndexOf(ReadOnlySpan<char> known, char character)
    {
        var low = 0;
        var high = known.Length - 1;

        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var current = known[middle];

            if (current == character)
            {
                return middle;
            }

            if (current < character)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return -1;
    }
}
