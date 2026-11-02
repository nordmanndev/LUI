using System.Text;

namespace Nordmann.LUI.Normalization;

/// <summary>
/// Turns a text into the form LUI searches and compares in.
/// <para>
/// The rule has two halves, and they have to agree: the SQL side (the
/// generated columns of the index) and the C# side (the query, the ranking,
/// the similarity). The C# side reproduces the folding of SQLite's trigram
/// tokenizer exactly — see <see cref="TokenizerFold"/> and
/// <c>docs/normalization.md</c>.
/// </para>
/// </summary>
public static class Normalizer
{
    /// <summary>
    /// The characters the compact form drops: the ASCII punctuation and the
    /// Unicode dashes, quotes and the ellipsis. The SQL side strips exactly
    /// the same set — see <see cref="SqlNormalizer"/>.
    /// </summary>
    internal const string Separators =
        " !\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~"
        + "\u00A0\u2010\u2011\u2012\u2013\u2014\u2015\u2018\u2019\u201A\u201B\u201C\u201D\u201E\u2026";

    private static readonly bool[] AsciiSeparators = BuildAsciiSeparators();
    private static readonly HashSet<char> UnicodeSeparators = BuildUnicodeSeparators();
    private static readonly string?[] GermanReplacements = BuildGermanReplacements();

    /// <summary>
    /// Normalises a text and keeps the map back to the original — needed to
    /// point at a match in the text the user sees.
    /// </summary>
    public static NormalizedText Normalize(string text, NormalizationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        var german = (options ?? NormalizationOptions.Default).GermanTransliteration;
        var search = new StringBuilder(text.Length);
        var compact = new StringBuilder(text.Length);
        var offsets = new List<int>(text.Length);

        for (var i = 0; i < text.Length; i++)
        {
            Append(text[i], german, search, compact, offsets, i);
        }

        return new NormalizedText(search.ToString(), compact.ToString(), [.. offsets]);
    }

    /// <summary>
    /// The search form of a text: what the index holds and what a query is
    /// compared against.
    /// </summary>
    public static string ToSearchForm(string text, NormalizationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        var german = (options ?? NormalizationOptions.Default).GermanTransliteration;
        var search = new StringBuilder(text.Length);

        for (var i = 0; i < text.Length; i++)
        {
            Append(text[i], german, search, null, null, i);
        }

        return search.ToString();
    }

    /// <summary>
    /// The compact form of a text: the search form without the separators, so
    /// that „apfelkuchen" finds „Apfel Kuchen".
    /// </summary>
    public static string ToCompactForm(string text, NormalizationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        var german = (options ?? NormalizationOptions.Default).GermanTransliteration;
        var compact = new StringBuilder(text.Length);

        for (var i = 0; i < text.Length; i++)
        {
            Append(text[i], german, null, compact, null, i);
        }

        return compact.ToString();
    }

    private static void Append(
        char source,
        bool german,
        StringBuilder? search,
        StringBuilder? compact,
        List<int>? offsets,
        int sourceIndex)
    {
        var replacement = german ? German(source) : null;

        if (replacement is null)
        {
            AppendFolded(source, search, compact, offsets, sourceIndex);
            return;
        }

        foreach (var ch in replacement)
        {
            AppendFolded(ch, search, compact, offsets, sourceIndex);
        }
    }

    private static void AppendFolded(
        char source,
        StringBuilder? search,
        StringBuilder? compact,
        List<int>? offsets,
        int sourceIndex)
    {
        var folded = TokenizerFold.Fold(source);
        if (folded == TokenizerFold.Removed)
        {
            return;
        }

        search?.Append(folded);
        offsets?.Add(sourceIndex);

        if (compact is not null && !IsSeparator(folded))
        {
            compact.Append(folded);
        }
    }

    /// <summary>
    /// The German transliteration of a character, or null when it has none.
    /// </summary>
    private static string? German(char value)
    {
        if (value < 0x100)
        {
            return GermanReplacements[value];
        }

        // ẞ — the capital sharp s, which the Latin-1 range does not hold.
        return value == 'ẞ' ? "ss" : null;
    }

    private static bool IsSeparator(char value) =>
        value < 0x80 ? AsciiSeparators[value] : UnicodeSeparators.Contains(value);

    private static bool[] BuildAsciiSeparators()
    {
        var separators = new bool[0x80];

        foreach (var ch in Separators)
        {
            if (ch < 0x80)
            {
                separators[ch] = true;
            }
        }

        return separators;
    }

    private static HashSet<char> BuildUnicodeSeparators()
    {
        var separators = new HashSet<char>();

        foreach (var ch in Separators)
        {
            if (ch >= 0x80)
            {
                separators.Add(ch);
            }
        }

        return separators;
    }

    private static string?[] BuildGermanReplacements()
    {
        var replacements = new string?[0x100];

        replacements['ä'] = "ae";
        replacements['Ä'] = "ae";
        replacements['ö'] = "oe";
        replacements['Ö'] = "oe";
        replacements['ü'] = "ue";
        replacements['Ü'] = "ue";
        replacements['ß'] = "ss";

        return replacements;
    }
}
