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
public sealed class PublicApiDocumentationTests
{
    /// <summary>The page: every public type of the two packages, with a sentence for each (ADR-0038).</summary>
    private static string DocumentPath => Path.Combine(Repository.RootPath, "docs", "api.md");

    private static string ReadmePath => Path.Combine(Repository.RootPath, "README.md");

    private static Assembly[] PackageAssemblies => [Assembly.Load("Nordmann.LUI"), Assembly.Load("Nordmann.LUI.Hosting")];

    /// <summary>Every public type of the two packages, by its simple name — without the arity of a generic type.</summary>
    private static string[] PublicTypes() =>
        PackageAssemblies
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Select(TypeNames.Of)
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

    [Fact]
    public void The_page_names_no_member_that_the_type_does_not_have()
    {
        var offenders = new List<string>();

        foreach (var (type, member) in MembersOfThePage())
        {
            if (!HasMember(type, member))
            {
                offenders.Add($"{TypeNames.Of(type)}.{member}");
            }
        }

        offenders.ShouldBeEmpty(
            "docs/api.md names no member that the type does not have (ADR-0038, issue #130): "
            + string.Join(", ", offenders)
            + ". A member that is gone needs its row removed, and a renamed one its new name.");
    }

    /// <summary>Every member the page names, with the type it names it for.</summary>
    private static IEnumerable<(Type Type, string Member)> MembersOfThePage()
    {
        var lines = Repository.NormalizedText(DocumentPath).Split('\n');
        var heading = string.Empty;
        var index = 0;

        while (index < lines.Length)
        {
            var line = lines[index];

            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                heading = line;
                index++;
                continue;
            }

            if (!line.StartsWith("| ", StringComparison.Ordinal) || index + 1 >= lines.Length || !IsSeparator(lines[index + 1]))
            {
                index++;
                continue;
            }

            var header = Cells(line);
            var rows = new List<string[]>();

            for (var row = index + 2; row < lines.Length && lines[row].StartsWith("| ", StringComparison.Ordinal); row++)
            {
                rows.Add(Cells(lines[row]));
            }

            foreach (var named in Members(header, rows, heading))
            {
                yield return named;
            }

            index += 2 + rows.Count;
        }
    }

    /// <summary>
    /// The members of a table of the page. A table of members names its type in the heading above it; a table of
    /// types names the type in its first column, and the members in the others.
    /// </summary>
    private static IEnumerable<(Type Type, string Member)> Members(string[] header, List<string[]> rows, string heading)
    {
        if (header.Length == 0)
        {
            yield break;
        }

        if (string.Equals(header[0], "Member", StringComparison.Ordinal))
        {
            var type = TypeNamedIn(heading);
            if (type is null)
            {
                yield break;
            }

            foreach (var row in rows.Where(row => row.Length > 0))
            {
                var member = MemberName(row[0]);

                if (member.Length > 0)
                {
                    yield return (type, member);
                }
            }

            yield break;
        }

        if (!string.Equals(header[0], "Type", StringComparison.Ordinal))
        {
            yield break;
        }

        foreach (var row in rows.Where(row => row.Length > 1))
        {
            var type = TypeNamedIn(row[0]);
            if (type is null)
            {
                continue;
            }

            foreach (var cell in row.Skip(1))
            {
                foreach (var token in CodeSpans(cell))
                {
                    if (!MemberToken.IsMatch(token))
                    {
                        continue;
                    }

                    var member = MemberName(token);

                    if (member.Length > 0)
                    {
                        yield return (type, member);
                    }
                }
            }
        }
    }

    /// <summary>The type a heading or a cell names, or null when it names none.</summary>
    private static Type? TypeNamedIn(string text)
    {
        foreach (var token in Backticked(text))
        {
            var name = token.Split('<')[0].Trim();

            var type = PackageAssemblies
                .SelectMany(assembly => assembly.GetExportedTypes())
                .FirstOrDefault(candidate => string.Equals(TypeNames.Of(candidate), name, StringComparison.Ordinal));

            if (type is not null)
            {
                return type;
            }
        }

        return null;
    }

    /// <summary>
    /// The name of a member as the page writes it: the identifier before the parentheses of a method, the identifier
    /// of a property.
    /// </summary>
    private static string MemberName(string cell)
    {
        var text = cell.Trim().Trim('`').Trim();

        var cut = text.IndexOfAny(['(', '{']);
        if (cut >= 0)
        {
            text = text[..cut];
        }

        var words = text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return string.Empty;
        }

        // the name of a member is the last word; a generic part belongs to the type of the member
        var name = words[^1];
        var generic = name.IndexOf('<', StringComparison.Ordinal);

        return generic < 0 ? name : name[..generic];
    }

    /// <summary>
    /// Whether a token of a table of types names a member: an identifier, with a generic part and a signature. A
    /// text in backticks that is prose — a path, a phrase — names no member.
    /// </summary>
    private static readonly Regex MemberToken = new(
        @"^[A-Za-z_][A-Za-z0-9_]*(?:<[^>]*>)?(?:\(.*\))?$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    /// <summary>Whether a type has a member with that name — a public one, static or not.</summary>
    private static bool HasMember(Type type, string member)
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;

        if (type.GetMember(member, Flags).Length > 0)
        {
            return true;
        }

        // a constructor carries the name of its type, and GetMember does not return the constructors
        if (string.Equals(TypeNames.Of(type), member, StringComparison.Ordinal) && type.GetConstructors().Length > 0)
        {
            return true;
        }

        // an enumeration has values, not members
        if (type.IsEnum)
        {
            return Enum.GetNames(type).Contains(member, StringComparer.Ordinal);
        }

        // an interface does not inherit its members the way a class does: the interfaces below it are asked too
        return type.GetInterfaces().Any(baseInterface => baseInterface.GetMember(member, Flags).Length > 0);
    }

    /// <summary>The cells of a row of a table of the page.</summary>
    private static string[] Cells(string line) =>
        line.Trim().Trim('|').Split('|').Select(cell => cell.Trim()).ToArray();

    /// <summary>Whether a line is the line of dashes below the header of a table.</summary>
    private static bool IsSeparator(string line)
    {
        var trimmed = line.Trim();

        return trimmed.Contains('|', StringComparison.Ordinal)
            && trimmed.Replace("|", string.Empty, StringComparison.Ordinal).All(character => character == '-');
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
            .Select(match => match.Groups["name"].Value)
            .ToArray();

    /// <summary>Every span in backticks in a text — a member with its signature, not only its name.</summary>
    private static IEnumerable<string> CodeSpans(string text) =>
        CodeSpan
            .Matches(text)
            .Select(match => match.Groups["span"].Value);

    /// <summary>
    /// A name in backticks — with the timeout the analyzer asks for (MA0009) and with the named group the
    /// analyzer asks for (MA0023). The group has a name: with <c>ExplicitCapture</c> an unnamed group captures
    /// nothing, and a guard that reads empty strings checks nothing.
    /// </summary>
    private static readonly Regex Backtick = new(
        @"`(?<name>[A-Za-z_][A-Za-z0-9_]*)`",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(1));

    /// <summary>A span in backticks — with the timeout (MA0009) and the named group (MA0023).</summary>
    private static readonly Regex CodeSpan = new(
        @"`(?<span>[^`\n]+)`",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(1));
}
