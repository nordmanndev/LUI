using Shouldly;

namespace Nordmann.LUI.Unit.Tests.Similarity;

/// <summary>
/// The probe of the measured window (issue #134).
/// <para>
/// The rule holds: the similarity of a query allocates nothing (ADR-0030, issue #30). The
/// probe watches the window of the measurement and writes down the delta of every
/// iteration, so that a dirty window names the iteration and the amount. The run of
/// 43228720565 said only that 1912 bytes had been allocated inside the window, and the
/// hunt for them began there.
/// </para>
/// <para>
/// The probe is armed, not run once: it is skipped with a reason until
/// <c>LUI_ALLOCATION_PROBE=1</c> arms it — the lane of the probe sets it, and nowhere
/// else. The allocation happened once, in one run, out of many; the number of the runs is
/// part of the evidence.
/// </para>
/// </summary>
public sealed class DamerauAllocationTests
{
    private const string Left = "apfelkuch";
    private const string Right = "gedeckter apfelkuchen mit streuseln";

    [Fact]
    public void The_probe_names_the_iteration_that_allocated()
    {
        // the report of a dirty window has to name the iteration and the amount: a report
        // that says only "something allocated" sends the next one after the wrong thing.
        var probe = new AllocationProbe(windows: 2, iterations: 4);

        var allocated = probe.Measure(Allocating);

        allocated.ShouldBeGreaterThan(0L, "the measurement of the probe has to allocate, otherwise it proves nothing");

        var report = probe.Report();
        report.ShouldContain("window 0");
        report.ShouldContain("iteration 0:");
        report.ShouldContain("bytes");
    }

    [ProbeFact]
    public void The_measured_window_is_clean()
    {
        // the warm-up repeats and uses its result: the type initialisation, the first call and
        // the tiering of the measure belong to the start of the run, not to the measured
        // window. A call whose result is discarded may be removed by an optimising JIT, and
        // a one-off cost then lands inside the window (issue #132: 1912 bytes on a runner).
        var warm = 0d;
        for (var i = 0; i < AllocationProbe.IterationsPerWindow; i++)
        {
            warm += Similarity();
        }

        // a hundred windows of the shape of the window of the rule: the allocation happened
        // once, in one run, out of many — the probe watches a hundred windows at a time.
        var probe = new AllocationProbe(windows: 100, iterations: AllocationProbe.IterationsPerWindow);
        var allocated = probe.Measure(Similarity);

        // the report is written after the window — the writing allocates, and it must not be
        // part of the measurement.
        var path = ReportPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, probe.Report());

        warm.ShouldBeGreaterThan(0d, "the warm-up has to run, otherwise the window measures the start of the run");
        allocated.ShouldBe(0, $"the similarity of a query must not allocate — the report is in {path}");
    }

    /// <summary>The similarity of the library, measured the way the rule measures it.</summary>
    private static double Similarity() => LuiSimilarity.DamerauLevenshtein.Similarity(Left, Right);

    /// <summary>A measurement that allocates — the probe has to see it.</summary>
    private static double Allocating() => GC.AllocateUninitializedArray<byte>(64).Length;

    /// <summary>Where the report goes — the folder the lane of the CI uploads.</summary>
    private static string ReportPath() =>
        Path.Combine(Repository.RootPath, "artifacts", "test-results", "allocation-probe.txt");
}
