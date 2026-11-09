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
internal static class Normalizer
{
    /// <summary>
    /// The characters the compact form drops: the ASCII punctuation, the
    /// control characters that separate words, and the Unicode spaces, dashes
    /// and quotes. The SQL side strips exactly the same set — see
    /// <see cref="SqlNormalizer"/>.
    /// <para>
    /// The list is a list, not a rule: SQLite cannot express "everything that
    /// is not a letter or a digit" in a generated column, so both sides work
    /// from the same explicit set. ADR-0005 records the deviation from the
    /// specification's wording.
    /// </para>
    /// </summary>
    internal const string Separators =
        " !\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~"
        + "\t\n\r"
        + "\u00A0\u00AB\u00B7\u00BB"
        + "\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200A"
        + "\u200B\u200C\u200D"
        + "\u2010\u2011\u2012\u2013\u2014\u2015"
        + "\u2018\u2019\u201A\u201B\u201C\u201D\u201E"
        + "\u2022\u2026\u2027\u2028\u2029\u202F"
        + "\u205F\u3000";

    /// <summary>The combining diaeresis — the mark of a decomposed umlaut.</summary>
    internal const char CombiningDiaeresis = '\u0308';

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

        Fold(text, german, search, compact, offsets);

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

        Fold(text, german, search, null, null);

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

        Fold(text, german, null, compact, null);

        return compact.ToString();
    }

    /// <summary>
    /// Writes the search form into a buffer without allocating — the path
    /// the ranking takes, once per candidate.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when the buffer is too small; the required
    /// length is in <paramref name="written"/>.
    /// </returns>
    public static bool TryWriteSearchForm(
        ReadOnlySpan<char> source,
        Span<char> destination,
        out int written,
        NormalizationOptions? options = null)
    {
        var german = (options ?? NormalizationOptions.Default).GermanTransliteration;

        return TryWrite(source, destination, german, out written);
    }

    private static bool TryWrite(
        ReadOnlySpan<char> source,
        Span<char> destination,
        bool german,
        out int written)
    {
        written = 0;

        for (var i = 0; i < source.Length; i++)
        {
            if (german && TryDecomposedUmlaut(source, i, out var umlaut))
            {
                foreach (var ch in umlaut)
                {
                    if (!TryWriteChar(ch, destination, ref written))
                    {
                        return false;
                    }
                }

                i++;
                continue;
            }

            var replacement = german ? German(source[i]) : null;

            if (replacement is null)
            {
                if (!TryWriteChar(source[i], destination, ref written))
                {
                    return false;
                }

                continue;
            }

            foreach (var ch in replacement)
            {
                if (!TryWriteChar(ch, destination, ref written))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool TryWriteChar(char source, Span<char> destination, ref int written)
    {
        var folded = TokenizerFold.Fold(source);
        if (folded == TokenizerFold.Removed)
        {
            return true;
        }

        if (written >= destination.Length)
        {
            written = destination.Length + 1;
            return false;
        }

        destination[written] = folded;
        written++;

        return true;
    }

    private static void Fold(
        string text,
        bool german,
        StringBuilder? search,
        StringBuilder? compact,
        List<int>? offsets)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (german && TryDecomposedUmlaut(text, i, out var umlaut))
            {
                foreach (var ch in umlaut)
                {
                    AppendFolded(ch, search, compact, offsets, i);
                }

                // The mark belongs to the letter.
                i++;
                continue;
            }

            var replacement = german ? German(text[i]) : null;

            if (replacement is null)
            {
                AppendFolded(text[i], search, compact, offsets, i);
                continue;
            }

            foreach (var ch in replacement)
            {
                AppendFolded(ch, search, compact, offsets, i);
            }
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
    /// The German transliteration of a decomposed umlaut — a letter followed
    /// by the combining diaeresis. Without this, „Müller" and „Müller"
    /// would be two different words, and one of them would never be found.
    /// </summary>
    private static bool TryDecomposedUmlaut(ReadOnlySpan<char> text, int index, out string replacement)
    {
        replacement = string.Empty;

        if (index + 1 >= text.Length || text[index + 1] != CombiningDiaeresis)
        {
            return false;
        }

        replacement = text[index] switch
        {
            'a' or 'A' => "ae",
            'o' or 'O' => "oe",
            'u' or 'U' => "ue",
            _ => string.Empty
        };

        return replacement.Length > 0;
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

    internal static bool IsSeparator(char value) =>
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
