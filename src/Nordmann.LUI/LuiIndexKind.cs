namespace Nordmann.LUI;

/// <summary>
/// How an index is searched. The kind is fixed when the index is created and
/// steers the query intrinsically: the caller does not have to know how the
/// search is done (ADR-0014).
/// </summary>
public enum LuiIndexKind
{
    /// <summary>Tags, single words, compounds, hashtags: the input is searched as a whole.</summary>
    SingleWord,

    /// <summary>Names and titles: the input is split into words, ANDed, and also searched compactly.</summary>
    MultiWord,
}
