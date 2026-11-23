namespace Nordmann.LUI;

/// <summary>
/// What <see cref="LuiCatalog.Initialize"/> tells its <see cref="LuiInitializeOptions.Progress"/> (ADR-0076): <b>right before</b> each step of an index —
/// the index and the step, <see cref="Sync"/> is <see langword="null"/> — and after every batch of a first sync, with the report of the sync.
/// It is called on the thread of the call, outside of every lock of LUI. A step that is not needed is not told: a second call right after the
/// first tells nothing.
/// </summary>
public sealed record LuiInitializeProgress
{
    /// <summary>The name of the index the step is for.</summary>
    public required string IndexName { get; init; }

    /// <summary>The step: the one that is about to begin, or — with <see cref="Sync"/> set — the first sync that is running.</summary>
    public required LuiInitializeStep Step { get; init; }

    /// <summary>What the first sync has done so far (<see cref="LuiSyncOptions.Progress"/>), after each batch; <see langword="null"/> when a step is told.</summary>
    public LuiSyncReport? Sync { get; init; }
}
