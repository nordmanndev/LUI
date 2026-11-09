using Nordmann.LUI.Sample.Api;
using Nordmann.LUI.Sample.Indexes;
using Shouldly;

namespace Nordmann.LUI.Sample.Tests;

/// <summary>
/// The panel of the indexes is one tab per index, and one index that cannot be read does not take the others down with it
/// (ADR-0029): an empty panel looks like data that was lost — the broken index says what is wrong with it instead.
/// </summary>
public sealed class SampleIndexPanelTests
{
    [Fact]
    public void A_broken_index_does_not_empty_the_panel()
    {
        var indexes = new SampleIndexes([new WorkingIndex("recipes"), new BrokenIndex("words"), new WorkingIndex("people")]);

        var described = indexes.DescribeAll();

        described.Count.ShouldBe(3);
        described.Single(index => string.Equals(index.Name, "recipes", StringComparison.Ordinal)).Count.ShouldBe(85);
        described.Single(index => string.Equals(index.Name, "people", StringComparison.Ordinal)).Count.ShouldBe(59);
        described.Single(index => string.Equals(index.Name, "words", StringComparison.Ordinal)).Error.ShouldBe("the index is broken");
    }

    /// <summary>An index that answers.</summary>
    private sealed class WorkingIndex(string name) : ISampleIndex
    {
        public string Name => name;

        public IndexInfo Describe() => new(
            name, "MultiWord", "Embedded", true, "Current", string.Equals(name, "recipes", StringComparison.Ordinal) ? 85 : 59, "none", "An index.", null, null);

        public SuggestResponse Suggest(string text, LuiQueryOptions options, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public LuiIntegrityReport Check() => throw new NotSupportedException();

        public void Optimize() => throw new NotSupportedException();

        public void WarmUpTypoDictionary(CancellationToken cancellationToken) => throw new NotSupportedException();

        public void RebuildTypoDictionary(CancellationToken cancellationToken) => throw new NotSupportedException();

        public object Sync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    /// <summary>An index that cannot be read.</summary>
    private sealed class BrokenIndex(string name) : ISampleIndex
    {
        public string Name => name;

        public IndexInfo Describe() => throw new InvalidOperationException("the index is broken");

        public SuggestResponse Suggest(string text, LuiQueryOptions options, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public LuiIntegrityReport Check() => throw new NotSupportedException();

        public void Optimize() => throw new NotSupportedException();

        public void WarmUpTypoDictionary(CancellationToken cancellationToken) => throw new NotSupportedException();

        public void RebuildTypoDictionary(CancellationToken cancellationToken) => throw new NotSupportedException();

        public object Sync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
