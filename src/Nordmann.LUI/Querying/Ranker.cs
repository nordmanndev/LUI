using Nordmann.LUI.Schema;
using Nordmann.LUI.Normalization;

namespace Nordmann.LUI.Querying;

/// <summary>
/// The ranking (LUI-Spezifikation §9.2): priority → word start → similarity →
/// weight, in the order the caller chose.
/// <para>
/// The first two stages are cheap, the similarity is not (microseconds, and
/// an allocation). So the candidates are put into <b>groups</b> by what the
/// cheap stages decide, and the similarity is computed group by group, only
/// until enough hits are found. An entry that cannot reach the hits never has
/// its similarity computed.
/// </para>
/// </summary>
internal static class Ranker
{
    private const int GroupCount = 8;

    /// <summary>
    /// Orders the candidates and returns the best <paramref name="take"/>.
    /// </summary>
    internal static Scored[] Rank(
        Scored[] candidates,
        int count,
        IReadOnlyList<string> queries,
        LuiRankingOrder order,
        LuiPriorityRanking priorities,
        ILuiSimilarity similarity,
        NormalizationOptions normalization,
        int take,
        CancellationToken cancellationToken)
    {
        var wordStartStage = order is LuiRankingOrder.WordStartThenSimilarity or LuiRankingOrder.WordStartThenWeight;
        var priorityFirst = priorities == LuiPriorityRanking.First;

        // weight order with the priority first is the order the candidates came in
        if (order == LuiRankingOrder.WeightOnly && priorityFirst)
        {
            return candidates[..Math.Min(count, take)];
        }

        Span<int> starts = stackalloc int[GroupCount + 1];
        var grouped = Group(candidates, count, priorityFirst, wordStartStage, starts);

        var comparer = ComparerFor(order);
        var needsSimilarity = order != LuiRankingOrder.WeightOnly;
        var result = new List<Scored>(Math.Min(count, take));

        for (var g = 0; g < GroupCount && result.Count < take; g++)
        {
            var length = starts[g + 1] - starts[g];
            if (length == 0)
            {
                continue;
            }

            if (needsSimilarity)
            {
                for (var i = starts[g]; i < starts[g + 1]; i++)
                {
                    // the similarity is the part that can be slow — a custom measure, 100,000 candidates
                    if (((i - starts[g]) & 31) == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    grouped[i].Similarity = Measure(grouped[i], queries, similarity, normalization);
                }
            }

            Array.Sort(grouped, starts[g], length, comparer);

            for (var i = starts[g]; i < starts[g + 1] && result.Count < take; i++)
            {
                result.Add(grouped[i]);
            }
        }

        return [.. result];
    }

    /// <summary>
    /// A stable counting sort of the candidates into their groups; <paramref name="starts"/> receives
    /// where each group begins (and, in the last place, the end).
    /// </summary>
    private static Scored[] Group(Scored[] candidates, int count, bool priorityFirst, bool wordStartStage, Span<int> starts)
    {
        Span<int> sizes = stackalloc int[GroupCount];
        for (var i = 0; i < count; i++)
        {
            sizes[GroupOf(candidates[i], priorityFirst, wordStartStage)]++;
        }

        for (var g = 0; g < GroupCount; g++)
        {
            starts[g + 1] = starts[g] + sizes[g];
        }

        var grouped = new Scored[count];
        Span<int> next = stackalloc int[GroupCount];
        starts[..GroupCount].CopyTo(next);
        for (var i = 0; i < count; i++)
        {
            grouped[next[GroupOf(candidates[i], priorityFirst, wordStartStage)]++] = candidates[i];
        }

        return grouped;
    }

    /// <summary>
    /// The similarity of the input to the text of an entry, measured from the
    /// word where the match begins — or against the whole text when there is
    /// no word start (§9.2).
    /// </summary>
    internal static double Measure(
        in Scored candidate,
        IReadOnlyList<string> queries,
        ILuiSimilarity similarity,
        NormalizationOptions normalization)
    {
        var text = Normalizer.ToSearchForm(candidate.Text, normalization);
        var from = candidate.WordStart > 0 && candidate.WordStart < text.Length ? text[candidate.WordStart..] : text;
        return similarity.Similarity(queries[candidate.Variant], from);
    }

    private static int GroupOf(in Scored candidate, bool priorityFirst, bool wordStartStage)
    {
        var priority = priorityFirst ? (int)RowId.PriorityOf(candidate.RowId) : 0;
        var noWordStart = wordStartStage && candidate.WordStart < 0 ? 1 : 0;
        return (priority * 2) + noWordStart;
    }

    private static IComparer<Scored> ComparerFor(LuiRankingOrder order) => order switch
    {
        LuiRankingOrder.WordStartThenSimilarity or LuiRankingOrder.SimilarityOnly => SimilarityFirst.Instance,
        LuiRankingOrder.WordStartThenWeight => WeightFirst.Instance,
        _ => WeightOnly.Instance,
    };

    private static int ByWeight(in Scored a, in Scored b)
    {
        var byWeight = RowId.WeightOf(b.RowId).CompareTo(RowId.WeightOf(a.RowId));
        return byWeight != 0 ? byWeight : a.RowId.CompareTo(b.RowId);
    }

    private sealed class SimilarityFirst : IComparer<Scored>
    {
        internal static readonly SimilarityFirst Instance = new();

        public int Compare(Scored x, Scored y)
        {
            var bySimilarity = y.Similarity.CompareTo(x.Similarity);
            return bySimilarity != 0 ? bySimilarity : ByWeight(x, y);
        }
    }

    private sealed class WeightFirst : IComparer<Scored>
    {
        internal static readonly WeightFirst Instance = new();

        public int Compare(Scored x, Scored y)
        {
            var byWeight = RowId.WeightOf(y.RowId).CompareTo(RowId.WeightOf(x.RowId));
            if (byWeight != 0)
            {
                return byWeight;
            }

            var bySimilarity = y.Similarity.CompareTo(x.Similarity);
            return bySimilarity != 0 ? bySimilarity : x.RowId.CompareTo(y.RowId);
        }
    }

    private sealed class WeightOnly : IComparer<Scored>
    {
        internal static readonly WeightOnly Instance = new();

        public int Compare(Scored x, Scored y) => ByWeight(x, y);
    }
}
