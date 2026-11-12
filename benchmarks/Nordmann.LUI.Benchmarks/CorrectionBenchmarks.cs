using BenchmarkDotNet.Attributes;
using Nordmann.LUI.Testing;

namespace Nordmann.LUI.Benchmarks;

/// <summary>
/// A query that needs a correction (§10, §16: p95 ≤ 10 ms): no candidate starts a word, so the typo dictionary gives
/// the corrections and the candidates of each are read. The dictionary is built in the setup, as a warmed-up
/// application has it.
/// </summary>
[MemoryDiagnoser]
#pragma warning disable CA1001 // BenchmarkDotNet owns the lifetime: [GlobalCleanup] disposes the fixture
public class CorrectionBenchmarks
{
    private IndexFixture? _fixture;

    [Params(200_000)]
    public int Entries { get; set; }

    /// <summary>A letter left out, a letter wrong, two letters swapped.</summary>
    [Params("kartofel", "bahmhof", "fusbal", "strasee")]
    public string Input { get; set; } = string.Empty;

    [GlobalSetup]
    public void Setup()
    {
        _fixture = new IndexFixture("words.txt", Entries, typoTolerance: true);
        _fixture.Index.WarmUpTypoDictionary();
    }

    [GlobalCleanup]
    public void Cleanup() => _fixture?.Dispose();

    [Benchmark]
    public int Search() => _fixture!.Index.Search(Input).Hits.Count;
}
#pragma warning restore CA1001
