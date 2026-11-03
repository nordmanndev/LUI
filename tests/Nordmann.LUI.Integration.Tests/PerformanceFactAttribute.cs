namespace Nordmann.LUI.Integration.Tests;

/// <summary>
/// A test that measures: it runs only when <c>LUI_PERF=1</c>. A budget test
/// on a shared CI machine is a coin toss — the measurements are run on
/// purpose, with the corpus and the machine written down (docs/benchmarks.md).
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class PerformanceFactAttribute : FactAttribute
{
    public PerformanceFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("LUI_PERF"), "1", StringComparison.Ordinal))
        {
            Skip = "Measurement: set LUI_PERF=1 (LUI_PERF_ENTRIES=3000000 for the full scenario).";
        }
    }
}
