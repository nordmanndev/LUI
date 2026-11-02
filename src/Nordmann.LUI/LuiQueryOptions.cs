using Nordmann.LUI.Similarity;

namespace Nordmann.LUI;

/// <summary>
/// The options of a query (LUI-Spezifikation §12.5), for configuration that
/// comes from a file. What follows from the kind of the index — how the input
/// is searched, the prefix path, the word-start rule — is not an option.
/// </summary>
public sealed record LuiQueryOptions
{
    /// <summary>The largest <see cref="MaxResults"/>: the hits are read in one statement.</summary>
    public const int MaxResultsLimit = 1000;

    /// <summary>The largest <see cref="Candidates"/>: ranking more than this is not an autocomplete.</summary>
    public const int CandidatesLimit = 100_000;

    /// <summary>How many hits come back. Default: 20.</summary>
    public int MaxResults { get; init; } = 20;

    /// <summary>
    /// How many candidates are ranked. Default: 500 — what is not among the
    /// candidates is not ranked, so the candidates are chosen by priority and
    /// weight (E2, E10).
    /// </summary>
    public int Candidates { get; init; } = 500;

    /// <summary>The order of the ranking. Default: word start, similarity, weight.</summary>
    public RankingOrder Rank { get; init; } = RankingOrder.WordStartThenSimilarity;

    /// <summary>What the priority does in the ranking. Default: it comes first.</summary>
    public PriorityRanking Priorities { get; init; } = PriorityRanking.First;

    /// <summary>
    /// Shows one entry per normalised text: the one with the highest priority,
    /// then the highest weight. Default: off.
    /// </summary>
    public bool DistinctText { get; init; }

    /// <summary>The similarity measure; <see langword="null"/> takes the default of the index.</summary>
    public ITextSimilarity? Similarity { get; init; }

    /// <summary>Measures where the time of the query goes. Default: off.</summary>
    public bool TrackTimings { get; init; }
}
