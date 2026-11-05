using F23.StringSimilarity;
using Shouldly;

namespace Nordmann.LUI.Unit.Tests;

/// <summary>
/// LUI's own Damerau-Levenshtein allocates nothing; it must give the same number as the library it replaces
/// (<c>F23.StringSimilarity.Damerau</c>) — the measurements of the specification are measurements of that
/// definition (§11). The parity is the proof; the allocation is the reason (issue #30).
/// </summary>
public sealed class DamerauEquivalenceTests
{
    private static readonly Damerau Reference = new();

    [Theory]
    [InlineData("apfelkuch", "apfelkuchen")]
    [InlineData("apfelkuchen", "apfelkuchen")]
    [InlineData("apfelkuch", "apfelstrudel")]
    [InlineData("muller", "muller")]
    [InlineData("müller", "muller")]
    [InlineData("MARTHA", "MARHTA")]
    [InlineData("DIXON", "DICKSONX")]
    [InlineData("jones", "johnson")]
    [InlineData("abcvwxyz", "cabvwxyz")]
    [InlineData("ca", "abc")]
    [InlineData("ab", "ba")]
    [InlineData("a", "a")]
    [InlineData("a", "b")]
    [InlineData("", "")]
    [InlineData("", "abc")]
    [InlineData("abc", "")]
    [InlineData("straße", "strasse")]
    [InlineData("😀party", "party😀")]
    public void It_gives_the_number_of_the_library_for_known_pairs(string a, string b)
    {
        LuiSimilarity.DamerauLevenshtein.Similarity(a, b).ShouldBe(ReferenceDistance(a, b), 0);
    }

    [Fact]
    public void It_gives_the_number_of_the_library_for_ten_thousand_random_pairs()
    {
        var random = new Random(2026);
        const string alphabet = "abcdeäöß -\t";

        for (var i = 0; i < 10_000; i++)
        {
            var a = Random(random, alphabet, random.Next(0, 40));
            var b = Random(random, alphabet, random.Next(0, 60));

            LuiSimilarity.DamerauLevenshtein.Similarity(a, b)
                .ShouldBe(ReferenceDistance(a, b), 0, $"for '{a}' and '{b}'");
        }
    }

    [Fact]
    public void It_gives_the_number_of_the_library_for_pairs_of_every_length()
    {
        // every length against every length: the transposition looks at the rows before it, and a
        // difference in the table shows up only at the edges
        var random = new Random(7);
        const string alphabet = "abc";

        for (var lengthA = 0; lengthA <= 12; lengthA++)
        {
            for (var lengthB = 0; lengthB <= 12; lengthB++)
            {
                for (var i = 0; i < 50; i++)
                {
                    var a = Random(random, alphabet, lengthA);
                    var b = Random(random, alphabet, lengthB);

                    LuiSimilarity.DamerauLevenshtein.Similarity(a, b)
                        .ShouldBe(ReferenceDistance(a, b), 0, $"for '{a}' and '{b}'");
                }
            }
        }
    }

    [Fact]
    public void A_long_text_is_compared_too()
    {
        // the table of the distances does not fit on the stack any more
        var a = "kuchen" + new string('x', 500);
        var b = "kuchen" + new string('x', 499) + "y";

        LuiSimilarity.DamerauLevenshtein.Similarity(a, b).ShouldBe(ReferenceDistance(a, b), 0);
    }

    [Fact]
    public void The_measure_allocates_nothing()
    {
        // the reason for the own implementation: a query compares up to 500 candidates
        LuiSimilarity.DamerauLevenshtein.Similarity("apfelkuch", "gedeckter apfelkuchen mit streuseln");

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            LuiSimilarity.DamerauLevenshtein.Similarity("apfelkuch", "gedeckter apfelkuchen mit streuseln");
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        allocated.ShouldBe(0, "the similarity of a query must not allocate");
    }

    /// <summary>The similarity of the library: one minus the distance, divided by the longer text.</summary>
    private static double ReferenceDistance(string a, string b)
    {
        var longest = Math.Max(a.Length, b.Length);
        return longest == 0 ? 1.0 : 1.0 - (Reference.Distance(a, b) / longest);
    }

    private static string Random(Random random, string alphabet, int length)
    {
        var chars = new char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = alphabet[random.Next(alphabet.Length)];
        }

        return new string(chars);
    }
}
