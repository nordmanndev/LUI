using BenchmarkDotNet.Attributes;
using Nordmann.LUI.Normalization;

namespace Nordmann.LUI.Benchmarks;

/// <summary>
/// The work of a query per candidate (ADR-0005, ADR-0030): the search form of a text — the normalisation the parity of ADR-0005 is
/// about — and the similarity measures the ranking compares the input with. No database.
/// </summary>
[MemoryDiagnoser]
public class TextBenchmarks
{
    [Params("Apfelkuchen", "Größenänderungsmaßnahme", "Gedeckter Apfelkuchen mit Streuseln")]
    public string Text { get; set; } = string.Empty;

    [Benchmark]
    public string SearchForm() => Normalizer.ToSearchForm(Text);

    [Benchmark]
    public double JaroWinkler() => LuiSimilarity.JaroWinkler.Similarity("apfelku", Text);

    [Benchmark]
    public double NgramDice() => LuiSimilarity.NgramDice.Similarity("apfelku", Text);
}
