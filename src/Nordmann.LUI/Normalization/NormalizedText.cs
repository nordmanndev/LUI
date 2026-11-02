namespace Nordmann.LUI.Normalization;

/// <summary>
/// A text in the form LUI searches and compares in, plus the map back to the
/// original.
/// </summary>
public sealed class NormalizedText
{
    internal NormalizedText(string searchForm, string compactForm, int[] sourceOffsets)
    {
        SearchForm = searchForm;
        CompactForm = compactForm;
        SourceOffsets = sourceOffsets;
    }

    /// <summary>
    /// The search form: lower case, without diacritics, with the German
    /// transliteration applied. This is what the index holds and what the
    /// query is compared against.
    /// </summary>
    public string SearchForm { get; }

    /// <summary>
    /// The compact form: the search form without the separators — so that
    /// „apfelkuchen" finds „Apfel Kuchen".
    /// </summary>
    public string CompactForm { get; }

    /// <summary>
    /// The position in the original text of every character of the search
    /// form. The normalisation changes the length (ü becomes ue, a combining
    /// mark disappears), so the position of a match in the original text
    /// cannot be computed from the search form alone.
    /// </summary>
    public IReadOnlyList<int> SourceOffsets { get; }

    /// <summary>
    /// The position in the original text of the character at
    /// <paramref name="searchFormIndex"/>, or -1 when the index is outside
    /// the search form.
    /// </summary>
    public int SourceOffset(int searchFormIndex) =>
        searchFormIndex >= 0 && searchFormIndex < SourceOffsets.Count
            ? SourceOffsets[searchFormIndex]
            : -1;
}
