using System.Text.RegularExpressions;
using Shouldly;

using Nordmann.LUI.Testing;

namespace Nordmann.LUI.Architecture.Tests;

/// <summary>
/// The pipelines are part of the contract (issue #74, ADR-0047): a build changes only through a change of the
/// repository — not because an action moved its tag, an image moved to "latest", or a test hung for six hours. The rules
/// are read out of the workflow files and the setup action themselves.
/// </summary>
public sealed partial class WorkflowRulesTests
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
        // GitHub's default is six hours: a test that hangs burns them, and nobody reads the result. A job that calls a
        // workflow has no runner and no timeout of its own — the jobs of the called workflow have theirs.
        var offenders = Workflows()
            .SelectMany(file => Jobs(file).Select(job => (File: file, Job: job)))
            .Where(entry => !CallsAWorkflow(entry.Job.Body))
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
            .Where(entry => !HangFlags.All(flag => entry.Step.Contains(flag, StringComparison.Ordinal)))
            .Select(entry => $"{Relative(entry.File)}: {FirstLine(entry.Step)}")
            .ToArray();

        // the type of the dump is named: without it one machine writes the dump and another only announces it (review
        // of pull request 83 — measured both ways)
        offenders.ShouldBeEmpty(
            "Every dotnet test of a pipeline runs with " + string.Join(", ", HangFlags)
            + ": a hang is stopped, and its dump lands where the upload looks.");
    }

    [Fact]
    public void Every_job_that_tests_uploads_the_evidence()
    {
        // a dump that is written and not uploaded is gone with the runner
        var offenders = Workflows()
            .SelectMany(file => Jobs(file).Select(job => (File: file, Job: job)))
            .Where(entry => entry.Job.Body.Contains("dotnet test", StringComparison.Ordinal))
            .Where(entry => !entry.Job.Body.Contains("actions/upload-artifact@", StringComparison.Ordinal)
                || !entry.Job.Body.Contains("path: " + ResultsDirectory, StringComparison.Ordinal))
            .Select(entry => $"{Relative(entry.File)}: the job {entry.Job.Name}")
            .ToArray();

        offenders.ShouldBeEmpty($"Every job that runs dotnet test uploads {ResultsDirectory} — the test results and the dump of a hang.");
    }

    [Fact]
    public void The_release_runs_the_gates_of_every_pull_request_and_stores_no_key()
    {
        // ADR-0048: the release has no gate of its own — it calls ci.yml, and the push waits for it and for the owner
        var release = Jobs(Path.Combine(Repository.RootPath, ".github", "workflows", "publish.yml")).ToArray();

        var gate = release.Where(job => job.Body.Contains("\n    uses: ./.github/workflows/ci.yml", StringComparison.Ordinal))
            .Select(job => job.Name)
            .ToArray();
        gate.ShouldHaveSingleItem("The release calls the workflow of every pull request (ci.yml) as one of its jobs.");

        var push = release.Where(job => job.Body.Contains("dotnet nuget push", StringComparison.Ordinal)).ToArray();
        push.ShouldHaveSingleItem("One job of the release pushes to nuget.org.");
        var body = push[0].Body;

        var needs = NeedsList().Match(body).Groups["needs"].Value.Split(',').Select(need => need.Trim()).ToArray();
        needs.Contains(gate[0], StringComparer.Ordinal)
            .ShouldBeTrue($"The push needs the gates ({gate[0]}) — it does not run before they are green; it needs {string.Join(", ", needs)}.");
        body.ShouldContain("\n    environment: nuget", Case.Sensitive, "The push runs in the environment the owner approves.");
        body.ShouldContain("id-token: write", Case.Sensitive, "The push asks GitHub for the OIDC token of Trusted Publishing.");
        body.ShouldContain("uses: NuGet/login@", Case.Sensitive, "The key of the push is the one nuget.org issues for one hour.");
        body.ShouldNotContain("secrets.NUGET_API_KEY", Case.Sensitive, "No key of nuget.org is stored in the repository.");
    }

    [Fact]
    public void The_sdk_is_the_one_of_global_json()
    {
        // the SDK of the pipelines and of the developer is one file; a pipeline that names a version of its own drifts
        var globalJson = Path.Combine(Repository.RootPath, "global.json");
        File.Exists(globalJson).ShouldBeTrue("global.json names the SDK (ADR-0047).");

        // a roll-forward across feature bands is no pin: the image carries a later band, and dotnet takes it — measured
        // in the first run of pull request #83 (10.0.403 instead of 10.0.114)
        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(globalJson));
        var sdk = json.RootElement.GetProperty("sdk");
        var rollForward = sdk.GetProperty("rollForward").GetString();
        string[] keepTheBand = ["latestPatch", "patch", "disable"];
        keepTheBand.Contains(rollForward, StringComparer.Ordinal)
            .ShouldBeTrue($"global.json keeps its feature band (ADR-0047) — rollForward is {rollForward}.");

        // the version is the other half that setup-dotnet reads: "10.0.x" becomes the channel 10.0, and the pin is gone
        // (review of pull request 83)
        var version = sdk.GetProperty("version").GetString() ?? string.Empty;
        ExactVersion().IsMatch(version).ShouldBeTrue($"global.json names an exact SDK (major.minor.patch) — the version is {version}.");

        // the setup action reads that file, and no other
        var setup = Repository.NormalizedText(Path.Combine(Repository.RootPath, ".github", "actions", "setup", "action.yml"));
        setup.ShouldContain("global-json-file: global.json", Case.Sensitive, "The setup action takes the SDK from global.json.");

        var offenders = PipelineFiles()
            .SelectMany(file => File.ReadAllLines(file).Select(line => (File: file, Line: line.Trim())))
            .Where(entry => entry.Line.StartsWith("dotnet-version:", StringComparison.Ordinal))
            .Select(entry => $"{Relative(entry.File)}: {entry.Line}")
            .ToArray();

        offenders.ShouldBeEmpty("No pipeline names a dotnet-version of its own — the setup action reads global.json.");
    }

    [Fact]
    public void Every_download_of_a_pipeline_retries_a_transient_error()
    {
        // issue #91: the download of actionlint met a 504 three times in a row and the job died before actionlint ran. A
        // download that gives up on the first gateway error turns a server's bad minute into a red check. The rule reads
        // commands, not lines: a command goes on over a trailing backslash, and curl may stand in $(…), after a pipe or in
        // quotes (review of pull request 92, I1).
        var offenders = CurlCommands()
            .Where(entry => !RetryFlag().IsMatch(entry.Command))
            .Select(entry => $"{entry.File}: {entry.Command}")
            .ToArray();

        offenders.ShouldBeEmpty("Every curl of a pipeline retries (--retry): a 5xx of a server is weather, not a finding.");
    }

    [Fact]
    public void Every_download_of_a_pipeline_says_who_answered()
    {
        // issue #91, the second half: after -L the failing address can be the first host or the one it redirects to. The
        // log has to name the status and the address that answered (review of pull request 92, M2).
        var offenders = CurlCommands()
            .Where(entry => !entry.Command.Contains("%{http_code}", StringComparison.Ordinal)
                            || !entry.Command.Contains("%{url_effective}", StringComparison.Ordinal))
            .Select(entry => $"{entry.File}: {entry.Command}")
            .ToArray();

        offenders.ShouldBeEmpty("Every curl of a pipeline writes the status and the address that answered (-w '%{http_code} %{url_effective}').");
    }

    /// <summary>
    /// The curl commands of the pipelines: the lines of each file joined over a trailing backslash, comments left out,
    /// and every command that names curl as a word — at the start, in <c>$(…)</c>, after a pipe, in quotes.
    /// </summary>
    private static IEnumerable<(string File, string Command)> CurlCommands()
    {
        foreach (var file in PipelineFiles())
        {
            var command = new System.Text.StringBuilder();
            foreach (var raw in File.ReadAllLines(file))
            {
                var line = raw.Trim();
                if (command.Length == 0 && line.StartsWith('#'))
                {
                    continue;
                }

                var continues = line.EndsWith('\\');
                command.Append(continues ? line[..^1] : line).Append(' ');
                if (continues)
                {
                    continue;
                }

                var text = command.ToString().Trim();
                command.Clear();
                if (CurlWord().IsMatch(text))
                {
                    yield return (Relative(file), text);
                }
            }
        }
    }

    /// <summary>Whether a job calls a reusable workflow instead of running steps.</summary>
    private static bool CallsAWorkflow(string jobBody) =>
        jobBody.Contains("\n    uses: ./.github/workflows/", StringComparison.Ordinal);

    /// <summary>The workflows and the actions of the repository.</summary>
    /// <remarks>
    /// The actions in both spellings GitHub reads, <c>action.yml</c> and <c>action.yaml</c>: an action under the other
    /// spelling was invisible to every rule of this file, the pins among them (issue #93).
    /// </remarks>
    private static IEnumerable<string> PipelineFiles() =>
        Workflows()
            .Concat(ActionPatterns.SelectMany(pattern =>
                Directory.EnumerateFiles(Path.Combine(Repository.RootPath, ".github", "actions"), pattern, SearchOption.AllDirectories)))
            .OrderBy(path => path, StringComparer.Ordinal);

    /// <summary>The two file names of an action.</summary>
    private static readonly string[] ActionPatterns = ["action.yml", "action.yaml"];

    /// <summary>
    /// The workflows — both spellings GitHub reads: a workflow renamed to <c>.yaml</c> must not leave the sight of the
    /// guard (review of pull request 83).
    /// </summary>
    private static IEnumerable<string> Workflows() =>
        WorkflowPatterns
            .SelectMany(pattern => Directory.EnumerateFiles(Path.Combine(Repository.RootPath, ".github", "workflows"), pattern))
            .OrderBy(path => path, StringComparer.Ordinal);

    /// <summary>The two file patterns of a workflow.</summary>
    private static readonly string[] WorkflowPatterns = ["*.yml", "*.yaml"];

    /// <summary>The two flags of a test run in a pipeline: the hang is stopped, and its dump is written in full.</summary>
    private static readonly string[] HangFlags = ["--blame-hang-timeout", "--blame-hang-dump-type full"];

    /// <summary>Where the test runs of the pipelines put their results — and the dumps.</summary>
    private const string ResultsDirectory = "artifacts/test-results";

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

    /// <summary>
    /// The steps of a workflow: the items of a list, one string each. Every item that starts with a key is a step — not
    /// only <c>name</c>, <c>uses</c>, <c>run</c> and <c>if</c>: a step that starts with <c>env</c> or
    /// <c>working-directory</c> must not hide in the step before it (review of pull request 83).
    /// </summary>
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

    [GeneratedRegex(@"\n\s*-\s+(?=[A-Za-z][A-Za-z0-9_-]*:)", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 2000)]
    private static partial Regex StepStart();

    [GeneratedRegex(@"\n    needs:\s*\[?(?<needs>[^\]\n]*)\]?", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 2000)]
    private static partial Regex NeedsList();

    [GeneratedRegex(@"(?<![A-Za-z0-9_.-])curl(?=\s)", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 2000)]
    private static partial Regex CurlWord();

    [GeneratedRegex(@"\s--retry(=|\s+)\d", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 2000)]
    private static partial Regex RetryFlag();

    [GeneratedRegex(@"^\d+\.\d+\.\d+$", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 2000)]
    private static partial Regex ExactVersion();
}
