using F23.StringSimilarity;
using Shouldly;

namespace Nordmann.LUI.Unit.Tests.Similarity;

/// <summary>
/// LUI's own Levenshtein allocates nothing; it must give the same number as the library it replaces
/// (<c>F23.StringSimilarity.NormalizedLevenshtein</c>) — the measurements of the specification are
/// measurements of that definition (§11). The parity is the proof; the allocation is the reason (issue #30).
/// </summary>
public sealed class LevenshteinEquivalenceTests
{
    private static readonly NormalizedLevenshtein Reference = new();

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
    [InlineData("a", "a")]
    [InlineData("a", "b")]
    [InlineData("ab", "ba")]
    [InlineData("", "")]
    [InlineData("", "abc")]
    [InlineData("abc", "")]
    [InlineData("straße", "strasse")]
    [InlineData("😀party", "party😀")]
    public void It_gives_the_number_of_the_library_for_known_pairs(string a, string b)
    {
        LuiSimilarity.Levenshtein.Similarity(a, b).ShouldBe(Reference.Similarity(a, b), 0);
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

            LuiSimilarity.Levenshtein.Similarity(a, b)
                .ShouldBe(Reference.Similarity(a, b), 0, $"for '{a}' and '{b}'");
        }
    }

    [Fact]
    public void It_gives_the_number_of_the_library_for_pairs_of_every_length()
    {
        // every length against every length: the distance is at least the difference of the lengths, and
        // the table of the distances is filled row by row
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

                    LuiSimilarity.Levenshtein.Similarity(a, b)
                        .ShouldBe(Reference.Similarity(a, b), 0, $"for '{a}' and '{b}'");
                }
            }
        }
    }

    [Fact]
    public void A_long_text_is_compared_too()
    {
        // the rows of the table do not fit on the stack any more
        var a = "kuchen" + new string('x', 500);
        var b = "kuchen" + new string('x', 499) + "y";

        LuiSimilarity.Levenshtein.Similarity(a, b).ShouldBe(Reference.Similarity(a, b), 0);
    }

    [Fact]
    public void The_measure_allocates_nothing()
    {
        // the reason for the own implementation: a query compares up to 500 candidates
        LuiSimilarity.Levenshtein.Similarity("apfelkuch", "gedeckter apfelkuchen mit streuseln");

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            LuiSimilarity.Levenshtein.Similarity("apfelkuch", "gedeckter apfelkuchen mit streuseln");
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        allocated.ShouldBe(0, "the similarity of a query must not allocate");
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
