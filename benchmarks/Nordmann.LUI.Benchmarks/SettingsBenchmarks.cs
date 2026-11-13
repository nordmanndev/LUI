using BenchmarkDotNet.Attributes;

namespace Nordmann.LUI.Benchmarks;

/// <summary>
/// The settings of a query as axes: the budget of the candidates, the order of the ranking, the measure of the similarity
/// and the correction on and off. Each of them changes what a query does — and therefore what a query costs — so each
/// of them is measured, not described.
/// <code>dotnet run -c Release --project benchmarks/Nordmann.LUI.Benchmarks -- --filter *SettingsBenchmarks*</code>
/// </summary>
[MemoryDiagnoser]
#pragma warning disable CA1001 // BenchmarkDotNet owns the lifetime: the fixture is shared by the cases of the class
public class SettingsBenchmarks
{
    private IndexFixture? _fixture;

    /// <summary>The corpus the index is built from.</summary>
    [Params(QuerySettings.CorpusWords)]
    public string Corpus { get; set; } = string.Empty;

    /// <summary>The entries of the index.</summary>
    [Params(200_000)]
    public int Entries { get; set; }

    /// <summary>What a user typed: a word that FTS5 finds.</summary>
    [Params("haus")]
    public string Input { get; set; } = string.Empty;

    /// <summary>How many candidates are ranked.</summary>
    [Params(500, 2000)]
    public int Candidates { get; set; }

    /// <summary>The order of the ranking.</summary>
    [Params(QuerySettings.OrderStartSimilarity, QuerySettings.OrderWeightOnly)]
    public string Order { get; set; } = string.Empty;

    /// <summary>The measure of the similarity.</summary>
    [Params(nameof(LuiSimilarity.JaroWinkler), nameof(LuiSimilarity.Levenshtein))]
    public string Similarity { get; set; } = string.Empty;

    /// <summary>Whether an input without a word start is corrected.</summary>
    [Params(false, true)]
    public bool Correction { get; set; }

    /// <summary>Where the content of the index lives.</summary>
    [Params(QuerySettings.Embedded)]
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
