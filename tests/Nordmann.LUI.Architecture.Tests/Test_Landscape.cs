using System.Text.RegularExpressions;
using Shouldly;

using Nordmann.LUI.Testing;

namespace Nordmann.LUI.Architecture.Tests;

/// <summary>
/// The test landscape (<c>docs/testing.md</c>) is a map of the test suite: which
/// test holds which rule of the specification up. A map that rots is worse
/// than no map — it sends the reader to a test that no longer proves the
/// rule.
/// <para>
/// So the map is a guard, not a document. This test reads the map and the tree
/// and fails when they disagree:
/// </para>
/// <list type="bullet">
/// <item>a test file that is not listed in the map</item>
/// <item>a row in the map that points at a file that does not exist</item>
/// <item>a row whose layer does not match the project the file lives in</item>
/// <item>a unit test that opens a database</item>
/// </list>
/// <para>
/// The unit of the map is the <b>file</b>, not the class: a file holds one test
/// class, and a file is what a reader opens. The classes are not parsed out of
/// the source — a rule that reads the prose of a file finds the prose.
/// </para>
/// </summary>
public sealed class Test_Landscape
{
    private const string Map = "docs/testing.md";

    /// <summary>
    /// The test projects of the tree — by their directory, relative to the root — and the layer each one holds. The tests
    /// of the sample sit beside the sample, not among the tests of the library (issue #76).
    /// </summary>
    private static readonly (string Project, string Layer)[] Layers =
    [
        ("tests/Nordmann.LUI.Unit.Tests", "Unit"),
        ("tests/Nordmann.LUI.Integration.Tests", "Integration"),
        ("tests/Nordmann.LUI.Architecture.Tests", "Architecture"),
        ("samples/Lui.Sample.Tests", "Sample")
    ];

    /// <summary>
    /// The attributes that make a file a test file — in any form: <c>[Fact]</c>, <c>[Fact(Skip = …)]</c>,
    /// <c>[Theory, InlineData(…)]</c>, <c>[InlineData(…), Theory]</c>. A plain string search for <c>[Fact]</c> let a file
    /// whose only test is skipped out of the map (review of PR #25, finding 4).
    /// </summary>
    private static readonly Regex TestAttribute = new(
        @"[\[,]\s*(Fact|Theory|PerformanceFact|FullWordListFact)\s*[\](,]",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(1));

    /// <summary>A string literal: a unit test may name a package (the identity of the package does), not use it.</summary>
    private static readonly Regex StringLiteral = new(
        @"""(?:[^""\\]|\\.)*""",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(1));

    /// <summary>What a unit test must not name: a database, or the infrastructure that opens one (ADR-0019).</summary>
    private static readonly Regex Database = new(
        @"\b(LuiCatalog|TestDatabase|Sqlite[A-Za-z]*|Microsoft\.Data\.Sqlite|Nordmann\.LUI\.Testing)\b",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(1));

    [Fact]
    public void Every_test_file_is_listed_in_the_map()
    {
        var listed = ListedRows();

        var missing = TestFiles()
            .Where(file => !listed.Any(row => string.Equals(row.File, file.File, StringComparison.Ordinal)
                                              && string.Equals(row.Project, file.Project, StringComparison.Ordinal)))
            .Select(file => $"{file.File} ({file.Project})")
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        missing.ShouldBeEmpty(
            $"Every test file belongs in the map ({Map}). A test that is not listed is a test nobody can find.");
    }

    [Fact]
    public void Every_file_the_map_lists_exists()
    {
        var listed = ListedRows();

        var known = TestFiles().Select(file => file.File).ToHashSet(StringComparer.Ordinal);

        var unknown = listed
            .Where(row => !known.Contains(row.File))
            .Select(row => $"{row.File} ({row.Project})")
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        unknown.ShouldBeEmpty($"Every file the map names ({Map}) exists in the tree.");
    }

    [Fact]
    public void Every_file_the_map_lists_sits_in_the_layer_the_map_names()
    {
        var listed = ListedRows();

        // a file name may live in two projects: the map row has to name one of them (review of PR #25, finding 4)
        var actual = TestFiles().ToLookup(file => file.File, file => file.Project, StringComparer.Ordinal);

        var wrong = listed
            .Where(row => actual.Contains(row.File)
                          && !actual[row.File].Contains(row.Project, StringComparer.Ordinal))
            .Select(row => $"{row.File}: the map says {row.Layer} ({row.Project}), the tree says {string.Join(", ", actual[row.File])}")
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToArray();

        wrong.ShouldBeEmpty($"Every file sits in the layer the map names ({Map}).");
    }

    [Fact]
    public void The_map_names_every_layer_of_the_pyramid()
    {
        var listed = ListedRows();

        var layers = Layers.Select(layer => layer.Layer).ToHashSet(StringComparer.Ordinal);

        var missing = layers
            .Where(layer => !listed.Any(row => string.Equals(row.Layer, layer, StringComparison.Ordinal)))
            .OrderBy(layer => layer, StringComparer.Ordinal)
            .ToArray();

        missing.ShouldBeEmpty($"Every layer of the pyramid has tests in the map ({Map}).");
    }

    [Fact]
    public void The_map_lists_no_test_file_twice()
    {
        var listed = ListedRows();

        var doubled = listed
            .GroupBy(row => $"{row.Project}/{row.File}", StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        doubled.ShouldBeEmpty($"Every test file is listed once in the map ({Map}).");
    }

    [Fact]
    public void The_unit_layer_opens_no_database()
    {
        // ADR-0019: the unit layer is the rules on their own. Microsoft.Data.Sqlite reaches it through the core anyway,
        // so the project file alone does not keep a unit test from opening a catalog: its source is read too.
        var directory = Path.Combine(Repository.RootPath, "tests", "Nordmann.LUI.Unit.Tests");
        var project = Repository.LoadXml(Path.Combine(directory, "Nordmann.LUI.Unit.Tests.csproj"));

        var references = Repository.Elements(project, "PackageReference")
            .Concat(Repository.Elements(project, "ProjectReference"))
            .Select(reference => reference.Attribute("Include")?.Value ?? string.Empty)
            .Where(include => Database.IsMatch(include) || include.Contains("Nordmann.LUI.Hosting", StringComparison.Ordinal))
            .ToArray();

        var lines = Repository.SourceFiles(directory)
            .SelectMany(file => File.ReadLines(file).Select((line, number) => (File: Path.GetFileName(file), Line: line, Number: number + 1)))
            .Where(entry => !entry.Line.TrimStart().StartsWith("//", StringComparison.Ordinal)
                            && Database.IsMatch(StringLiteral.Replace(entry.Line, "\"\"")))
            .Select(entry => $"{entry.File}:{entry.Number}: {entry.Line.Trim()}")
            .ToArray();

        references.ShouldBeEmpty("The unit layer references no database and no infrastructure that opens one.");
        lines.ShouldBeEmpty("A unit test opens no database: it belongs in Nordmann.LUI.Integration.Tests (docs/testing.md).");
    }

    /// <summary>A row of the map: the file it names, the project it lives in and the layer.</summary>
    private sealed record Row(string Class, string Project, string File, string Layer);

    /// <summary>
    /// The rows of the map: the lines that name a test file and the layer it
    /// sits in. The rows of the other tables of the map — the projects, the
    /// shared infrastructure, the corpora — carry a layer this guard does not
    /// know, and are not rows of the map of the tests.
    /// </summary>
    private static List<Row> ListedRows()
    {
        var path = Path.Combine(Repository.RootPath, Map);

        File.Exists(path).ShouldBeTrue($"the map ({Map}) is missing");

        var rows = new List<Row>();

        foreach (var line in File.ReadLines(path))
        {
            var cells = line.Split('|');
            if (cells.Length < 4)
            {
                continue;
            }

            var link = cells[1].Trim();
            var layer = cells[2].Trim();

            if (!Layers.Any(l => string.Equals(l.Layer, layer, StringComparison.Ordinal)))
            {
                continue;
            }

            if (!link.StartsWith("[`", StringComparison.Ordinal))
            {
                continue;
            }

            var open = link.IndexOf("](../", StringComparison.Ordinal);
            if (open < 0)
            {
                continue;
            }

            // the path of the link, relative to the root: <project directory>/<file>
            var relative = link[(open + "](../".Length)..link.IndexOf(')', open)];
            var project = Layers
                .Select(entry => entry.Project)
                .FirstOrDefault(directory => relative.StartsWith(directory + "/", StringComparison.Ordinal));
            if (project is null)
            {
                continue;
            }

            var nameStart = link.IndexOf('`', StringComparison.Ordinal) + 1;
            var name = link[nameStart..link.IndexOf('`', nameStart)];

            rows.Add(new Row(name, project, relative[(project.Length + 1)..], layer));
        }

        rows.ShouldNotBeEmpty($"the map ({Map}) lists no test file at all — the row reader is broken.");
        return rows;
    }

    /// <summary>
    /// The test files of the tree: their project and their file name. The
    /// files are read from the source — a rule that reads a hand-written list
    /// proves nothing about the tree.
    /// </summary>
    private static List<(string Project, string Layer, string File)> TestFiles()
    {
        var files = new List<(string, string, string)>();

        foreach (var (project, layer) in Layers)
        {
            var directory = Path.Combine(Repository.RootPath, project);
            Directory.Exists(directory).ShouldBeTrue($"the test project {project} is missing");

            foreach (var path in Repository.SourceFiles(directory))
            {
                var content = File.ReadAllText(path);

                // A file that holds a test: it carries one of the attributes, in any form.
                if (!TestAttribute.IsMatch(content))
                {
                    continue;
                }

                files.Add((project, layer, Path.GetRelativePath(directory, path).Replace('\\', '/')));
            }
        }

        return files;
    }
}
