using System.Globalization;
using System.Text.Json;

namespace Nordmann.LUI.Performance.Tests;

/// <summary>
/// The scenario of a measurement run (ADR-0058): the corpus, the prefix length and the settings.
/// <para>
/// It takes the place of the variables of the environment (ADR-0058 names them). A variable is invisible in the
/// code and in the report; a file is a thing one can read, check in and diff. The file that is checked in is the default
/// scenario, so a run without the lane works too — and the lane of the schedule writes the file before it measures.
/// </para>
/// <para>
/// A member of the file that nobody reads is an error, not a shrug: a file that grows a member beside the ones that are
/// read would be a second truth beside the scenario.
/// </para>
/// </summary>
/// <param name="Corpus">The corpus of the run — the word list of the repository until the corpora of the owner arrive.</param>
/// <param name="PrefixLength">How many entries of the corpus the scenario has.</param>
/// <param name="Kind">The kind of the index the measurement builds: single words or titles.</param>
/// <param name="Entries">How many entries a measurement builds, or null when it builds the scenario of its target.</param>
/// <param name="Batch">How many entries a sync writes in one batch.</param>
/// <param name="Weights">How the weights are spread over the entries.</param>
/// <param name="Optimize">Whether the index is optimized after the build.</param>
/// <param name="Machine">The machine of the run: the one of the record, or another one.</param>
/// <param name="Database">The file of a database to keep between the runs, or empty for a new one.</param>
public sealed record PerformanceScenario(
    string Corpus,
    int PrefixLength,
    CorpusKind Kind,
    int? Entries,
    int Batch,
    WeightDistribution Weights,
    bool Optimize,
    BudgetMachine Machine,
    string Database)
{
    /// <summary>The file of the scenario, in the repository — the lane of the schedule writes it there.</summary>
    public static string FilePath { get; } = Path.Combine(
        Repository.RootPath,
        "tests",
        "Nordmann.LUI.Performance.Tests",
        "performance.scenario.json");

    /// <summary>The scenario of this run: the file of the repository, read once.</summary>
    public static PerformanceScenario Current { get; } = Read();

    /// <summary>
    /// The words of the corpus of this scenario: the whole word list when it is there, the sample otherwise — cut to the
    /// prefix of the scenario. Which corpus it was is reported: two measurements on two corpora are not a comparison.
    /// </summary>
    /// <param name="corpus">The name of the corpus the words come from.</param>
    /// <returns>The words of the corpus.</returns>
    public IReadOnlyList<string> Words(out string corpus)
    {
        var words = TestFiles.MeasurementWords(out corpus);
        return words.Count <= PrefixLength ? words : [.. words.Take(PrefixLength)];
    }

    /// <summary>The line that says what this run measures — the corpus, the size and the machine.</summary>
    /// <param name="corpus">The name of the corpus the words come from.</param>
    /// <param name="words">The words of the corpus.</param>
    /// <returns>The line.</returns>
    public string Describe(string corpus, int words) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"scenario: {Corpus} ({corpus}, {words:N0} words, prefix {PrefixLength:N0}), {Entries?.ToString(CultureInfo.InvariantCulture) ?? "the scenario of the target"} entries, batch {Batch:N0}, {Kind}, {Weights}, machine {Machine}");

    /// <summary>Reads the file of the scenario out of the repository and turns it into a scenario.</summary>
    /// <returns>The scenario of the file.</returns>
    private static PerformanceScenario Read()
    {
        if (!File.Exists(FilePath))
        {
            throw new InvalidOperationException(
                $"The scenario file is missing ({FilePath}). A measurement without a scenario measures nothing (ADR-0058).");
        }

        using var document = JsonDocument.Parse(File.ReadAllText(FilePath));
        var root = document.RootElement;
        Only(root, "note", "scenario");

        if (!root.TryGetProperty("scenario", out var scenario) || scenario.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("The scenario file needs an object under 'scenario'.");
        }

        Only(scenario, "corpus", "prefixLength", "kind", "entries", "batch", "weights", "optimize", "machine", "database");

        return new PerformanceScenario(
            Text(scenario, "corpus"),
            Integer(scenario, "prefixLength"),
            ReadKind(Text(scenario, "kind")),
            ReadEntries(scenario),
            Integer(scenario, "batch"),
            ReadWeights(Text(scenario, "weights")),
            Flag(scenario, "optimize"),
            ReadMachine(Text(scenario, "machine")),
            Text(scenario, "database"));
    }

    /// <summary>The kind of the index: single words or titles.</summary>
    /// <param name="kind">The name of the kind.</param>
    /// <returns>The kind.</returns>
    private static CorpusKind ReadKind(string kind) =>
        kind switch
        {
            "single" => CorpusKind.SingleWord,
            "multi" => CorpusKind.MultiWord,
            _ => throw new InvalidOperationException(
                $"The kind '{kind}' of the scenario is not known: a measurement builds a single-word index or a multi-word one.")
        };

    /// <summary>The weights of the entries: a long tail, or the sparse stock of Anhang A1.</summary>
    /// <param name="weights">The name of the distribution.</param>
    /// <returns>The distribution.</returns>
    private static WeightDistribution ReadWeights(string weights) =>
        weights switch
        {
            "longtail" => WeightDistribution.LongTail,
            "sparse" => WeightDistribution.Sparse,
            _ => throw new InvalidOperationException(
                $"The weights '{weights}' of the scenario are not known: the entries carry a long tail of weights or a sparse stock.")
        };

    /// <summary>The machine of the run: the one of the record, or another one (ADR-0041).</summary>
    /// <param name="machine">The name of the machine.</param>
    /// <returns>The machine.</returns>
    private static BudgetMachine ReadMachine(string machine) =>
        machine switch
        {
            "measurement" => BudgetMachine.Measurement,
            "other" => BudgetMachine.Other,
            _ => throw new InvalidOperationException(
                $"The machine '{machine}' of the scenario is not known: a run is on the machine of the record or on another one.")
        };

    /// <summary>The entries a measurement builds, or null when it builds the scenario of its target.</summary>
    /// <param name="scenario">The scenario of the file.</param>
    /// <returns>The number of entries, or null.</returns>
    private static int? ReadEntries(JsonElement scenario)
    {
        if (!scenario.TryGetProperty("entries", out var entries))
        {
            return null;
        }

        if (entries.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return entries.ValueKind == JsonValueKind.Number && entries.TryGetInt32(out var number)
            ? number
            : throw new InvalidOperationException("The scenario file needs the member 'entries' as a whole number, or as null.");
    }

    /// <summary>A member of the file is a text.</summary>
    /// <param name="element">The object that holds the member.</param>
    /// <param name="name">The name of the member.</param>
    /// <returns>The text of the member.</returns>
    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new InvalidOperationException($"The scenario file needs the member '{name}' as a text.");

    /// <summary>A member of the file is a whole number.</summary>
    /// <param name="element">The object that holds the member.</param>
    /// <param name="name">The name of the member.</param>
    /// <returns>The number of the member.</returns>
    private static int Integer(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : throw new InvalidOperationException($"The scenario file needs the member '{name}' as a whole number.");

    /// <summary>A member of the file is a flag.</summary>
    /// <param name="element">The object that holds the member.</param>
    /// <param name="name">The name of the member.</param>
    /// <returns>Whether the flag is set.</returns>
    private static bool Flag(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : throw new InvalidOperationException($"The scenario file needs the member '{name}' as true or false.");

    /// <summary>
    /// The members of an object of the file: a member that is not known is an error, not a
    /// shrug. A file that grows a member nobody reads would be a second truth beside the
    /// scenario.
    /// </summary>
    /// <param name="element">The object to check.</param>
    /// <param name="known">The members the object may have.</param>
    private static void Only(JsonElement element, params string[] known)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (Array.IndexOf(known, property.Name) < 0)
            {
                throw new InvalidOperationException(
                    $"The scenario file names the member '{property.Name}', which is not one of {string.Join(", ", known)} — "
                    + "a member that nobody reads is a second truth beside the scenario.");
            }
        }
    }
}
