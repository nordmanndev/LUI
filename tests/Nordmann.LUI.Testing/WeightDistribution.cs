namespace Nordmann.LUI.Testing;

/// <summary>How the weights of a corpus are spread.</summary>
public enum WeightDistribution
{
    /// <summary>A long tail: most entries are unpopular, a few very popular; nearly every weight is distinct — the harder case for the rowid (ADR-0010).</summary>
    LongTail,

    /// <summary>Half of the entries have the weight 0, as in the stock of Anhang A1: most tags are used never or once.</summary>
    Sparse,
}
