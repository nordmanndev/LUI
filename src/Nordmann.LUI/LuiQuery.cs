
namespace Nordmann.LUI;

/// <summary>
/// A query being described (LUI-Spezifikation §12.5). Every method returns
/// the query and checks its value at once; <see cref="Execute"/> runs it.
/// </summary>
public sealed class LuiQuery
{
    private readonly ILuiIndex _index;
    private readonly string _text;
    private LuiQueryOptions _options = new();

    internal LuiQuery(ILuiIndex index, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _index = index;
        _text = text;
    }

    /// <summary>How many hits come back. Default: 20.</summary>
    /// <param name="maxResults">1 … <see cref="LuiQueryOptions.MaxResultsLimit"/>.</param>
    /// <returns>The query.</returns>
    public LuiQuery Take(int maxResults)
    {
        _options = _options with { MaxResults = maxResults };
        return this;
    }

    /// <summary>How many candidates are ranked. Default: 500.</summary>
    /// <param name="candidates">1 … <see cref="LuiQueryOptions.CandidatesLimit"/>.</param>
    /// <returns>The query.</returns>
    public LuiQuery Candidates(int candidates)
    {
        _options = _options with { Candidates = candidates };
        return this;
    }

    /// <summary>The order of the ranking.</summary>
    /// <param name="order">The order.</param>
    /// <returns>The query.</returns>
    public LuiQuery Rank(LuiRankingOrder order)
    {
        _options = _options with { Rank = order };
        return this;
    }

    /// <summary>What the priority does in the ranking.</summary>
    /// <param name="mode">The rule.</param>
    /// <returns>The query.</returns>
    public LuiQuery PriorityMode(LuiPriorityMode mode)
    {
        _options = _options with { PriorityMode = mode };
        return this;
    }

    /// <summary>Overrides the default similarity measure of the index.</summary>
    /// <param name="similarity">The measure.</param>
    /// <returns>The query.</returns>
    public LuiQuery Similarity(ILuiSimilarity similarity)
    {
        ArgumentNullException.ThrowIfNull(similarity);
        _options = _options with { Similarity = similarity };
        return this;
    }

    /// <summary>
    /// Corrects an input without a word start by one edit (§10). Overrides the setting of the index; the
    /// dictionary is built on the first use.
    /// </summary>
    /// <param name="enabled">Whether to correct.</param>
    /// <returns>The query.</returns>
    public LuiQuery TypoTolerance(bool enabled = true)
    {
        _options = _options with { TypoTolerance = enabled };
        return this;
    }

    /// <summary>Measures where the time of the query goes.</summary>
    /// <param name="track">Whether to measure.</param>
    /// <returns>The query.</returns>
    public LuiQuery TrackTimings(bool track = true)
    {
        _options = _options with { TrackTimings = track };
        return this;
    }

    /// <summary>Runs the query.</summary>
    /// <param name="cancellationToken">Checked while the candidates are read.</param>
    /// <returns>The answer.</returns>
    public LuiResult Execute(CancellationToken cancellationToken = default) =>
        _index.Search(_text, _options, cancellationToken);
}
