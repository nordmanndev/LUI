using Shouldly;

using Nordmann.LUI.Testing;

namespace Nordmann.LUI.Architecture.Tests;

/// <summary>
/// A test whose verdict depends on the clock carries the trait <c>Category=Timing</c> (issue #78). On a loaded runner such a
/// test can turn red with no fault — the most expensive kind of flake, because it costs the trust in the whole suite. The
/// trait says which tests those are: <c>dotnet test --filter "Category!=Timing"</c> is the fast local loop, and the CI runs
/// all of them.
/// <para>
/// A test reads the clock when its body measures (<c>Stopwatch</c>), waits for a claim until a deadline
/// (<c>WaitUntil</c>, <c>DateTime.UtcNow</c>) or races a task against a delay (<c>Task.WhenAny</c> with
/// <c>Task.Delay</c>). A wait on a signal with a timeout of seconds is a stop for a hang, not a reading of the clock. The
/// measurements (<c>[PerformanceFact]</c>) read nothing else and run only on purpose (<c>LUI_PERF=1</c>).
/// </para>
/// </summary>
public sealed class TimingTraitTests
{
    private const string TimingTrait = "[Trait(\"Category\", \"Timing\")]";

    private static readonly string[] TestAttributes = ["[Fact", "[Theory", "[FullWordListFact"];

    [Fact]
    public void Every_test_that_reads_the_clock_carries_the_timing_trait()
    {
        var tests = TestMethods().ToArray();
        tests.ShouldNotBeEmpty("the reader of the test methods is broken");

        var offenders = tests
            .Where(test => ReadsTheClock(test.Body) && !test.HasTrait)
            .Select(test => $"{test.File}: {test.Name}")
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        offenders.ShouldBeEmpty($"A test whose verdict depends on the clock carries {TimingTrait} (docs/testing.md).");
    }

    /// <summary>Whether the body of a test reads the clock: it measures, waits until a deadline, or races a delay.</summary>
    private static bool ReadsTheClock(string body) =>
        body.Contains("Stopwatch", StringComparison.Ordinal)
        || body.Contains("WaitUntil(", StringComparison.Ordinal)
        || body.Contains("DateTime.UtcNow", StringComparison.Ordinal)
        || (body.Contains("Task.WhenAny(", StringComparison.Ordinal) && body.Contains("Task.Delay(", StringComparison.Ordinal));

    /// <summary>
    /// The test methods of every test project: the attributes four spaces deep, the declaration, and the body up to the
    /// closing brace four spaces deep — the way the tests of this repository are written. A trait on the class counts for
    /// every test of the class.
    /// </summary>
    private static IEnumerable<(string File, string Name, string Body, bool HasTrait)> TestMethods()
    {
        foreach (var project in Repository.ProjectFiles().Where(Repository.IsTestProject))
        {
            foreach (var file in Repository.SourceFiles(Path.GetDirectoryName(project)!))
            {
                var lines = File.ReadAllLines(file);
                var relative = Path.GetRelativePath(Repository.RootPath, file).Replace('\\', '/');
                var classTrait = ClassCarriesTheTrait(lines);

                for (var i = 0; i < lines.Length; i++)
                {
                    if (!TestAttributes.Any(attribute => lines[i].StartsWith("    " + attribute, StringComparison.Ordinal)))
                    {
                        continue;
                    }

                    // the attributes and comments above the declaration, then the declaration
                    var start = i;
                    while (start > 0 && lines[start - 1].StartsWith("    [", StringComparison.Ordinal))
                    {
                        start--;
                    }

                    var declaration = i;
                    while (declaration < lines.Length && !lines[declaration].StartsWith("    public ", StringComparison.Ordinal))
                    {
                        declaration++;
                    }

                    var end = declaration;
                    while (end < lines.Length && !string.Equals(lines[end], "    }", StringComparison.Ordinal))
                    {
                        end++;
                    }

                    var attributes = lines[start..declaration];
                    var name = lines[Math.Min(declaration, lines.Length - 1)].Trim();
                    var body = string.Join('\n', lines[declaration..Math.Min(end + 1, lines.Length)]);
                    var hasTrait = classTrait || attributes.Any(line => string.Equals(line.Trim(), TimingTrait, StringComparison.Ordinal));

                    yield return (relative, name, body, hasTrait);
                    i = end;
                }
            }
        }
    }

    /// <summary>Whether the trait stands among the attributes of the class.</summary>
    private static bool ClassCarriesTheTrait(string[] lines)
    {
        var declaration = Array.FindIndex(lines, line => line.StartsWith("public sealed class ", StringComparison.Ordinal)
                                                          || line.StartsWith("public sealed partial class ", StringComparison.Ordinal));
        for (var i = declaration - 1; i >= 0 && lines[i].StartsWith('['); i--)
        {
            if (string.Equals(lines[i].Trim(), TimingTrait, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
