namespace Nordmann.LUI;

/// <summary>What a sync of a <c>Linked</c> index did (ADR-0015): what it indexed, and what it left out and why.</summary>
public sealed record LuiLinkedSyncReport
{
    /// <summary>The entries in the index now.</summary>
    public long Indexed { get; init; }

    /// <summary>The rows left out because their text is <c>NULL</c> or empty.</summary>
    public long WithoutText { get; init; }

    /// <summary>The rows left out because their key is outside 0 … 4,294,967,295 (0 … 2,147,483,647 for an <c>int</c> index) — it cannot be a document id of the index.</summary>
    public long KeyOutOfRange { get; init; }
}
