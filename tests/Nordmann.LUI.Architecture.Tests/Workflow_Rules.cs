using System.Text.RegularExpressions;
using Shouldly;

using Nordmann.LUI.Testing;

namespace Nordmann.LUI.Architecture.Tests;

/// <summary>
/// The pipelines are part of the contract (issue #74, ADR-0047): a build changes only through a change of the
/// repository — not because an action moved its tag, an image moved to "latest", or a test hung for six hours. The rules
/// are read out of the workflow files and the setup action themselves.
/// </summary>
public sealed partial class Workflow_Rules
{
    [Fact]
    public void Every_action_is_pinned_by_its_commit_and_names_its_version()
    {
        // a tag can be moved by whoever owns the action; a commit cannot. The version beside it is what a reader and
        // Dependabot read.
        var offenders = PipelineFiles()
            .SelectMany(file => File.ReadAllLines(file).Select(line => (File: file, Line: line)))
            .Select(entry => (entry.File, Match: UsesLine().Match(entry.Line)))
            .Where(entry => entry.Match.Success)
            .Where(entry => !entry.Match.Groups["action"].Value.StartsWith("./", StringComparison.Ordinal))
            .Where(entry => !PinnedAction().IsMatch(entry.Match.Groups["action"].Value + entry.Match.Groups["rest"].Value))
            .Select(entry => $"{Relative(entry.File)}: {entry.Match.Groups["action"].Value}")
            .ToArray();

        offenders.ShouldBeEmpty("An action is pinned by its commit, with the version as a comment: uses: owner/action@<40 hex> # vX.Y.Z.");
    }

    [Fact]
    public void Every_job_runs_on_a_named_image()
    {
        var offenders = Workflows()
            .SelectMany(file => File.ReadAllLines(file).Select(line => (File: file, Line: line.Trim())))
            .Where(entry => entry.Line.StartsWith("runs-on:", StringComparison.Ordinal))
            .Where(entry => entry.Line.Contains("-latest", StringComparison.Ordinal))
            .Select(entry => $"{Relative(entry.File)}: {entry.Line}")
            .ToArray();

        offenders.ShouldBeEmpty("A runner image is named (ubuntu-24.04, windows-2025): a new image is a change of the repository, not of the weather.");
    }

    [Fact]
    public void Every_job_has_a_timeout()
    {
        // GitHub's default is six hours: a test that hangs burns them, and nobody reads the result
        var offenders = Workflows()
            .SelectMany(file => Jobs(file).Select(job => (File: file, Job: job)))
            .Where(entry => !entry.Job.Body.Contains("\n    timeout-minutes:", StringComparison.Ordinal))
            .Select(entry => $"{Relative(entry.File)}: the job {entry.Job.Name}")
            .ToArray();

        offenders.ShouldBeEmpty("Every job names its timeout-minutes.");
    }

    [Fact]
    public void Every_test_run_of_a_pipeline_stops_a_hang_and_keeps_its_dump()
    {
        var offenders = Workflows()
            .SelectMany(file => Steps(file).Select(step => (File: file, Step: step)))
            .Where(entry => entry.Step.Contains("dotnet test", StringComparison.Ordinal))
            .Where(entry => !entry.Step.Contains("--blame-hang-timeout", StringComparison.Ordinal))
            .Select(entry => $"{Relative(entry.File)}: {FirstLine(entry.Step)}")
            .ToArray();

        offenders.ShouldBeEmpty("Every dotnet test of a pipeline runs with --blame-hang-timeout: a hang is stopped, and its dump says where it hung.");
    }

    [Fact]
    public void The_sdk_is_the_one_of_global_json()
    {
        // the SDK of the pipelines and of the developer is one file; a pipeline that names a version of its own drifts
        File.Exists(Path.Combine(Repository.RootPath, "global.json")).ShouldBeTrue("global.json names the SDK (ADR-0047).");

        var offenders = PipelineFiles()
            .SelectMany(file => File.ReadAllLines(file).Select(line => (File: file, Line: line.Trim())))
            .Where(entry => entry.Line.StartsWith("dotnet-version:", StringComparison.Ordinal))
            .Select(entry => $"{Relative(entry.File)}: {entry.Line}")
            .ToArray();

        offenders.ShouldBeEmpty("No pipeline names a dotnet-version of its own — the setup action reads global.json.");
    }

    /// <summary>The workflows and the actions of the repository.</summary>
    private static IEnumerable<string> PipelineFiles() =>
        Workflows().Concat(Directory.EnumerateFiles(Path.Combine(Repository.RootPath, ".github", "actions"), "action.yml", SearchOption.AllDirectories))
            .OrderBy(path => path, StringComparer.Ordinal);

    private static IEnumerable<string> Workflows() =>
        Directory.EnumerateFiles(Path.Combine(Repository.RootPath, ".github", "workflows"), "*.yml")
            .OrderBy(path => path, StringComparer.Ordinal);

    /// <summary>
    /// The jobs of a workflow: every key two spaces deep below <c>jobs:</c>, with the lines up to the next one. The
    /// workflows of this repository are written that way — a guard that parses YAML would need a package for it.
    /// </summary>
    private static IEnumerable<(string Name, string Body)> Jobs(string workflow)
    {
        var lines = Repository.NormalizedText(workflow).Split('\n');
        var start = Array.IndexOf(lines, "jobs:");
        if (start < 0)
        {
            yield break;
        }

        string? name = null;
        var body = new List<string>();
        foreach (var line in lines.Skip(start + 1))
        {
            var key = JobKey().Match(line);
            if (key.Success)
            {
                if (name is not null)
                {
                    yield return (name, "\n" + string.Join('\n', body));
                }

                name = key.Groups["name"].Value;
                body.Clear();
                continue;
            }

            if (line.Length > 0 && !char.IsWhiteSpace(line[0]) && !line.StartsWith('#'))
            {
                break;
            }

            body.Add(line);
        }

        if (name is not null)
        {
            yield return (name, "\n" + string.Join('\n', body));
        }
    }

    /// <summary>The steps of a workflow: the items of a list, one string each.</summary>
    private static IEnumerable<string> Steps(string workflow) =>
        StepStart().Split(Repository.NormalizedText(workflow)).Skip(1);

    private static string Relative(string path) => Path.GetRelativePath(Repository.RootPath, path).Replace('\\', '/');

    private static string FirstLine(string text) => text.Split('\n')[0].Trim();

    [GeneratedRegex(@"^\s*(-\s+)?uses:\s*(?<action>\S+)(?<rest>.*)$", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 2000)]
    private static partial Regex UsesLine();

    [GeneratedRegex(@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_./-]+@[0-9a-f]{40}\s+#\s+v\d+(\.\d+)*\s*$", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 2000)]
    private static partial Regex PinnedAction();

    [GeneratedRegex(@"^  (?<name>[A-Za-z0-9_-]+):\s*$", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 2000)]
    private static partial Regex JobKey();

    [GeneratedRegex(@"\n\s*-\s+(?=name:|uses:|run:|if:)", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 2000)]
    private static partial Regex StepStart();
}
