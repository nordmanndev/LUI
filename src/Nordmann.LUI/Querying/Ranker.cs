using System.Buffers;
using System.Diagnostics;
using Nordmann.LUI.Schema;
using Nordmann.LUI.Normalization;

namespace Nordmann.LUI.Querying;

/// <summary>
/// The ranking (ADR-0013, ADR-0055): priority → word start → similarity →
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
        LuiPriorityMode priorities,
        ILuiSimilarity similarity,
        NormalizationOptions normalization,
        int take,
        CancellationToken cancellationToken)
    {
        var wordStartStage = order is LuiRankingOrder.WordStartThenSimilarity or LuiRankingOrder.WordStartThenWeight;
        var priorityFirst = priorities == LuiPriorityMode.First;

        // weight order with the priority first is the order the candidates came in
        if (order == LuiRankingOrder.WeightOnly && priorityFirst)
        {
            var taken = Math.Min(count, take);
            var ranked = ScoredBuffer.Rent(taken);
            Array.Copy(candidates, ranked, taken);
            return ranked;
        }

        Span<int> starts = stackalloc int[GroupCount + 1];
        // the length of the buffer of the groups is the length of the buffer of the candidates: it is the
        // budget, and the budget does not change from query to query (the number of candidates read does)
        var grouped = ScoredBuffer.Rent(candidates.Length);
        var buffer = ArrayPool<char>.Shared.Rent(BufferLength);

        try
        {
            Group(candidates, grouped, count, priorityFirst, wordStartStage, starts);

            var comparer = ComparerFor(order);
            var needsSimilarity = order != LuiRankingOrder.WeightOnly;
            var result = new List<Scored>(Math.Min(count, take));

            for (var g = 0; g < GroupCount && result.Count < take; g++)
            {
                if (starts[g + 1] == starts[g])
                {
                    continue;
                }

                if (needsSimilarity)
                {
                    MeasureGroup(grouped, starts, g, queries, similarity, normalization, ref buffer, cancellationToken);
                }

                Array.Sort(grouped, starts[g], starts[g + 1] - starts[g], comparer);

                for (var i = starts[g]; i < starts[g + 1] && result.Count < take; i++)
                {
                    result.Add(grouped[i]);
                }
            }

            var ranked = ScoredBuffer.Rent(result.Count);
            for (var i = 0; i < result.Count; i++)
            {
                ranked[i] = result[i];
            }

            return ranked;
        }
        finally
        {
            ScoredBuffer.Return(grouped);
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// The similarity of every candidate of a group — the part that can be slow: a custom measure,
    /// 100,000 candidates.
    /// </summary>
    private static void MeasureGroup(
        Scored[] grouped,
        ReadOnlySpan<int> starts,
        int group,
        IReadOnlyList<string> queries,
        ILuiSimilarity similarity,
        NormalizationOptions normalization,
        ref char[] buffer,
        CancellationToken cancellationToken)
    {
        for (var i = starts[group]; i < starts[group + 1]; i++)
        {
            if (((i - starts[group]) & 31) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            grouped[i].Similarity = Measure(grouped[i], queries, similarity, normalization, ref buffer);
        }
    }

    /// <summary>
    /// A stable counting sort of the candidates into their groups; <paramref name="starts"/> receives
    /// where each group begins (and, in the last place, the end). The candidates are sorted into
    /// <paramref name="grouped"/> — a buffer of its own, reused between the queries (issue #30).
    /// </summary>
    private static void Group(Scored[] candidates, Scored[] grouped, int count, bool priorityFirst, bool wordStartStage, Span<int> starts)
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

        Span<int> next = stackalloc int[GroupCount];
        starts[..GroupCount].CopyTo(next);
        for (var i = 0; i < count; i++)
        {
            grouped[next[GroupOf(candidates[i], priorityFirst, wordStartStage)]++] = candidates[i];
        }
    }

    /// <summary>
    /// The similarity of the input to the text of an entry, measured from the
    /// word where the match begins — or against the whole text when there is
    /// no word start (ADR-0013, ADR-0055).
    /// <para>
    /// A measure of LUI is asked on the search form in <paramref name="buffer"/>: the form is written
    /// into the buffer and measured there, so neither the form nor the part of it the match begins at
    /// becomes a string — up to 500 candidates per query (issue #30). A measure of an application that
    /// only speaks strings is measured the old way.
    /// </para>
    /// </summary>
    internal static double Measure(
        in Scored candidate,
        IReadOnlyList<string> queries,
        ILuiSimilarity similarity,
        NormalizationOptions normalization,
        ref char[] buffer)
    {
        if (similarity is ISpanSimilarity span)
        {
            var needed = (candidate.Text.Length * 3) + 1;
            if (buffer.Length < needed)
            {
                ArrayPool<char>.Shared.Return(buffer);
                buffer = ArrayPool<char>.Shared.Rent(needed);
            }

            if (!Normalizer.TryWriteSearchForm(candidate.Text, buffer, out var written, normalization))
            {
                // the transliteration at most doubles a text (ä → ae, ß → ss); the buffer holds three times
                throw new UnreachableException("The search form of a text is longer than three times the text.");
            }

            var form = buffer.AsSpan(0, written);
            var from = candidate.WordStart > 0 && candidate.WordStart < form.Length ? form[candidate.WordStart..] : form;
            return span.Similarity(queries[candidate.Variant].AsSpan(), from);
        }

        var text = Normalizer.ToSearchForm(candidate.Text, normalization);
        var fromString = candidate.WordStart > 0 && candidate.WordStart < text.Length ? text[candidate.WordStart..] : text;
        return similarity.Similarity(queries[candidate.Variant], fromString);
    }

    /// <summary>The length of the buffer of the search form to start with.</summary>
    private const int BufferLength = 256;

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
