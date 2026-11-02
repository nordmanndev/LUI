namespace Nordmann.LUI.Querying;

/// <summary>A candidate on its way through the ranking.</summary>
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
