namespace Nordmann.LUI;

/// <summary>
/// How many errors a query forgives when it corrects a typo (ADR-0012). An <b>enumeration, not a number</b>:
/// the dictionary of the word beginnings grows with every additional error — SymSpell keeps the
/// <i>delete variants</i> of every beginning, and their number grows with the distance — so the
/// possible distances are the ones that were measured, and a caller cannot ask for a tenth.
/// <para>
/// The distance belongs to the <b>index</b>, not to the query: the dictionary is built once, with the
/// distance of the index, and a query can only switch the correction on or off
/// (<c>Query(text).TypoTolerance(…)</c>). The distance is part of the stored definition, so an index
/// that is opened again knows how its dictionary was built.
/// </para>
/// <para>
/// What the distances cost in memory is measured, not promised — the numbers stand in
/// <c>docs/typo-dictionary.md</c>.
/// </para>
/// </summary>
public enum LuiTypoDistance
{
    /// <summary>
    /// No correction: a typo is not corrected. The only distance that costs no memory — an index that
    /// forgives nothing builds no dictionary at all, and its state stays <c>NotBuilt</c>.
    /// </summary>
    None = 0,

    /// <summary>
    /// One error is forgiven: a letter left out, one too many, one wrong, two swapped. The dictionary
    /// of the word beginnings costs 138 MB for 685,739 words.
    /// </summary>
    OneError = 1,

    /// <summary>
    /// Two errors are forgiven. The dictionary holds the delete variants of two deletions, so it costs
    /// 1.7 times the memory and twice the time of <see cref="OneError"/> — 240 MB for 685,739 words,
    /// and more than the budget of ADR-0020 allows. Measured, see <c>docs/typo-dictionary.md</c>.
    /// </summary>
    TwoErrors = 2,
}
