namespace Nordmann.LUI.Schema;

/// <summary>
/// The rules of a single-word index (LUI-Spezifikation §4): tags, words, compounds. The input is searched
/// as a whole, in <c>s_norm</c>.
/// </summary>
internal sealed class SingleWordRules : IndexKindRules
{
    internal static readonly SingleWordRules Instance = new();

    private SingleWordRules()
    {
    }

    internal override LuiIndexKind Kind => LuiIndexKind.SingleWord;

    internal override ILuiSimilarity DefaultSimilarity => LuiSimilarity.JaroWinkler;

    internal override string IndexedColumn => "s_norm";

    // s_norm without its '#': the hash is not part of the word (§9.1). lower() folds ASCII only (§6.2).
    internal override string PrefixSource => "replace(s_norm, '#', '')";

    internal override string CompactColumn => string.Empty;

    // the search index reads s_norm
    internal override string NormStorage => "STORED";

    internal override IReadOnlyList<string> Words(string search) => [];

    internal override string PrefixKey(string search, string compact) =>
        search.Replace("#", string.Empty, StringComparison.Ordinal);

    internal override bool IsPrefixPath(int searchLength, int compactLength) => searchLength < MinimumTrigramLength;

    /// <summary><c>s_norm : "…"</c>.</summary>
    internal override string Expression(string search, string compact, IReadOnlyList<string> words) =>
        $"s_norm : {Quote(search)}";
}
