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
///   Indexes               an index: puts a content source, the writes and the query path together; the stored definitions
///     Querying  Writing   the query path · the write statements
///       Schema            definitions, the rules of a kind, the content sources and their SQL
///         Typos  Normalization  Storage   … · … · the connections, the writer, the lifetime
/// </code>
/// The root namespace holds the public types every layer speaks in, and the facade above all of them: the
/// builders and the catalog. A layer may use the public types, never the facade — that would be a way up
/// past the rule (review of pull request 14, S1). The catalog has no exception any more: its connections and its
/// lifetime are <c>Storage.Database</c>, its definitions <c>Indexes.IndexStore</c> (review DevEx D4, D6; PR-007). The
/// dependency injection is a package of its own above all of it, <c>Nordmann.LUI.Hosting</c> (ADR-0018): the compiler
/// keeps it on the public API.
/// </summary>
public sealed class InternalLayeringTests
{
    private const string Root = "Nordmann.LUI";

    private static Assembly LibraryAssembly => Assembly.Load(Root);

    [Theory]
    [InlineData("Normalization", new[] { "Typos", "Storage", "Schema", "Querying", "Writing", "Indexes" })]
    [InlineData("Typos", new[] { "Storage", "Schema", "Querying", "Writing", "Indexes" })]
    [InlineData("Storage", new[] { "Normalization", "Typos", "Schema", "Querying", "Writing", "Indexes" })]
    [InlineData("Schema", new[] { "Typos", "Storage", "Querying", "Writing", "Indexes" })]
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
    [InlineData("Normalization")]
    [InlineData("Typos")]
    [InlineData("Storage")]
    [InlineData("Schema")]
    [InlineData("Writing")]
    [InlineData("Querying")]
    [InlineData("Indexes")]
    public void No_layer_goes_up_through_the_facade(string layer)
    {
        // every builder of the public API, the ones to come included
        var facade = LibraryAssembly
            .GetExportedTypes()
            .Where(type => string.Equals(type.Namespace, Root, StringComparison.Ordinal)
                        && type.Name.Contains("Builder", StringComparison.Ordinal))
            .Select(type => type.FullName!)
            .ToList();
        facade.ShouldNotBeEmpty();
        facade.Add($"{Root}.LuiCatalog");

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
            "LuiIndexKindBuilder.cs",
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
