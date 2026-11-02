using Nordmann.LUI.Similarity;
using Shouldly;

namespace Nordmann.LUI.Tests;

/// <summary>The similarity measures (LUI-Spezifikation §11).</summary>
public sealed class SimilarityTests
{
    public static TheoryData<string> Measures => new()
    {
        { nameof(Similarity.Similarity.JaroWinkler) },
        { nameof(Similarity.Similarity.NgramDice) },
        { nameof(Similarity.Similarity.DamerauLevenshtein) },
        { nameof(Similarity.Similarity.Levenshtein) },
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
        ReferenceEquals(Similarity.Similarity.DefaultFor(IndexKind.SingleWord), Similarity.Similarity.JaroWinkler).ShouldBeTrue();
        ReferenceEquals(Similarity.Similarity.DefaultFor(IndexKind.MultiWord), Similarity.Similarity.NgramDice).ShouldBeTrue();
    }

    private static ITextSimilarity Get(string name) => name switch
    {
        nameof(Similarity.Similarity.JaroWinkler) => Similarity.Similarity.JaroWinkler,
        nameof(Similarity.Similarity.NgramDice) => Similarity.Similarity.NgramDice,
        nameof(Similarity.Similarity.DamerauLevenshtein) => Similarity.Similarity.DamerauLevenshtein,
        nameof(Similarity.Similarity.Levenshtein) => Similarity.Similarity.Levenshtein,
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };
}
