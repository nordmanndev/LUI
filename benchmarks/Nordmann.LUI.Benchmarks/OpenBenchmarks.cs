using BenchmarkDotNet.Attributes;

namespace Nordmann.LUI.Benchmarks;

/// <summary>
/// Opening an index through the catalog (ADR-0075, issue #265): a request takes its handle where it needs it —
/// <c>catalog.Index(definition)</c> — so the call is what a request pays before its query. Three cases: the open call alone
/// (time and allocation — a lookup in the memory of the catalog, 0 B), a query with a handle taken per request, and the same
/// query with a held handle; the two queries differ by the open call, nothing else. On 0.3.0 the open call cost ~23 µs and
/// ~20 KB (20,000 entries), and a query with a fresh handle allocated twice as much as one with a held handle.
/// <code>dotnet run -c Release --project benchmarks/Nordmann.LUI.Benchmarks -- --filter *OpenBenchmarks*</code>
/// </summary>
[MemoryDiagnoser]
#pragma warning disable CA1001 // BenchmarkDotNet owns the lifetime: the fixture is shared by the cases of the class
public class OpenBenchmarks
{
    /// <summary>The definition of the index the fixture built: single word, with the typo tolerance, as the other benchmarks have it.</summary>
    private static readonly LuiIndexDefinition<long> Definition = new("bench", index => index.SingleWord().TypoTolerance());

    private IndexFixture? _fixture;
    private LuiEmbeddedIndex<long>? _held;

    /// <summary>The entries of the index — the size of the review of 2026-11-20 that measured the open call on 0.3.0.</summary>
    [Params(20_000)]
    public int Entries { get; set; }

    /// <summary>What a user typed.</summary>
    [Params("ka")]
    public string Input { get; set; } = string.Empty;

    [GlobalSetup]
    public void Setup()
    {
        _fixture = IndexFixture.Of(QuerySettings.CorpusWords, Entries, QuerySettings.Embedded);
        _held = _fixture.Catalog.Index(Definition);
    }

    /// <summary>The open call alone: after the first call a lookup — the same handle, nothing allocated, no SQL.</summary>
    [Benchmark(Description = "Index_of_a_definition")]
    public LuiEmbeddedIndex<long> IndexOfADefinition() => _fixture!.Catalog.Index(Definition);

    /// <summary>A query through a handle that the request takes from the catalog.</summary>
    [Benchmark(Description = "Query_with_a_handle_per_request")]
    public int QueryWithAHandlePerRequest() => _fixture!.Catalog.Index(Definition).Search(Input).Hits.Count;

    /// <summary>The same query through a handle that is held.</summary>
    [Benchmark(Description = "Query_with_a_held_handle")]
    public int QueryWithAHeldHandle() => _held!.Search(Input).Hits.Count;
}
#pragma warning restore CA1001
