using BenchmarkDotNet.Attributes;
using Nordmann.LUI.Testing;

namespace Nordmann.LUI.Benchmarks;

/// <summary>
/// A single write in a transaction of its own (§16: p95 ≤ 1 ms): the weight of an entry, an upsert of an entry as it is
/// — which leaves the search index alone —, and an upsert with another text — which writes it.
/// </summary>
[MemoryDiagnoser]
#pragma warning disable CA1001 // BenchmarkDotNet owns the lifetime: [GlobalCleanup] disposes the fixture
public class WriteBenchmarks
{
    private IndexFixture? _fixture;
    private int _next;

    [Params(200_000)]
    public int Entries { get; set; }

    [GlobalSetup]
    public void Setup() => _fixture = new IndexFixture(QuerySettings.CorpusWords, Entries);

    [GlobalCleanup]
    public void Cleanup() => _fixture?.Dispose();

    [Benchmark]
    public bool UpdateWeight() => _fixture!.Embedded.UpdateWeight(Key(), _next & 0xFFFF);

    [Benchmark]
    public void UpsertTheSame()
    {
        var key = Key();
        _fixture!.Embedded.Upsert(key, _fixture.Corpus.Texts[(int)key - 1], _fixture.Corpus.Weights[(int)key - 1]);
    }

    [Benchmark]
    public void UpsertAnotherText()
    {
        var key = Key();
        _fixture!.Embedded.Upsert(key, _fixture.Corpus.Texts[(int)(key % _fixture.Corpus.Count)], _fixture.Corpus.Weights[(int)key - 1]);
    }

    /// <summary>The keys in a stride through the corpus: every write meets another entry.</summary>
    private long Key() => 1 + ((_next++ * 7_919L) % Entries);
}
#pragma warning restore CA1001
