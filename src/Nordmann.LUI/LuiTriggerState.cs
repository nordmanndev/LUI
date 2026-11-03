namespace Nordmann.LUI;

/// <summary>The triggers of a <c>Linked</c> index on the application's table (ADR-0015).</summary>
public enum LuiTriggerState
{
    /// <summary>There are none: the index follows <c>Refresh</c> and <c>Sync</c> only.</summary>
    None = 0,

    /// <summary>All three are there, as <c>TriggerScript()</c> gives them now.</summary>
    Current = 1,

    /// <summary>Some are there, or they differ from <c>TriggerScript()</c>: recreate them.</summary>
    Stale = 2,
}
