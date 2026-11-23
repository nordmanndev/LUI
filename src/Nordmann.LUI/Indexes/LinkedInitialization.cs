namespace Nordmann.LUI.Indexes;

/// <summary>
/// What a linked index says about its start (ADR-0076), read from the file in one snapshot: the state of its triggers, whether its first complete sync is
/// still due, and whether a sync of it holds the lease right now. <c>Initialize()</c> acts on it, and <c>Indexes()</c> reports it as
/// <see cref="LuiInitializationNeeds"/>.
/// </summary>
/// <param name="Triggers">The state of the triggers on the table of the application.</param>
/// <param name="NeedsInitialSync">Whether the first complete sync is due.</param>
/// <param name="SyncRunning">Whether a sync holds the lease of the index, and it has not expired. Read only while the first sync is due.</param>
internal readonly record struct LinkedInitialization(LuiTriggerState Triggers, bool NeedsInitialSync, bool SyncRunning)
{
    /// <summary>The needs this state names — the triggers and the first sync; the typo dictionary is the process's.</summary>
    internal LuiInitializationNeeds Needs =>
        (Triggers == LuiTriggerState.None ? LuiInitializationNeeds.Triggers : LuiInitializationNeeds.None)
        | (Triggers == LuiTriggerState.Stale ? LuiInitializationNeeds.StaleTriggers : LuiInitializationNeeds.None)
        | (NeedsInitialSync ? (SyncRunning ? LuiInitializationNeeds.InitialSyncRunningElsewhere : LuiInitializationNeeds.InitialSync) : LuiInitializationNeeds.None);
}
