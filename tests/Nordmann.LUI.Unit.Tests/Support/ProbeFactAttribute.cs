using System.Runtime.CompilerServices;

using Xunit;

namespace Nordmann.LUI.Unit.Tests.Support;

/// <summary>
/// The fact of the probe of the measured window (issue #134): it is skipped with a reason
/// when the probe is not armed — a skipped test that looks green proves nothing.
/// <para>
/// The gate is a dynamic skip (xUnit.net v3, ADR-0051): the runner asks
/// <see cref="ProbeGate.ProbeRuns"/> when the test is about to run, and the report says why
/// it did not.
/// </para>
/// </summary>
public sealed class ProbeFactAttribute : FactAttribute
{
    public ProbeFactAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        Skip = "The probe of the measured window is not armed (LUI_ALLOCATION_PROBE=1 arms it).";
        SkipType = typeof(ProbeGate);
        SkipUnless = nameof(ProbeGate.ProbeRuns);
    }
}
