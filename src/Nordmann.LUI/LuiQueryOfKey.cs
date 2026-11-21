#pragma warning disable MA0048 // the generic form of a name that has a non-generic file: <Name>OfKey.cs (ADR-0073)
namespace Nordmann.LUI;

/// <summary>
/// A query that is configured step by step and then executed (ADR-0073): every method returns the query, and
/// <see cref="Execute"/> runs it. The settings are those of <see cref="LuiQueryOptions"/>; the answer has typed keys.
/// </summary>
/// <typeparam name="TKey">The type of the keys of the index.</typeparam>
public sealed class LuiQuery<TKey>
    where TKey : notnull
{
    private readonly LuiIndex<TKey> _index;
    private readonly string _text;
    private LuiQueryOptions _options = new();

    internal LuiQuery(LuiIndex<TKey> index, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _index = index;
        _text = text;
    }

    /// <summary>How many hits the query returns, 1 to 1000; the default is 20.</summary>
    /// <param name="maxResults">The number of hits.</param>
    /// <returns>The query.</returns>
    public LuiQuery<TKey> Take(int maxResults)
    {
        _options = _options with { MaxResults = maxResults };
        return this;
    }

    /// <summary>How many candidates the query looks at, 1 to 100,000; the default is 500.</summary>
    /// <param name="candidates">The number of candidates.</param>
    /// <returns>The query.</returns>
    public LuiQuery<TKey> Candidates(int candidates)
    {
        _options = _options with { Candidates = candidates };
        return this;
    }

    /// <summary>The order of the hits.</summary>
    /// <param name="order">The ranking.</param>
    /// <returns>The query.</returns>
    public LuiQuery<TKey> Rank(LuiRankingOrder order)
    {
        _options = _options with { Rank = order };
        return this;
    }

    /// <summary>How the priority of an entry counts.</summary>
    /// <param name="mode">The mode.</param>
    /// <returns>The query.</returns>
    public LuiQuery<TKey> PriorityMode(LuiPriorityMode mode)
    {
        _options = _options with { PriorityMode = mode };
        return this;
    }

    /// <summary>The measure of the ranking, for this query.</summary>
    /// <param name="similarity">The measure.</param>
    /// <returns>The query.</returns>
    public LuiQuery<TKey> Similarity(ILuiSimilarity similarity)
    {
        ArgumentNullException.ThrowIfNull(similarity);
        _options = _options with { Similarity = similarity };
        return this;
    }

    /// <summary>Forgives a typo in this query — within the distance of the index; a query cannot widen it.</summary>
    /// <param name="enabled">Whether the correction is on.</param>
    /// <returns>The query.</returns>
    public LuiQuery<TKey> TypoTolerance(bool enabled = true)
    {
        _options = _options with { TypoTolerance = enabled };
        return this;
    }

    /// <summary>Measures the phases of the query.</summary>
    /// <param name="track">Whether the phases are measured.</param>
    /// <returns>The query.</returns>
    public LuiQuery<TKey> TrackTimings(bool track = true)
    {
        _options = _options with { TrackTimings = track };
        return this;
    }

    /// <summary>Runs the query.</summary>
    /// <param name="cancellationToken">Checked while the query runs.</param>
    /// <returns>The answer.</returns>
    /// <exception cref="LuiIndexNotFoundException">The index was dropped.</exception>
    public LuiResult<TKey> Execute(CancellationToken cancellationToken = default) =>
        _index.Search(_text, _options, cancellationToken);
}
#pragma warning restore MA0048
