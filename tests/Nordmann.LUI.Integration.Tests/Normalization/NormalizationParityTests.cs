using System.Globalization;
using Nordmann.LUI.Normalization;
using Shouldly;

namespace Nordmann.LUI.Integration.Tests.Normalization;

/// <summary>
/// The normalisation rule exists twice — as SQL expressions in the index and
/// as the C# <see cref="Normalizer"/>. This is the test that holds the two
/// together: it asks the live tokenizer what it makes of a text and compares
/// the answer with what the C# side produces.
/// </summary>
public sealed class NormalizationParityTests
{
    [Fact]
    public void The_fold_table_matches_the_tokenizer_for_every_code_point()
    {
        using var probe = new TokenizerProbe();

        var expected = new Dictionary<int, string>();

        for (var codePoint = 1; codePoint <= 0xFFFF; codePoint++)
        {
            if (codePoint is >= 0xD800 and <= 0xDFFF)
            {
                continue;
            }

            var row = probe.Add("aaa" + char.ConvertFromUtf32(codePoint) + "aaa");
            expected[row] = char.ConvertFromUtf32(codePoint).ToString();
        }

        var folded = probe.FoldAll();

        var mismatches = new List<string>();

        foreach (var (row, original) in expected)
        {
            var actual = folded.TryGetValue(row, out var text) ? text : string.Empty;
            var middle = actual.Length >= 6 ? actual[3..^3] : string.Empty;

            // A character the tokenizer drops entirely is the one the table
            // marks as removed.
            var expectedChar = middle.Length switch
            {
                0 => TokenizerFold.Removed,
                1 => middle[0],
                _ => '?'
            };

            var tableChar = TokenizerFold.Fold(original[0]);

            if (expectedChar != tableChar)
            {
                mismatches.Add(
                    $"U+{(int)original[0]:X4}: tokenizer '{Describe(expectedChar)}', table '{Describe(tableChar)}'");
            }
        }

        mismatches.ShouldBeEmpty(
            "The fold table must be the tokenizer's behaviour — regenerate it with tools/FoldTableGenerator.");
    }

    [Fact]
    public void The_normalizer_and_the_tokenizer_agree_on_the_word_corpus()
    {
        var words = ReadWords(TestFiles.SampleWordListPath);

        words.Count.ShouldBeGreaterThan(1_000);

        AssertParity(words);
    }

    [FullWordListFact]
    public void The_normalizer_and_the_tokenizer_agree_on_the_full_word_list()
    {
        var full = TestFiles.FullWordListPath;

        if (!File.Exists(full))
        {
            // The attribute skips this test when the list is missing — unless
            // the release gate demands it, then it fails here with the reason.
            // A test that returns here would report *Passed*: it would have
            // compared nothing with nothing, and the report would say green.
            throw new InvalidOperationException(
                $"The full word list is required but missing: {full}. Run tools/fetch-wordlist.sh.");
        }

        var words = File.ReadAllLines(full).Where(line => line.Length > 0).ToArray();

        words.Length.ShouldBe(TestFiles.FullWordListCount, "the German word list has 685,829 entries");

        AssertParity(words);
    }

    [Fact]
    public void The_normalizer_and_the_tokenizer_agree_on_the_edge_cases()
    {
        var cases = ReadEdgeCases();

        cases.ShouldNotBeEmpty();

        AssertParity([.. cases.Select(edgeCase => edgeCase.Input)]);
    }

    [Fact]
    public void The_edge_cases_hold_the_measured_expectations()
    {
        var cases = ReadEdgeCases();

        var mismatches = new List<string>();

        foreach (var edgeCase in cases)
        {
            var normalized = Normalizer.Normalize(edgeCase.Input);

            if (!string.Equals(normalized.SearchForm, edgeCase.ExpectedSearchForm, StringComparison.Ordinal))
            {
                mismatches.Add(
                    $"'{edgeCase.Input}': search form '{normalized.SearchForm}', expected '{edgeCase.ExpectedSearchForm}'");
            }

            if (!string.Equals(normalized.CompactForm, edgeCase.ExpectedCompactForm, StringComparison.Ordinal))
            {
                mismatches.Add(
                    $"'{edgeCase.Input}': compact form '{normalized.CompactForm}', expected '{edgeCase.ExpectedCompactForm}'");
            }
        }

        mismatches.ShouldBeEmpty();
    }

    [Fact]
    public void The_compact_form_of_the_sql_column_matches_the_compact_form_of_the_normalizer()
    {
        var words = ReadWords(TestFiles.SampleWordListPath).Take(2_000).ToArray();

        using var probe = new TokenizerProbe();

        var rows = new Dictionary<string, int>(StringComparer.Ordinal);
        var paddedRows = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var word in words)
        {
            var compact = probe.Scalar(SqlNormalizer.Compact(SqlNormalizer.Transliteration("$value")), word);

            rows[word] = probe.Add(compact);
            paddedRows[word] = probe.Add(Padding + compact);
        }

        var folded = probe.FoldAll();

        var mismatches = new List<string>();

        foreach (var word in words)
        {
            var expected = Normalizer.ToCompactForm(word);
            var actual = folded.TryGetValue(rows[word], out var text) ? text : string.Empty;

            if (actual.Length == 0)
            {
                // Too short for a trigram — measure it through the padding.
                actual = Unpad(folded, paddedRows[word]);
            }

            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                mismatches.Add($"'{word}': normalizer '{expected}', tokenizer '{actual}'");
            }
        }

        mismatches.ShouldBeEmpty(
            "The compact column and the compact form of the normaliser must be the same text.");
    }

    [Fact]
    public void No_separator_is_folded_into_a_letter()
    {
        // The other direction: a separator the tokenizer folds into a letter
        // would make the SQL column (which strips before the folding) and the
        // C# compact form (which strips after it) differ.
        var offenders = new List<string>();

        foreach (var separator in Normalizer.Separators)
        {
            var folded = TokenizerFold.Fold(separator);

            if (folded != TokenizerFold.Removed && !Normalizer.Separators.Contains(folded))
            {
                offenders.Add($"'{separator}' folds to '{folded}'");
            }
        }

        offenders.ShouldBeEmpty("A separator must not fold into a letter.");
    }

    [Fact]
    public void No_folded_character_is_a_separator()
    {
        // The SQL side strips the separators from the *unfolded* column, the
        // C# side from the *folded* text. The two only agree if the folding
        // never produces a separator.
        var offenders = new List<string>();

        for (var codePoint = 1; codePoint <= 0xFFFF; codePoint++)
        {
            if (codePoint is >= 0xD800 and <= 0xDFFF)
            {
                continue;
            }

            var original = char.ConvertFromUtf32(codePoint)[0];
            var folded = TokenizerFold.Fold(original);
            if (folded == TokenizerFold.Removed)
            {
                continue;
            }

            // A separator that folds to itself is fine — what would break the
            // parity is a character that *becomes* a separator.
            if (!Normalizer.Separators.Contains(original) && Normalizer.Separators.Contains(folded))
            {
                offenders.Add($"U+{codePoint:X4} folds to the separator '{folded}'");
            }
        }

        offenders.ShouldBeEmpty("The folding must not produce a separator.");
    }

    private static void AssertParity(IReadOnlyList<string> texts)
    {
        using var probe = new TokenizerProbe();

        var rows = new Dictionary<string, int>(StringComparer.Ordinal);
        var paddedRows = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var text in texts)
        {
            // The SQL side stores the transliterated text; the tokenizer folds
            // it when it indexes it. That is the form the query is matched
            // against, so that is what the C# side has to produce.
            var transliterated = probe.Scalar(SqlNormalizer.Transliteration("$value"), text);

            rows[text] = probe.Add(transliterated);

            // A text shorter than a trigram produces nothing at all — the
            // tokenizer cannot fold it. The padded copy measures the folding
            // of the same characters; the padding is removed afterwards.
            paddedRows[text] = probe.Add(Padding + transliterated);
        }

        var folded = probe.FoldAll();
        var mismatches = new List<string>();

        foreach (var text in texts)
        {
            var expected = Normalizer.ToSearchForm(text);
            var actual = folded.TryGetValue(rows[text], out var value) ? value : string.Empty;

            if (actual.Length == 0)
            {
                actual = Unpad(folded, paddedRows[text]);
            }

            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                mismatches.Add($"'{text}': normalizer '{expected}', tokenizer '{actual}'");
            }
        }

        mismatches.Count.ShouldBe(0, $"{mismatches.Count} text(s) differ: {string.Join(" | ", mismatches.Take(5))}");
    }

    /// <summary>
    /// The padding characters. Two, so that even a one-character text makes
    /// a trigram; the tokenizer leaves both of them alone.
    /// </summary>
    private const string Padding = "xx";

    private static string Unpad(Dictionary<int, string> folded, int row) =>
        folded.TryGetValue(row, out var value) && value.StartsWith(Padding, StringComparison.Ordinal)
            ? value[Padding.Length..]
            : string.Empty;

    private static List<string> ReadWords(string path)
    {
        File.Exists(path).ShouldBeTrue($"the corpus {path} is missing");

        return [.. File.ReadAllLines(path).Where(line => line.Length > 0)];
    }

    private static List<EdgeCase> ReadEdgeCases()
    {
        var path = TestFiles.EdgeCasesPath;

        File.Exists(path).ShouldBeTrue($"the corpus {path} is missing");

        var cases = new List<EdgeCase>();

        foreach (var line in File.ReadAllLines(path))
        {
            // A case is a line with tab-separated values. Everything else is
            // a comment — including "#Hashtag", which starts with a hash but
            // carries its expectations.
            if (line.Length == 0 || !line.Contains('\t'))
            {
                continue;
            }

            var parts = line.Split('\t');

            cases.Add(new EdgeCase(
                parts[0],
                parts.Length > 1 ? parts[1] : string.Empty,
                parts.Length > 2 ? parts[2] : string.Empty));
        }

        return cases;
    }

    private sealed record EdgeCase(string Input, string ExpectedSearchForm, string ExpectedCompactForm);

    private static string Describe(char value) =>
        value == TokenizerFold.Removed ? "(removed)" : value.ToString(CultureInfo.InvariantCulture);
}
