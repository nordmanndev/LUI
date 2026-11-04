using System.Diagnostics.CodeAnalysis;

namespace Nordmann.LUI.Sample.Indexes;

/// <summary>The indexes of the sample by name — what the routes look up.</summary>
internal sealed class SampleIndexes(IEnumerable<ISampleIndex> indexes)
{
    private readonly Dictionary<string, ISampleIndex> _byName = indexes.ToDictionary(index => index.Name, StringComparer.Ordinal);

    internal IEnumerable<ISampleIndex> All => _byName.Values;

    internal bool TryGet(string name, [NotNullWhen(true)] out ISampleIndex? index) => _byName.TryGetValue(name, out index);
}
