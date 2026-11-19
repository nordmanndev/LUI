using System.Text.RegularExpressions;
using Shouldly;

using Nordmann.LUI.Testing;

namespace Nordmann.LUI.Architecture.Tests;

/// <summary>
/// The prose of the README says a thing once (issue #182). A merge of two branches can leave the same paragraph twice —
/// the one of the older branch beside the one of the newer, which only adds a sentence — and a reader cannot tell which
/// of the two is the current one. The test compares the <b>sentences</b> after the whitespace is made one (a line break
/// is no difference): a paragraph that is copied with one sentence more shares all the others with its original, and an
/// exact comparison of the paragraphs would not see it.
/// <para>
/// Tables, code and the rendered benchmark section are no prose: a table repeats its own words by design, and the
/// section is rendered from the reports (<see cref="ReadmeBenchmarksTests"/>).
/// </para>
/// </summary>
public sealed class ReadmeProseTests
{
    /// <summary>A sentence shorter than this is a phrase, and a phrase may stand twice ("See below.").</summary>
    private const int MinimumWords = 8;

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static readonly Regex EndOfSentence = new(@"(?<=[.!?])\s+", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static readonly Regex Rendered = new(
        @"<!-- benchmarks:begin -->.*?<!-- benchmarks:end -->",
        RegexOptions.CultureInvariant | RegexOptions.Singleline,
        TimeSpan.FromSeconds(1));

    /// <summary>A heading ("# Title", "## Title") — and not a line that wraps before "#176".</summary>
    private static readonly Regex Heading = new(@"^\s*#{1,6}\s", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>The mark of a list item or a quote: the same sentence in a list and in a paragraph is the same sentence.</summary>
    private static readonly Regex Marker = new(@"^\s*(?:>\s*)*(?:[-*+]|\d+\.)?\s+", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static readonly Regex Fence = new(@"^```.*?^```", RegexOptions.CultureInvariant | RegexOptions.Multiline | RegexOptions.Singleline, TimeSpan.FromSeconds(1));

    [Fact]
    public void No_sentence_of_the_prose_of_the_readme_stands_twice()
    {
        var sentences = ParagraphsOfTheProse()
            .SelectMany(paragraph => EndOfSentence.Split(paragraph))
            .Select(sentence => sentence.Trim())
            .Where(sentence => sentence.Split(' ').Length >= MinimumWords)
            .ToList();

        var twice = sentences
            .GroupBy(sentence => sentence, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Count()}× \"{Shorten(group.Key)}\"")
            .ToList();

        twice.ShouldBeEmpty(
            "README.md says a thing twice — a paragraph that was copied with a sentence more (merge of two branches, issue #182): "
            + string.Join("; ", twice));
    }

    [Fact]
    public void The_typo_run_of_the_dictionary_is_named_once()
    {
        // issue #182: the run of the typo dictionary stands once in the README, in the paragraph that carries it — not in a second copy of
        // it. The run of 2026-11-12 was measured again on 2026-11-19 (issue #242): the date moves with the run.
        var text = string.Join("\n\n", ParagraphsOfTheProse());

        Regex.Count(text, "typo dictionary was measured again on", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).ShouldBe(
            1,
            "the run of the typo dictionary stands once in the README — in the paragraph that carries it, not in a second copy of it");
        text.Contains("2026-11-12", StringComparison.Ordinal).ShouldBeFalse("the run of 2026-11-12 could not be reproduced and is not quoted as a measurement");
    }

    /// <summary>The paragraphs of the README with their whitespace made one — without tables, code and the rendered section.</summary>
    private static IEnumerable<string> ParagraphsOfTheProse()
    {
        var text = File.ReadAllText(Path.Combine(Repository.RootPath, "README.md")).Replace("\r\n", "\n", StringComparison.Ordinal);
        text = Rendered.Replace(text, string.Empty);
        text = Fence.Replace(text, string.Empty);

        foreach (var block in text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var lines = block.Split('\n').Where(line => !line.TrimStart().StartsWith('|') && !Heading.IsMatch(line)).Select(line => Marker.Replace(line, string.Empty)).ToArray();
            if (lines.Length == 0)
            {
                continue;
            }

            yield return Whitespace.Replace(string.Join(' ', lines), " ").Trim();
        }
    }

    private static string Shorten(string sentence) => sentence.Length <= 90 ? sentence : sentence[..90] + "…";
}
