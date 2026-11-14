namespace Nordmann.LUI.Unit.Tests.Support;

/// <summary>
/// The gate of the probe of the measured window (issue #134): the probe runs when it is
/// armed, and it is armed by the lane of the probe alone — the variable of the environment
/// is set there, and nowhere else. The map of the tests names the gate.
/// </summary>
public static class ProbeGate
{
    /// <summary>The probe runs when it is armed.</summary>
    public static bool ProbeRuns =>
        string.Equals(Environment.GetEnvironmentVariable("LUI_ALLOCATION_PROBE"), "1", StringComparison.Ordinal);
}
