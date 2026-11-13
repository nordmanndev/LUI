using Shouldly;

using Nordmann.LUI.Testing;

namespace Nordmann.LUI.Architecture.Tests;

/// <summary>
/// Folder = namespace. The spec fixes the namespace layout (
/// ADR-0014); a file that lives in <c>Indexing/</c> and declares another namespace
/// makes the public API's shape a matter of guesswork. The tests follow the same
/// rule: their folders are their subjects (issue #77).
/// </summary>
public sealed class NamespaceConventionTests
{
    private const string RootNamespace = "Nordmann.LUI";

    [Fact]
    public void Every_source_file_declares_the_namespace_of_its_folder()
    {
        var offenders = new List<string>();

        foreach (var (directory, file) in Repository.PackageDirectories.SelectMany(directory => Repository.SourceFiles(directory).Select(file => (directory, file))))
        {
            var relative = Path.GetRelativePath(directory, file);
            var folder = Path.GetDirectoryName(relative) ?? string.Empty;
            var segments = folder.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            var expected = segments.Length == 0
                ? RootNamespace
                : $"{RootNamespace}.{string.Join(".", segments)}";

            var declared = Repository.DeclaredNamespace(file);
            if (!string.Equals(declared, expected, StringComparison.Ordinal))
            {
                offenders.Add($"{relative}: declares '{declared ?? "<none>"}', expected '{expected}'");
            }
        }

        offenders.ShouldBeEmpty("Folder = namespace (ADR-0014).");
    }

    [Fact]
    public void Every_test_source_file_declares_the_namespace_of_its_folder()
    {
        // issue #77: the tests are sorted by subject, and the folder of a test is its namespace — as in the library. The root
        // is the RootNamespace of the project; a file of global usings declares none and is none of this rule's business.
        var offenders = new List<string>();

        foreach (var project in TestTreeProjects())
        {
            var directory = Path.GetDirectoryName(project)!;
            var root = Repository.Elements(Repository.LoadXml(project), "RootNamespace").SingleOrDefault()?.Value.Trim();
            if (string.IsNullOrEmpty(root))
            {
                // review of pull request 90, M2: the rule names the project that gives it no root, rather than stopping
                offenders.Add($"{Path.GetRelativePath(Repository.RootPath, project).Replace('\\', '/')}: names no RootNamespace");
                continue;
            }

            foreach (var file in Repository.SourceFiles(directory))
            {
                var relative = Path.GetRelativePath(directory, file);
                var segments = (Path.GetDirectoryName(relative) ?? string.Empty)
                    .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
                var expected = segments.Length == 0 ? root : $"{root}.{string.Join(".", segments)}";

                var declared = Repository.DeclaredNamespace(file);
                if (declared is null && OnlyGlobalUsings(file))
                {
                    continue;
                }

                if (!string.Equals(declared, expected, StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetFileName(directory)}/{relative.Replace('\\', '/')}: declares '{declared ?? "<none>"}', expected '{expected}'");
                }
            }
        }

        offenders.ShouldBeEmpty("Folder = namespace, in the tests as in the library.");
    }

    [Fact]
    public void Every_namespace_lives_below_the_root_namespace()
    {
        var offenders = Repository
            .PackageDirectories
            .SelectMany(directory => Repository.SourceFiles(directory))
            .Select(file => (File: Path.GetRelativePath(Repository.SourceDirectory, file), Namespace: Repository.DeclaredNamespace(file)))
            .Where(entry => entry.Namespace is null || !entry.Namespace.StartsWith(RootNamespace, StringComparison.Ordinal))
            .Select(entry => $"{entry.File}: '{entry.Namespace ?? "<none>"}'")
            .ToArray();

        offenders.ShouldBeEmpty($"Every namespace lives below {RootNamespace}.");
    }

    /// <summary>
    /// The projects of the tests: every test project of the tree (<c>IsTestProject</c>), wherever it lives — the tests of
    /// the sample sit beside the sample — and every project under <c>tests/</c>, the shared infrastructure among them.
    /// </summary>
    private static string[] TestTreeProjects()
    {
        var tests = Path.Combine(Repository.RootPath, "tests") + Path.DirectorySeparatorChar;

        return Repository.ProjectFiles()
            .Where(file => file.StartsWith(tests, StringComparison.Ordinal) || Repository.IsTestProject(file))
            .ToArray();
    }

    /// <summary>Whether a file holds nothing but <c>global using</c> lines and comments.</summary>
    private static bool OnlyGlobalUsings(string file) =>
        File.ReadLines(file)
            .Select(line => line.Trim())
            .All(line => line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal)
                                          || line.StartsWith("global using ", StringComparison.Ordinal));
}
