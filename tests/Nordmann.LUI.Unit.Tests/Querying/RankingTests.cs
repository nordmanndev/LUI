using Nordmann.LUI.Querying;
using Nordmann.LUI.Schema;
using Shouldly;

namespace Nordmann.LUI.Unit.Tests.Querying;

/// <summary>
/// The ranking in groups, and the lazy similarity (ADR-0008). The similarity is
/// the expensive stage — 4.5 µs and an allocation per comparison, and 500
/// candidates are 2.25 ms, more than the whole budget of §16. So the
/// candidates are grouped by the cheap stages, and the similarity is computed
/// group by group, only until the hits are full.
/// <para>
/// The promise: <b>a group that cannot reach the hits never has its similarity
/// computed</b>. This test counts the calls of the measure — a promise about
/// work is only provable by counting.
/// </para>
/// </summary>
public sealed class RankingTests
{
    [Fact]
    public void A_group_that_cannot_reach_the_hits_never_has_its_similarity_computed()
    {
        // Two groups: the first one fills the hits, the second one is never read.
        var candidates = new Scored[200];

        for (var i = 0; i < 100; i++)
        {
            // group 0: the priority of the rowid decides, and it is the highest
            candidates[i] = new Scored
            {
                RowId = RowId.Encode(LuiPriority.System, 100, (long)i),
                Text = "apfelkuchen",
                WordStart = 0
            };
        }

        for (var i = 100; i < 200; i++)
        {
            // group 6: the normal priority, a lower group
            candidates[i] = new Scored
            {
                RowId = RowId.Encode(LuiPriority.Normal, 100, (long)i),
                Text = "birnenkuchen",
                WordStart = 0
            };
        }

        var measure = new CountingSimilarity();

        var result = Ranker.Rank(
            candidates,
            candidates.Length,
            ["apfelkuch"],
            LuiRankingOrder.WordStartThenSimilarity,
            LuiPriorityMode.First,
            measure,
            Nordmann.LUI.Normalization.NormalizationOptions.Default,
            take: 20,
            CancellationToken.None);

        result.Length.ShouldBe(20);
        measure.Calls.ShouldBe(100, "only the first group is measured — the second one cannot reach the hits");
    }

    [Fact]
    public void The_whole_group_is_measured_even_when_only_a_few_of_it_reach_the_hits()
    {
        // The group is sorted as a whole, so every candidate of it is measured.
        // The promise of ADR-0008 is about the <b>group</b>: a group that is
        // never read costs nothing.
        var candidates = new Scored[500];
        for (var i = 0; i < candidates.Length; i++)
        {
            candidates[i] = new Scored
            {
                RowId = RowId.Encode(LuiPriority.Normal, 100, (long)i),
                Text = "apfelkuchen",
                WordStart = 0
            };
        }

        var measure = new CountingSimilarity();

        var result = Ranker.Rank(
            candidates,
            candidates.Length,
            ["apfelkuch"],
            LuiRankingOrder.WordStartThenSimilarity,
            LuiPriorityMode.First,
            measure,
            Nordmann.LUI.Normalization.NormalizationOptions.Default,
            take: 20,
            CancellationToken.None);

        result.Length.ShouldBe(20);
        measure.Calls.ShouldBe(500, "one group of 500 — every candidate of it is measured to sort it");
    }

    [Fact]
    public void The_weight_order_computes_no_similarity_at_all()
    {
        // "WeightOnly" is the order of the candidates; there is nothing to sort
        // and nothing to measure.
        var candidates = new Scored[300];
        for (var i = 0; i < candidates.Length; i++)
        {
            candidates[i] = new Scored
            {
                RowId = RowId.Encode(LuiPriority.Normal, 300 - i, (long)i),
                Text = "apfelkuchen",
                WordStart = 0
            };
        }

        var measure = new CountingSimilarity();

        var result = Ranker.Rank(
            candidates,
            candidates.Length,
            ["apfelkuch"],
            LuiRankingOrder.WeightOnly,
            LuiPriorityMode.First,
            measure,
            Nordmann.LUI.Normalization.NormalizationOptions.Default,
            take: 20,
            CancellationToken.None);

        result.Length.ShouldBe(20);
        measure.Calls.ShouldBe(0, "the weight order measures nothing");
    }

    /// <summary>A measure that counts how often it was asked.</summary>
    private sealed class CountingSimilarity : ILuiSimilarity
    {
        public int Calls { get; private set; }

        public double Similarity(string query, string text)
        {
            Calls++;
            return 1.0;
        }
    }
}
