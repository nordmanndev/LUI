using System.Text.RegularExpressions;
using Shouldly;

using Nordmann.LUI.Testing;

namespace Nordmann.LUI.Architecture.Tests;

/// <summary>
/// A test whose verdict depends on the clock carries the trait <c>Category=Timing</c> (issue #78). On a loaded runner such a
/// test can turn red with no fault — the most expensive kind of flake, because it costs the trust in the whole suite. The
/// trait says which tests those are: <c>dotnet test --filter "Category!=Timing"</c> is the fast local loop, and the CI runs
/// all of them.
/// <para>
/// A test depends on the clock when it waits for a time or reads one — in its body or in a helper of its file that it calls
/// (review of pull request 95, M1): <c>Stopwatch</c>, <c>DateTime.UtcNow</c>, <c>Environment.TickCount</c>,
/// <c>Task.Delay</c>, <c>Thread.Sleep</c>, a <c>CancellationTokenSource</c> that cancels after a time, <c>WaitUntil</c>. A
/// wait on a signal with a timeout of seconds (<c>Entered.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue(…)</c>) is a stop for
/// a hang, not a reading of the clock. The measurements (<c>[PerformanceFact]</c>) run only on purpose.
/// </para>
/// </summary>
public sealed partial class TimingTraitTests
{
    private static readonly string[] TestAttributes = ["[Fact", "[Theory", "[FullWordListFact"];

    private static readonly string[] Clock =
    [
        "Stopwatch", "DateTime.UtcNow", "DateTime.Now", "Environment.TickCount", "Task.Delay(", "Thread.Sleep(",
        ".CancelAfter(", "CancellationTokenSource(TimeSpan", "WaitUntil(",
    ];

    [Fact]
    public void Every_test_that_reads_the_clock_carries_the_timing_trait()
    {
        var tests = TestFiles().SelectMany(Tests).ToArray();
        tests.ShouldNotBeEmpty("the reader of the test methods is broken");

        var offenders = tests
            .Where(test => test.ReadsTheClock && !test.HasTrait)
            .Select(test => $"{test.File}: {test.Name}")
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        offenders.ShouldBeEmpty("A test whose verdict depends on the clock carries [Trait(\"Category\", \"Timing\")] (docs/testing.md).");
    }

    /// <summary>The source files of every test project.</summary>
    private static IEnumerable<string> TestFiles() =>
        Repository.ProjectFiles()
            .Where(Repository.IsTestProject)
            .SelectMany(project => Repository.SourceFiles(Path.GetDirectoryName(project)!));

    /// <summary>
    /// The tests of a file, and whether each reads the clock — directly, or through a helper of the file it calls, at any
    /// depth — and carries the trait, on itself or on its class.
    /// </summary>
    private static IEnumerable<(string File, string Name, bool ReadsTheClock, bool HasTrait)> Tests(string file)
    {
        var lines = File.ReadAllLines(file);
        var members = Members(lines).ToArray();
        var relative = Path.GetRelativePath(Repository.RootPath, file).Replace('\\', '/');
        var classTrait = ClassCarriesTheTrait(lines);

        // the helpers that read the clock, to a fixed point: a helper that calls one is one
        var clockHelpers = new HashSet<string>(StringComparer.Ordinal);
        bool grew;
        do
        {
            grew = false;
            foreach (var member in members.Where(member => !member.IsTest && !clockHelpers.Contains(member.Name)))
            {
                if (ReadsTheClock(member.Body, clockHelpers))
                {
                    clockHelpers.Add(member.Name);
                    grew = true;
                }
            }
        }
        while (grew);

        return members
            .Where(member => member.IsTest)
            .Select(member => (relative, member.Name, ReadsTheClock(member.Body, clockHelpers),
                               classTrait || member.Attributes.Any(line => TimingTrait().IsMatch(line))))
            .ToArray();
    }

    private static bool ReadsTheClock(string body, HashSet<string> clockHelpers) =>
        Clock.Any(token => body.Contains(token, StringComparison.Ordinal))
        || clockHelpers.Any(helper => Regex.IsMatch(body, $@"\b{Regex.Escape(helper)}\s*\(", RegexOptions.None, TimeSpan.FromSeconds(1)));

    /// <summary>
    /// The members of a class, four spaces deep — the way the tests of this repository are written: the attributes above
    /// the declaration, then a body in braces up to the closing brace four spaces deep, or an expression up to its
    /// semicolon. A member with a test attribute is a test.
    /// </summary>
    private static IEnumerable<(string Name, bool IsTest, string[] Attributes, string Body)> Members(string[] lines)
    {
        var attributes = new List<string>();
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.StartsWith("    [", StringComparison.Ordinal))
            {
                attributes.Add(line);
                continue;
            }

            var declaration = MemberDeclaration().Match(line);
            if (!declaration.Success)
            {
                if (!line.StartsWith("    //", StringComparison.Ordinal) && !line.StartsWith("    ///", StringComparison.Ordinal))
                {
                    attributes.Clear();
                }

                continue;
            }

            var end = i;
            var expression = line.Contains("=>", StringComparison.Ordinal)
                             && (i + 1 >= lines.Length || !string.Equals(lines[i + 1].Trim(), "{", StringComparison.Ordinal));
            while (end < lines.Length
                   && !(expression ? lines[end].TrimEnd().EndsWith(';') : string.Equals(lines[end], "    }", StringComparison.Ordinal)))
            {
                end++;
            }

            var body = string.Join('\n', lines[i..Math.Min(end + 1, lines.Length)]);
            var isTest = attributes.Any(attribute => TestAttributes.Any(test => attribute.StartsWith("    " + test, StringComparison.Ordinal)));
            yield return (declaration.Groups["name"].Value, isTest, attributes.ToArray(), body);

            attributes.Clear();
            i = end;
        }
    }

    /// <summary>Whether the trait stands among the attributes of the class.</summary>
    private static bool ClassCarriesTheTrait(string[] lines)
    {
        var declaration = Array.FindIndex(lines, line => line.StartsWith("public sealed class ", StringComparison.Ordinal)
                                                          || line.StartsWith("public sealed partial class ", StringComparison.Ordinal));
        for (var i = declaration - 1; i >= 0 && lines[i].StartsWith('['); i--)
        {
            if (TimingTrait().IsMatch(lines[i]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The trait by its name and value, in any spelling xUnit takes — the filter reads the values, not the text (review of
    /// pull request 95, M2): <c>[Trait("Category", "Timing")]</c>, <c>[Trait("Category","Timing")]</c>,
    /// <c>[Fact, Trait("Category", "Timing")]</c>.
    /// </summary>
    [GeneratedRegex(@"\bTrait\s*\(\s*""Category""\s*,\s*""Timing""\s*\)", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 2000)]
    private static partial Regex TimingTrait();

    /// <summary>A method four spaces deep: its modifiers, a return type, its name and the opening parenthesis.</summary>
    [GeneratedRegex(@"^    (public|private|internal|protected)\s[^=(]*?\b(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(<[^>]*>)?\s*\(", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 2000)]
    private static partial Regex MemberDeclaration();
}
