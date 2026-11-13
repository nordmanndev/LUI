namespace Nordmann.LUI.Testing;

/// <summary>
/// A target of the budgets that is missed and has no waiver. The run fails with the reason: a target that is missed is
/// fixed or waived with a reason and a record (ADR-0020) — never lowered quietly.
/// <para>
/// The exception lives in the shared infrastructure and not in the test framework: the budget logic is a rule, and a rule
/// is tested in the unit layer (ADR-0058).
/// </para>
/// </summary>
public sealed class BudgetTargetMissedException(IReadOnlyList<string> missed)
    : InvalidOperationException(
        "A target of the budgets that is missed is fixed or waived with a reason and a record (ADR-0020) — never lowered quietly: "
        + string.Join("; ", missed))
{
    /// <summary>The targets that were missed, with their measured value and their limit.</summary>
    public IReadOnlyList<string> Missed { get; } = missed;
}
