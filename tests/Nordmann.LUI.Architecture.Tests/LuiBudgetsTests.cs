using System.Text.Json;
using Shouldly;

using Nordmann.LUI.Testing;

namespace Nordmann.LUI.Architecture.Tests;

/// <summary>
/// The budgets of the repository live in one file (ADR-0056) — the promise, its scenario and nothing else. This test
/// holds the shape of that file up: a budget that loses its scenario or its kind is a promise that cannot be measured,
/// and a measured value in the file would be a second truth beside the promise (the measurement belongs in the
/// record, beside the machine it was measured on).
/// </summary>
public sealed class LuiBudgetsTests
{
    /// <summary>The file of the budgets, as it lies in the tree.</summary>
    private static string FilePath =>
        Path.Combine(Repository.RootPath, "tests", "Nordmann.LUI.Testing", "Budgets", "budgets.json");

    /// <summary>The units a budget may carry: the scales of the two kinds (ADR-0041).</summary>
    private static readonly string[] TimeUnits = ["ms", "s", "min"];

    private static readonly string[] SizeUnits = ["KB", "MB"];

    [Fact]
    public void Every_budget_of_the_file_has_a_scenario_a_limit_a_unit_and_a_kind()
    {
        LuiBudgets.All.ShouldNotBeEmpty("the file of the budgets is empty — a promise that is nowhere is no promise.");

        foreach (var budget in LuiBudgets.All)
        {
            budget.Id.ShouldNotBeNullOrWhiteSpace("a budget without an id cannot be asserted.");
            budget.Scenario.Corpus.ShouldNotBeNullOrWhiteSpace($"the budget '{budget.Id}' names no corpus.");
            budget.Scenario.PrefixLength.ShouldBeGreaterThan(0, $"the budget '{budget.Id}' names no scenario.");
            budget.Scenario.Condition.ShouldNotBeNullOrWhiteSpace($"the budget '{budget.Id}' names no condition.");
            budget.Limit.ShouldBeGreaterThan(0, $"the budget '{budget.Id}' has no limit.");
            budget.Unit.ShouldNotBeNullOrWhiteSpace($"the budget '{budget.Id}' has no unit.");
        }
    }

    [Fact]
    public void The_ids_of_the_budgets_are_unique()
    {
        var doubled = LuiBudgets.All
            .GroupBy(budget => budget.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        doubled.ShouldBeEmpty("Every budget of the file is one promise — an id twice is a promise twice.");
    }

    [Fact]
    public void The_kind_of_a_budget_follows_its_unit()
    {
        foreach (var budget in LuiBudgets.All)
        {
            var units = budget.Kind == LuiBudgets.Kind.Time ? TimeUnits : SizeUnits;

            units.Contains(budget.Unit, StringComparer.Ordinal).ShouldBeTrue(
                $"the budget '{budget.Id}' is a {budget.Kind} and carries the unit '{budget.Unit}'.");
        }
    }

    [Fact]
    public void A_budget_carries_nothing_but_its_promise()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FilePath));

        foreach (var budget in document.RootElement.GetProperty("budgets").EnumerateArray())
        {
            Members(budget).ShouldBe(
                ["id", "kind", "limit", "scenario", "unit"],
                $"the budget '{budget.GetProperty("id").GetString()}' carries a member that is not part of the promise.");

            Members(budget.GetProperty("scenario")).ShouldBe(
                ["condition", "corpus", "prefixLength"],
                "a scenario is the corpus, the prefix length and the condition — and nothing else. "
                + "The measured value belongs in the record.");
        }
    }

    /// <summary>The members of an object of the file, sorted.</summary>
    /// <param name="element">The object to read.</param>
    /// <returns>The names of its members.</returns>
    private static string[] Members(JsonElement element) =>
        element.EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

    [Fact]
    public void The_dictionary_of_two_errors_has_a_limit_of_260_MB_and_a_reserve_over_what_was_measured()
    {
        // issue #175 (decision of the owner, 2026-11-14): the limit was 240 MB — exactly what was measured, 100 % of the limit,
        // no reserve. The limit is 260 MB (+8.3 %); the measured 240 MB stays in the record as an observation.
        var budget = LuiBudgets.All.Single(item => string.Equals(item.Id, "typo-dictionary-two-errors-size", StringComparison.Ordinal));

        (budget.Limit == 260d && string.Equals(budget.Unit, "MB", StringComparison.Ordinal))
            .ShouldBeTrue($"the limit of the dictionary of two errors is 260 MB — the file says {budget.Limit} {budget.Unit}");

        // the measured value of the row of TwoErrors of the record: an observation that stays, and a reserve under the limit
        var row = File.ReadAllLines(Path.Combine(Repository.RootPath, "docs", "typo-dictionary.md"))
            .Single(line => line.StartsWith("| `TwoErrors` | built", StringComparison.Ordinal));
        var measured = double.Parse(System.Text.RegularExpressions.Regex.Match(row, @"\*\*(?<mb>\d+) MB\*\*", System.Text.RegularExpressions.RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1)).Groups["mb"].Value, System.Globalization.CultureInfo.InvariantCulture);

        (measured == 240d).ShouldBeTrue($"the measured value of the dictionary of two errors stays in the record: {row}");
        (budget.Limit >= measured * 1.05).ShouldBeTrue($"the limit has a reserve of at least 5 % over the measured {measured} MB");
    }

    [Fact]
    public void Every_budget_of_the_file_is_asserted_by_a_measurement()
    {
        // issue #242: a limit that no measurement compares with a measured value is a number on paper — the dictionary of two
        // errors stood at 100 % of its limit and then at 260 MB, and no test ever held a size against it
        var sources = Directory.EnumerateFiles(Path.Combine(Repository.RootPath, "tests", "Nordmann.LUI.Performance.Tests"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(File.ReadAllText)
            .ToList();

        var unasserted = LuiBudgets.All
            .Select(budget => budget.Id)
            .Where(id => !sources.Any(source => source.Contains($"AtMost(\"{id}\"", StringComparison.Ordinal)))
            .ToList();

        unasserted.ShouldBeEmpty("a budget of budgets.json that no measurement asserts (budget.AtMost(id, …)): " + string.Join(", ", unasserted));
    }
}
