using System.Globalization;
using Shouldly;
using Xunit.Abstractions;

namespace Nordmann.LUI.Integration.Tests;

/// <summary>
/// The targets of one scenario: each one measured, then met, waived or missed.
/// </summary>
internal sealed class PerformanceBudget(ITestOutputHelper output, bool scenario, Machine machine)
{
    /// <summary>The machine a run of the budgets happens on: the one of the record, or another one.</summary>
    internal enum Machine
    {
        /// <summary>
        /// The machine of the record (Intel Xeon 2.10 GHz, 4 cores, 15 GB, Ubuntu 24.04). Every target of §16 is
        /// asserted here: the times were measured here, and only here they can be met or missed.
        /// </summary>
        Measurement,

        /// <summary>
        /// Another machine. A time belongs to the machine it was measured on, so the times are **reported** there; the
        /// sizes — bytes — are the same on every machine and are asserted (ADR-0041).
        /// </summary>
        Other
    }

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
    internal static Machine CurrentMachine() =>
        string.Equals(Environment.GetEnvironmentVariable("LUI_PERF_MACHINE"), "other", StringComparison.Ordinal)
            ? Machine.Other
            : Machine.Measurement;

    /// <summary>
    /// The line that says which machine this run is on — the times of §16 belong to it.
    /// </summary>
    internal void SayTheMachine()
    {
        var line = string.Create(CultureInfo.InvariantCulture, $"budget machine: {machine}");
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
        var verdict = (met, waiver, scenario) switch
        {
            (_, _, false) => "reported (not the scenario)",
            (true, null, _) => "met",
            (true, _, _) => "met — the waiver can go",
            (false, null, _) => "MISSED",
            (false, _, _) => $"waived: {waiver}",
        };

        var line = string.Create(CultureInfo.InvariantCulture, $"budget | {target,-28} | {condition,-58} | {measured,8:F2} {unit,-2} | ≤ {limit} {unit} | {verdict}");
        output.WriteLine(line);
        Console.Error.WriteLine("[perf] " + line);

        if (scenario && !met && waiver is null)
        {
            _missed.Add($"{target}: {measured:F2} {unit} > {limit} {unit} ({condition})");
        }
    }

    /// <summary>The targets that were missed: the run fails if there is one.</summary>
    internal void Verify() =>
        _missed.ShouldBeEmpty("A target of §16 that is missed is fixed or waived with a reason and a record (ADR-0020) — never lowered quietly.");
}
