namespace Nordmann.LUI.Testing;

/// <summary>The machine a run of the budgets happens on: the one of the record, or another one.</summary>
public enum BudgetMachine
{
    /// <summary>
    /// The machine of the record (Intel Xeon 2.1 GHz, 4 cores, 16 GB, Ubuntu 24.04). Every target of §16 is asserted
    /// here: the times were measured here, and only here they can be met or missed.
    /// </summary>
    Measurement,

    /// <summary>
    /// Another machine. A time belongs to the machine it was measured on, so the times are **reported** there; the
    /// sizes — bytes — are the same on every machine and are asserted (ADR-0041).
    /// </summary>
    Other
}
