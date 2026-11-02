namespace Nordmann.LUI.Normalization;

/// <summary>
/// The options of the normalisation rule. They are fixed per index, because
/// the rule decides what the index contains.
/// </summary>
public sealed record NormalizationOptions
{
    /// <summary>The options every index uses unless it says otherwise.</summary>
    public static NormalizationOptions Default { get; } = new();

    /// <summary>
    /// German transliteration: <c>ä → ae</c>, <c>ö → oe</c>, <c>ü → ue</c>,
    /// <c>ß → ss</c> (and the capital letters). On by default — a German user
    /// types „mueller" and „müller", and both have to find the same entry.
    /// <para>
    /// Turn it off for corpora without German text: the rule exists for the
    /// German orthography, and on other languages it only makes the words
    /// longer.
    /// </para>
    /// </summary>
    public bool GermanTransliteration { get; init; } = true;
}
