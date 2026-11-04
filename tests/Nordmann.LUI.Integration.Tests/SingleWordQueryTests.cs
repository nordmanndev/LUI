using Shouldly;

namespace Nordmann.LUI.Integration.Tests;

/// <summary>
/// The query of a single-word index: the prefix path, the trigram
/// candidates, the ranking (LUI-Spezifikation §8, §9, §12.5).
/// </summary>
public sealed class SingleWordQueryTests
{
    [Fact]
    public void A_three_character_input_finds_the_text_anywhere_in_the_word()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("tags").SingleWord().Ensure();
        index.Upsert(1, "Apfelkuchen", 10);
        index.Upsert(2, "Birne", 10);

        index.Query("kuc").Execute().Hits.ShouldHaveSingleItem().Text.ShouldBe("Apfelkuchen");
    }

    [Fact]
    public void The_case_and_the_umlauts_of_the_input_do_not_matter()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("tags").SingleWord().Ensure();
        index.Upsert(1, "Äpfel", 10);
        index.Upsert(2, "Straße", 10);
        index.Upsert(3, "Café", 10);

        foreach (var input in new[] { "äpfel", "ÄPFEL", "aepfel", "AEPFEL" })
        {
            index.Query(input).Execute().Hits.ShouldHaveSingleItem().Key.ShouldBe(1);
        }

        foreach (var input in new[] { "strasse", "Straße", "STRASSE" })
        {
            index.Query(input).Execute().Hits.ShouldHaveSingleItem().Key.ShouldBe(2);
        }

        foreach (var input in new[] { "cafe", "café", "CAFÉ" })
        {
            index.Query(input).Execute().Hits.ShouldHaveSingleItem().Key.ShouldBe(3);
        }
    }

    [Fact]
    public void Turning_the_transliteration_off_keeps_the_umlaut_apart_from_its_spelling()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("tags").SingleWord().GermanTransliteration(false).Ensure();
        index.Upsert(1, "Müller", 10);

        index.Query("mueller").Execute().Hits.ShouldBeEmpty();
        index.Query("muller").Execute().Hits.ShouldHaveSingleItem().Key.ShouldBe(1);
    }

    [Fact]
    public void The_prefix_path_answers_one_and_two_characters_with_the_most_popular_first()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("tags").SingleWord().Ensure();
        index.Upsert(1, "apfel", 5);
        index.Upsert(2, "ananas", 50);
        index.Upsert(3, "aprikose", 30);
        index.Upsert(4, "banane", 99);
        index.Upsert(5, "kuchen apfel", 99);

        index.Query("a").Execute().Hits.Select(h => h.Text).ShouldBe(["ananas", "aprikose", "apfel"]);
        index.Query("ap").Execute().Hits.Select(h => h.Text).ShouldBe(["aprikose", "apfel"]);
    }

    [Fact]
    public void The_prefix_path_finds_only_the_beginning_of_the_text()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("tags").SingleWord().Ensure();
        index.Upsert(1, "kuchen apfel", 1);

        index.Query("ap").Execute().Hits.ShouldBeEmpty();
    }

    [Fact]
    public void The_prefix_path_ignores_the_hash_of_a_hashtag_and_resolves_the_umlaut()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("tags").SingleWord().Ensure();
        index.Upsert(1, "#Bahnhof", 10);
        index.Upsert(2, "Äpfel", 10);

        index.Query("b").Execute().Hits.ShouldHaveSingleItem().Key.ShouldBe(1);
        index.Query("#b").Execute().Hits.ShouldHaveSingleItem().Key.ShouldBe(1);
        index.Query("ä").Execute().Hits.ShouldHaveSingleItem().Key.ShouldBe(2);
        index.Query("a").Execute().Hits.ShouldHaveSingleItem().Key.ShouldBe(2);
    }

    [Fact]
    public void Known_limitation_the_prefix_path_folds_ascii_only()
    {
        // ADR-0006: s_p1 and s_p2 use SQLite's lower(), which knows ASCII. German needs nothing
        // more (Ä is transliterated first); another accented capital is found from three characters on.
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("tags").SingleWord().Ensure();
        index.Upsert(1, "Éclair", 10);

        index.Query("e").Execute().Hits.ShouldBeEmpty();
        index.Query("ecl").Execute().Hits.ShouldHaveSingleItem().Key.ShouldBe(1);
    }

    [Fact]
    public void A_hit_of_the_prefix_path_carries_a_similarity_and_the_word_start()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("tags").SingleWord().Ensure();
        index.Upsert(1, "apfel", 10);

        var hit = index.Query("ap").Execute().Hits.ShouldHaveSingleItem();

        hit.AtWordStart.ShouldBeTrue();
        hit.Similarity.ShouldBeGreaterThan(0.8);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#")]
    public void An_input_without_content_finds_nothing(string input)
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("tags").SingleWord().Ensure();
        index.Upsert(1, "apfel", 10);

        var result = index.Query(input).Execute();

        result.Hits.ShouldBeEmpty();
        result.CandidatesConsidered.ShouldBe(0);
        result.Truncated.ShouldBeFalse();
    }

    [Theory]
    [InlineData("\"")]
    [InlineData("apfel\"kuchen")]
    [InlineData("\"apfel\" OR \"birne\"")]
    [InlineData("apfel*")]
    [InlineData("NEAR(apfel kuchen)")]
    [InlineData("s_norm : apfel")]
    [InlineData("apfel; DROP TABLE lui_tags; --")]
    [InlineData("a\0pfel")]
    [InlineData("(((")]
    public void The_input_is_never_copied_into_the_expression_raw(string input)
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("tags").SingleWord().Ensure();
        index.Upsert(1, "apfelkuchen", 10);
        index.Upsert(2, "birne", 10);

        Should.NotThrow(() => index.Query(input).Execute());
        index.Count().ShouldBe(2);
    }

    [Fact]
    public void A_quotation_mark_in_the_input_is_searched_as_text()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("tags").SingleWord().Ensure();
        index.Upsert(1, "das \"beste\" rezept", 10);
        index.Upsert(2, "das beste rezept", 10);

        index.Query("\"beste\"").Execute().Hits.ShouldHaveSingleItem().Key.ShouldBe(1);
    }

    [Fact]
    public void An_input_that_is_found_in_the_middle_of_a_word_is_ranked_behind_the_word_starts()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("tags").SingleWord().Ensure();
        index.Upsert(1, "bratapfelkuchen", 100_000);
        index.Upsert(2, "apfelkuchen", 10);
        index.Upsert(3, "gedeckter apfelkuchen", 20);

        var hits = index.Query("apfelkuch").Execute().Hits;

        hits.Select(h => h.Text).Last().ShouldBe("bratapfelkuchen");
        hits.Select(h => h.AtWordStart).ShouldBe([true, true, false]);
    }

    [Fact]
    public void Among_the_word_starts_the_better_match_comes_first_and_the_weight_decides_ties()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("tags").SingleWord().Ensure();
        index.Upsert(1, "apfelkuchenrezept", 1_000);
        index.Upsert(2, "apfelkuchen", 10);
        index.Upsert(3, "apfelkuchen mit sahne und zimt", 5_000);

        index.Query("apfelkuch").Execute().Hits.Select(h => h.Key).ShouldBe([2, 1, 3]);
        index.Query("apfelkuch").Rank(LuiRankingOrder.WordStartThenWeight).Execute().Hits.Select(h => h.Key).ShouldBe([3, 1, 2]);
    }

    [Fact]
    public void The_four_ranking_orders_differ_where_the_specification_says_they_do()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("tags").SingleWord().Ensure();
        index.Upsert(1, "kuchen" + new string('x', 200), 5);  // word start, but nothing like the input
        index.Upsert(2, "xkuchen", 100);                      // no word start, much like the input

        Keys(index, "kuchen", LuiRankingOrder.WordStartThenSimilarity).ShouldBe([1, 2]);
        Keys(index, "kuchen", LuiRankingOrder.WordStartThenWeight).ShouldBe([1, 2]);
        Keys(index, "kuchen", LuiRankingOrder.SimilarityOnly).ShouldBe([2, 1]);
        Keys(index, "kuchen", LuiRankingOrder.WeightOnly).ShouldBe([2, 1]);
    }

    [Fact]
    public void The_priority_comes_before_every_stage_unless_it_is_asked_to_only_make_a_candidate()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("tags").SingleWord().Ensure();
        index.Upsert(1, "apfelkuchen", 100);
        index.Upsert(2, "bratapfelkuchen", 0, priority: LuiPriority.System);

        index.Query("apfelkuch").Execute().Hits.Select(h => h.Key).ShouldBe([2, 1]);
        index.Query("apfelkuch").Priorities(LuiPriorityRanking.CandidatesOnly).Execute().Hits.Select(h => h.Key).ShouldBe([1, 2]);
    }

    [Fact]
    public void The_priority_comes_first_in_weight_order_too_unless_it_is_asked_not_to()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("tags").SingleWord().Ensure();
        index.Upsert(1, "kuchen a", 100);
        index.Upsert(2, "kuchen b", 1, priority: LuiPriority.High);

        Keys(index, "kuchen", LuiRankingOrder.WeightOnly).ShouldBe([2, 1]);
        index.Query("kuchen").Rank(LuiRankingOrder.WeightOnly).Priorities(LuiPriorityRanking.CandidatesOnly).Execute()
            .Hits.Select(h => h.Key).ShouldBe([1, 2]);
    }

    [Fact]
    public void DistinctText_keeps_the_entry_of_the_highest_priority()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("tags").SingleWord().Ensure();
        index.Upsert(1, "Vegan", 900_000);
        index.Upsert(2, "vegan", 5, priority: LuiPriority.System);
        index.Upsert(3, "veganer kuchen", 10);

        index.Query("vegan").Execute().Hits.Count.ShouldBe(3);

        var distinct = index.Query("vegan").DistinctText().Execute().Hits;
        distinct.Count.ShouldBe(2);
        distinct.Select(h => h.Key).ShouldContain(2L);
        distinct.Select(h => h.Key).ShouldNotContain(1L);
    }

    [Fact]
    public void Take_limits_the_hits_and_Candidates_limits_what_is_ranked()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("tags").SingleWord().Ensure();
        for (var i = 1; i <= 30; i++)
        {
            index.Upsert(i, $"kuchen{i:00}", i);
        }

        var all = index.Query("kuchen").Take(100).Execute();
        all.Hits.Count.ShouldBe(30);
        all.CandidatesConsidered.ShouldBe(30);
        all.Truncated.ShouldBeFalse();

        var take = index.Query("kuchen").Take(5).Execute();
        take.Hits.Count.ShouldBe(5);

        var truncated = index.Query("kuchen").Candidates(10).Take(100).Execute();
        truncated.CandidatesConsidered.ShouldBe(10);
        truncated.Truncated.ShouldBeTrue();
        truncated.Hits.Select(h => h.Weight).ShouldBe(Enumerable.Range(21, 10).Reverse().OrderByDescending(x => x), "the candidates are the heaviest ones");
    }

    [Fact]
    public void The_prefix_path_truncates_like_the_trigram_path()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("tags").SingleWord().Ensure();
        for (var i = 1; i <= 30; i++)
        {
            index.Upsert(i, $"kuchen{i:00}", i);
        }

        var result = index.Query("ku").Candidates(10).Take(100).Execute();

        result.CandidatesConsidered.ShouldBe(10);
        result.Truncated.ShouldBeTrue();
        result.Hits.Select(h => h.Weight).ShouldBe(Enumerable.Range(21, 10).OrderByDescending(x => x));
    }

    [Fact]
    public void The_options_object_does_what_the_fluent_query_does()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("tags").SingleWord().Ensure();
        for (var i = 1; i <= 30; i++)
        {
            index.Upsert(i, $"kuchen{i:00}", i);
        }

        var viaOptions = index.Search("kuchen", new LuiQueryOptions { MaxResults = 7, Rank = LuiRankingOrder.WeightOnly });
        var fluent = index.Query("kuchen").Take(7).Rank(LuiRankingOrder.WeightOnly).Execute();

        viaOptions.Hits.Select(h => h.Key).ShouldBe(fluent.Hits.Select(h => h.Key));
        viaOptions.Hits.Count.ShouldBe(7);
    }

    [Fact]
    public void A_similarity_measure_can_be_chosen_per_query()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("tags").SingleWord().Ensure();
        index.Upsert(1, "apfelkuchen", 10);

        var jaro = index.Query("apfelkuchen").Execute().Hits[0].Similarity;
        var dice = index.Query("apfelkuch").Similarity(LuiSimilarity.NgramDice).Execute().Hits[0].Similarity;
        var levenshtein = index.Query("apfelkuch").Similarity(LuiSimilarity.Levenshtein).Execute().Hits[0].Similarity;

        jaro.ShouldBe(1.0, 1e-9);
        dice.ShouldBeInRange(0.8, 1.0);
        levenshtein.ShouldBeInRange(0.7, 1.0);
    }

    [Fact]
    public void The_default_similarity_of_the_index_can_be_replaced()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("tags").SingleWord()
            .Similarity(new ConstantSimilarity(0.25)).Ensure();
        index.Upsert(1, "apfelkuchen", 10);

        index.Query("apfel").Execute().Hits.ShouldHaveSingleItem().Similarity.ShouldBe(0.25);
    }

    [Fact]
    public void Timings_are_measured_only_when_asked_for()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("tags").SingleWord().Ensure();
        index.Upsert(1, "apfelkuchen", 10);

        index.Query("apfel").Execute().Timings.ShouldBeNull();

        var timings = index.Query("apfel").TrackTimings().Execute().Timings.ShouldNotBeNull();
        timings.Total.ShouldBeGreaterThan(TimeSpan.Zero);
        (timings.Candidates + timings.Ranking + timings.Payload).ShouldBeLessThanOrEqualTo(timings.Total);
    }

    [Fact]
    public void A_cancelled_query_throws()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("tags").SingleWord().Ensure();
        index.Upsert(1, "apfelkuchen", 10);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Should.Throw<OperationCanceledException>(() => index.Query("apfel").Execute(cancelled.Token));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1001)]
    public void Take_outside_its_range_is_refused(int take)
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("tags").SingleWord().Ensure();

        Should.Throw<ArgumentOutOfRangeException>(() => index.Query("apfel").Take(take).Execute());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(100_001)]
    [InlineData(int.MaxValue)]
    public void Candidates_outside_their_range_are_refused(int candidates)
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("tags").SingleWord().Ensure();

        Should.Throw<ArgumentOutOfRangeException>(() => index.Query("apfel").Candidates(candidates).Execute());
    }

    [Fact]
    public void The_result_says_what_was_asked()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("tags").SingleWord().Ensure();

        var result = index.Query("Äpfel").Execute();

        result.Query.ShouldBe("Äpfel");
        result.CorrectedQuery.ShouldBeNull();
    }

    private static long[] Keys(ILuiEmbeddedIndex<long, LuiNoPayload> index, string input, LuiRankingOrder order) =>
        [.. index.Query(input).Rank(order).Execute().Hits.Select(h => h.Key)];

    private sealed class ConstantSimilarity(double value) : ILuiSimilarity
    {
        public string Name => "Constant";

        public double Similarity(string query, string text) => value;
    }
}
