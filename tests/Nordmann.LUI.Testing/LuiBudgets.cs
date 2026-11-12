using System.Text.Json;

namespace Nordmann.LUI.Testing;

/// <summary>
/// The budgets of LUI — one file, two readers (ADR-0056). The file
/// (<c>Budgets/budgets.json</c>) is an embedded resource of this assembly and the
/// only place a limit is written down: the table of the entrance and the tables
/// of the record are checked against it, and the measurements are asserted
/// against it.
/// <para>
/// A budget names its <b>scenario</b> (the corpus file, the prefix length, the
/// condition), its <b>limit</b>, its <b>unit</b> and its <b>kind</b>: a time
/// belongs to the machine it was measured on, a size belongs to nobody
/// (ADR-0041). The measured value does not live here — it lives in the record,
/// beside the machine it was measured on.
/// </para>
/// </summary>
public static class LuiBudgets
{
    /// <summary>The name of the embedded resource: the file travels with the assembly.</summary>
    private const string ResourceName = "Nordmann.LUI.Testing.Budgets.budgets.json";

    private static readonly Lazy<List<Budget>> Budgets = new(Read);

    private static readonly Lazy<IReadOnlyDictionary<string, Budget>> ById = new(() =>
        Budgets.Value.ToDictionary(budget => budget.Id, StringComparer.Ordinal));

    /// <summary>Every budget of the file, in the order of the file.</summary>
    public static IReadOnlyList<Budget> All => Budgets.Value;

    /// <summary>
    /// The budget with that id — the id is the name of the promise, and it is what
    /// the measurement asserts.
    /// </summary>
    /// <param name="id">The id of the budget, as the file writes it.</param>
    /// <returns>The budget of that id.</returns>
    public static Budget By(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        return ById.Value.TryGetValue(id, out var budget)
            ? budget
            : throw new InvalidOperationException(
                $"The budget '{id}' is not in the file — the budgets are: {string.Join(", ", ById.Value.Keys)}.");
    }

    /// <summary>Whether the file has a budget with that id.</summary>
    /// <param name="id">The id of the budget, as the file writes it.</param>
    /// <param name="budget">The budget of that id, when there is one.</param>
    /// <returns>Whether the file has that budget.</returns>
    public static bool TryGet(string id, out Budget budget)
    {
        ArgumentNullException.ThrowIfNull(id);

        return ById.Value.TryGetValue(id, out budget!);
    }

    /// <summary>Reads the file out of the assembly and turns it into budgets.</summary>
    /// <returns>The budgets of the file, in the order of the file.</returns>
    private static List<Budget> Read()
    {
        using var stream = typeof(LuiBudgets).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The budget file is missing from the assembly: {ResourceName}.");

        using var document = JsonDocument.Parse(stream);

        var root = document.RootElement;
        Only(root, "note", "budgets");

        if (!root.TryGetProperty("budgets", out var budgets) || budgets.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("The budget file needs an array of budgets under 'budgets'.");
        }

        var read = new List<Budget>();
        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var budget in budgets.EnumerateArray())
        {
            var parsed = ReadBudget(budget);

            if (!ids.Add(parsed.Id))
            {
                throw new InvalidOperationException($"The budget '{parsed.Id}' is in the file twice — every budget is one promise.");
            }

            read.Add(parsed);
        }

        return read;
    }

    /// <summary>One budget of the file.</summary>
    /// <param name="budget">The budget as the file writes it.</param>
    /// <returns>The budget.</returns>
    private static Budget ReadBudget(JsonElement budget)
    {
        Only(budget, "id", "scenario", "limit", "unit", "kind");

        var id = Text(budget, "id");
        var limit = Number(budget, "limit");
        var unit = Text(budget, "unit");

        if (!budget.TryGetProperty("scenario", out var scenario) || scenario.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"The budget '{id}' needs a scenario: the corpus file, the prefix length and the condition.");
        }

        Only(scenario, "corpus", "prefixLength", "condition");

        return new Budget(
            id,
            new Scenario(Text(scenario, "corpus"), Integer(scenario, "prefixLength"), Text(scenario, "condition")),
            limit,
            unit,
            ReadKind(budget, id));
    }

    /// <summary>The kind of a budget: a time or a size (ADR-0041).</summary>
    /// <param name="budget">The budget as the file writes it.</param>
    /// <param name="id">The id of the budget, for the error message.</param>
    /// <returns>The kind of the budget.</returns>
    private static Kind ReadKind(JsonElement budget, string id)
    {
        var kind = Text(budget, "kind");

        return kind switch
        {
            "time" => Kind.Time,
            "size" => Kind.Size,
            _ => throw new InvalidOperationException(
                $"The kind '{kind}' of the budget '{id}' is not known: a budget is a time or a size (ADR-0041).")
        };
    }

    /// <summary>A member of the file that is a text.</summary>
    /// <param name="element">The object that holds the member.</param>
    /// <param name="name">The name of the member.</param>
    /// <returns>The text of the member.</returns>
    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new InvalidOperationException($"The budget file needs the member '{name}' as a text.");

    /// <summary>A member of the file that is a number.</summary>
    /// <param name="element">The object that holds the member.</param>
    /// <param name="name">The name of the member.</param>
    /// <returns>The number of the member.</returns>
    private static double Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : throw new InvalidOperationException($"The budget file needs the member '{name}' as a number.");

    /// <summary>A member of the file that is a whole number.</summary>
    /// <param name="element">The object that holds the member.</param>
    /// <param name="name">The name of the member.</param>
    /// <returns>The number of the member.</returns>
    private static int Integer(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : throw new InvalidOperationException($"The budget file needs the member '{name}' as a whole number.");

    /// <summary>
    /// The members of an object of the file: a member that is not known is an error, not a
    /// shrug. A file that grows a member nobody reads — a measured value, a comment — would
    /// be a second truth beside the promise.
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
                    $"The budget file names the member '{property.Name}', which is not one of {string.Join(", ", known)} — "
                    + "the measured value belongs in the record, not in the file.");
            }
        }
    }

    /// <summary>
    /// One budget: the scenario it is measured in, the limit it may not exceed, the
    /// unit of the limit and the kind of the limit.
    /// </summary>
    /// <param name="Id">The name of the promise — what the measurement asserts.</param>
    /// <param name="Scenario">The scenario the limit is measured in.</param>
    /// <param name="Limit">The limit, in the unit of the promise.</param>
    /// <param name="Unit">The unit of the limit: <c>ms</c>, <c>s</c>, <c>min</c>, <c>KB</c> or <c>MB</c>.</param>
    /// <param name="Kind">Whether the limit is a time or a size (ADR-0041).</param>
    public sealed record Budget(string Id, Scenario Scenario, double Limit, string Unit, Kind Kind)
    {
        /// <summary>
        /// The limit in the unit of a measurement: the promise of four minutes is compared
        /// against the seconds of a run. The unit of the budget and the unit of the
        /// measurement must be of the same kind — a time against a time, a size against a size.
        /// </summary>
        /// <param name="unit">The unit of the measurement.</param>
        /// <returns>The limit in that unit.</returns>
        public double LimitIn(string unit)
        {
            ArgumentNullException.ThrowIfNull(unit);

            if (string.Equals(Unit, unit, StringComparison.Ordinal))
            {
                return Limit;
            }

            return Limit * Factor(Kind, Unit) / Factor(Kind, unit);
        }

        /// <summary>The factor of a unit: the scale of its kind — seconds for a time, bytes for a size.</summary>
        /// <param name="kind">The kind of the budget.</param>
        /// <param name="unit">The unit to factor.</param>
        /// <returns>The factor of the unit.</returns>
        private static double Factor(Kind kind, string unit)
        {
            if (kind == Kind.Time)
            {
                return unit switch
                {
                    "ms" => 0.001,
                    "s" => 1,
                    "min" => 60,
                    _ => throw new ArgumentException($"'{unit}' is not a unit of time.", nameof(unit))
                };
            }

            return unit switch
            {
                "KB" => 1024,
                "MB" => 1024 * 1024,
                _ => throw new ArgumentException($"'{unit}' is not a unit of size.", nameof(unit))
            };
        }
    }

    /// <summary>
    /// The scenario of a budget: the corpus it is measured on, how many entries of it,
    /// and the condition of the measurement.
    /// </summary>
    /// <param name="Corpus">The file of the corpus, as the plan of the work names it.</param>
    /// <param name="PrefixLength">How many entries of the corpus the scenario has.</param>
    /// <param name="Condition">The condition of the measurement.</param>
    public sealed record Scenario(string Corpus, int PrefixLength, string Condition);

    /// <summary>
    /// The two kinds of a budget (ADR-0041): a <b>time</b> belongs to the machine it was
    /// measured on and is reported on any other one; a <b>size</b> belongs to nobody and
    /// is asserted everywhere.
    /// </summary>
    public enum Kind
    {
        /// <summary>A time: the latency of a query, the build of an index, the build of a dictionary.</summary>
        Time,

        /// <summary>A size: the allocation of a query, the memory of a dictionary, the size of a file.</summary>
        Size
    }
}
