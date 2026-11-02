namespace Nordmann.LUI.Querying;

/// <summary>
/// A candidate on its way through the ranking. A mutable struct on purpose — the ranking writes the
/// similarity into the array element in place, so it is only ever used as an array element or passed
/// by <c>in</c>; a copy would silently lose the write.
/// </summary>
internal struct Scored
{
    /// <summary>The rowid: priority, weight and doc id.</summary>
    internal long RowId;

    /// <summary>The text as it was stored.</summary>
    internal string Text;

    /// <summary>Where the word that carries the match begins in the search form; -1: no word start.</summary>
    internal int WordStart;

    /// <summary>The similarity; only valid when it was computed.</summary>
    internal double Similarity;
}
