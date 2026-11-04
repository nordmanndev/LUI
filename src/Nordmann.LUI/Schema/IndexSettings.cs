namespace Nordmann.LUI.Schema;

/// <summary>
/// The settings of an index description besides its kind, key and payload (review of 2026-11-04, C.1, issue #40): the
/// similarity of the queries, the German transliteration (part of the definition) and the typo tolerance. One record,
/// so that a builder that changes its key or payload — <c>WithKey</c>, <c>WithPayload</c>, <c>LinkTo</c> — passes all of
/// them on at once: a setting copied by hand is a setting that can be forgotten (PR #12).
/// </summary>
/// <param name="Similarity">The similarity measure; <see langword="null"/> takes the default of the kind.</param>
/// <param name="GermanTransliteration">Whether <c>ä → ae</c>, <c>ß → ss</c> is applied.</param>
/// <param name="TypoTolerance">Whether an input without a word start is corrected.</param>
internal sealed record IndexSettings(ILuiSimilarity? Similarity, bool GermanTransliteration, bool TypoTolerance)
{
    /// <summary>The settings of a description that changed none: the kind's similarity, transliteration on, typo tolerance off.</summary>
    internal static IndexSettings Default { get; } = new(Similarity: null, GermanTransliteration: true, TypoTolerance: false);
}
