namespace Nordmann.LUI;

/// <summary>
/// A similarity that works on spans — the way the ranking measures the candidates (issue #30).
/// <para>
/// The public <see cref="ILuiSimilarity"/> is the contract for an application that brings its own
/// measure; it takes strings, so the text of a candidate would have to become a string for every
/// comparison — up to 500 of them per query. A measure of LUI implements this interface as well, and
/// the ranking asks it on the search form in its buffer: no string, no allocation.
/// </para>
/// </summary>
internal interface ISpanSimilarity
{
    /// <summary>The similarity of two texts: a number between 0 and 1.</summary>
    /// <param name="query">The search form of the input.</param>
    /// <param name="text">The search form of the entry.</param>
    double Similarity(ReadOnlySpan<char> query, ReadOnlySpan<char> text);
}
