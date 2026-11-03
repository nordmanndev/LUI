using System.Xml.Linq;

namespace Nordmann.LUI.Testing;

/// <summary>
/// Finds the repository on disk. The architecture rules read the *source* —
/// a rule that reads a hand-written list proves nothing about the tree.
/// </summary>
public static class Repository
{
    private static readonly Lazy<DirectoryInfo> Root = new(FindRoot);

    public static DirectoryInfo RootDirectory => Root.Value;

    public static string RootPath => RootDirectory.FullName;

    public static string SourceDirectory => Path.Combine(RootPath, "src");

    public static string LibraryDirectory => Path.Combine(SourceDirectory, "Nordmann.LUI");

    /// <summary>The second package: LUI in a .NET host (ADR-0018).</summary>
    public static string HostingDirectory => Path.Combine(SourceDirectory, "Nordmann.LUI.Hosting");

    /// <summary>The directories of the two packages, whose sources share the root namespace <c>Nordmann.LUI</c>.</summary>
    public static IReadOnlyList<string> PackageDirectories => [LibraryDirectory, HostingDirectory];

    /// <summary>All project files, excluding build output.</summary>
    public static IReadOnlyList<string> ProjectFiles() =>
        Directory
            .EnumerateFiles(RootPath, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

    /// <summary>All C# sources below a directory, excluding build output.</summary>
    public static IReadOnlyList<string> SourceFiles(string rootDirectory) =>
        Directory
            .EnumerateFiles(rootDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

    public static XDocument LoadXml(string path) => XDocument.Load(path);

    /// <summary>
    /// All descendant elements with the given local name, regardless of the
    /// XML namespace. MSBuild files live in the MSBuild namespace, so matching
    /// on the plain name would silently find nothing.
    /// </summary>
    public static IEnumerable<XElement> Elements(XDocument document, string localName) =>
        document.Descendants().Where(element => string.Equals(element.Name.LocalName, localName, StringComparison.Ordinal));

    /// <summary>The namespace declared by a C# file, or null when it declares none.</summary>
    public static string? DeclaredNamespace(string sourceFile)
    {
        using var reader = new StreamReader(sourceFile);
        var content = reader.ReadToEnd();

        // A file-scoped or block namespace declaration always starts a line in
        // this codebase; a match inside a comment or string does not.
        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (!trimmed.StartsWith("namespace ", StringComparison.Ordinal))
            {
                continue;
            }

            var rest = trimmed["namespace ".Length..].Trim();
            var end = rest.IndexOfAny([';', '{', ' ']);
            var ns = end < 0 ? rest : rest[..end];
            return ns.Length == 0 ? null : ns;
        }

        return null;
    }

    private static DirectoryInfo FindRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Directory.Build.props"))
                && Directory.Exists(Path.Combine(current.FullName, "src")))
            {
                return current;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate the repository root (a directory with Directory.Build.props and src/).");
    }
}
