using Shouldly;

using Nordmann.LUI.Testing;

namespace Nordmann.LUI.Integration.Tests.Performance;

/// <summary>
/// The targets of §16 come in two kinds: the ones that are **bytes** — the same on every machine — and the ones that
/// are **times**, and a time belongs to the machine it was measured on. A machine that is not the one of the record
/// therefore reports the times and asserts the bytes (ADR-0041): a shared runner cannot decide a time target, but it
/// can decide a size.
/// <para>
/// The limits are not written in the tests: they live in the file of the budgets (ADR-0056), and the tests measure
/// against it — one above the limit of the file is a missed target, whatever the file says.
/// </para>
/// </summary>
public sealed class BudgetMachineTests(ITestOutputHelper output)
{
    [Fact]
    public void A_run_never_builds_more_than_the_scenario_of_its_target()
    {
        // issue #99: the lane sets LUI_PERF_ENTRIES=3000000 for the single-word index, and the target of one million
        // titles built three million — its file missed the 500 MB of one million
        PerformanceBudget.ScenarioEntries(1_000_000, "3000000").ShouldBe(1_000_000);
        PerformanceBudget.ScenarioEntries(3_000_000, "3000000").ShouldBe(3_000_000);
    }

    [Fact]
    public void A_quick_run_builds_fewer_and_no_setting_builds_the_scenario()
    {
        PerformanceBudget.ScenarioEntries(3_000_000, "200000").ShouldBe(200_000);
        PerformanceBudget.ScenarioEntries(1_000_000, null).ShouldBe(1_000_000);
    }

    [Fact]
    public void A_time_belongs_to_the_machine_it_was_measured_on()
    {
        PerformanceBudget.BelongsToTheMachine(LuiBudgets.Kind.Time).ShouldBeTrue();
    }

    [Fact]
    public void A_size_belongs_to_no_machine()
    {
        PerformanceBudget.BelongsToTheMachine(LuiBudgets.Kind.Size).ShouldBeFalse();
    }

    [Fact]
    public void Another_machine_reports_a_missed_time_and_does_not_fail()
    {
        var budget = new PerformanceBudget(output, scenario: true, BudgetMachine.Other);
        var target = LuiBudgets.By("query-p50");

        budget.AtMost("query-p50", target.Limit + 1, target.Unit);

        Should.NotThrow(() => budget.Verify());
    }

    [Fact]
    public void Another_machine_asserts_a_missed_size_and_fails()
    {
        var budget = new PerformanceBudget(output, scenario: true, BudgetMachine.Other);
        var target = LuiBudgets.By("allocation-p95");

        budget.AtMost("allocation-p95", target.Limit + 1, target.Unit);

        Should.Throw<ShouldAssertException>(() => budget.Verify());
    }

    [Fact]
    public void The_machine_of_the_record_asserts_a_missed_time_and_fails()
    {
        var budget = new PerformanceBudget(output, scenario: true, BudgetMachine.Measurement);
        var target = LuiBudgets.By("query-p50");

        budget.AtMost("query-p50", target.Limit + 1, target.Unit);

        Should.Throw<ShouldAssertException>(() => budget.Verify());
    }

    [Fact]
    public void A_run_that_is_not_the_scenario_reports_every_target_and_fails_nothing()
    {
        var budget = new PerformanceBudget(output, scenario: false, BudgetMachine.Measurement);
        var target = LuiBudgets.By("query-p50");

        budget.AtMost("query-p50", target.Limit + 1, target.Unit);

        Should.NotThrow(() => budget.Verify());
    }
}
