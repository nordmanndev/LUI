using Shouldly;

using Nordmann.LUI.Testing;

namespace Nordmann.LUI.Architecture.Tests;

/// <summary>
/// The specification is deleted (ADR-0061), and nothing points at it any more.
/// A reference to a document that is gone is a dead end: the reader follows it
/// and finds nothing — and the rule that lived in it has an ADR, so the
/// pointer can name that.
/// </summary>
public sealed class SpecificationIsGoneTests
{
    /// <summary>The papers of the work are dated records: they may name the deleted document to say that it is gone.</summary>
    private const string PapersDirectory = ".agents";

    /// <summary>The section sign, written as an escape — the guard must not carry the sign it looks for.</summary>
    private const char SectionSign = '\u00A7';

    private static readonly string[] Extensions =
        [".cs", ".md", ".sh", ".yml", ".yaml", ".txt", ".props", ".json", ".js", ".html"];

    [Fact]
    public void The_specification_is_gone()
    {
        var path = Path.Combine(Repository.RootPath, ".agents", "spec", "LUI-Spezifikation.md");

        File.Exists(path).ShouldBeFalse(
            "the specification is deleted (ADR-0061); every rule of it has an ADR or a page that carries it.");
    }

    [Fact]
    public void No_file_outside_the_papers_names_the_specification()
    {
        var offenders = TextFiles()
            .Where(file => !IsPaper(file))
            .Where(file => Repository.NormalizedText(file).Contains("LUI-Spezifikation", StringComparison.Ordinal)
                || Repository.NormalizedText(file).Contains(".agents/spec", StringComparison.Ordinal))
            .Select(Relative)
            .ToArray();

        offenders.ShouldBeEmpty(
            "the papers of the work may name the deleted document; the tree around it may not.");
    }

    [Fact]
    public void Every_section_reference_names_a_document_that_exists()
    {
        // `docs/normalization.md` has a section 7 of its own, and the tree refers
        // to it. Every other section reference pointed at the deleted
        // specification, and it now names the ADR or the page that carries the
        // same rule (ADR-0061).
        var offenders = new List<string>();

        foreach (var file in TextFiles().Where(file => !IsPaper(file)))
        {
            var text = Repository.NormalizedText(file);
            var index = 0;

            while ((index = text.IndexOf(SectionSign, index)) >= 0)
            {
                var named = text.LastIndexOf("docs/normalization.md", index, StringComparison.Ordinal);
                if (named < 0 || index - named > 60)
                {
                    offenders.Add($"{Relative(file)}:{text[..index].Count(character => character == '\n') + 1}");
                }

                index++;
            }
        }

        offenders.ShouldBeEmpty(
            "a section reference names a document that exists — the specification does not.");
    }

    /// <summary>The text files of the repository, without the build output and without the guard itself.</summary>
    private static IEnumerable<string> TextFiles() =>
        Directory
            .EnumerateFiles(Repository.RootPath, "*", SearchOption.AllDirectories)
            .Where(path => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}artifacts{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.EndsWith("SpecificationIsGoneTests.cs", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal);

    /// <summary>A file of the papers of the work: a dated record that may name the deleted document.</summary>
    private static bool IsPaper(string file) =>
        file.Contains($"{Path.DirectorySeparatorChar}{PapersDirectory}{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string Relative(string file) =>
        Path.GetRelativePath(Repository.RootPath, file).Replace('\\', '/');
}
