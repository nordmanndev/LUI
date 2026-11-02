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

    /// <summary>The prefix columns hold one and two characters.</summary>
    private const int LongestPrefix = 2;

    private readonly IndexKind _kind;

    private PreparedQuery(string search, string compact, IndexKind kind)
    {
        _kind = kind;
        Search = search;
        Compact = compact;
        Words = SplitWords(search);
        SearchLength = CodePoints(search);
        CompactLength = CodePoints(compact);

        var prefix = search.Replace("#", string.Empty, StringComparison.Ordinal);
        PrefixKey = kind == IndexKind.MultiWord ? FirstCodePoints(prefix, LongestPrefix) : prefix;
        PrefixLength = CodePoints(PrefixKey);
    }

    /// <summary>
    /// The search form: what is compared with the index. A leading <c>#</c> of the input is not part
    /// of it — the hash is not part of the word (§9.1), and both paths must give the same answer
    /// whether the user typed it or not (ADR-0008).
    /// </summary>
    internal string Search { get; }

    /// <summary>The compact form: for the word-start rule, and for the compact column of a multi-word index.</summary>
    internal string Compact { get; }

    /// <summary>The words of the input: the runs of the search form between separators (§4).</summary>
    internal IReadOnlyList<string> Words { get; }

    /// <summary>The search form without <c>#</c> — the key of the prefix columns.</summary>
    internal string PrefixKey { get; }

    /// <summary>The length of <see cref="Search"/> in code points — the unit of SQLite's <c>substr</c> and of the trigram.</summary>
    internal int SearchLength { get; }

    /// <summary>The length of <see cref="Compact"/> in code points.</summary>
    internal int CompactLength { get; }

    /// <summary>The length of <see cref="PrefixKey"/> in code points: 1 selects <c>s_p1</c>, 2 selects <c>s_p2</c>.</summary>
    internal int PrefixLength { get; }

    /// <summary>
    /// Whether the input is too short for a trigram — fewer than three <b>code points</b>, not
    /// UTF-16 units: the prefix path answers it (§8.1). For a multi-word index the spaces do not
    /// count: „a b" has two letters, and no word of three to search for.
    /// </summary>
    internal bool IsPrefixPath =>
        (_kind == IndexKind.MultiWord ? CompactLength : SearchLength) < MinimumTrigramLength;

    /// <summary>Nothing to search for.</summary>
    internal bool IsEmpty => IsPrefixPath && PrefixLength == 0;

    /// <summary>The FTS5 expression for the candidates (§8.2).</summary>
    internal string Expression => _kind == IndexKind.MultiWord ? MultiWordExpression() : SingleWordExpression();

    internal static PreparedQuery Create(string input, NormalizationOptions options, IndexKind kind = IndexKind.SingleWord)
    {
        ArgumentNullException.ThrowIfNull(input);

        // A NUL cannot be part of an FTS5 string; it is not part of any word either.
        var text = input.Contains('\0', StringComparison.Ordinal)
            ? input.Replace("\0", string.Empty, StringComparison.Ordinal)
            : input;

        text = text.Trim();

        return new PreparedQuery(
            Normalizer.ToSearchForm(text, options).TrimStart('#'),
            Normalizer.ToCompactForm(text, options),
            kind);
    }

    /// <summary>
    /// A term as an FTS5 string: in quotation marks, quotation marks doubled. Nothing of the input
    /// is ever copied into an expression raw (§8.2).
    /// </summary>
    internal static string Quote(string term) => $"\"{term.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    /// <summary>The expression for a single-word index: <c>s_norm : "…"</c>.</summary>
    private string SingleWordExpression() => $"s_norm : {Quote(Search)}";

    /// <summary>
    /// The expression for a multi-word index: the words of three or more characters ANDed in
    /// <c>s_norm</c>, or the whole input as one compact phrase in <c>s_compact</c> (§8.2). Shorter
    /// words stay out of the expression — they count in the ranking.
    /// </summary>
    private string MultiWordExpression()
    {
        var compact = $"s_compact : {Quote(Compact)}";

        var words = Words.Where(word => CodePoints(word) >= MinimumTrigramLength).ToArray();
        if (words.Length == 0)
        {
            return compact;
        }

        // One word without a separator: every title that holds it in s_norm holds it in s_compact too (the
        // separators are only taken out between the letters), and s_compact holds more — „apfelkuch" in
        // „Apfel Kuchen". The compact column alone gives the same hits as §8.2's two arms, and FTS5 walks
        // one posting list instead of two (ADR-0011).
        if (Words.Count == 1 && string.Equals(Words[0], Compact, StringComparison.Ordinal))
        {
            return compact;
        }

        var anded = new StringBuilder("s_norm : (");
        anded.AppendJoin(" AND ", words.Select(Quote));
        anded.Append(')');

        return $"({anded}) OR ({compact})";
    }

    private static List<string> SplitWords(string searchForm)
    {
        var words = new List<string>();
        var start = -1;

        for (var i = 0; i < searchForm.Length; i++)
        {
            if (Normalizer.IsSeparator(searchForm[i]))
            {
                if (start >= 0)
                {
                    words.Add(searchForm[start..i]);
                    start = -1;
                }
            }
            else if (start < 0)
            {
                start = i;
            }
        }

        if (start >= 0)
        {
            words.Add(searchForm[start..]);
        }

        return words;
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

    private static string FirstCodePoints(string text, int count)
    {
        var taken = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (!char.IsLowSurrogate(text[i]) && taken++ == count)
            {
                return text[..i];
            }
        }

        return text;
    }
}
