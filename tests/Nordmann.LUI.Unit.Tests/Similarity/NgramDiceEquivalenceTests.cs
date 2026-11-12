using F23.StringSimilarity;
using Shouldly;

namespace Nordmann.LUI.Unit.Tests.Similarity;

/// <summary>
/// LUI's own N-gram Dice allocates nothing; it must give the same number as the library it replaces
/// (<c>F23.StringSimilarity.SorensenDice(2)</c>) — the measurements of the specification are measurements
/// of that definition (§11).
/// </summary>
public sealed class NgramDiceEquivalenceTests
{
    private static readonly SorensenDice Reference = new(2);

    [Theory]
    [InlineData("apfelkuch", "apfelkuchen")]
    [InlineData("kuchen apfel", "apfel kuchen")]
    [InlineData("aepfeln", "apfelkuchen mit aepfeln")]
    [InlineData("ab", "ba")]
    [InlineData("abab", "baba")]
    [InlineData("aaaa", "aa")]
    [InlineData("gedeckter  apfelkuchen", "gedeckter apfelkuchen")]
    [InlineData("apfel\tkuchen", "apfel kuchen")]
    [InlineData("😀party", "party😀")]
    public void It_gives_the_number_of_the_library_for_known_pairs(string a, string b)
    {
        LuiSimilarity.NgramDice.Similarity(a, b).ShouldBe(Reference.Similarity(a, b), 1e-12);
    }

    [Fact]
    public void It_gives_the_number_of_the_library_for_ten_thousand_random_pairs()
    {
        var random = new Random(2026);
        const string alphabet = "abcdeäöß -\t";

        for (var i = 0; i < 10_000; i++)
        {
            var a = Random(random, alphabet, random.Next(2, 40));
            var b = Random(random, alphabet, random.Next(2, 60));

            var expected = Reference.Similarity(a, b);
            if (double.IsNaN(expected))
            {
                continue;
            }

            LuiSimilarity.NgramDice.Similarity(a, b).ShouldBe(expected, 1e-12, $"for '{a}' and '{b}'");
        }
    }

    [Fact]
    public void A_long_text_is_compared_too()
    {
        var a = string.Concat(Enumerable.Repeat("apfelkuchen ", 80));
        var b = string.Concat(Enumerable.Repeat("kuchenapfel ", 90));

        LuiSimilarity.NgramDice.Similarity(a, b).ShouldBe(Reference.Similarity(a, b), 1e-12);
    }

    [Fact]
    public void A_comparison_allocates_nothing()
    {
        var measure = LuiSimilarity.NgramDice;

        // the warm-up repeats and uses its result: the type initialisation, the first call and the
        // tiering of the measure belong to the start of the run, not to the measured window. A call
        // whose result is discarded may be removed by an optimising JIT, and a one-off cost then
        // lands inside the window (issue #132: 1912 bytes on a runner).
        var warm = 0d;
        for (var i = 0; i < 100; i++)
        {
            warm += measure.Similarity("apfelkuch", "gedeckter apfelkuchen mit sahne");
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1_000; i++)
        {
            measure.Similarity("apfelkuch", "gedeckter apfelkuchen mit sahne");
        }

        warm.ShouldBeGreaterThan(0d, "the warm-up has to run, otherwise the window measures the start of the run");
        (GC.GetAllocatedBytesForCurrentThread() - before).ShouldBeLessThan(1_000, "less than a byte per call");
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("a", "a")]
    [InlineData("a", "b")]
    [InlineData("", "abc")]
    public void Texts_shorter_than_a_bigram_are_identical_or_have_nothing_in_common(string a, string b)
    {
        LuiSimilarity.NgramDice.Similarity(a, b).ShouldBe(string.Equals(a, b, StringComparison.Ordinal) ? 1.0 : 0.0);
    }

    private static string Random(Random random, string alphabet, int length) =>
        string.Create(length, (random.Next(), alphabet), static (span, state) =>
        {
            var local = new Random(state.Item1);
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = state.alphabet[local.Next(state.alphabet.Length)];
            }
        });
}
