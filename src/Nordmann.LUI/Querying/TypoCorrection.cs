using Microsoft.Data.Sqlite;
using Nordmann.LUI.Schema;
using Nordmann.LUI.Typos;

namespace Nordmann.LUI.Querying;

/// <summary>
/// The second stage of a query, when it is wanted (LUI-Spezifikation §10.2, §10.3): an input without a word
/// start may be a typo. Its corrections are searched like the input itself, and the candidates are united
/// with those of the input; the ranking then measures each candidate against the input it was found with.
/// </summary>
internal sealed class TypoCorrection(TypoDictionary typos, CandidateReader candidates, ContentSource source)
{
    /// <summary>Whether the correction is asked for: no candidate stands at a word start (§10.2) — not only "no hits".</summary>
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
    /// Whether the dictionary can be asked now: it is built, or this query builds it — not when another build runs, then
    /// the query answers without a correction instead of waiting for it (ADR-0021).
    /// </summary>
    internal bool IsReady(CancellationToken cancellationToken) => typos.TryEnsureBuilt(cancellationToken);

    /// <summary>
    /// The candidates of the input and of its corrections, united; the inputs that were searched (the
    /// input first); and the correction that was searched, if one still has a candidate after the cap.
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

        var united = Union(parts, options.Candidates, options.DistinctText, out var total, out var capped);

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
    /// The candidates of several inputs as one list in the order of the rowid. A document found with more than
    /// one input keeps the version that stands at a word start (§10.3: the best value per <c>doc_id</c>); with
    /// <paramref name="distinct"/> one entry per normalised text remains, again the one at a word start first. At
    /// most <paramref name="limit"/> remain, and the cap keeps the word starts first — they are what the
    /// correction was for (review M2): hits in the middle of words, in rowid order, fill what is left.
    /// </summary>
    private static Scored[] Union(List<(Scored[] Items, int Count)> parts, int limit, bool distinct, out int count, out bool cut)
    {
        var all = new List<Scored>();
        foreach (var (items, found) in parts)
        {
            all.AddRange(items.AsSpan(0, found));
        }

        all.Sort(static (a, b) =>
        {
            var byRowId = a.RowId.CompareTo(b.RowId);
            return byRowId != 0 ? byRowId : BestFirst(a, b);
        });

        var united = new List<Scored>(all.Count);
        foreach (var candidate in all)
        {
            if (united.Count == 0 || united[^1].RowId != candidate.RowId)
            {
                united.Add(candidate);
            }
        }

        if (distinct)
        {
            united = DistinctByKey(united);
        }

        cut = united.Count > limit;

        var kept = new List<Scored>(Math.Min(united.Count, limit));
        kept.AddRange(united.Where(static c => c.WordStart >= 0).Take(limit));
        kept.AddRange(united.Where(static c => c.WordStart < 0).Take(limit - kept.Count));
        kept.Sort(static (a, b) => a.RowId.CompareTo(b.RowId));

        count = kept.Count;
        return [.. kept];
    }

    /// <summary>A word start first, then the earlier input.</summary>
    private static int BestFirst(in Scored a, in Scored b)
    {
        var byStart = (b.WordStart >= 0).CompareTo(a.WordStart >= 0);
        return byStart != 0 ? byStart : a.Variant.CompareTo(b.Variant);
    }

    /// <summary>One entry per normalised text: the one at a word start, then the better rowid (§9.2).</summary>
    private static List<Scored> DistinctByKey(List<Scored> candidates)
    {
        var best = new Dictionary<string, Scored>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            var key = candidate.Key ?? candidate.Text;
            if (!best.TryGetValue(key, out var kept)
                || (candidate.WordStart >= 0 && kept.WordStart < 0)
                || ((candidate.WordStart >= 0) == (kept.WordStart >= 0) && candidate.RowId < kept.RowId))
            {
                best[key] = candidate;
            }
        }

        var result = new List<Scored>(best.Values);
        result.Sort(static (a, b) => a.RowId.CompareTo(b.RowId));
        return result;
    }
}
