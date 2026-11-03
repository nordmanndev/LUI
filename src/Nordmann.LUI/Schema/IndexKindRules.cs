namespace Nordmann.LUI.Schema;

/// <summary>
/// Everything that follows from the kind of an index (LUI-Spezifikation §4), in one place per kind: which
/// column the search index holds, what the prefix columns are made of, the default similarity, when the
/// prefix path answers and what the FTS5 expression is. Nothing outside the rules asks which kind an
/// index is — a rule that is decided in two places is a rule that is forgotten in one (review of pull
/// request 8, M1; ADR-0014). An architecture test holds that.
/// </summary>
internal abstract class IndexKindRules
{
    /// <summary>The shortest input the trigram tokenizer can search for.</summary>
    internal const int MinimumTrigramLength = 3;

    /// <summary>The prefix columns hold one and two characters.</summary>
    internal const int LongestPrefix = 2;

    /// <summary>The kind these rules are for.</summary>
    internal abstract LuiIndexKind Kind { get; }

    /// <summary>The similarity of a query that names none (§4, §11).</summary>
    internal abstract ILuiSimilarity DefaultSimilarity { get; }

    // ---- the schema (§7.2, §7.4) ---------------------------------------------------------------

    /// <summary>The one column the search index holds.</summary>
    internal abstract string IndexedColumn { get; }

    /// <summary>The SQL expression the prefix columns <c>s_p1</c> and <c>s_p2</c> take their first letters from.</summary>
    internal abstract string PrefixSource { get; }

    /// <summary>
    /// The definition of the compact column of a content table — <c>s_compact TEXT GENERATED …</c> —, or
    /// <see langword="null"/> when the kind has none. The layout of the statement it goes into is the source's
    /// (review of pull request 14, N3).
    /// </summary>
    internal abstract string? CompactColumnDefinition { get; }

    /// <summary>
    /// The value of <see cref="IndexedColumn"/> as an SQL expression over an expression of the search form — for a
    /// source without generated columns (<c>Linked</c>, ADR-0015).
    /// </summary>
    internal abstract string IndexedValue(string searchForm);

    /// <summary>The text the prefix columns take their first letters from, as an SQL expression over the search form.</summary>
    internal abstract string PrefixValue(string searchForm);

    /// <summary>How <c>s_norm</c> is kept: <c>STORED</c> when the search index reads it, <c>VIRTUAL</c> otherwise.</summary>
    internal abstract string NormStorage { get; }

    // ---- the query (§8) ------------------------------------------------------------------------

    /// <summary>The words of the input that the expression is made of; empty when the input is searched whole.</summary>
    internal abstract IReadOnlyList<string> Words(string search);

    /// <summary>The key of the prefix columns for an input.</summary>
    internal abstract string PrefixKey(string search, string compact);

    /// <summary>Whether an input is too short for a trigram, so that the prefix path answers it (§8.1).</summary>
    internal abstract bool IsPrefixPath(int searchLength, int compactLength);

    /// <summary>The FTS5 expression for the candidates of an input (§8.2).</summary>
    internal abstract string Expression(string search, string compact, IReadOnlyList<string> words);

    /// <summary>
    /// Whether a text still matches an input the way the search index matched it: the folded search form of the text
    /// holds what the FTS5 expression asks for, or begins with the prefix key. For a source whose text can change under
    /// the index (<c>Linked</c>): a candidate of an index that is not current is not a hit (review of pull request 16,
    /// S1).
    /// </summary>
    /// <param name="searchForm">The search form of the candidate's text as it is now.</param>
    /// <param name="prefixPath">Whether the input took the prefix path.</param>
    /// <param name="prefixKey">The key of the prefix path.</param>
    /// <param name="search">The search form of the input.</param>
    /// <param name="compact">The compact form of the input.</param>
    /// <param name="words">The words of the input.</param>
    internal abstract bool StillMatches(
        ReadOnlySpan<char> searchForm, bool prefixPath, string prefixKey, string search, string compact, IReadOnlyList<string> words);

    /// <summary>The rules of a kind.</summary>
    internal static IndexKindRules For(LuiIndexKind kind) => kind switch
    {
        LuiIndexKind.SingleWord => SingleWordRules.Instance,
        LuiIndexKind.MultiWord => MultiWordRules.Instance,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown index kind."),
    };

    /// <summary>
    /// A term as an FTS5 string: in quotation marks, quotation marks doubled. Nothing of the input is ever
    /// copied into an expression raw (§8.2).
    /// </summary>
    internal static string Quote(string term) => $"\"{term.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}
