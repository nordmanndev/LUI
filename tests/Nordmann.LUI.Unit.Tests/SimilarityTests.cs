using Nordmann.LUI.Schema;
using Shouldly;

namespace Nordmann.LUI.Unit.Tests;

/// <summary>The similarity measures (LUI-Spezifikation §11).</summary>
public sealed class SimilarityTests
{
    public static TheoryData<string> Measures => new()
    {
        { nameof(LuiSimilarity.JaroWinkler) },
        { nameof(LuiSimilarity.NgramDice) },
        { nameof(LuiSimilarity.DamerauLevenshtein) },
        { nameof(LuiSimilarity.Levenshtein) },
    };

    [Theory]
    [MemberData(nameof(Measures))]
    public void Identical_texts_are_fully_similar_and_unrelated_ones_are_not(string name)
    {
        var measure = Get(name);

        measure.Name.ShouldBe(name);
        measure.Similarity("apfelkuchen", "apfelkuchen").ShouldBe(1.0, 1e-9);
        measure.Similarity("apfelkuchen", "xyzxyzxyz").ShouldBeLessThan(0.3);
    }

    [Theory]
    [MemberData(nameof(Measures))]
    public void A_closer_text_is_more_similar(string name)
    {
        var measure = Get(name);

        measure.Similarity("apfelkuch", "apfelkuchen")
            .ShouldBeGreaterThan(measure.Similarity("apfelkuch", "apfelstrudel"));
    }

    [Theory]
    [MemberData(nameof(Measures))]
    public void The_measure_stays_between_zero_and_one(string name)
    {
        var measure = Get(name);

        foreach (var (a, b) in new[] { ("a", "b"), ("", "abc"), ("abc", ""), ("kuchen", "kuchen" + new string('x', 500)) })
        {
            measure.Similarity(a, b).ShouldBeInRange(0.0, 1.0);
        }
    }

    [Theory]
    [MemberData(nameof(Measures))]
    public void A_measure_can_be_used_from_many_threads(string name)
    {
        var measure = Get(name);
        string[] texts = ["apfelkuchen", "birnenkuchen", "gedeckter apfelkuchen", "bratapfel", "kuchen"];
        var expected = texts.Select(t => measure.Similarity("apfelkuch", t)).ToArray();

        Parallel.For(0, 4_000, i =>
        {
            var index = i % texts.Length;
            measure.Similarity("apfelkuch", texts[index]).ShouldBe(expected[index]);
        });
    }

    [Fact]
    public void Each_kind_has_the_default_the_specification_names()
    {
        // §4: Jaro-Winkler for single words, N-gram Dice for several
        ReferenceEquals(IndexKindRules.For(LuiIndexKind.SingleWord).DefaultSimilarity, LuiSimilarity.JaroWinkler).ShouldBeTrue();
        ReferenceEquals(IndexKindRules.For(LuiIndexKind.MultiWord).DefaultSimilarity, LuiSimilarity.NgramDice).ShouldBeTrue();
    }

    private static ILuiSimilarity Get(string name) => name switch
    {
        nameof(LuiSimilarity.JaroWinkler) => LuiSimilarity.JaroWinkler,
        nameof(LuiSimilarity.NgramDice) => LuiSimilarity.NgramDice,
        nameof(LuiSimilarity.DamerauLevenshtein) => LuiSimilarity.DamerauLevenshtein,
        nameof(LuiSimilarity.Levenshtein) => LuiSimilarity.Levenshtein,
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };
}
