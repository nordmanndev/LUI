using System.Runtime.CompilerServices;

namespace Nordmann.LUI.Integration.Tests.Support;

/// <summary>
/// A test that measures: it runs only when <c>LUI_PERF=1</c>. A budget test
/// on a shared CI machine is a coin toss — the measurements are run on
/// purpose, with the corpus and the machine written down (docs/benchmarks.md).
/// <para>
/// The gate is a dynamic skip (xUnit.net v3, ADR-0051): the runner asks
/// <see cref="TestGates.MeasurementsRun"/> when the test is about to run, and
/// the report says why it did not.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class PerformanceFactAttribute : FactAttribute
{
    public PerformanceFactAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        Skip = "Measurement: set LUI_PERF=1 (LUI_PERF_ENTRIES=3000000 for the full scenario).";
        SkipType = typeof(TestGates);
        SkipUnless = nameof(TestGates.MeasurementsRun);
    }
}
