using BenchmarkDotNet.Attributes;
using Nordmann.LUI.Testing;

namespace Nordmann.LUI.Benchmarks;

/// <summary>
/// A query through the whole pipeline (LUI-Spezifikation §8, §16): the prefix path of one and two characters, FTS5 of
/// three and more, a single-word and a multi-word index. The allocation is measured with it — §16 budgets 250 KB.
/// <code>dotnet run -c Release --project benchmarks/Nordmann.LUI.Benchmarks -- --filter *QueryBenchmarks*</code>
/// </summary>
[MemoryDiagnoser]
#pragma warning disable CA1001 // BenchmarkDotNet owns the lifetime: [GlobalCleanup] disposes the fixture
public class QueryBenchmarks
{
    private IndexFixture? _fixture;

    /// <summary>The kind of the index.</summary>
    [Params(CorpusKind.SingleWord, CorpusKind.MultiWord)]
    public CorpusKind Kind { get; set; }

    /// <summary>The entries of the index. The budget scenario of three million is the budget test's; a benchmark is about the code.</summary>
    [Params(200_000)]
    public int Entries { get; set; }

    /// <summary>What a user typed: one letter, two, a short word, a long beginning.</summary>
    [Params("b", "ka", "haus", "apfelkuc")]
    public string Input { get; set; } = string.Empty;

    [GlobalSetup]
    public void Setup() => _fixture = new IndexFixture(Kind, Entries);

    [GlobalCleanup]
    public void Cleanup() => _fixture?.Dispose();

    [Benchmark]
    public int Search() => _fixture!.Index.Search(Input).Hits.Count;
}
#pragma warning restore CA1001
