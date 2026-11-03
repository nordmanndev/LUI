namespace Nordmann.LUI;

/// <summary>
/// The order in which the candidates are ranked, after the priority
/// (LUI-Spezifikation §9.2).
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
