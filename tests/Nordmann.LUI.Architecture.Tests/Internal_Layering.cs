using System.Reflection;
using System.Text.RegularExpressions;
using NetArchTest.Rules;
using Shouldly;

using Nordmann.LUI.Testing;

namespace Nordmann.LUI.Architecture.Tests;

/// <summary>
/// The inside of the library has a direction (ADR-0014): it is read from the bottom up, and a namespace
/// knows only the ones below it.
/// <code>
/// Nordmann.LUI            the public types and the facade (LuiCatalog, the builders)
///   Indexes               an index: puts a content source, the writes and the query path together
///     Querying  Writing   the query path · the write statements
///       Schema            definitions, the rules of a kind, the content sources and their SQL
///         Typos  Normalization
/// </code>
/// The root namespace holds the public types every layer speaks in, and the facade above all of them: the
/// builders and the catalog. A layer may use the public types, never the builders — that would be a way up
/// past the rule (review of pull request 14, S1). The catalog is the one named exception, for the query path
/// and the indexes, until it is taken apart (D4, PR-007): they need its operation scope and its connections.
/// </summary>
public sealed class Internal_Layering
{
    private const string Root = "Nordmann.LUI";

    private static Assembly LibraryAssembly => Assembly.Load(Root);

    [Theory]
    [InlineData("Normalization", new[] { "Typos", "Schema", "Querying", "Writing", "Indexes" })]
    [InlineData("Typos", new[] { "Schema", "Querying", "Writing", "Indexes" })]
    [InlineData("Schema", new[] { "Typos", "Querying", "Writing", "Indexes" })]
    [InlineData("Writing", new[] { "Typos", "Querying", "Indexes" })]
    [InlineData("Querying", new[] { "Writing", "Indexes" })]
    public void A_namespace_knows_only_the_ones_below_it(string layer, string[] above)
    {
        var result = Types
            .InAssembly(LibraryAssembly)
            .That()
            .ResideInNamespace($"{Root}.{layer}")
            .ShouldNot()
            .HaveDependencyOnAny([.. above.Select(name => $"{Root}.{name}")])
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"{Root}.{layer} must not depend on {string.Join(", ", above)}. Failing types: "
            + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Theory]
    [InlineData("Normalization", false)]
    [InlineData("Typos", false)]
    [InlineData("Schema", false)]
    [InlineData("Writing", false)]
    [InlineData("Querying", true)]
    [InlineData("Indexes", true)]
    public void No_layer_goes_up_through_the_facade(string layer, bool mayUseTheCatalog)
    {
        // every builder of the public API, the ones to come included
        var facade = LibraryAssembly
            .GetExportedTypes()
            .Where(type => string.Equals(type.Namespace, Root, StringComparison.Ordinal)
                        && type.Name.Contains("Builder", StringComparison.Ordinal))
            .Select(type => type.FullName!)
            .ToList();
        facade.ShouldNotBeEmpty();

        // the named exception (D4, PR-007)
        if (!mayUseTheCatalog)
        {
            facade.Add($"{Root}.LuiCatalog");
        }

        var result = Types
            .InAssembly(LibraryAssembly)
            .That()
            .ResideInNamespace($"{Root}.{layer}")
            .ShouldNot()
            .HaveDependencyOnAny([.. facade])
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"{Root}.{layer} must not depend on {string.Join(", ", facade)}. Failing types: "
            + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Only_the_rules_know_the_rules_of_a_kind()
    {
        // The kind is asked through the rules, never by testing which rules they are:
        // `source.Rules is MultiWordRules` is the comparison of the kind under another name (review of pull request 14, S2).
        var result = Types
            .InAssembly(LibraryAssembly)
            .That()
            .DoNotHaveName("IndexKindRules", "SingleWordRules", "MultiWordRules")
            .ShouldNot()
            .HaveDependencyOnAny($"{Root}.Schema.SingleWordRules", $"{Root}.Schema.MultiWordRules")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            "Only IndexKindRules.For knows the classes of the rules. Failing types: "
            + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Only_the_rules_of_a_kind_ask_which_kind_an_index_is()
    {
        // A rule of a kind that is decided in two places is forgotten in one (review of pull request 8, M1):
        // everything that depends on the kind lives in Schema/*Rules.cs (D1). The builder names the kind it
        // creates; nothing else compares one.
        string[] allowed =
        [
            Path.Combine("Schema", "IndexKindRules.cs"),
            Path.Combine("Schema", "SingleWordRules.cs"),
            Path.Combine("Schema", "MultiWordRules.cs"),
            "LuiIndexBuilder.cs",
            "LuiIndexKind.cs",
        ];

        // the name of a kind, with or without its enum: `using static` makes `kind == MultiWord` possible too
        var kind = new Regex(@"\b(SingleWord|MultiWord)\b", RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));

        var offenders = Repository
            .SourceFiles(Repository.LibraryDirectory)
            .Select(file => (File: file, Relative: Path.GetRelativePath(Repository.LibraryDirectory, file)))
            .Where(entry => !allowed.Contains(entry.Relative, StringComparer.Ordinal))
            .SelectMany(entry => File.ReadLines(entry.File)
                .Select((line, number) => (entry.Relative, Line: line, Number: number + 1)))
            .Where(entry => !entry.Line.TrimStart().StartsWith("///", StringComparison.Ordinal) && kind.IsMatch(entry.Line))
            .Select(entry => $"{entry.Relative}:{entry.Number}: {entry.Line.Trim()}")
            .ToArray();

        offenders.ShouldBeEmpty("The kind of an index is asked only in Schema/*Rules.cs (ADR-0014).");
    }
}
