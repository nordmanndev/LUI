using Shouldly;
using Xunit.Abstractions;

namespace Nordmann.LUI.Integration.Tests;

/// <summary>
/// The targets of §16 come in two kinds: the ones that are **bytes** — the same on every machine — and the ones that
/// are **times**, and a time belongs to the machine it was measured on. A machine that is not the one of the record
/// therefore reports the times and asserts the bytes (ADR-0041): a shared runner cannot decide a time target, but it
/// can decide a size.
/// </summary>
public sealed class PerformanceBudget_Machine(ITestOutputHelper output)
{
    [Fact]
    public void A_time_belongs_to_the_machine_it_was_measured_on()
    {
        PerformanceBudget.BelongsToTheMachine("ms").ShouldBeTrue();
        PerformanceBudget.BelongsToTheMachine("s").ShouldBeTrue();
    }

    [Fact]
    public void A_size_belongs_to_no_machine()
    {
        PerformanceBudget.BelongsToTheMachine("KB").ShouldBeFalse();
        PerformanceBudget.BelongsToTheMachine("MB").ShouldBeFalse();
    }

    [Fact]
    public void Another_machine_reports_a_missed_time_and_does_not_fail()
    {
        var budget = new PerformanceBudget(output, scenario: true, PerformanceBudget.Machine.Other);

        budget.AtMost("query p50", "the condition of the target", measured: 5.0, limit: 2.0, unit: "ms");

        Should.NotThrow(() => budget.Verify());
    }

    [Fact]
    public void Another_machine_asserts_a_missed_size_and_fails()
    {
        var budget = new PerformanceBudget(output, scenario: true, PerformanceBudget.Machine.Other);

        budget.AtMost("allocation p95", "the condition of the target", measured: 150.0, limit: 100.0, unit: "KB");

        Should.Throw<ShouldAssertException>(() => budget.Verify());
    }

    [Fact]
    public void The_machine_of_the_record_asserts_a_missed_time_and_fails()
    {
        var budget = new PerformanceBudget(output, scenario: true, PerformanceBudget.Machine.Measurement);

        budget.AtMost("query p50", "the condition of the target", measured: 5.0, limit: 2.0, unit: "ms");

        Should.Throw<ShouldAssertException>(() => budget.Verify());
    }

    [Fact]
    public void A_run_that_is_not_the_scenario_reports_every_target_and_fails_nothing()
    {
        var budget = new PerformanceBudget(output, scenario: false, PerformanceBudget.Machine.Measurement);

        budget.AtMost("query p50", "the condition of the target", measured: 5.0, limit: 2.0, unit: "ms");

        Should.NotThrow(() => budget.Verify());
    }
}
