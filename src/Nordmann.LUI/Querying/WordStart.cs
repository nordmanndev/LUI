using System.Buffers;
using System.Diagnostics;
using Nordmann.LUI.Normalization;

namespace Nordmann.LUI.Querying;

/// <summary>
/// The word-start rule (ADR-0008): an entry satisfies it when
/// there is a word <c>i</c> in the normalised text such that the <b>compact</b>
/// text from word <c>i</c> on begins with the <b>compact</b> input. Words are
/// separated by spaces and hyphens; a leading <c>#</c> does not count.
/// <para>
/// The split is <b>only</b> at a space or a hyphen, as ADR-0008 says. The <i>compact</i> comparison skips the
/// whole separator set (punctuation, no-break space, dashes), so „foo/bar" is one word whose compact
/// text is „foobar": „bar" does not stand at a word start there.
/// </para>
/// </summary>
internal static class WordStart
{
    /// <summary>
    /// The position in <paramref name="searchForm"/> where the first matching
    /// word begins — the similarity is measured from there (ADR-0013, ADR-0055).
    /// </summary>
    /// <param name="searchForm">The normalised text of the entry.</param>
    /// <param name="compactQuery">The compact form of the input.</param>
    /// <returns>The position, or -1 when the input stands at no word start.</returns>
    internal static int Find(ReadOnlySpan<char> searchForm, ReadOnlySpan<char> compactQuery)
    {
        if (compactQuery.IsEmpty)
        {
            return -1;
        }

        var wordBegin = 0;
        while (wordBegin <= searchForm.Length)
        {
            if (CompactStartsWith(searchForm, wordBegin, compactQuery))
            {
                // the word begins at its first character, not at the separator before it
                var begin = wordBegin;
                while (begin < searchForm.Length && Normalizer.IsSeparator(searchForm[begin]))
                {
                    begin++;
                }

                return begin;
            }

            var next = searchForm[wordBegin..].IndexOfAny(' ', '-');
            if (next < 0)
            {
                return -1;
            }

            wordBegin += next + 1;
        }

        return -1;
    }

    /// <summary>
    /// The word start of a text that was not read with its candidate (issue #173): the same as the candidate stage finds when it
    /// reads the text — the text in its search form, and the position where the input stands.
    /// </summary>
    /// <param name="text">The text of the entry, as it was stored.</param>
    /// <param name="compactQuery">The compact form of the input.</param>
    /// <param name="normalization">How the search form is made.</param>
    /// <returns>The position, or -1 when the input stands at no word start.</returns>
    internal static int Of(string text, ReadOnlySpan<char> compactQuery, NormalizationOptions normalization)
    {
        char[]? rented = null;
        var buffer = text.Length * 3 <= 256
            ? stackalloc char[256]
            : rented = ArrayPool<char>.Shared.Rent(text.Length * 3);

        try
        {
            if (!Normalizer.TryWriteSearchForm(text, buffer, out var written, normalization))
            {
                // the transliteration at most doubles a text (ä → ae, ß → ss); the buffer holds three times
                throw new UnreachableException("The search form of a text is longer than three times the text.");
            }

            return Find(buffer[..written], compactQuery);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<char>.Shared.Return(rented);
            }
        }
    }

    private static bool CompactStartsWith(ReadOnlySpan<char> text, int from, ReadOnlySpan<char> compactQuery)
    {
        var position = from;

        foreach (var wanted in compactQuery)
        {
            while (position < text.Length && Normalizer.IsSeparator(text[position]))
            {
                position++;
            }

            if (position >= text.Length || text[position] != wanted)
            {
                return false;
            }

            position++;
        }

        return true;
    }
}
