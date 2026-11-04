namespace Nordmann.LUI;

/// <summary>
/// What a check of an index found (LUI-Spezifikation §12.6, §14, ADR-0016). Each finding is a sentence that names what is
/// wrong and what repairs it; an index without findings is healthy. The counts compare the index with what it should
/// hold — the source of <c>Check(source)</c> for an <c>Embedded</c> index, the application's table for a <c>Linked</c> one.
/// </summary>
public sealed record LuiIntegrityReport
{
    /// <summary>The findings; empty when the index is healthy.</summary>
    public required IReadOnlyList<string> Problems { get; init; }

    /// <summary>Whether nothing was found.</summary>
    public bool IsHealthy => Problems.Count == 0;

    /// <summary>The entries the index should hold and does not.</summary>
    public long Missing { get; init; }

    /// <summary>The entries the index holds and should not.</summary>
    public long Extra { get; init; }

    /// <summary>
    /// The entries the index holds, and with something else than they should: another text, weight, priority or payload
    /// (<c>Embedded</c>, against the source); another weight, priority or beginning of the text (<c>Linked</c>, against the row).
    /// </summary>
    public long Different { get; init; }
}
