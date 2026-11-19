using System.Globalization;
using Nordmann.LUI.Normalization;
using Shouldly;

namespace Nordmann.LUI.Integration.Tests.Querying;

/// <summary>
/// The word start is made when the ranking needs it (issue #174): the prefix path ranks by the weight alone and the
/// candidates carry no text, so its hits make one search form each — and no candidate makes any; the search index makes the
/// word start of its candidates only for a ranking that sorts by it or for a typo correction that asks for it. The answer
/// stays what it was. The counter of the normaliser counts the search forms a query made.
/// </summary>
public sealed class WordStartDemandTests
{
    [Fact]
    public void A_prefix_query_makes_one_search_form_for_each_hit_and_none_for_its_candidates()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = Entries(catalog);

        // what the preparation of the input costs: a query of the same length that finds nothing
        var preparation = SearchForms(() => index.Search("q"));
        var found = default(LuiResult);
        var made = SearchForms(() => found = index.Search("a"));

        found!.Hits.Count.ShouldBeGreaterThan(5);
        found.CandidatesConsidered.ShouldBeGreaterThan(found.Hits.Count, "there are more candidates than hits — otherwise the test proves nothing");
        made.ShouldBe(preparation + found.Hits.Count, "one search form for each hit, none for the candidates");
    }

    [Fact]
    public void The_search_index_makes_the_word_start_of_every_candidate_for_a_ranking_that_sorts_by_it()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = Entries(catalog);

        foreach (var rank in new[] { LuiRankingOrder.WordStartThenSimilarity, LuiRankingOrder.WordStartThenWeight })
        {
            var result = default(LuiResult);
            var made = SearchForms(() => result = index.Search("apf", new LuiQueryOptions { Rank = rank, TypoTolerance = false }));

            made.ShouldBeGreaterThanOrEqualTo(result!.CandidatesConsidered, $"{rank} sorts by the word start: every candidate has one");
        }
    }

    [Fact]
    public void The_search_index_leaves_the_word_start_to_the_hits_when_the_ranking_measures_no_similarity_and_the_answer_is_the_same()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = Entries(catalog);

        // only the weight alone ranks without a similarity — and the similarity is measured from the word start
        var cheap = default(LuiResult);
        var made = SearchForms(() => cheap = index.Search("apf", new LuiQueryOptions { Rank = LuiRankingOrder.WeightOnly, TypoTolerance = false }));

        // a correction may ask for the word start of every candidate: with the typo tolerance on, the word starts are made
        var asked = default(LuiResult);
        var madeForCorrection = SearchForms(() => asked = index.Search("apf", new LuiQueryOptions { Rank = LuiRankingOrder.WeightOnly, TypoTolerance = true }));

        var preparation = SearchForms(() => index.Search("qxz", new LuiQueryOptions { Rank = LuiRankingOrder.WeightOnly, TypoTolerance = false }));
        made.ShouldBe(preparation + cheap!.Hits.Count, "WeightOnly measures nothing for the candidates: no candidate makes a search form, each hit makes one");
        madeForCorrection.ShouldBeGreaterThanOrEqualTo(asked!.CandidatesConsidered, "the typo tolerance may ask for the word start of every candidate");
        Describe(cheap).ShouldBe(Describe(asked), "the hits, their order, their word starts and similarities do not change");
    }

    [Theory]
    [InlineData(LuiRankingOrder.SimilarityOnly)]
    [InlineData(LuiRankingOrder.WordStartThenSimilarity)]
    [InlineData(LuiRankingOrder.WordStartThenWeight)]
    public void A_ranking_that_measures_a_similarity_measures_it_from_the_word_start_with_or_without_the_typo_tolerance(LuiRankingOrder rank)
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = Names(catalog);

        // the match stands in a later word: measured from the word start and measured from the start of the text differ
        foreach (var input in new[] { "must", "mus", "pet", "mayer" })
        {
            var without = index.Search(input, new LuiQueryOptions { Rank = rank, TypoTolerance = false, Candidates = 40 });
            var with = index.Search(input, new LuiQueryOptions { Rank = rank, TypoTolerance = true, Candidates = 40 });

            without.Hits.ShouldNotBeEmpty(input);
            Describe(without).ShouldBe(Describe(with), $"{rank} '{input}': the similarity is measured from the word start, whether or not a typo correction may ask for it");
        }
    }

    private static string Describe(LuiResult result) =>
        string.Join('|', result.Hits.Select(hit => string.Create(CultureInfo.InvariantCulture, $"{hit.Key}={hit.Text} w{hit.Weight} s{hit.AtWordStart} {hit.Similarity:F4}")));

    private static int SearchForms(Action query)
    {
        var before = Normalizer.SearchFormsWritten;
        query();
        return Normalizer.SearchFormsWritten - before;
    }

    private static ILuiEmbeddedIndex Names(LuiCatalog catalog)
    {
        var index = catalog.Create("names", builder => builder.MultiWord().TypoTolerance());
        string[] firstNames = ["Max", "Anna", "Hans-Peter", "Ärger", "Straße", "Maria", "Jan"];
        string[] lastNames = ["Mustermann", "Muster", "Mustafa", "Maier", "Mayer", "Petersen", "Peters"];
        var entries = new List<LuiEntry>();
        var key = 1;
        foreach (var first in firstNames)
        {
            foreach (var last in lastNames)
            {
                entries.Add(new LuiEntry(key, $"{first} {last}") { Weight = (key * 5) % 31, Priority = key % 9 == 0 ? LuiPriority.High : LuiPriority.Normal });
                key++;
                entries.Add(new LuiEntry(key, $"{last}, {first} {last}") { Weight = (key * 5) % 31 });
                key++;
            }
        }

        index.UpsertMany(entries);
        return index;
    }

    private static ILuiEmbeddedIndex Entries(LuiCatalog catalog)
    {
        var index = catalog.Create("words", builder => builder.MultiWord().TypoTolerance());
        string[] words = ["apfel", "apfelkuchen", "apfelmus", "apfelsaft", "ananas", "aprikose", "avocado", "birne", "banane", "brot", "kuchen", "kaffee"];
        var entries = new List<LuiEntry>();
        var key = 1;
        for (var round = 0; round < 40; round++)
        {
            foreach (var word in words)
            {
                entries.Add(new LuiEntry(key, round == 0 ? word : $"{word} {round}") { Weight = (key * 7) % 53 });
                key++;
            }
        }

        index.UpsertMany(entries);
        return index;
    }
}
