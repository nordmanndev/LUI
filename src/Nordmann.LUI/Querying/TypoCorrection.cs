using Microsoft.Data.Sqlite;
using Nordmann.LUI.Schema;
using Nordmann.LUI.Typos;

namespace Nordmann.LUI.Querying;

/// <summary>
/// The second stage of a query, when it is wanted (ADR-0012, ADR-0012): an input without a word
/// start may be a typo. Its corrections are searched like the input itself, and the candidates are united
/// with those of the input; the ranking then measures each candidate against the input it was found with.
/// </summary>
internal sealed class TypoCorrection(TypoDictionary typos, CandidateReader candidates, ContentSource source)
{
    /// <summary>Whether the correction is asked for: no candidate stands at a word start (ADR-0012) — not only "no hits".</summary>
    internal static bool IsWanted(Scored[] candidates, int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (candidates[i].WordStart >= 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether the dictionary can be asked now: it is built. A query does <b>not</b> build it (ADR-0042, issue #35) — the
    /// build belongs to the application, which asks for it with <c>WarmUpTypoDictionary</c>, and to the rebuild and to
    /// the writes. A query that finds no dictionary answers with what the input found and says that it skipped the
    /// correction (ADR-0021).
    /// </summary>
    internal bool IsReady() => typos.IsBuilt;

    /// <summary>
    /// The candidates of the input and of its corrections, united; the inputs that were searched (the
    /// input first); and the correction that was searched, if one still has a candidate after the cap. It does not build
    /// the dictionary: the caller asks <see cref="IsReady"/> first — a dictionary that is not built gives no correction
    /// (review of pull request 34, M8).
    /// </summary>
    internal (Scored[] Candidates, int Count, bool Truncated, IReadOnlyList<string> Queries, string? Corrected) Apply(
        SqliteConnection connection,
        SqliteTransaction snapshot,
        PreparedQuery query,
        LuiQueryOptions options,
        Scored[] found,
        int count,
        bool truncated,
        CancellationToken cancellationToken)
    {
        var queries = new List<string> { query.Search };
        var parts = new List<(Scored[] Items, int Count)> { (found, count) };

        foreach (var text in typos.Corrections(query.Search))
        {
            var variant = PreparedQuery.Create(text, source.Normalization, source.Rules);
            if (variant.IsPrefixPath || variant.IsEmpty)
            {
                continue;
            }

            queries.Add(variant.Search);
            var items = candidates.Read(
                connection, snapshot, variant, options, (byte)(queries.Count - 1), cancellationToken, out var itemCount, out var itemsTruncated);

            truncated |= itemsTruncated;
            if (itemCount > 0)
            {
                parts.Add((items, itemCount));
            }
        }

        if (parts.Count == 1)
        {
            return (found, count, truncated, queries, null);
        }

        var united = Union(parts, options.Candidates, out var total, out var capped);

        // the candidates of the variants are in the union now: their buffers go back to the thread
        // (the buffer of the input itself belongs to the caller)
        foreach (var (items, itemCount) in parts)
        {
            if (!ReferenceEquals(items, found))
            {
                ScoredBuffer.Return(items);
            }
        }

        // what was searched: the first correction that still has a candidate after the cap (review M2)
        byte best = 0;
        for (var i = 0; i < total; i++)
        {
            if (united[i].Variant > 0 && (best == 0 || united[i].Variant < best))
            {
                best = united[i].Variant;
            }
        }

        return (united, total, truncated | capped, queries, best > 0 ? queries[best] : null);
    }

    /// <summary>
    /// The candidates of several inputs as one list in the order of the rowid.
    /// <para>
    /// The buffer of the union has always the same length: the budget times the number of the inputs
    /// (the input and up to three corrections). A length that changes from query to query would miss
    /// the pool every time and cost the union anew — 32 KB per corrected query.
    /// </para> A document found with more than
    /// one input keeps the version that stands at a word start (ADR-0012: the best value per <c>doc_id</c>). At
    /// most <paramref name="limit"/> remain, and the cap keeps the word starts first — they are what the
    /// correction was for (review M2): hits in the middle of words, in rowid order, fill what is left.
    /// </summary>
    private static Scored[] Union(List<(Scored[] Items, int Count)> parts, int limit, out int count, out bool cut)
    {
        var total = 0;
        foreach (var (_, found) in parts)
        {
            total += found;
        }

        // the buffers are reused between the queries of a thread (issue #30): four lists of the size of
        // the candidates would have cost four times the budget of 500 — about 200 KB per corrected query
        //
        var all = ScoredBuffer.Rent(limit * 4);
        if (all.Length < total)
        {
            // more corrections than the dictionary gives — the buffer has to grow
            ScoredBuffer.Return(all);
            all = ScoredBuffer.Rent(total);
        }

        var kept = ScoredBuffer.Rent(limit);

        try
        {
            var at = 0;
            foreach (var (items, found) in parts)
            {
                items.AsSpan(0, found).CopyTo(all.AsSpan(at));
                at += found;
            }

            Array.Sort(all, 0, total, ByRowIdAndWordStart);

            // one entry per document: the first after the sort is the one at a word start
            var united = 0;
            for (var i = 0; i < total; i++)
            {
                if (united == 0 || all[united - 1].RowId != all[i].RowId)
                {
                    all[united++] = all[i];
                }
            }

            cut = united > limit;

            var taken = Keep(all, united, kept, limit);
            Array.Sort(kept, 0, taken, ByRowId);

            count = taken;
            return kept;
        }
        finally
        {
            ScoredBuffer.Return(all);
        }
    }

    /// <summary>
    /// The candidates that remain: the word starts first — they are what the correction was for (review M2):
    /// hits in the middle of words, in rowid order, fill what is left.
    /// </summary>
    private static int Keep(Scored[] all, int united, Scored[] kept, int limit)
    {
        var taken = 0;
        for (var i = 0; i < united && taken < limit; i++)
        {
            if (all[i].WordStart >= 0)
            {
                kept[taken++] = all[i];
            }
        }

        for (var i = 0; i < united && taken < limit; i++)
        {
            if (all[i].WordStart < 0)
            {
                kept[taken++] = all[i];
            }
        }

        return taken;
    }

    /// <summary>The order of the candidates: the rowid, and for the same rowid the word start first.</summary>
    private static readonly IComparer<Scored> ByRowIdAndWordStart = Comparer<Scored>.Create(static (a, b) =>
    {
        var byRowId = a.RowId.CompareTo(b.RowId);
        return byRowId != 0 ? byRowId : BestFirst(a, b);
    });

    /// <summary>The order of the rowid.</summary>
    private static readonly IComparer<Scored> ByRowId = Comparer<Scored>.Create(static (a, b) => a.RowId.CompareTo(b.RowId));

    /// <summary>A word start first, then the earlier input.</summary>
    private static int BestFirst(in Scored a, in Scored b)
    {
        var byStart = (b.WordStart >= 0).CompareTo(a.WordStart >= 0);
        return byStart != 0 ? byStart : a.Variant.CompareTo(b.Variant);
    }
}
