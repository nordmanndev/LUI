using Shouldly;

using Nordmann.LUI.Testing;

namespace Nordmann.LUI.Architecture.Tests;

/// <summary>
/// Folder = namespace. The spec fixes the namespace layout (LUI-Spezifikation
/// §17.2); a file that lives in <c>Indexing/</c> and declares another namespace
/// makes the public API's shape a matter of guesswork.
/// </summary>
public sealed class Namespace_Convention
{
    private const string RootNamespace = "Nordmann.LUI";

    [Fact]
    public void Every_source_file_declares_the_namespace_of_its_folder()
    {
        var offenders = new List<string>();

        foreach (var file in Repository.SourceFiles(Repository.LibraryDirectory))
        {
            var relative = Path.GetRelativePath(Repository.LibraryDirectory, file);
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

        offenders.ShouldBeEmpty("Folder = namespace (LUI-Spezifikation §17.2).");
    }

    [Fact]
    public void Every_namespace_lives_below_the_root_namespace()
    {
        var offenders = Repository
            .SourceFiles(Repository.LibraryDirectory)
            .Select(file => (File: Path.GetRelativePath(Repository.LibraryDirectory, file), Namespace: Repository.DeclaredNamespace(file)))
            .Where(entry => entry.Namespace is null || !entry.Namespace.StartsWith(RootNamespace, StringComparison.Ordinal))
            .Select(entry => $"{entry.File}: '{entry.Namespace ?? "<none>"}'")
            .ToArray();

        offenders.ShouldBeEmpty($"Every namespace lives below {RootNamespace}.");
    }
}
