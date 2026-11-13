using BenchmarkDotNet.Attributes;

namespace Nordmann.LUI.Benchmarks;

/// <summary>
/// A query through the whole pipeline: the prefix path of one and two characters, FTS5 of three and more, over the
/// words and over the titles. The content of the index lives either in the index itself or in a table of the
/// application — the source is an axis, and the report names it.
/// <code>dotnet run -c Release --project benchmarks/Nordmann.LUI.Benchmarks -- --filter *QueryBenchmarks*</code>
/// </summary>
[MemoryDiagnoser]
#pragma warning disable CA1001 // BenchmarkDotNet owns the lifetime: the fixture is shared by the cases of the class
public class QueryBenchmarks
{
    private IndexFixture? _fixture;

    /// <summary>The corpus the index is built from.</summary>
    [Params(QuerySettings.CorpusWords, QuerySettings.CorpusTitles)]
    public string Corpus { get; set; } = string.Empty;

    /// <summary>The entries of the index. The budget scenario of three million is the budget test's; a benchmark is about the code.</summary>
    [Params(200_000)]
    public int Entries { get; set; }

    /// <summary>What a user typed: one letter, two, a short word, a long beginning.</summary>
    [Params("b", "ka", "haus", "apfelkuc")]
    public string Input { get; set; } = string.Empty;

    /// <summary>How many candidates are ranked.</summary>
    [Params(500)]
    public int Candidates { get; set; }

    /// <summary>The order of the ranking.</summary>
    [Params(QuerySettings.OrderStartSimilarity)]
    public string Order { get; set; } = string.Empty;

    /// <summary>The measure of the similarity.</summary>
    [Params(nameof(LuiSimilarity.JaroWinkler))]
    public string Similarity { get; set; } = string.Empty;

    /// <summary>Whether an input without a word start is corrected.</summary>
    [Params(false)]
    public bool Correction { get; set; }

    /// <summary>Where the content of the index lives.</summary>
    [Params(QuerySettings.Embedded, QuerySettings.Linked)]
    public string Source { get; set; } = string.Empty;

    [GlobalSetup]
    public void Setup()
    {
        _fixture = IndexFixture.Of(Corpus, Entries, Source);
        _fixture.WarmUp(Correction);
    }

    [Benchmark]
    public int Search() => Settings.Run(_fixture!.Index).Hits.Count;

    /// <summary>The settings of this case — the query the report names.</summary>
    private QuerySettings Settings => new()
    {
        Corpus = Corpus,
        Entries = Entries,
        Input = Input,
        Candidates = Candidates,
        Order = Order,
        Similarity = Similarity,
        Correction = Correction,
        Source = Source
    };
}
#pragma warning restore CA1001
