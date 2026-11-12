using F23.StringSimilarity;
using Shouldly;

namespace Nordmann.LUI.Unit.Tests.Similarity;

/// <summary>
/// LUI's own Jaro-Winkler allocates nothing; it must give the same number as the library it replaces
/// (<c>F23.StringSimilarity.JaroWinkler</c>) — the measurements of the specification are measurements of that
/// definition (§11). The parity is the proof; the allocation is the reason (issue #30).
/// </summary>
public sealed class JaroWinklerEquivalenceTests
{
    private static readonly JaroWinkler Reference = new();

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
        LuiSimilarity.JaroWinkler.Similarity(a, b).ShouldBe(Reference.Similarity(a, b), 0);
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

            var expected = Reference.Similarity(a, b);
            if (double.IsNaN(expected))
            {
                continue;
            }

            LuiSimilarity.JaroWinkler.Similarity(a, b).ShouldBe(expected, 1e-12, $"for '{a}' and '{b}'");
        }
    }

    [Fact]
    public void It_gives_the_number_of_the_library_for_pairs_of_every_length()
    {
        // every length against every length: the match window changes with the length, and a difference in
        // the window shows up only at the edges
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

                    LuiSimilarity.JaroWinkler.Similarity(a, b)
                        .ShouldBe(Reference.Similarity(a, b), 0, $"for '{a}' and '{b}'");
                }
            }
        }
    }

    [Fact]
    public void A_long_text_is_compared_too()
    {
        var a = "kuchen" + new string('x', 500);
        var b = "kuchen" + new string('x', 499) + "y";

        LuiSimilarity.JaroWinkler.Similarity(a, b).ShouldBe(Reference.Similarity(a, b), 0);
    }

    [Fact]
    public void The_measure_allocates_nothing()
    {
        // the reason for the own implementation: a query compares up to 500 candidates

        // the warm-up repeats and uses its result: the type initialisation, the first call and the
        // tiering of the measure belong to the start of the run, not to the measured window. A call
        // whose result is discarded may be removed by an optimising JIT, and a one-off cost then
        // lands inside the window (issue #132: 1912 bytes on a runner).
        var warm = 0d;
        for (var i = 0; i < 100; i++)
        {
            warm += LuiSimilarity.JaroWinkler.Similarity("apfelkuch", "gedeckter apfelkuchen mit streuseln");
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            LuiSimilarity.JaroWinkler.Similarity("apfelkuch", "gedeckter apfelkuchen mit streuseln");
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        warm.ShouldBeGreaterThan(0d, "the warm-up has to run, otherwise the window measures the start of the run");
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
