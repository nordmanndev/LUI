using System.ComponentModel;
using System.Diagnostics;
using Shouldly;

using Nordmann.LUI.Testing;

namespace Nordmann.LUI.Architecture.Tests;

/// <summary>
/// The documentation promises: every relative link of every markdown file of the repository points at something that
/// exists — a file, and the anchor inside it. A link that points at nothing is a broken promise, and the reader of the
/// documentation finds it before the reader of the repository does.
/// <para>
/// The check is the tool of the repository (<c>tools/link-check.py</c>), and it runs as a test of the architecture
/// layer: it runs in every lane of the CI, on every pull request and in the release gate — and the exit code of the
/// tool decides. A tool that runs only by hand goes stale silently (review of 2026-11-06, D).
/// </para>
/// </summary>
public sealed class Link_Check
{
    /// <summary>The tool of the repository: it reads every markdown file and asks every relative link whether its target exists.</summary>
    private const string Tool = "link-check.py";

    [Fact]
    public async Task Every_relative_link_of_the_documentation_points_at_something_that_exists()
    {
        var root = Repository.RootPath;
        var script = Path.Combine(root, "tools", Tool);

        File.Exists(script).ShouldBeTrue($"The link check of the documentation is {script} — without the tool, nothing checks the links.");

        var (exitCode, output) = await Run(root, script);

        exitCode.ShouldBe(0,
            "Every relative link of every markdown file points at a file that exists — a link that points at nothing is a broken promise (tools/link-check.py).\n"
            + output);
    }

    /// <summary>Runs the tool in the root of the repository and returns its exit code and its output.</summary>
    private static async Task<(int ExitCode, string Output)> Run(string root, string script)
    {
        Exception? last = null;

        foreach (var (interpreter, prefix) in Interpreters())
        {
            var startInfo = new ProcessStartInfo(interpreter)
            {
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            foreach (var argument in prefix)
            {
                startInfo.ArgumentList.Add(argument);
            }

            startInfo.ArgumentList.Add(script);

            Process? started;
            try
            {
                started = Process.Start(startInfo);
            }
            catch (Win32Exception exception)
            {
                // the interpreter is not installed here — the next candidate may be
                last = exception;
                continue;
            }

            if (started is null)
            {
                last = new InvalidOperationException($"The process {interpreter} did not start.");
                continue;
            }

            using var process = started;

            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();

            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(2)).ConfigureAwait(false);
            }
            catch (TimeoutException exception)
            {
                process.Kill(entireProcessTree: true);
                throw new InvalidOperationException("The link check did not finish in two minutes — a link check that hangs is a link check that lies.", exception);
            }

            var output = (await standardOutput.ConfigureAwait(false)) + (await standardError.ConfigureAwait(false));
            return (process.ExitCode, output);
        }

        throw new InvalidOperationException(
            "The link check needs Python 3 — the tools of the repository are Python (tools/link-check.py).",
            last);
    }

    /// <summary>
    /// The interpreters of Python 3, in the order they are tried. The tools of the repository are Python, so Python is a
    /// dependency of the repository and not of a single test.
    /// </summary>
    private static (string Interpreter, string[] Prefix)[] Interpreters()
    {
        if (OperatingSystem.IsWindows())
        {
            return [("python", Array.Empty<string>()), ("py", new[] { "-3" })];
        }

        return [("python3", Array.Empty<string>())];
    }
}
