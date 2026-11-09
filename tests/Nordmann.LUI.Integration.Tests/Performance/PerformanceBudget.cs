using System.Globalization;
using Shouldly;

namespace Nordmann.LUI.Integration.Tests.Performance;

/// <summary>
/// The targets of one scenario: each one measured, then met, waived or missed.
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

    /// <summary>Whether a target in this unit belongs to the machine it was measured on: a time does, a size does not.</summary>
    internal static bool BelongsToTheMachine(string unit) => unit is "ms" or "s";

    /// <summary>The machine of this run: the one of the record, unless the run says that it is another one.</summary>
    internal static BudgetMachine CurrentMachine() =>
        string.Equals(Environment.GetEnvironmentVariable("LUI_PERF_MACHINE"), "other", StringComparison.Ordinal)
            ? BudgetMachine.Other
            : BudgetMachine.Measurement;

    /// <summary>
    /// The line that says which machine this run is on — the times of §16 belong to it.
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
    /// </summary>
    internal void AtMost(string target, string condition, double measured, double limit, string unit)
    {
        var met = measured <= limit;
        var waiver = Waivers.GetValueOrDefault(target);

        // a time belongs to the machine it was measured on: on another machine it is reported, never missed
        // (ADR-0041). A size — bytes — is the same everywhere and is asserted.
        var foreign = BelongsToTheMachine(unit) && machine != BudgetMachine.Measurement;

        var verdict = (met, waiver, scenario, foreign) switch
        {
            (_, _, false, _) => "reported (not the scenario)",
            (true, null, _, _) => "met",
            (true, _, _, _) => "met — the waiver can go",
            (_, _, _, true) => "reported (belongs to the machine it was measured on)",
            (false, null, _, _) => "MISSED",
            (false, _, _, _) => $"waived: {waiver}",
        };

        var line = string.Create(CultureInfo.InvariantCulture, $"budget | {target,-28} | {condition,-58} | {measured,8:F2} {unit,-2} | ≤ {limit} {unit} | {verdict}");
        output.WriteLine(line);
        Console.Error.WriteLine("[perf] " + line);

        if (scenario && !met && waiver is null && !foreign)
        {
            _missed.Add($"{target}: {measured:F2} {unit} > {limit} {unit} ({condition})");
        }
    }

    /// <summary>The targets that were missed: the run fails if there is one.</summary>
    internal void Verify() =>
        _missed.ShouldBeEmpty("A target of §16 that is missed is fixed or waived with a reason and a record (ADR-0020) — never lowered quietly.");
}
