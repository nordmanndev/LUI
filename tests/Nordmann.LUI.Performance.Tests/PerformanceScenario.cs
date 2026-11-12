using System.Globalization;
using System.Text.Json;

namespace Nordmann.LUI.Performance.Tests;

/// <summary>
/// The scenario of a measurement run (ADR-0058): the settings of the run. The corpus itself is not here — the budgets
/// name their corpus (ADR-0056), and the corpus file carries the entries, their weights and their kind (ADR-0059).
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
/// <param name="PrefixLength">How many entries of the corpus a measurement takes, or null when it takes the
/// scenario of its target.</param>
/// <param name="Batch">How many entries a sync writes in one batch.</param>
/// <param name="MinimumWeight">The smallest weight an entry of the corpus may have; the entries below it are not
/// measured.</param>
/// <param name="Optimize">Whether the index is optimized after the build.</param>
/// <param name="Machine">The machine of the run: the one of the record, or another one.</param>
/// <param name="Database">The file of a database to keep between the runs, or empty for a new one.</param>
public sealed record PerformanceScenario(
    int? PrefixLength,
    int Batch,
    int MinimumWeight,
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
    /// The corpus of a measurement: the corpus file that the budget of the target names, cut to the prefix the run
    /// builds. Which corpus it was is reported: two measurements on two corpora are not a comparison.
    /// </summary>
    /// <param name="budgetId">The id of the budget the measurement belongs to — it names the corpus and the
    /// prefix length.</param>
    /// <param name="name">The name of the corpus file.</param>
    /// <returns>The corpus of the run.</returns>
    public Corpus CorpusOf(string budgetId, out string name)
    {
        var budget = LuiBudgets.By(budgetId);
        name = budget.Scenario.Corpus;
        var prefix = PerformanceBudget.ScenarioEntries(budget.Scenario.PrefixLength, PrefixLength);
        return Corpus.From(CorpusFile.Open(name, prefix, MinimumWeight));
    }

    /// <summary>The line that says what this run measures — the corpus, the size and the settings.</summary>
    /// <param name="corpus">The name of the corpus the entries come from.</param>
    /// <param name="entries">How many entries of the corpus the run takes.</param>
    /// <param name="prefix">The prefix of the scenario of the budget, or null when the run builds the whole file.</param>
    /// <returns>The line.</returns>
    public string Describe(string corpus, int entries, int? prefix) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"scenario: {corpus} ({entries:N0} entries, {(prefix is { } length ? $"prefix {length:N0}" : "the whole corpus")}), "
            + $"{PrefixLength?.ToString(CultureInfo.InvariantCulture) ?? "the scenario of the target"} entries of the corpus, "
            + $"batch {Batch:N0}, "
            + $"minimum weight {MinimumWeight:N0}, machine {Machine}");

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

        Only(scenario, "prefixLength", "batch", "minimumWeight", "optimize", "machine", "database");

        return new PerformanceScenario(
            ReadEntries(scenario),
            Integer(scenario, "batch"),
            Integer(scenario, "minimumWeight"),
            Flag(scenario, "optimize"),
            ReadMachine(Text(scenario, "machine")),
            Text(scenario, "database"));
    }

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

    /// <summary>The prefix a measurement takes, or null when it takes the scenario of its target.</summary>
    /// <param name="scenario">The scenario of the file.</param>
    /// <returns>The number of entries, or null.</returns>
    private static int? ReadEntries(JsonElement scenario)
    {
        if (!scenario.TryGetProperty("prefixLength", out var entries))
        {
            return null;
        }

        if (entries.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return entries.ValueKind == JsonValueKind.Number && entries.TryGetInt32(out var number)
            ? number
            : throw new InvalidOperationException("The scenario file needs the member 'prefixLength' as a whole number, or as null.");
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
