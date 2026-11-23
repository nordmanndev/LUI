namespace Nordmann.LUI;

/// <summary>
/// What an index still needs at the start of a process (ADR-0076) — <see cref="None"/> when it is ready. It is what
/// <see cref="LuiIndexInfo.NeedsInitialization"/> reports and what <see cref="LuiCatalog.Initialize"/> does something about: read from the state of the
/// file and of the process, step by step. Several needs at once are several flags: <c>Triggers | InitialSync</c> is what a linked index that was
/// just created needs, and its <see cref="object.ToString"/> is readable text for a log — <c>Triggers, InitialSync</c>.
/// </summary>
[Flags]
public enum LuiInitializationNeeds
{
    /// <summary>Nothing: every step this index needs is done, in the file and in this process.</summary>
    None = 0,

    /// <summary>A linked index has no triggers on the table of the application (<see cref="LuiTriggerState.None"/>): the outbox is never filled, and a change of the table is not noticed.</summary>
    Triggers = 1,

    /// <summary>A linked index has some triggers, or they differ from the ones LUI writes now (<see cref="LuiTriggerState.Stale"/>): <c>CreateTriggers()</c> replaces them.</summary>
    StaleTriggers = 2,

    /// <summary>The first complete sync of a linked index is due (<c>NeedsInitialSync</c>, ADR-0077) and no sync of it is running.</summary>
    InitialSync = 4,

    /// <summary>
    /// The first complete sync of a linked index is due, and another sync of it holds the lease right now — in another process, or on another thread
    /// (ADR-0077). It is instead of <see cref="InitialSync"/>, not beside it: <c>Initialize()</c> does not wait for that sync and does not run one of its
    /// own, and the lease of a sync that stopped renewing expires — then the need is <see cref="InitialSync"/> again.
    /// </summary>
    InitialSyncRunningElsewhere = 8,

    /// <summary>
    /// The typo dictionary of an index that forgives typos is not built <b>in this process</b> (ADR-0042): the dictionary lives in memory, every process builds its
    /// own, and a query never builds it — a typo-tolerant index without it never corrects. It is also the state while a build runs.
    /// </summary>
    TypoDictionary = 16,
}
