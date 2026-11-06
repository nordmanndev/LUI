namespace Nordmann.LUI.Schema;

/// <summary>
/// The options of an index description besides its kind, key and payload (review of 2026-11-04, C.1, issue #40): the
/// similarity of the queries, the German transliteration and the typo tolerance. One record, so that a builder that
/// becomes another builder — <c>LinkTo</c> — passes all of them on at once: an
/// option copied by hand is an option that can be forgotten (PR #12).
/// <para>
/// "Options", as in <c>LuiCatalogOptions</c>, <c>LuiQueryOptions</c>, <c>LuiSyncOptions</c>: what the builder holds, not
/// what is stored. The stored contract is the <see cref="IndexDefinition"/>; of these options only the transliteration
/// goes into it, when the index is created (review of pull request 45, M1).
/// </para>
/// </summary>
/// <param name="Similarity">The similarity measure; <see langword="null"/> takes the default of the kind.</param>
/// <param name="GermanTransliteration">Whether <c>ä → ae</c>, <c>ß → ss</c> is applied.</param>
/// <param name="TypoTolerance">Whether an input without a word start is corrected.</param>
internal sealed record IndexOptions(ILuiSimilarity? Similarity, bool GermanTransliteration, bool TypoTolerance)
{
    /// <summary>The options of a description that changed none: the kind's similarity, transliteration on, typo tolerance off.</summary>
    internal static IndexOptions Default { get; } = new(Similarity: null, GermanTransliteration: true, TypoTolerance: false);
}
