using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Nordmann.LUI.Normalization;
using Nordmann.LUI.Testing;
using Nordmann.LUI.Typos;

using Shouldly;

namespace Nordmann.LUI.Integration.Tests.Typos;

/// <summary>
/// The contract of the typo tolerance with SymSpell (issue #44, ADR-0064): what LUI asks of the package, and what the
/// package gives back.
/// <para>
/// Two parts. The <b>rules</b> — a corpus with known weights, where every rule of the contract is visible, and where a
/// change of the contract breaks a test. The <b>measurement</b> over the whole word list: the time of the build, the
/// memory of the dictionary, the number of the corrections and their fingerprint. The fingerprint is what an own
/// implementation of the dictionary would have to reproduce; the numbers are what a decision about it is made with.
/// </para>
/// </summary>
public sealed class TypoContractTests(ITestOutputHelper output)
{
    /// <summary>
    /// A beginning is of three to ten letters: a word of two letters is no beginning, a word of three is.
    /// </summary>
    [Fact]
    public void A_beginning_is_of_three_to_ten_letters()
    {
        var minimum = Dictionary(("Ab", 100), ("Abc", 100));

        minimum.Corrections("xb").ShouldBeEmpty("two letters are no beginning");
        minimum.Corrections("xbc").ShouldBe(["abc"], "three letters are a beginning");
    }

    /// <summary>
    /// The count of a word is its weight plus one: of two corrections of the same distance and the same length, the
    /// heavier one comes first — and the weight decides, not the order of the corpus.
    /// </summary>
    [Fact]
    public void The_count_of_a_word_is_its_weight_plus_one()
    {
        var heavier = Dictionary(("Apfelkuchen", 100), ("Apfelkuchan", 5));
        heavier.Corrections("apfelkuchxn").ShouldBe(["apfelkuchen", "apfelkuchan", "apfelkuchn"]);

        var lighter = Dictionary(("Apfelkuchen", 5), ("Apfelkuchan", 100));
        lighter.Corrections("apfelkuchxn").ShouldBe(["apfelkuchan", "apfelkuchen", "apfelkuchn"]);
    }

    /// <summary>
    /// The count threshold is one: a word with the weight zero is a word.
    /// </summary>
    [Fact]
    public void The_count_threshold_is_one()
    {
        var zero = Dictionary(("Zebra", 0));

        zero.Corrections("zebrx").ShouldContain("zebra", StringComparer.Ordinal, "a word with the weight zero is a word");
    }

    /// <summary>
    /// The order of the corrections: the distance, then the length that was typed, then the count, then the ordinal.
    /// </summary>
    [Fact]
    public void The_order_of_the_corrections_is_the_one_the_record_says()
    {
        var length = Dictionary(("Apfelkuchen", 100));
        length.Corrections("apfelkucx").ShouldBe(["apfelkuch", "apfelkuc"], "the length that was typed comes first");

        var ordinal = Dictionary(("Apfelkuchen", 100), ("Apfelkuchan", 100));
        ordinal.Corrections("apfelkuchxn").ShouldBe(["apfelkuchan", "apfelkuchen", "apfelkuchn"], "the ordinal is the last tie");
    }

    /// <summary>
    /// The head of a lookup is the first ten letters: a typo in the tenth letter is corrected, a typo in the eleventh is
    /// not, and the tail of the word is kept as it was typed.
    /// </summary>
    [Fact]
    public void The_head_of_a_lookup_is_the_first_ten_letters()
    {
        var reach = Dictionary(("Apfelkuchen", 100), ("Apfelkuchenbrötchen", 100));

        reach.Corrections("apfelkuchxn").ShouldBe(["apfelkuchen", "apfelkuchn"], "a typo in the tenth letter is corrected");
        reach.Corrections("apfelkuchenxbrötchen").ShouldBeEmpty("a typo in the eleventh letter is not corrected");
    }

    /// <summary>
    /// The measurement over the whole word list: the cost of the dictionary and the fingerprint of the corrections.
    /// </summary>
    [FullWordListFact]
    [Trait("Category", "Timing")]
    public void The_contract_over_the_whole_word_list_is_measured()
    {
        var words = File.ReadLines(TestFiles.FullWordListPath)
            .Where(word => word.Length >= 3 && word.All(char.IsLetter))
            .Select((word, index) => (Text: word, Weight: index + 1))
            .ToList();

        // The dictionary, built by LUI itself — the same code path an index uses, without the index around it.
        GC.Collect();
        var before = GC.GetTotalMemory(forceFullCollection: true);
        var watch = Stopwatch.StartNew();
        var dictionary = new TypoDictionary(NormalizationOptions.Default, () => words, LuiTypoDistance.OneError);
        dictionary.EnsureBuilt(CancellationToken.None);
        var build = watch.Elapsed;
        GC.Collect();
        var memory = GC.GetTotalMemory(forceFullCollection: true) - before;

        // The corrections of the whole list: the fingerprint is what an own implementation would have to reproduce.
        var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var corrected = 0;
        var length = new StringBuilder();
        foreach (var (word, _) in words)
        {
            var search = Normalizer.ToSearchForm(word, NormalizationOptions.Default);
            var corrections = dictionary.Corrections(Typo(search));

            length.Clear();
            foreach (var correction in corrections)
            {
                length.Append(correction).Append('|');
            }

            hash.AppendData(Encoding.UTF8.GetBytes(length.ToString()));
            if (corrections.Count > 0)
            {
                corrected++;
            }
        }

        var fingerprint = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();

        output.WriteLine($"the words: {words.Count}");
        output.WriteLine($"the build: {build.TotalSeconds:F1} s");
        output.WriteLine($"the memory: {memory / 1_048_576.0:F0} MB");
        output.WriteLine($"the corrected words: {corrected} of {words.Count}");
        output.WriteLine($"the fingerprint: {fingerprint}");

        Report(words.Count, build, memory, corrected, fingerprint);

        // The cost of the dictionary is the promise of ADR-0020: 200 MB for 700,000 words.
        (memory / 1_048_576.0).ShouldBeLessThan(200, "the dictionary of the whole word list stays inside the budget of ADR-0020");
        fingerprint.ShouldBe("936f9a7240fe2d362dafb3bf9739fae078c4f0f9c145fcf42ec49f24d7061955", "the corrections of the whole list are the ones the record says");
    }

    /// <summary>
    /// The report of the measurement: the numbers, beside the machine that measured them.
    /// </summary>
    private static void Report(int words, TimeSpan build, long memory, int corrected, string fingerprint)
    {
        var report = Path.Combine(Repository.RootPath, "artifacts", "test-results", "typo-contract.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(report)!);
        File.WriteAllText(report, string.Join(Environment.NewLine,
        [
            $"the words: {words}",
            $"the build: {build.TotalSeconds:F1} s",
            $"the memory: {memory / 1_048_576.0:F0} MB",
            $"the corrected words: {corrected} of {words}",
            $"the fingerprint: {fingerprint}",
            $"the runtime: {Environment.Version} on {Environment.OSVersion}",
        ]) + Environment.NewLine);
    }

    /// <summary>
    /// A dictionary of LUI's own, with the words and their weights.
    /// </summary>
    private static TypoDictionary Dictionary(params (string Text, int Weight)[] words)
    {
        var dictionary = new TypoDictionary(NormalizationOptions.Default, () => words, LuiTypoDistance.OneError);
        dictionary.EnsureBuilt(CancellationToken.None);
        return dictionary;
    }

    /// <summary>
    /// A search form with one letter wrong — the third one, always, so that the measurement is the same every time.
    /// </summary>
    private static string Typo(string search)
    {
        if (search.Length < 3)
        {
            return search;
        }

        var letter = search[2] == 'x' ? 'y' : 'x';
        return search[..2] + letter + search[3..];
    }
}
