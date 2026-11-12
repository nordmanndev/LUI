using System.Diagnostics.CodeAnalysis;
using Nordmann.LUI.Sample.Api;

namespace Nordmann.LUI.Sample.Indexes;

/// <summary>The indexes of the sample by name — what the routes look up.</summary>
internal sealed class SampleIndexes(IEnumerable<ISampleIndex> indexes)
{
    private readonly Dictionary<string, ISampleIndex> _byName = indexes.ToDictionary(index => index.Name, StringComparer.Ordinal);

    internal IEnumerable<ISampleIndex> All => _byName.Values;

    internal bool TryGet(string name, [NotNullWhen(true)] out ISampleIndex? index) => _byName.TryGetValue(name, out index);

    /// <summary>
    /// The description of every index. One index that cannot be read does not take the others down with it: the panel of
    /// the indexes would be empty, and an empty panel looks like data that was lost — the broken index says what is wrong
    /// with it instead (ADR-0029).
    /// </summary>
    internal IReadOnlyList<IndexInfo> DescribeAll() =>
        [.. _byName.Values.Select(Describe)];

    private static IndexInfo Describe(ISampleIndex index)
    {
        try
        {
            return index.Describe();
        }
        catch (Exception e)
        {
            return new IndexInfo(
                index.Name,
                "?",
                "?",
                "?",
                "?",
                0,
                "?",
                "This index could not be read.",
                null,
                null,
                Error: e.Message);
        }
    }
}
