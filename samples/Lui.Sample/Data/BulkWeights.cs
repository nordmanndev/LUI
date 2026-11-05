namespace Nordmann.LUI.Sample.Data;

/// <summary>
/// The weight of a generated entry: a stable hash of its text. It has to be stable — the same text must always carry the same
/// weight, or a sync against the source would find every entry <em>changed</em> instead of <em>unchanged</em> and would write the
/// whole index again. <see cref="string.GetHashCode()"/> is not: it is randomized per process.
/// </summary>
internal static class BulkWeights
{
    private const uint Offset = 2166136261;
    private const uint Prime = 16777619;
    private const int Highest = 1000;

    /// <summary>The weight of a text: 1 … 1,000, the same for the same text in every process.</summary>
    internal static int Of(string text)
    {
        var hash = Offset;
        foreach (var character in text)
        {
            hash ^= character;
            hash *= Prime;
        }

        return (int)(hash % Highest) + 1;
    }
}
