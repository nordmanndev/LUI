#pragma warning disable MA0048 // the generic form of a name that has a non-generic file: <Name>OfKey.cs (ADR-0073)
namespace Nordmann.LUI;

/// <summary>
/// The answer of a search through a typed handle (ADR-0073). It is a class with reference equality: a result holds a list,
/// and a value equality over a list would be a lie.
/// </summary>
/// <typeparam name="TKey">The type of the keys of the index.</typeparam>
public sealed class LuiResult<TKey>
    where TKey : notnull
{
    /// <summary>The query that was run.</summary>
    public required string Query { get; init; }

    /// <summary>The hits, in the order of the ranking.</summary>
    public required IReadOnlyList<LuiHit<TKey>> Hits { get; init; }

    /// <summary>How many candidates the query looked at.</summary>
    public int CandidatesConsidered { get; init; }

    /// <summary>Whether more candidates were found than the query took.</summary>
    public bool Truncated { get; init; }

    /// <summary>The query after the correction of a typo; <see langword="null"/> when there was none.</summary>
    public string? CorrectedQuery { get; init; }

    /// <summary>Whether a correction was skipped because the dictionary was not built — a query never builds it — or is being built.</summary>
    public bool CorrectionSkipped { get; init; }

    /// <summary>The phases of the query, when <c>TrackTimings</c> was on.</summary>
    public LuiTimings? Timings { get; init; }

    /// <summary>The result of the index below the handle, with typed keys.</summary>
    internal static LuiResult<TKey> From(RawResult result, string indexName)
    {
        var hits = new LuiHit<TKey>[result.Hits.Count];
        for (var i = 0; i < hits.Length; i++)
        {
            hits[i] = LuiHit<TKey>.From(result.Hits[i], indexName);
        }

        return new LuiResult<TKey>
        {
            Query = result.Query,
            Hits = hits,
            CandidatesConsidered = result.CandidatesConsidered,
            Truncated = result.Truncated,
            CorrectedQuery = result.CorrectedQuery,
            CorrectionSkipped = result.CorrectionSkipped,
            Timings = result.Timings,
        };
    }
}
#pragma warning restore MA0048
