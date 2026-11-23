namespace Nordmann.LUI;

/// <summary>
/// A step of <see cref="LuiCatalog.Initialize"/> (ADR-0076), in the order the call takes them: the triggers of every linked index, the first sync of
/// every linked index that needs one, the outbox, the typo dictionaries. <see cref="ReadState"/> is no step of the work: it is where a failure is
/// named when the state of an index could not be read.
/// </summary>
public enum LuiInitializeStep
{
    /// <summary>Reading the state of the index — the stored definition, the triggers, the flag of the first sync, the lease. Told in a failure, never as a step that starts.</summary>
    ReadState = 0,

    /// <summary><c>CreateTriggers()</c> of a linked index whose triggers are missing or stale.</summary>
    Triggers = 1,

    /// <summary>The first complete <c>Sync()</c> of a linked index, in batches (ADR-0077).</summary>
    InitialSync = 2,

    /// <summary><c>ApplyPending()</c> of a linked index whose outbox holds rows.</summary>
    Outbox = 3,

    /// <summary>The typo dictionary of an index that forgives typos, built in this process.</summary>
    TypoDictionary = 4,
}
