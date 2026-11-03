using Shouldly;
using Xunit.Abstractions;

namespace Nordmann.LUI.Tests;

/// <summary>
/// The quality of the correction (LUI-Spezifikation §10.4, §19, Anhang A4): how often the intended word is
/// among the best 20 when the beginning of it is typed with one typo, against how often it is when the
/// beginning is typed right. The floors are measured, with a margin — a change that makes the correction
/// worse fails here.
/// </summary>
public sealed class TypoQualityTests(ITestOutputHelper output)
{
    private const int Cases = 200;

    [Theory]
    [InlineData(4, 0.55)]   // measured 64 %: many words begin alike
    [InlineData(5, 0.70)]   // 82 %
    [InlineData(6, 0.80)]   // 93 % — the specification names 86 … 92 % on a corpus 400 times larger
    [InlineData(7, 0.85)]   // 96 %
    [InlineData(8, 0.88)]   // 98 %
    public void A_typo_in_a_beginning_finds_the_word_almost_as_often_as_the_right_beginning(int length, double floor)
    {
        var words = Words();
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("words").SingleWord().TypoTolerance().Ensure();
        Load(catalog, index, words);

        var random = new Random(1000 + length);
        var cases = words.Where(w => w.Length >= length).OrderBy(_ => random.Next()).Take(Cases).ToList();

        var clean = 0;
        var typo = 0;
        foreach (var word in cases)
        {
            var beginning = word[..length].ToLowerInvariant();
            var typed = TypoMaker.WithOneTypo(beginning, random);

            if (Found(index, beginning, word, typoTolerance: false))
            {
                clean++;
            }

            if (Found(index, typed, word, typoTolerance: true))
            {
                typo++;
            }
        }

        var ratio = clean == 0 ? 0 : (double)typo / clean;
        output.WriteLine($"{length} letters: right {clean}/{cases.Count}, with a typo {typo}/{cases.Count}, ratio {ratio:P0}");

        ratio.ShouldBeGreaterThanOrEqualTo(floor, $"{typo} of {clean} at {length} letters");
    }

    private static bool Found(ILuiOwnedIndex<long, LuiNoPayload> index, string typed, string word, bool typoTolerance) =>
        index.Query(typed).Take(20).TypoTolerance(typoTolerance).Execute().Hits
            .Any(h => string.Equals(h.Text, word, StringComparison.Ordinal));

    private static List<string> Words() =>
        [.. File.ReadLines(Path.Combine(Testing.Repository.RootPath, "tests", "Nordmann.LUI.TestData", "wordlist", "german-words.sample.txt"))
            .Where(w => w.Length >= 3 && w.All(char.IsLetter))
            .Distinct(StringComparer.Ordinal)];

    private static void Load(LuiCatalog catalog, ILuiOwnedIndex<long, LuiNoPayload> index, List<string> words)
    {
        var random = new Random(77);
        using var connection = catalog.OpenConnection();
        using var transaction = connection.BeginTransaction();
        for (var i = 0; i < words.Count; i++)
        {
            index.Upsert(i + 1, words[i], random.Next(1, 1_000_000), transaction: transaction);
        }

        transaction.Commit();
    }
}
