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

    /// <summary>
    /// The most words that go into the expression: the longest ones. Every word is a phrase with posting
    /// lists of its own, and a hundred words must not be a hundred lists to walk; the others count in the
    /// ranking through the compact form (§8.2).
    /// </summary>
    private const int MaxExpressionWords = 8;

    private readonly LuiIndexKind _kind;

    private PreparedQuery(string search, string compact, LuiIndexKind kind)
    {
        _kind = kind;
        Search = search;
        Compact = compact;
        SearchLength = CodePoints(search);
        CompactLength = CodePoints(compact);

        if (kind == LuiIndexKind.MultiWord)
        {
            // A multi-word index is read compactly everywhere: the words are the runs between separators, and
            // the prefix columns hold the first letters of the compact text — spaces and punctuation do
            // not count (§4, review of pull request 8).
            Words = SplitWords(search);
            PrefixKey = FirstCodePoints(compact, LongestPrefix);
        }
        else
        {
            Words = [];
            PrefixKey = search.Replace("#", string.Empty, StringComparison.Ordinal);
        }

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

    /// <summary>The words of the input of a multi-word index: the runs of the search form between separators (§4).</summary>
    internal IReadOnlyList<string> Words { get; }

    /// <summary>
    /// The key of the prefix columns: the search form without <c>#</c> for a single-word index, the
    /// first two letters of the compact form for a multi-word index.
    /// </summary>
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
        (_kind == LuiIndexKind.MultiWord ? CompactLength : SearchLength) < MinimumTrigramLength;

    /// <summary>Nothing to search for.</summary>
    internal bool IsEmpty => IsPrefixPath && PrefixLength == 0;

    /// <summary>The FTS5 expression for the candidates (§8.2).</summary>
    internal string Expression => _kind == LuiIndexKind.MultiWord ? MultiWordExpression() : SingleWordExpression();

    internal static PreparedQuery Create(string input, NormalizationOptions options, LuiIndexKind kind)
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
    /// The expression for a multi-word index: the words of three or more characters, each a phrase, ANDed
    /// in <c>s_compact</c> — the only column of the search index (ADR-0011). Shorter words stay out of the
    /// expression, they count in the ranking (§8.2). With no word of three characters the whole input is
    /// one compact phrase.
    /// <para>
    /// §8.2 asked for <c>(s_norm : (w1 AND w2)) OR (s_compact : "w1w2")</c>. A title that holds a word in
    /// <c>s_norm</c> holds it in <c>s_compact</c> too (the separators are taken out between the letters,
    /// never inside a word), and a title that holds <c>w1w2</c> holds both words — so the one phrase
    /// per word finds all of it. The only titles it adds are those whose words run across a boundary of
    /// the title, which the ranking puts behind every word start.
    /// </para>
    /// </summary>
    private string MultiWordExpression()
    {
        var words = ExpressionWords();
        if (words.Length == 0)
        {
            return $"s_compact : {Quote(Compact)}";
        }

        if (words.Length == 1)
        {
            return $"s_compact : {Quote(words[0])}";
        }

        var anded = new StringBuilder("s_compact : (");
        anded.AppendJoin(" AND ", words.Select(Quote));
        anded.Append(')');
        return anded.ToString();
    }

    /// <summary>
    /// The words of three or more code points — at most <see cref="MaxExpressionWords"/>, the longest, in the
    /// order they were typed.
    /// </summary>
    private string[] ExpressionWords()
    {
        var words = Words.Where(word => CodePoints(word) >= MinimumTrigramLength).ToArray();
        if (words.Length <= MaxExpressionWords)
        {
            return words;
        }

        var keep = Enumerable.Range(0, words.Length)
            .OrderByDescending(i => CodePoints(words[i]))
            .ThenBy(i => i)
            .Take(MaxExpressionWords)
            .Order()
            .ToArray();

        return [.. keep.Select(i => words[i])];
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
