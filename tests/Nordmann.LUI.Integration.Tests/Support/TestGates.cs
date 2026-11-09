namespace Nordmann.LUI.Integration.Tests.Support;

/// <summary>
/// The gates of the tests that do not run in an ordinary run (docs/testing.md, "The gates"). xUnit.net v3 reads them when
/// a test is about to run — a dynamic skip, not a value the attribute fixed when the tests were found (ADR-0051).
/// </summary>
public static class TestGates
{
    /// <summary>The measurements run on purpose: <c>LUI_PERF=1</c>.</summary>
    public static bool MeasurementsRun =>
        string.Equals(Environment.GetEnvironmentVariable("LUI_PERF"), "1", StringComparison.Ordinal);

    /// <summary>
    /// The parity over the full word list runs when the list is there — or when the lane demands it
    /// (<c>LUI_REQUIRE_FULL_WORDLIST=1</c>): then a missing list fails the test with the reason instead of skipping it.
    /// </summary>
    public static bool FullWordListRuns =>
        File.Exists(TestFiles.FullWordListPath)
        || string.Equals(Environment.GetEnvironmentVariable("LUI_REQUIRE_FULL_WORDLIST"), "1", StringComparison.Ordinal);
}
