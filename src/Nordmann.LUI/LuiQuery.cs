using Nordmann.LUI.Similarity;

namespace Nordmann.LUI;

/// <summary>
/// A query being described (LUI-Spezifikation §12.5). Every method returns
/// the query; <see cref="Execute"/> runs it.
/// </summary>
/// <typeparam name="TKey">The key of the application.</typeparam>
/// <typeparam name="TPayload">The payload that comes back with a hit.</typeparam>
public sealed class LuiQuery<TKey, TPayload>
{
    private readonly ILuiIndex<TKey, TPayload> _index;
    private readonly string _text;
    private LuiQueryOptions _options = new();

    internal LuiQuery(ILuiIndex<TKey, TPayload> index, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _index = index;
        _text = text;
    }

    /// <summary>How many hits come back. Default: 20.</summary>
    /// <param name="maxResults">1 … <see cref="LuiQueryOptions.MaxResultsLimit"/>.</param>
    /// <returns>The query.</returns>
    public LuiQuery<TKey, TPayload> Take(int maxResults)
    {
        _options = _options with { MaxResults = maxResults };
        return this;
    }

    /// <summary>How many candidates are ranked. Default: 500.</summary>
    /// <param name="candidates">1 … <see cref="LuiQueryOptions.CandidatesLimit"/>.</param>
    /// <returns>The query.</returns>
    public LuiQuery<TKey, TPayload> Candidates(int candidates)
    {
        _options = _options with { Candidates = candidates };
        return this;
    }

    /// <summary>The order of the ranking.</summary>
    /// <param name="order">The order.</param>
    /// <returns>The query.</returns>
    public LuiQuery<TKey, TPayload> Rank(RankingOrder order)
    {
        _options = _options with { Rank = order };
        return this;
    }

    /// <summary>What the priority does in the ranking.</summary>
    /// <param name="priorities">The rule.</param>
    /// <returns>The query.</returns>
    public LuiQuery<TKey, TPayload> Priorities(PriorityRanking priorities)
    {
        _options = _options with { Priorities = priorities };
        return this;
    }

    /// <summary>Shows one entry per normalised text.</summary>
    /// <param name="distinct">Whether duplicates are dropped.</param>
    /// <returns>The query.</returns>
    public LuiQuery<TKey, TPayload> DistinctText(bool distinct = true)
    {
        _options = _options with { DistinctText = distinct };
        return this;
    }

    /// <summary>Overrides the default similarity measure of the index.</summary>
    /// <param name="similarity">The measure.</param>
    /// <returns>The query.</returns>
    public LuiQuery<TKey, TPayload> Similarity(ITextSimilarity similarity)
    {
        ArgumentNullException.ThrowIfNull(similarity);
        _options = _options with { Similarity = similarity };
        return this;
    }

    /// <summary>Measures where the time of the query goes.</summary>
    /// <param name="track">Whether to measure.</param>
    /// <returns>The query.</returns>
    public LuiQuery<TKey, TPayload> TrackTimings(bool track = true)
    {
        _options = _options with { TrackTimings = track };
        return this;
    }

    /// <summary>Runs the query.</summary>
    /// <param name="cancellationToken">Checked while the candidates are read.</param>
    /// <returns>The answer.</returns>
    public LuiResult<TKey, TPayload> Execute(CancellationToken cancellationToken = default) =>
        _index.Query(_text, _options, cancellationToken);
}
