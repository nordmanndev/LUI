using System.Diagnostics;
using Shouldly;

using Nordmann.LUI.Testing;

namespace Nordmann.LUI.Architecture.Tests;

/// <summary>
/// The lane of the schedule writes down what it measured (issue #177). The step summary of a run is gone after the run;
/// a lane that does not leave its numbers behind proves nothing — the last record of the repository was of 2026-11-12 and
/// nobody noticed. The lane writes <c>docs/benchmarks-latest.md</c> with <c>tools/perf-record.sh</c>: the <c>[perf]</c>
/// lines of the run with its date, its iteration and its disk.
/// <para>
/// The file is written by a job of its own that does nothing but publish that text: the job that runs the code of the
/// repository has the token to read, the job that has the token to write runs no code of the repository. The file goes to
/// a branch of its own — never to <c>main</c>, <c>dev</c> or the branch of a release — for a person to look at.
/// </para>
/// </summary>
public sealed class PerformanceLaneTests
{
    private static string WorkflowPath => Path.Combine(Repository.RootPath, ".github", "workflows", "performance.yml");

    private static string ScriptPath => Path.Combine(Repository.RootPath, "tools", "perf-record.sh");

    [Fact]
    public void The_record_of_a_run_names_its_date_its_iteration_and_its_disk_and_holds_the_perf_lines()
    {
        if (!OperatingSystem.IsLinux())
        {
            return; // the lane runs on Linux, and so does its script
        }

        var directory = Directory.CreateTempSubdirectory("lui-perf-record-").FullName;
        try
        {
            var log = Path.Combine(directory, "budgets.log");
            File.WriteAllText(log, "noise before\n[perf] query: p50 1.34 ms, p95 3.10 ms\n  [perf] build: 120.4 s\nnoise after\n");
            var output = Path.Combine(directory, "benchmarks-latest.md");

            var result = RunScript([log, output], new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["LUI_PERF_DATE"] = "2026-11-19",
                ["LUI_PERF_RUN"] = "run 123, attempt 1",
                ["LUI_PERF_DIR"] = directory,
                ["LUI_PERF_COMMIT"] = "0123456789abcdef0123456789abcdef01234567",
                ["LUI_PERF_REF"] = "refs/heads/main",
                ["LUI_PERF_MACHINE"] = "other",
                ["LUI_PERF_RESULT"] = "passed"
            });

            result.ExitCode.ShouldBe(0, result.Error);
            var text = File.ReadAllText(output);

            text.ShouldContain("2026-11-19", Case.Sensitive, "the record names the date of the run");
            text.ShouldContain("run 123, attempt 1", Case.Sensitive, "the record names the iteration of the run");
            text.ShouldContain("Disk", Case.Sensitive, "the record names the disk the run measured on");
            text.ShouldContain("local", Case.Sensitive, "the disk is a local one — a temporary directory is");
            text.ShouldContain("0123456789abcdef", Case.Sensitive, "the record names the commit it measured");
            text.ShouldContain("query: p50 1.34 ms, p95 3.10 ms", Case.Sensitive, "the [perf] lines are the record");
            text.ShouldContain("build: 120.4 s", Case.Sensitive);
            text.ShouldNotContain("noise", Case.Sensitive, "only the [perf] lines of the log are the record");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void A_run_without_perf_lines_writes_no_record_and_fails()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var directory = Directory.CreateTempSubdirectory("lui-perf-record-").FullName;
        try
        {
            var log = Path.Combine(directory, "budgets.log");
            File.WriteAllText(log, "the tests did not run\n");
            var output = Path.Combine(directory, "benchmarks-latest.md");

            var result = RunScript([log, output], new Dictionary<string, string>(StringComparer.Ordinal) { ["LUI_PERF_DIR"] = directory });

            result.ExitCode.ShouldNotBe(0, "a record without a single number is not a record");
            File.Exists(output).ShouldBeFalse("a failed run does not replace the last record by an empty one");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void The_lane_publishes_the_record_from_a_job_of_its_own_that_runs_no_code_of_the_repository()
    {
        var workflow = File.ReadAllText(WorkflowPath);

        workflow.ShouldContain("tools/perf-record.sh", Case.Sensitive, "the lane writes the record with the script");
        workflow.ShouldContain("docs/benchmarks-latest.md", Case.Sensitive, "the record has a name that says it is the latest");
        workflow.ShouldContain("record: ${{ steps.record.outputs.text }}", Case.Sensitive, "the record is the output of the job that measured");

        var jobs = JobsOf(workflow);
        jobs.ContainsKey("budgets").ShouldBeTrue("the lane has the job that measures");
        jobs.ContainsKey("record").ShouldBeTrue("the lane has the job that records");
        var budgets = jobs["budgets"];
        var record = jobs["record"];

        // the token: the workflow reads, the job that measures reads, only the job that publishes writes
        ContentsPermissions(workflow, indentation: "").ShouldBe(["read"], "the workflow can only read");
        budgets.ShouldNotContain(line => line.Contains("write", StringComparison.Ordinal) && line.Contains("contents", StringComparison.Ordinal), "the job that runs the code of the repository can only read");
        record.ShouldContain(line => line.Trim() == "contents: write", "the job that publishes the record can write — and only that");
        record.ShouldContain(line => line.Trim() == "needs: budgets");

        // the job with the token to write runs nothing of the repository: no build, no test, no action of its own
        record.ShouldNotContain(line => line.Contains("dotnet ", StringComparison.Ordinal) || line.Contains("tools/", StringComparison.Ordinal));
        record.Where(line => line.Contains("uses:", StringComparison.Ordinal)).ShouldAllBe(line => line.Contains("actions/checkout@", StringComparison.Ordinal), "the only action of the job is the checkout");

        // an expression is never written into the script: the text of the record arrives through the environment
        record.Where(line => line.Contains("${{", StringComparison.Ordinal))
            .ShouldAllBe(line => line.Trim().StartsWith("if:", StringComparison.Ordinal) || line.Trim().StartsWith("RECORD:", StringComparison.Ordinal) || line.Trim().StartsWith("RUN:", StringComparison.Ordinal),
                "an expression of the job is its condition or an environment variable — never a part of a script");

        // where it goes: exactly one push, to the branch of the record, nothing forced and nothing protected
        record.Where(line => line.Contains("git push", StringComparison.Ordinal)).ShouldBe(["          git push origin perf/latest"]);

        // one writer at a time
        record.ShouldContain(line => line.Trim() == "group: perf-latest");
    }

    /// <summary>The jobs of a workflow by their name, each as its lines without comments.</summary>
    private static Dictionary<string, string[]> JobsOf(string workflow)
    {
        var jobs = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var lines = workflow.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
        var start = Array.IndexOf(lines, "jobs:");
        string? name = null;
        var body = new List<string>();

        for (var i = start + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.StartsWith("  ", StringComparison.Ordinal) && !line.StartsWith("   ", StringComparison.Ordinal) && line.TrimEnd().EndsWith(':') && !line.TrimStart().StartsWith('#'))
            {
                if (name is not null)
                {
                    jobs[name] = [.. body];
                }

                name = line.Trim().TrimEnd(':');
                body.Clear();
            }
            else if (name is not null && !line.TrimStart().StartsWith('#'))
            {
                body.Add(line);
            }
        }

        if (name is not null)
        {
            jobs[name] = [.. body];
        }

        return jobs;
    }

    /// <summary>The values of "contents:" in a permissions block of the given indentation.</summary>
    private static string[] ContentsPermissions(string workflow, string indentation)
    {
        var lines = workflow.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
        var at = Array.IndexOf(lines, indentation + "permissions:");
        at.ShouldBeGreaterThanOrEqualTo(0, "the workflow names its permissions");
        return [.. lines.Skip(at + 1).TakeWhile(line => line.StartsWith(indentation + "  ", StringComparison.Ordinal))
            .Where(line => line.Trim().StartsWith("contents:", StringComparison.Ordinal))
            .Select(line => line.Split(':')[1].Trim())];
    }

    private static (int ExitCode, string Error) RunScript(string[] arguments, IReadOnlyDictionary<string, string> environment)
    {
        var start = new ProcessStartInfo("bash") { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
        start.ArgumentList.Add(ScriptPath);
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in environment)
        {
            start.Environment[name] = value;
        }

        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEnd();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, error);
    }
}
