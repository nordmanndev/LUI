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
}
