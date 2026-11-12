using System.Globalization;
using Shouldly;

using Nordmann.LUI.Testing;

namespace Nordmann.LUI.Integration.Tests.Performance;

/// <summary>
/// The targets of one scenario: each one measured, then met, waived or missed.
/// <para>
/// The limits are not written here: they live in one file, with their scenario,
/// their unit and their kind (ADR-0056). This class reads the file and measures
/// against it — one source, two readers.
/// </para>
/// </summary>
internal sealed class PerformanceBudget(ITestOutputHelper output, bool scenario, BudgetMachine machine)
{

    /// <summary>
    /// The targets that are missed on purpose, with the reason and the record that holds it. A target that is not here
    /// has to be met. The owner's decisions of 2026-11-04 (ADR-0020) stated four targets anew, and they are asserted;
    /// ADR-0032 lowered the allocation (the causes of #30 are measured, the allocation is 83 % lower) and the build
    /// stays at four minutes with an issue for a faster first load (#32). Empty.
    /// </summary>
    internal static readonly Dictionary<string, string> Waivers = new(StringComparer.Ordinal);

    private readonly List<string> _missed = [];

    /// <summary>Whether a target of this kind belongs to the machine it was measured on: a time does, a size does not.</summary>
    internal static bool BelongsToTheMachine(LuiBudgets.Kind kind) => kind == LuiBudgets.Kind.Time;

    /// <summary>
    /// The entries a run builds: the scenario of the target, or fewer when <c>LUI_PERF_ENTRIES</c> asks for a quick run —
    /// never more. A size set for one scenario must not grow another: the first run of the lane built three million titles
    /// for the target of one million, and the file of three million missed the 500 MB of one (issue #99).
    /// </summary>
    /// <param name="scenario">The size of the scenario of the target.</param>
    /// <param name="configured">The value of <c>LUI_PERF_ENTRIES</c>, or null.</param>
    /// <returns>The number of entries to build.</returns>
    internal static int ScenarioEntries(int scenario, string? configured) =>
        configured is null
            ? scenario
            : Math.Min(int.Parse(configured, System.Globalization.CultureInfo.InvariantCulture), scenario);

    /// <summary>The machine of this run: the one of the record, unless the run says that it is another one.</summary>
    internal static BudgetMachine CurrentMachine() =>
        string.Equals(Environment.GetEnvironmentVariable("LUI_PERF_MACHINE"), "other", StringComparison.Ordinal)
            ? BudgetMachine.Other
            : BudgetMachine.Measurement;

    /// <summary>
    /// The line that says which machine this run is on — the times of the file belong to it.
    /// </summary>
    internal void SayTheMachine()
    {
        var line = string.Create(CultureInfo.InvariantCulture,
            $"budget machine: {machine} — {(machine == BudgetMachine.Measurement ? "every target is asserted" : "the times are reported, the sizes are asserted")}");
        output.WriteLine(line);
        Console.Error.WriteLine("[perf] " + line);
    }

    /// <summary>
    /// One target: measured, then met, waived or missed. A target that is missed and has no waiver fails the run.
    /// <para>
    /// The limit, the unit, the condition and the kind of the target come from the file of the budgets (ADR-0056);
    /// the unit that comes with the measurement is the unit the limit is brought into — the promise of four minutes
    /// is compared against the seconds of a run.
    /// </para>
    /// </summary>
    /// <param name="id">The id of the target, as the file of the budgets writes it.</param>
    /// <param name="measured">The measured value, in the unit of the measurement.</param>
    /// <param name="unit">The unit of the measurement.</param>
    internal void AtMost(string id, double measured, string unit)
    {
        var budget = LuiBudgets.By(id);
        var limit = budget.LimitIn(unit);
        var condition = budget.Scenario.Condition;

        var met = measured <= limit;
        var waiver = Waivers.GetValueOrDefault(id);

        // a time belongs to the machine it was measured on: on another machine it is reported, never missed
        // (ADR-0041). A size — bytes — is the same everywhere and is asserted.
        var foreign = BelongsToTheMachine(budget.Kind) && machine != BudgetMachine.Measurement;

        var verdict = (met, waiver, scenario, foreign) switch
        {
            (_, _, false, _) => "reported (not the scenario)",
            (true, null, _, _) => "met",
            (true, _, _, _) => "met — the waiver can go",
            (_, _, _, true) => "reported (belongs to the machine it was measured on)",
            (false, null, _, _) => "MISSED",
            (false, _, _, _) => $"waived: {waiver}",
        };

        var line = string.Create(CultureInfo.InvariantCulture, $"budget | {id,-28} | {condition,-58} | {measured,8:F2} {unit,-2} | ≤ {limit} {unit} | {verdict}");
        output.WriteLine(line);
        Console.Error.WriteLine("[perf] " + line);

        if (scenario && !met && waiver is null && !foreign)
        {
            _missed.Add($"{id}: {measured:F2} {unit} > {limit} {unit} ({condition})");
        }
    }

    /// <summary>The targets that were missed: the run fails if there is one.</summary>
    internal void Verify() =>
        _missed.ShouldBeEmpty("A target of the budgets that is missed is fixed or waived with a reason and a record (ADR-0020) — never lowered quietly.");
}
