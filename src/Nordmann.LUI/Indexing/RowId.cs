namespace Nordmann.LUI.Indexing;

/// <summary>
/// The <c>rowid</c> of the search index: priority and weight live in it, so
/// that <c>ORDER BY rowid LIMIT n</c> reads the best entries first without a
/// sorter (LUI-Spezifikation §3 E2, §7.2).
/// <code>
/// [0][ priority: 2 bit ][ 2^29 − 1 − weight: 29 bit ][ doc_id: 32 bit ]
/// </code>
/// A smaller rowid is a higher priority, and within a priority a higher
/// weight. The weight is stored reversed so that the index reads forwards.
/// </summary>
internal static class RowId
{
    /// <summary>The largest weight: 2^29 − 1.</summary>
    internal const int MaxWeight = 536_870_911;

    /// <summary>The largest <c>doc_id</c>: 2^32 − 1.</summary>
    internal const long MaxDocId = 4_294_967_295;

    internal static long Encode(Priority priority, int weight, long docId)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(weight, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(weight, MaxWeight);
        ArgumentOutOfRangeException.ThrowIfLessThan(docId, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(docId, MaxDocId);
        ValidatePriority(priority);

        return ((long)priority << 61) | ((long)(MaxWeight - weight) << 32) | docId;
    }

    internal static Priority PriorityOf(long rowId) => (Priority)(rowId >> 61);

    internal static int WeightOf(long rowId) => MaxWeight - (int)((rowId >> 32) & MaxWeight);

    internal static long DocIdOf(long rowId) => rowId & MaxDocId;

    internal static void ValidateWeight(int weight)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(weight, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(weight, MaxWeight);
    }

    internal static void ValidatePriority(Priority priority)
    {
        if (priority is < Priority.System or > Priority.Normal)
        {
            throw new ArgumentOutOfRangeException(
                nameof(priority), priority, "The priority is 0 (System) … 3 (Normal).");
        }
    }
}
