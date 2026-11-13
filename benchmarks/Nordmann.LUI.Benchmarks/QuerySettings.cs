namespace Nordmann.LUI.Benchmarks;

/// <summary>
/// The settings of a query: the corpus, the input and the four settings that change what a query does — the budget of the
/// candidates, the order of the ranking, the measure of the similarity and whether a wrong input is corrected. The
/// benchmark measures them as axes, and the report names them. The number of hits of a line belongs to exactly these
/// settings, and it is measured with the same query the benchmark measured — one truth for both.
/// </summary>
internal sealed record QuerySettings
{
    /// <summary>The corpus file the index is built from.</summary>
    internal string Corpus { get; init; } = string.Empty;

    /// <summary>How many entries of the corpus the index holds.</summary>
    internal int Entries { get; init; }

    /// <summary>What a user typed.</summary>
    internal string Input { get; init; } = string.Empty;

    /// <summary>How many candidates are ranked.</summary>
    internal int Candidates { get; init; } = 500;

    /// <summary>The order of the ranking, named as the report names it.</summary>
    internal string Order { get; init; } = OrderStartSimilarity;

    /// <summary>The measure of the similarity, named as the report names it.</summary>
    internal string Similarity { get; init; } = nameof(LuiSimilarity.JaroWinkler);

    /// <summary>Whether an input without a word start is corrected.</summary>
    internal bool Correction { get; init; }

    /// <summary>Where the content lives: in the index itself or in a table of the application.</summary>
    internal string Source { get; init; } = Embedded;

    /// <summary>The source of an embedded index: the content lives in the index itself.</summary>
    internal const string Embedded = "Embedded";

    /// <summary>The order of the ranking: word start first, then the similarity, then the weight.</summary>
    internal const string OrderStartSimilarity = "Start/Sim";

    /// <summary>The order of the ranking: word start first, then the weight, then the similarity.</summary>
    internal const string OrderStartWeight = "Start/Weight";

    /// <summary>The order of the ranking: the similarity alone.</summary>
    internal const string OrderSimilarityOnly = "SimOnly";

    /// <summary>The order of the ranking: the weight alone.</summary>
    internal const string OrderWeightOnly = "WeightOnly";

    /// <summary>The corpus of the words: <c>tests/TestData/corpora/words.txt.gz</c>.</summary>
    internal const string CorpusWords = "words";

    /// <summary>The corpus of the titles: <c>tests/TestData/corpora/wiki-titles.tsv.gz</c>.</summary>
    internal const string CorpusTitles = "titles";

    /// <summary>The corpus files of the benchmark, by the short name the report shows.</summary>
    internal static readonly (string Name, string File)[] Corpora =
    [
        (CorpusWords, "words.txt"),
        (CorpusTitles, "wiki-titles.tsv")
    ];

    /// <summary>The file of a corpus of the benchmark.</summary>
    /// <param name="name">The short name the report shows.</param>
    /// <returns>The name of the file.</returns>
    internal static string FileOf(string name) =>
        Corpora.FirstOrDefault(corpus => string.Equals(corpus.Name, name, StringComparison.Ordinal)).File
        ?? throw new InvalidOperationException($"The corpus '{name}' is not one of the corpora of the benchmark.");

    /// <summary>The source of a linked index: the content lives in a table of the application.</summary>
    internal const string Linked = "Linked";

    /// <summary>The settings of a benchmark case, read by the name of its parameters.</summary>
    /// <param name="parameters">The parameters of the case.</param>
    /// <returns>The settings.</returns>
    internal static QuerySettings From(IReadOnlyDictionary<string, string> parameters) => new()
    {
        Corpus = Text(parameters, "Corpus"),
        Entries = Number(parameters, "Entries"),
        Input = Text(parameters, "Input"),
        Candidates = Number(parameters, "Candidates"),
        Order = Text(parameters, "Order"),
        Similarity = Text(parameters, "Similarity"),
        Correction = Flag(parameters, "Correction"),
        Source = Text(parameters, "Source")
    };

    /// <summary>
    /// The query of these settings. The benchmark and the report build it here: a line of the report names the hits of
    /// exactly the query that was measured.
    /// </summary>
    /// <param name="index">The index the query runs on.</param>
    /// <returns>The result of the query.</returns>
    internal LuiResult Run(ILuiIndex index) => index
        .Query(Input)
        .Candidates(Candidates)
        .Rank(Rank())
        .Similarity(Measure())
        .TypoTolerance(Correction)
        .Execute();

    /// <summary>The order of the ranking, named as the report names it.</summary>
    private LuiRankingOrder Rank() => Order switch
    {
        OrderStartSimilarity => LuiRankingOrder.WordStartThenSimilarity,
        OrderStartWeight => LuiRankingOrder.WordStartThenWeight,
        OrderSimilarityOnly => LuiRankingOrder.SimilarityOnly,
        OrderWeightOnly => LuiRankingOrder.WeightOnly,
        _ => throw new InvalidOperationException($"The order '{Order}' is not one of the four orders of the ranking.")
    };

    /// <summary>The measure of the similarity, named as the report names it.</summary>
    private ILuiSimilarity Measure() => Similarity switch
    {
        nameof(LuiSimilarity.JaroWinkler) => LuiSimilarity.JaroWinkler,
        nameof(LuiSimilarity.NgramDice) => LuiSimilarity.NgramDice,
        "DamerauLev" => LuiSimilarity.DamerauLevenshtein,
        nameof(LuiSimilarity.Levenshtein) => LuiSimilarity.Levenshtein,
        _ => throw new InvalidOperationException($"The measure '{Similarity}' is not one of the measures of LUI.")
    };

    /// <summary>A parameter of the report, as a text.</summary>
    private static string Text(IReadOnlyDictionary<string, string> parameters, string name) =>
        parameters.TryGetValue(name, out var value)
            ? value
            : throw new InvalidOperationException($"The report has no parameter '{name}'.");

    /// <summary>A parameter of the report, as a number.</summary>
    private static int Number(IReadOnlyDictionary<string, string> parameters, string name) =>
        int.TryParse(Text(parameters, name), System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new InvalidOperationException($"The parameter '{name}' of the report is not a number.");

    /// <summary>A parameter of the report, as a flag.</summary>
    private static bool Flag(IReadOnlyDictionary<string, string> parameters, string name) =>
        bool.TryParse(Text(parameters, name), out var value)
            ? value
            : throw new InvalidOperationException($"The parameter '{name}' of the report is not a flag.");
}
