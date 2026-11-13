namespace Nordmann.LUI;

/// <summary>The answer to a query (ADR-0008).</summary>
public sealed record LuiResult
{
    /// <summary>The input, as it was given.</summary>
    public required string Query { get; init; }

    /// <summary>The best entries, best first; at most <see cref="LuiQueryOptions.MaxResults"/>.</summary>
    public required IReadOnlyList<LuiHit> Hits { get; init; }

    /// <summary>How many candidates were ranked; at most <see cref="LuiQueryOptions.Candidates"/>.</summary>
    public int CandidatesConsidered { get; init; }

    /// <summary>
    /// Whether there were more candidates than <see cref="LuiQueryOptions.Candidates"/>. For a <c>Linked</c> index it is
    /// also set when the read stopped at a second budget of rows whose text changed and is not applied yet — then the
    /// answer can be empty although matching rows exist: the index is behind, and <c>Pending()</c> / <c>ApplyPending()</c>
    /// are the way out (ADR-0017).
    /// </summary>
    public bool Truncated { get; init; }

    /// <summary>
    /// What was searched when the typo tolerance corrected the input (in search form, e.g. <c>apfelkuchen</c>
    /// for <c>apflekuchen</c>); <see langword="null"/> when nothing was corrected.
    /// </summary>
    public string? CorrectedQuery { get; init; }

    /// <summary>
    /// Whether a correction was wanted and not searched (ADR-0021, ADR-0042): the typo dictionary is not built — a query
    /// never builds it, the application asks for it with <c>WarmUpTypoDictionary()</c> —, it is being built, or its
    /// build failed. The query did not wait and did not fail: it answered with what the input found. Ask again once
    /// <see cref="ILuiIndex.TypoDictionaryState"/> is <see cref="LuiTypoDictionaryState.Current"/>.
    /// </summary>
    public bool CorrectionSkipped { get; init; }

    /// <summary>Where the time went; only set with <see cref="LuiQueryOptions.TrackTimings"/>.</summary>
    public LuiTimings? Timings { get; init; }

    /// <summary>
    /// The same answer, with the payload in the type of the application (ADR-0044) — the end of the fluent path:
    /// <c>index.Query("veg").Take(5).Execute().As&lt;TagChip&gt;()</c>. The answer stays untyped; the type is named
    /// where it is used, and nothing is read until a hit is asked for its payload.
    /// </summary>
    /// <typeparam name="TPayload">The type of the payload — the application's to know.</typeparam>
    /// <returns>The typed answer.</returns>
    public LuiTypedResult<TPayload> As<TPayload>()
    {
        var hits = new LuiTypedHit<TPayload>[Hits.Count];
        for (var i = 0; i < hits.Length; i++)
        {
            hits[i] = new LuiTypedHit<TPayload> { Hit = Hits[i] };
        }

        return new LuiTypedResult<TPayload>
        {
            Query = Query,
            Hits = hits,
            CandidatesConsidered = CandidatesConsidered,
            Truncated = Truncated,
            CorrectedQuery = CorrectedQuery,
            CorrectionSkipped = CorrectionSkipped,
            Timings = Timings,
        };
    }
}
