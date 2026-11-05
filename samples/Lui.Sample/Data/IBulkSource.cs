namespace Nordmann.LUI.Sample.Data;

/// <summary>
/// A source of entries for a bulk index: what the index holds is decided by the number the user asks for, not by a list that
/// is checked in. Every source is deterministic — the same count gives the same entries, and the first entries of a count are
/// the first entries of every larger one — so a sync against it is idempotent and a fill with fewer entries removes the rest.
/// </summary>
internal interface IBulkSource
{
    /// <summary>The name of the index the source fills.</summary>
    string Name { get; }

    /// <summary>The kind of the index it fills.</summary>
    LuiIndexKind Kind { get; }

    /// <summary>What the index is for, in the words of the index panel.</summary>
    string Description { get; }

    /// <summary>Where the entries come from — the panel shows it, so that nobody plays with a subset and thinks it is the whole.</summary>
    string Origin { get; }

    /// <summary>The most entries the source can deliver, or <see langword="null"/> when it has no ceiling.</summary>
    long? Capacity { get; }

    /// <summary>The first <paramref name="count"/> entries — the same ones for the same count, every time.</summary>
    IEnumerable<LuiEntry<long, LuiNoPayload>> Entries(int count);
}
