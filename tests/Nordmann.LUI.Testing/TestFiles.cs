using System.Globalization;

namespace Nordmann.LUI.Testing;

/// <summary>
/// The corpora of the tests, and where they live. One place, because eight
/// tests each built their own path out of the repository root — and a path
/// that is spelled eight times is a path that is wrong once.
/// </summary>
public static class TestFiles
{
    /// <summary>The word list of the repository: the deterministic sample, always there.</summary>
    public static string SampleWordListPath =>
        Path.Combine(Repository.RootPath, "tests", "TestData", "wordlist", "german-words.sample.txt");

    /// <summary>The characters a stride cannot reach for: umlauts, ß, accents, ligatures.</summary>
    public static string EdgeCasesPath =>
        Path.Combine(Repository.RootPath, "tests", "TestData", "wordlist", "edge-cases.txt");

    /// <summary>
    /// The whole German word list (685,829 words). It is not checked in —
    /// <c>tools/fetch-wordlist.sh</c> puts it there. The path can be overridden
    /// with <c>LUI_WORDLIST</c>.
    /// </summary>
    public static string FullWordListPath =>
        Environment.GetEnvironmentVariable("LUI_WORDLIST")
        ?? Path.Combine(Repository.RootPath, "artifacts", "wordlist", "german-words.txt");

    /// <summary>The golden schema of an embedded index, as <c>sqlite_master</c> wrote it.</summary>
    public static string GoldenSchemaPath =>
        Path.Combine(Repository.RootPath, "tests", "TestData", "golden", "embedded-schema.txt");

    /// <summary>The words of the sample corpus.</summary>
    public static string[] SampleWords() =>
        File.ReadLines(SampleWordListPath)
            .Where(line => line.Length > 0)
            .ToArray();

    /// <summary>The number of words the German word list has, as ADR-0019 names it.</summary>
    public const int FullWordListCount = 685_829;

    /// <summary>Formats a count the way the tests report it.</summary>
    public static string Count(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
