using Nordmann.LUI.Normalization;
using Nordmann.LUI.Schema;

namespace Nordmann.LUI.Querying;

/// <summary>
/// What the user typed, in the forms the query path needs (ADR-0008). What the forms mean for
/// the search — the words, the prefix key, the prefix path, the expression — is decided by the rules of
/// the kind of the index (<see cref="IndexKindRules"/>).
/// </summary>
internal sealed class PreparedQuery
{
    private readonly IndexKindRules _kind;

    private PreparedQuery(string search, string compact, IndexKindRules kind)
    {
        _kind = kind;
        Search = search;
        Compact = compact;
        SearchLength = CodePoint.Count(search);
        CompactLength = CodePoint.Count(compact);
        Words = kind.Words(search);
        PrefixKey = kind.PrefixKey(search, compact);
        PrefixLength = CodePoint.Count(PrefixKey);
    }

    /// <summary>
    /// The search form: what is compared with the index. A leading <c>#</c> of the input is not part
    /// of it — the hash is not part of the word (ADR-0008), and both paths must give the same answer
    /// whether the user typed it or not (ADR-0008).
    /// </summary>
    internal string Search { get; }

    /// <summary>The compact form: for the word-start rule, and for the compact column of a multi-word index.</summary>
    internal string Compact { get; }

    /// <summary>The words of the input of a multi-word index: the runs of the search form between separators (ADR-0014).</summary>
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
    /// UTF-16 units: the prefix path answers it (ADR-0008).
    /// </summary>
    internal bool IsPrefixPath => _kind.IsPrefixPath(SearchLength, CompactLength);

    /// <summary>Nothing to search for.</summary>
    internal bool IsEmpty => IsPrefixPath && PrefixLength == 0;

    /// <summary>The FTS5 expression for the candidates (ADR-0008).</summary>
    internal string Expression => _kind.Expression(Search, Compact, Words);

    internal static PreparedQuery Create(string input, NormalizationOptions options, IndexKindRules kind)
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
}
