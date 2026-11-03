using System.Text;
using Nordmann.LUI.Normalization;

namespace Nordmann.LUI.Schema;

/// <summary>
/// The rules of a multi-word index (LUI-Spezifikation §4): names and titles. A multi-word index is read
/// compactly everywhere — the words are the runs between separators, the search index holds
/// <c>s_compact</c> alone (ADR-0011), and the prefix columns hold the first letters of the compact text:
/// spaces and punctuation do not count (review of pull request 8).
/// </summary>
internal sealed class MultiWordRules : IndexKindRules
{
    internal static readonly MultiWordRules Instance = new();

    /// <summary>
    /// The most words that go into the expression: the longest ones. Every word is a phrase with posting
    /// lists of its own, and a hundred words must not be a hundred lists to walk; the others count in the
    /// ranking through the compact form (§8.2).
    /// </summary>
    private const int MaxExpressionWords = 8;

    private MultiWordRules()
    {
    }

    internal override LuiIndexKind Kind => LuiIndexKind.MultiWord;

    internal override ILuiSimilarity DefaultSimilarity => LuiSimilarity.NgramDice;

    // s_compact alone: it holds everything a multi-word query asks, and s_norm would only store every
    // title a second time (ADR-0011)
    internal override string IndexedColumn => "s_compact";

    // no spaces, no punctuation, no '#' (§4: „spaces do not matter")
    internal override string PrefixSource => "s_compact";

    // the separators stripped from the transliterated text (§6.1); the tokenizer folds it, as it folds s_norm
    internal override string? CompactColumnDefinition =>
        $"s_compact TEXT GENERATED ALWAYS AS ({SqlNormalizer.Compact("s_norm")}) STORED";

    internal override string IndexedValue(string searchForm) => SqlNormalizer.Compact(searchForm);

    internal override string PrefixValue(string searchForm) => SqlNormalizer.Compact(searchForm);

    // nothing reads s_norm but s_compact, which derives from it: a stored copy of every title is a tenth of the file
    internal override string NormStorage => "VIRTUAL";

    internal override IReadOnlyList<string> Words(string search)
    {
        var words = new List<string>();
        var start = -1;

        for (var i = 0; i < search.Length; i++)
        {
            if (Normalizer.IsSeparator(search[i]))
            {
                if (start >= 0)
                {
                    words.Add(search[start..i]);
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
            words.Add(search[start..]);
        }

        return words;
    }

    internal override string PrefixKey(string search, string compact) => CodePoint.First(compact, LongestPrefix);

    // „a b" has two letters, and no word of three to search for
    internal override bool IsPrefixPath(int searchLength, int compactLength) => compactLength < MinimumTrigramLength;

    /// <summary>
    /// The words of three or more characters, each a phrase, ANDed in <c>s_compact</c> — the only column of
    /// the search index (ADR-0011). Shorter words stay out of the expression, they count in the ranking
    /// (§8.2). With no word of three characters the whole input is one compact phrase.
    /// <para>
    /// §8.2 asked for <c>(s_norm : (w1 AND w2)) OR (s_compact : "w1w2")</c>. A title that holds a word in
    /// <c>s_norm</c> holds it in <c>s_compact</c> too (the separators are taken out between the letters,
    /// never inside a word), and a title that holds <c>w1w2</c> holds both words — so the one phrase
    /// per word finds all of it. The only titles it adds are those whose words run across a boundary of
    /// the title, which the ranking puts behind every word start.
    /// </para>
    /// </summary>
    internal override string Expression(string search, string compact, IReadOnlyList<string> words)
    {
        var chosen = ExpressionWords(words);
        if (chosen.Length == 0)
        {
            return $"s_compact : {Quote(compact)}";
        }

        if (chosen.Length == 1)
        {
            return $"s_compact : {Quote(chosen[0])}";
        }

        var anded = new StringBuilder("s_compact : (");
        anded.AppendJoin(" AND ", chosen.Select(Quote));
        anded.Append(')');
        return anded.ToString();
    }

    /// <summary>
    /// The words of three or more code points — at most <see cref="MaxExpressionWords"/>, the longest, in the
    /// order they were typed.
    /// </summary>
    private static string[] ExpressionWords(IReadOnlyList<string> words)
    {
        var long3 = words.Where(word => CodePoint.Count(word) >= MinimumTrigramLength).ToArray();
        if (long3.Length <= MaxExpressionWords)
        {
            return long3;
        }

        var keep = Enumerable.Range(0, long3.Length)
            .OrderByDescending(i => CodePoint.Count(long3[i]))
            .ThenBy(i => i)
            .Take(MaxExpressionWords)
            .Order()
            .ToArray();

        return [.. keep.Select(i => long3[i])];
    }
}
