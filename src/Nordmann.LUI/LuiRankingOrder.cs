namespace Nordmann.LUI;

/// <summary>
/// The order in which the candidates are ranked, after the priority
/// (ADR-0013, ADR-0055).
/// </summary>
public enum LuiRankingOrder
{
    /// <summary>The default: word start, then similarity, then weight.</summary>
    WordStartThenSimilarity,

    /// <summary>Word start, then weight, then similarity.</summary>
    WordStartThenWeight,

    /// <summary>Similarity, then weight.</summary>
    SimilarityOnly,

    /// <summary>Weight only — the order of the candidates, no similarity is computed for the ranking.</summary>
    WeightOnly,
}
