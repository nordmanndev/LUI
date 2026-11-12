using Shouldly;

namespace Nordmann.LUI.Performance.Tests;

/// <summary>
/// The reader of the scenario file: a member that nobody reads would be a second truth beside the scenario (ADR-0058),
/// so an unknown member is an <b>error</b> and not a shrug. The file of the test is a file of the test — the file of the
/// repository belongs to the run, and a test that wrote it would race with the lane.
/// </summary>
public sealed class PerformanceScenarioTests
{
    [Fact]
    public void A_scenario_with_an_unknown_member_is_refused()
    {
        var path = Write("""
            {
              "scenario": {
                "prefixLength": 1000,
                "batch": 100,
                "minimumWeight": 0,
                "optimize": false,
                "machine": "measurement",
                "database": "",
                "corpus": "words.txt"
              }
            }
            """);

        try
        {
            var error = Should.Throw<InvalidOperationException>(() => PerformanceScenario.Read(path));

            // the member that is not known is named — a shrug would hide the second truth
            error.Message.ShouldContain("corpus");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_scenario_with_the_members_of_the_run_is_read()
    {
        var path = Write("""
            {
              "scenario": {
                "prefixLength": 1000,
                "batch": 100,
                "minimumWeight": 0,
                "optimize": false,
                "machine": "measurement",
                "database": ""
              }
            }
            """);

        try
        {
            var scenario = PerformanceScenario.Read(path);

            scenario.PrefixLength.ShouldBe(1000);
            scenario.Batch.ShouldBe(100);
            scenario.MinimumWeight.ShouldBe(0);
            scenario.Optimize.ShouldBeFalse();
            scenario.Machine.ShouldBe(BudgetMachine.Measurement);
            scenario.Database.ShouldBe(string.Empty);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Writes a scenario file of the test and gives back its path.</summary>
    /// <param name="content">The content of the file.</param>
    /// <returns>The path of the file.</returns>
    private static string Write(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lui-scenario-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, content);
        return path;
    }
}
