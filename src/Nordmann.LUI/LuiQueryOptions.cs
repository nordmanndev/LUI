using Nordmann.LUI.Schema;


namespace Nordmann.LUI;

/// <summary>
/// The options of a query (LUI-Spezifikation §12.5), for configuration that
/// comes from a file. What follows from the kind of the index — how the input
/// is searched, the prefix path, the word-start rule — is not an option. Every
/// value is checked when it is set, and the exception names the property (bound through
/// <c>IConfiguration.Bind</c>, it arrives wrapped in a
/// <see cref="System.Reflection.TargetInvocationException"/>).
/// </summary>
public sealed record LuiQueryOptions
{
    /// <summary>The largest <see cref="MaxResults"/>: the hits are read in one statement.</summary>
    public const int MaxResultsLimit = 1000;

    /// <summary>The largest <see cref="Candidates"/>: ranking more than this is not an autocomplete.</summary>
    public const int CandidatesLimit = 100_000;

    /// <summary>How many hits come back. Default: 20.</summary>
    public int MaxResults
    {
        get;
        init => field = Setting.InRange(value, 1, MaxResultsLimit, nameof(MaxResults));
    } = 20;

    /// <summary>
    /// How many candidates are ranked. Default: 500 — what is not among the
    /// candidates is not ranked, so the candidates are chosen by priority and
    /// weight (E2, E10).
    /// </summary>
    public int Candidates
    {
        get;
        init => field = Setting.InRange(value, 1, CandidatesLimit, nameof(Candidates));
    } = 500;

    /// <summary>The order of the ranking. Default: word start, similarity, weight.</summary>
    public LuiRankingOrder Rank
    {
        get;
        init => field = Setting.Defined(value, nameof(Rank));
    } = LuiRankingOrder.WordStartThenSimilarity;

    /// <summary>What the priority does in the ranking. Default: it comes first.</summary>
    public LuiPriorityMode PriorityMode
    {
        get;
        init => field = Setting.Defined(value, nameof(PriorityMode));
    } = LuiPriorityMode.First;

    /// <summary>The similarity measure; <see langword="null"/> takes the default of the index.</summary>
    public ILuiSimilarity? Similarity { get; init; }

    /// <summary>
    /// Whether an input without a word start is corrected (§10). <see langword="null"/> takes the distance of the
    /// index. <see langword="true"/> asks for the correction — but only within the distance of the index: a query
    /// cannot widen it, and a distance of <see cref="LuiTypoDistance.None"/> forgives nothing at all.
    /// </summary>
    public bool? TypoTolerance { get; init; }

    /// <summary>Measures where the time of the query goes. Default: off.</summary>
    public bool TrackTimings { get; init; }
}
