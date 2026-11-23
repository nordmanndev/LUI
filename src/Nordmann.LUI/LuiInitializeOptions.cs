namespace Nordmann.LUI;

/// <summary>
/// What <see cref="LuiCatalog.Initialize"/> does (ADR-0076): each step can be switched off, the run can be limited to some indexes, and the first
/// syncs and the progress can be set. Every default is what an application wants at the start of a process: all of it, for every index. A record with
/// <c>init</c> properties; the four switches can be bound from configuration, the definitions and the progress cannot.
/// </summary>
public sealed record LuiInitializeOptions
{
    /// <summary>
    /// Creates the triggers of every linked index whose triggers are missing or stale (<c>CreateTriggers()</c>, idempotent, ADR-0077) — the first step. Default: <see langword="true"/>.
    /// <b>Off, the first sync runs without triggers — the order that can lose a row written in between (ADR-0077).</b> An index whose triggers are missing and not installed here keeps the need <see cref="LuiInitializationNeeds.Triggers"/>, and gets no typo dictionary from this call.
    /// </summary>
    public bool InstallTriggers { get; init; } = true;

    /// <summary>
    /// Runs the first complete <c>Sync()</c> of every linked index that still needs one, in batches, one index after the other (ADR-0077), guarded by its lease: a sync
    /// that another process or thread is running is skipped and reported as <see cref="LuiInitializationNeeds.InitialSyncRunningElsewhere"/>. Default: <see langword="true"/>.
    /// </summary>
    public bool InitialSync { get; init; } = true;

    /// <summary>Applies the outbox of every linked index once — the rows its triggers noted. Default: <see langword="true"/>.</summary>
    public bool ApplyPending { get; init; } = true;

    /// <summary>
    /// Builds the typo dictionary of every index that forgives typos, <b>one after the other</b> and last, so that it knows the words of the syncs and the outbox. Default: <see langword="true"/>.
    /// A dictionary costs seconds and memory — some hundred MB at the size of the German word list.
    /// </summary>
    public bool WarmUpTypoDictionaries { get; init; } = true;

    /// <summary>
    /// The indexes to initialize, by their definitions: <c>[SearchIndexes.Chefs]</c>. <see langword="null"/> is <b>every index of the catalog</b>, read from the stored
    /// definitions — a tool that has no definitions can initialize everything. Only the names are used. A definition of an index that does not exist is a
    /// <see cref="LuiIndexNotFoundException"/> before any step.
    /// </summary>
    public IReadOnlyCollection<LuiIndexDefinitionBase>? Indexes { get; init; }

    /// <summary>The options of the first syncs, above all <see cref="LuiSyncOptions.BatchSize"/>; <see langword="null"/> takes the defaults. Its <see cref="LuiSyncOptions.Progress"/> is called as well.</summary>
    public LuiSyncOptions? Sync { get; init; }

    /// <summary>Told before every step, and after every batch of a first sync (<see cref="LuiInitializeProgress"/>), on the thread of the call; <see langword="null"/> for no reports.</summary>
    public IProgress<LuiInitializeProgress>? Progress { get; init; }
}
