using Shouldly;

using Nordmann.LUI.Testing;

namespace Nordmann.LUI.Architecture.Tests;

/// <summary>
/// A catalogue is one file (issue #183): <c>Embedded</c> and <c>Linked</c> indexes live side by side in it, the table of the
/// application lies beside the tables of LUI, and the difference between the two kinds is <b>who owns the content table</b> —
/// not which file. The documents say so, in the reference of the API and in the README: a reader who reads "Embedded" and
/// "Linked" as "two files" would plan around a thing that is not there.
/// </summary>
public sealed class CatalogueIsOneFileDocumentationTests
{
    [Theory]
    [InlineData("docs/api.md")]
    [InlineData("README.md")]
    public void The_document_says_that_a_catalogue_is_one_file_and_what_separates_embedded_from_linked(string relativePath)
    {
        var text = File.ReadAllText(Path.Combine(Repository.RootPath, relativePath))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        var flat = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        flat.ShouldContain("One catalogue is one file", Case.Insensitive, $"{relativePath} says that a catalogue is one file");
        flat.ShouldContain("who owns the content table", Case.Insensitive, $"{relativePath} says what separates Embedded from Linked");
        flat.ShouldContain("mixing", Case.Insensitive, $"{relativePath} says that mixing Embedded and Linked is defined");
    }

    [Fact]
    public void The_reference_says_how_the_file_of_the_application_becomes_a_catalogue_and_what_its_journal_mode_is()
    {
        var flat = string.Join(' ', File.ReadAllText(Path.Combine(Repository.RootPath, "docs", "api.md"))
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        flat.ShouldContain("file of the application becomes a catalogue with `OpenOrCreate`", Case.Insensitive,
            "docs/api.md says how the file of the application becomes a catalogue");
        flat.ShouldContain("only when `OpenOrCreate` **creates the file itself**", Case.Insensitive,
            "docs/api.md says when LUI sets the journal mode (ADR-0066, ADR-0068)");
        flat.ShouldContain("already there — with tables or empty — keeps its mode", Case.Insensitive,
            "docs/api.md says that a file that is there keeps its mode, empty or not");
        flat.ShouldNotContain("schema was empty", Case.Insensitive, "the rule of 'an empty schema' was replaced (ADR-0066, 2026-11-18)");
    }
}
