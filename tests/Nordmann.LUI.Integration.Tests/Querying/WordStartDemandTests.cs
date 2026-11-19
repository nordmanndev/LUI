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
    public void The_search_index_leaves_the_word_start_to_the_hits_when_nothing_ranks_by_it_and_the_answer_is_the_same()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = Entries(catalog);

        foreach (var rank in new[] { LuiRankingOrder.WeightOnly, LuiRankingOrder.SimilarityOnly })
        {
            var cheap = default(LuiResult);
            var made = SearchForms(() => cheap = index.Search("apf", new LuiQueryOptions { Rank = rank, TypoTolerance = false }));

            // a correction may ask for the word start of every candidate: with the typo tolerance on, the word starts are made
            var asked = default(LuiResult);
            var madeForCorrection = SearchForms(() => asked = index.Search("apf", new LuiQueryOptions { Rank = rank, TypoTolerance = true }));

            made.ShouldBeLessThan(cheap!.CandidatesConsidered, $"{rank} does not sort by the word start: the candidates make none");
            madeForCorrection.ShouldBeGreaterThanOrEqualTo(asked!.CandidatesConsidered, "the typo tolerance may ask for the word start of every candidate");
            Describe(cheap).ShouldBe(Describe(asked), $"{rank}: the hits, their order, their word starts and similarities do not change");
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
