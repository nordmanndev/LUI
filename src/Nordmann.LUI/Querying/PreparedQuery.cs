using System.Text;
using Nordmann.LUI.Normalization;

namespace Nordmann.LUI.Querying;

/// <summary>
/// What the user typed, in the forms the query path needs (LUI-Spezifikation §8).
/// </summary>
internal sealed class PreparedQuery
{
    /// <summary>The shortest input the trigram tokenizer can search for.</summary>
    internal const int MinimumTrigramLength = 3;

    private PreparedQuery(string search, string compact)
    {
        Search = search;
        Compact = compact;
        PrefixKey = search.Replace("#", string.Empty, StringComparison.Ordinal);
        SearchLength = CodePoints(search);
        PrefixLength = CodePoints(PrefixKey);
    }

    /// <summary>
    /// The search form: what is compared with the index. A leading <c>#</c> of the input is not part
    /// of it — the hash is not part of the word (§9.1), and both paths must give the same answer
    /// whether the user typed it or not (ADR-0008).
    /// </summary>
    internal string Search { get; }

    /// <summary>The compact form: for the word-start rule.</summary>
    internal string Compact { get; }

    /// <summary>The search form without <c>#</c> — the key of the prefix columns.</summary>
    internal string PrefixKey { get; }

    /// <summary>The length of <see cref="Search"/> in code points — the unit of SQLite's <c>substr</c> and of the trigram.</summary>
    internal int SearchLength { get; }

    /// <summary>The length of <see cref="PrefixKey"/> in code points: 1 selects <c>s_p1</c>, 2 selects <c>s_p2</c>.</summary>
    internal int PrefixLength { get; }

    /// <summary>
    /// Whether the input is too short for a trigram — fewer than three <b>code points</b>, not
    /// UTF-16 units: the prefix path answers it (§8.1).
    /// </summary>
    internal bool IsPrefixPath => SearchLength < MinimumTrigramLength;

    /// <summary>Nothing to search for.</summary>
    internal bool IsEmpty => IsPrefixPath && PrefixLength == 0;

    internal static PreparedQuery Create(string input, NormalizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(input);

        // A NUL cannot be part of an FTS5 string; it is not part of any word either.
        var text = input.Contains('\0', StringComparison.Ordinal)
            ? input.Replace("\0", string.Empty, StringComparison.Ordinal)
            : input;

        text = text.Trim();

        return new PreparedQuery(
            Normalizer.ToSearchForm(text, options).TrimStart('#'),
            Normalizer.ToCompactForm(text, options));
    }

    /// <summary>The number of code points: a surrogate pair is one.</summary>
    private static int CodePoints(string text)
    {
        var count = 0;
        foreach (var c in text)
        {
            if (!char.IsLowSurrogate(c))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// The FTS5 expression for a single-word index: <c>s_norm : "…"</c>. The
    /// term is quoted, quotation marks doubled — the input is never copied
    /// into the expression raw (§8.2).
    /// </summary>
    internal string SingleWordExpression()
    {
        var builder = new StringBuilder("s_norm : \"", Search.Length + 12);
        builder.Append(Search.Replace("\"", "\"\"", StringComparison.Ordinal));
        builder.Append('"');
        return builder.ToString();
    }
}
