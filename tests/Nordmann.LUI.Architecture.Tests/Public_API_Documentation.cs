using System.Reflection;
using System.Text.RegularExpressions;
using Shouldly;

using Nordmann.LUI.Testing;

namespace Nordmann.LUI.Architecture.Tests;

/// <summary>
/// The public API has a page of its own — <c>docs/api.md</c> — and this test holds it up (ADR-0038). A page that is
/// written by hand rots: the API grows, the page stays, and the reader of the package trusts a document that was true
/// in an earlier version. The assemblies are the truth; the page is checked against them.
/// <para>
/// Two directions: a public type that is not named on the page (the API grew and the page did not), and a type the page
/// names that the code does not have (the API shrank and the page did not).
/// </para>
/// </summary>
public sealed class Public_API_Documentation
{
    /// <summary>The page: every public type of the two packages, with a sentence for each (ADR-0038).</summary>
    private static string DocumentPath => Path.Combine(Repository.RootPath, "docs", "api.md");

    private static string ReadmePath => Path.Combine(Repository.RootPath, "README.md");

    private static Assembly[] PackageAssemblies => [Assembly.Load("Nordmann.LUI"), Assembly.Load("Nordmann.LUI.Hosting")];

    /// <summary>Every public type of the two packages, by its simple name.</summary>
    private static string[] PublicTypes() =>
        PackageAssemblies
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Select(type => type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

    [Fact]
    public void Every_public_type_of_the_package_is_on_the_page()
    {
        var page = File.ReadAllText(DocumentPath);

        var missing = PublicTypes()
            .Where(name => !NamesThe(page, name))
            .ToArray();

        missing.ShouldBeEmpty(
            "docs/api.md documents every public type of the package (ADR-0038). Missing: "
            + string.Join(", ", missing)
            + ". A new public type needs a row and a sentence there.");
    }

    [Fact]
    public void The_page_names_no_type_that_the_code_does_not_have()
    {
        var page = File.ReadAllText(DocumentPath);
        var known = PublicTypes().ToHashSet(StringComparer.Ordinal);

        // the rows of the table at the end of the page: "| `LuiCatalog` | class | … |"
        var named = TableTypes(page)
            .Where(name => !known.Contains(name))
            .ToArray();

        named.ShouldBeEmpty(
            "docs/api.md names no type that the packages do not have (ADR-0038): "
            + string.Join(", ", named)
            + ". A type that is gone needs its row removed.");
    }

    [Fact]
    public void The_readme_names_no_type_that_the_code_does_not_have()
    {
        var readme = File.ReadAllText(ReadmePath);
        var known = PublicTypes().ToHashSet(StringComparer.Ordinal);

        // the type names of the README: `LuiCatalog` in backticks
        var named = Backticked(readme)
            .Where(name => name.StartsWith("Lui", StringComparison.Ordinal) || name.StartsWith("ILui", StringComparison.Ordinal))
            .Where(name => !known.Contains(name))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        named.ShouldBeEmpty(
            "the README shows no type that the code does not have (AGENTS.md): "
            + string.Join(", ", named));
    }

    /// <summary>Whether the page names a type: as a row of the table, or as a member of it.</summary>
    private static bool NamesThe(string page, string name) =>
        TableTypes(page).Contains(name, StringComparer.Ordinal)
        || Backticked(page).Contains(name, StringComparer.Ordinal);

    /// <summary>The types of the table at the end of the page: the first column, in backticks.</summary>
    private static string[] TableTypes(string page) =>
        page
            .Split('\n')
            .Where(line => line.StartsWith("| `", StringComparison.Ordinal))
            .Select(line => line.Split('|').Select(cell => cell.Trim()).ToArray())
            .Where(cells => cells.Length > 3 && Kinds.Contains(cells[2].Trim('`')))
            .Select(cells => cells[1].Trim('`'))
            .Where(name => name.Length > 0)
            .ToArray();

    /// <summary>The kinds a type row of the table carries: class, record, interface, enum, struct.</summary>
    private static readonly string[] Kinds = ["class", "record", "interface", "enum", "struct"];

    /// <summary>Every name in backticks in a text.</summary>
    private static string[] Backticked(string text) =>
        Backtick
            .Matches(text)
            .Select(match => match.Groups[1].Value)
            .ToArray();

    /// <summary>A name in backticks — with the timeout the analyzer asks for (MA0009).</summary>
    private static readonly Regex Backtick = new(
        @"`([A-Za-z_][A-Za-z0-9_]*)`",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(1));
}
