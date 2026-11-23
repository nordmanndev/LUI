namespace Nordmann.LUI;

/// <summary>
/// What <see cref="LuiCatalog.Initialize"/> did for one index (ADR-0076) — one line of <see cref="LuiInitializeReport.Indexes"/>: each step that ran, what is
/// left, whether the index failed and how long it took. An index that needed nothing has an empty line.
/// </summary>
public sealed record LuiInitializeIndexReport
{
    /// <summary>The name of the index.</summary>
    public required string Name { get; init; }

    /// <summary>Whether this call created or replaced the triggers of the linked index.</summary>
    public bool TriggersInstalled { get; init; }

    /// <summary>What the first sync of the linked index did, when this call ran it; <see langword="null"/> when it ran none — nothing was due, the lease was held, or it was switched off.</summary>
    public LuiLinkedSyncReport? Sync { get; init; }

    /// <summary>The rows of the outbox this call brought into the linked index.</summary>
    public int PendingApplied { get; init; }

    /// <summary>Whether this call built the typo dictionary of the index in this process.</summary>
    public bool TypoDictionaryBuilt { get; init; }

    /// <summary>What the index still needs when the call is through — <see cref="LuiInitializationNeeds.None"/> when it is ready. Read again from the state at the end.</summary>
    public LuiInitializationNeeds Skipped { get; init; }

    /// <summary>Whether a step of the index failed: its later steps were skipped, the other indexes went on. The error is in <see cref="LuiInitializeException.Errors"/>.</summary>
    public bool Failed { get; init; }

    /// <summary>The step that failed; <see langword="null"/> when none did.</summary>
    public LuiInitializeStep? FailedStep { get; init; }

    /// <summary>The time the call spent on this index.</summary>
    public TimeSpan Elapsed { get; init; }
}
