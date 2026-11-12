using System.Globalization;

namespace Nordmann.LUI.Sample.Api;

/// <summary>
/// The query string of <c>GET /api/suggest/{index}</c>, mapped one to one onto <see cref="LuiQueryOptions"/> — every
/// setting of a query is a parameter, and a missing one is the default of the library (not a default of the sample).
/// </summary>
internal sealed record SuggestParameters(
    string Text,
    LuiQueryOptions Options,
    int DelayMs)
{
    /// <summary>The slowest the demo may be asked to answer: enough to watch an abort, too little to hold a thread.</summary>
    internal const int MaxDelayMs = 2000;

    private static readonly Dictionary<string, ILuiSimilarity> Similarities = new(StringComparer.OrdinalIgnoreCase)
    {
        ["jaro-winkler"] = LuiSimilarity.JaroWinkler,
        ["ngram-dice"] = LuiSimilarity.NgramDice,
        ["damerau-levenshtein"] = LuiSimilarity.DamerauLevenshtein,
        ["levenshtein"] = LuiSimilarity.Levenshtein,
    };

    /// <summary>The names of the similarity measures the page may ask for.</summary>
    internal static IReadOnlyCollection<string> SimilarityNames => Similarities.Keys;

    /// <summary>Reads the query string. A value that is not valid is a <see cref="FormatException"/> or <see cref="ArgumentOutOfRangeException"/>, which the endpoint answers with a 400.</summary>
    internal static SuggestParameters From(IQueryCollection query)
    {
        var options = new LuiQueryOptions
        {
            MaxResults = Int(query, "take") ?? new LuiQueryOptions().MaxResults,
            Candidates = Int(query, "candidates") ?? new LuiQueryOptions().Candidates,
            Rank = Enum<LuiRankingOrder>(query, "rank") ?? new LuiQueryOptions().Rank,
            PriorityMode = Enum<LuiPriorityMode>(query, "priorityMode") ?? new LuiQueryOptions().PriorityMode,
            Similarity = Similarity(query),
            TypoTolerance = Bool(query, "typo"),
            TrackTimings = Bool(query, "timings") ?? false,
        };

        var delay = Int(query, "delayMs") ?? 0;
        if (delay is < 0 or > MaxDelayMs)
        {
            throw new ArgumentOutOfRangeException(nameof(query), delay, $"delayMs is 0 … {MaxDelayMs}.");
        }

        return new SuggestParameters(query["q"].ToString(), options, delay);
    }

    private static ILuiSimilarity? Similarity(IQueryCollection query)
    {
        var name = Value(query, "similarity");
        if (name is null)
        {
            return null;
        }

        return Similarities.TryGetValue(name, out var similarity)
            ? similarity
            : throw new FormatException($"similarity '{name}' is not one of: {string.Join(", ", Similarities.Keys)}.");
    }

    private static string? Value(IQueryCollection query, string name) =>
        query.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value.ToString()) ? value.ToString() : null;

    private static int? Int(IQueryCollection query, string name) =>
        Value(query, name) is { } text
            ? int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture)
            : null;

    private static bool? Bool(IQueryCollection query, string name) =>
        Value(query, name) is { } text ? bool.Parse(text) : null;

    private static T? Enum<T>(IQueryCollection query, string name)
        where T : struct, Enum =>
        Value(query, name) is { } text
            ? System.Enum.TryParse<T>(text, ignoreCase: true, out var value) && System.Enum.IsDefined(value)
                ? value
                : throw new FormatException($"{name} '{text}' is not one of: {string.Join(", ", System.Enum.GetNames<T>())}.")
            : null;
}
