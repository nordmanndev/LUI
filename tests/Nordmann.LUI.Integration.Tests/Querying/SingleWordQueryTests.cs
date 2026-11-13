using Shouldly;

namespace Nordmann.LUI.Integration.Tests.Querying;

/// <summary>
/// The query of a single-word index: the prefix path, the trigram
/// candidates, the ranking (ADR-0008, ADR-0008, docs/api.md).
/// </summary>
public sealed class SingleWordQueryTests
{
    [Fact]
    public void A_three_character_input_finds_the_text_anywhere_in_the_word()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        tags.Upsert(1, "Apfelkuchen", 10);
        tags.Upsert(2, "Birne", 10);

        tags.Query("kuc").Execute().Hits.ShouldHaveSingleItem().Text.ShouldBe("Apfelkuchen");
    }

    [Fact]
    public void The_case_and_the_umlauts_of_the_input_do_not_matter()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        tags.Upsert(1, "Äpfel", 10);
        tags.Upsert(2, "Straße", 10);
        tags.Upsert(3, "Café", 10);

        foreach (var input in new[] { "äpfel", "ÄPFEL", "aepfel", "AEPFEL" })
        {
            tags.Query(input).Execute().Hits.ShouldHaveSingleItem().Key.ShouldBe(1);
        }

        foreach (var input in new[] { "strasse", "Straße", "STRASSE" })
        {
            tags.Query(input).Execute().Hits.ShouldHaveSingleItem().Key.ShouldBe(2);
        }

        foreach (var input in new[] { "cafe", "café", "CAFÉ" })
        {
            tags.Query(input).Execute().Hits.ShouldHaveSingleItem().Key.ShouldBe(3);
        }
    }

    [Fact]
    public void Turning_the_transliteration_off_keeps_the_umlaut_apart_from_its_spelling()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord().GermanTransliteration(false));
        tags.Upsert(1, "Müller", 10);

        tags.Query("mueller").Execute().Hits.ShouldBeEmpty();
        tags.Query("muller").Execute().Hits.ShouldHaveSingleItem().Key.ShouldBe(1);
    }

    [Fact]
    public void The_prefix_path_answers_one_and_two_characters_with_the_most_popular_first()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        tags.Upsert(1, "apfel", 5);
        tags.Upsert(2, "ananas", 50);
        tags.Upsert(3, "aprikose", 30);
        tags.Upsert(4, "banane", 99);
        tags.Upsert(5, "kuchen apfel", 99);

        tags.Query("a").Execute().Hits.Select(h => h.Text).ShouldBe(["ananas", "aprikose", "apfel"]);
        tags.Query("ap").Execute().Hits.Select(h => h.Text).ShouldBe(["aprikose", "apfel"]);
    }

    [Fact]
    public void The_prefix_path_finds_only_the_beginning_of_the_text()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        tags.Upsert(1, "kuchen apfel", 1);

        tags.Query("ap").Execute().Hits.ShouldBeEmpty();
    }

    [Fact]
    public void The_prefix_path_ignores_the_hash_of_a_hashtag_and_resolves_the_umlaut()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        tags.Upsert(1, "#Bahnhof", 10);
        tags.Upsert(2, "Äpfel", 10);

        tags.Query("b").Execute().Hits.ShouldHaveSingleItem().Key.ShouldBe(1);
        tags.Query("#b").Execute().Hits.ShouldHaveSingleItem().Key.ShouldBe(1);
        tags.Query("ä").Execute().Hits.ShouldHaveSingleItem().Key.ShouldBe(2);
        tags.Query("a").Execute().Hits.ShouldHaveSingleItem().Key.ShouldBe(2);
    }

    [Fact]
    public void Known_limitation_the_prefix_path_folds_ascii_only()
    {
        // ADR-0006: s_p1 and s_p2 use SQLite's lower(), which knows ASCII. German needs nothing
        // more (Ä is transliterated first); another accented capital is found from three characters on.
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        tags.Upsert(1, "Éclair", 10);

        tags.Query("e").Execute().Hits.ShouldBeEmpty();
        tags.Query("ecl").Execute().Hits.ShouldHaveSingleItem().Key.ShouldBe(1);
    }

    [Fact]
    public void A_hit_of_the_prefix_path_carries_a_similarity_and_the_word_start()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        tags.Upsert(1, "apfel", 10);

        var hit = tags.Query("ap").Execute().Hits.ShouldHaveSingleItem();

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
        var tags = catalog.Create("tags", index => index.SingleWord());
        tags.Upsert(1, "apfel", 10);

        var result = tags.Query(input).Execute();

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
        var tags = catalog.Create("tags", index => index.SingleWord());
        tags.Upsert(1, "apfelkuchen", 10);
        tags.Upsert(2, "birne", 10);

        Should.NotThrow(() => tags.Query(input).Execute());
        tags.Count().ShouldBe(2);
    }

    [Fact]
    public void A_quotation_mark_in_the_input_is_searched_as_text()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        tags.Upsert(1, "das \"beste\" rezept", 10);
        tags.Upsert(2, "das beste rezept", 10);

        tags.Query("\"beste\"").Execute().Hits.ShouldHaveSingleItem().Key.ShouldBe(1);
    }

    [Fact]
    public void An_input_that_is_found_in_the_middle_of_a_word_is_ranked_behind_the_word_starts()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        tags.Upsert(1, "bratapfelkuchen", 100_000);
        tags.Upsert(2, "apfelkuchen", 10);
        tags.Upsert(3, "gedeckter apfelkuchen", 20);

        var hits = tags.Query("apfelkuch").Execute().Hits;

        hits.Select(h => h.Text).Last().ShouldBe("bratapfelkuchen");
        hits.Select(h => h.AtWordStart).ShouldBe([true, true, false]);
    }

    [Fact]
    public void Among_the_word_starts_the_better_match_comes_first_and_the_weight_decides_ties()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        tags.Upsert(1, "apfelkuchenrezept", 1_000);
        tags.Upsert(2, "apfelkuchen", 10);
        tags.Upsert(3, "apfelkuchen mit sahne und zimt", 5_000);

        tags.Query("apfelkuch").Execute().Hits.Select(h => h.Key).ShouldBe([2, 1, 3]);
        tags.Query("apfelkuch").Rank(LuiRankingOrder.WordStartThenWeight).Execute().Hits.Select(h => h.Key).ShouldBe([3, 1, 2]);
    }

    [Fact]
    public void The_four_ranking_orders_differ_where_the_specification_says_they_do()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        tags.Upsert(1, "kuchen" + new string('x', 200), 5);  // word start, but nothing like the input
        tags.Upsert(2, "xkuchen", 100);                      // no word start, much like the input

        Keys(tags, "kuchen", LuiRankingOrder.WordStartThenSimilarity).ShouldBe([1, 2]);
        Keys(tags, "kuchen", LuiRankingOrder.WordStartThenWeight).ShouldBe([1, 2]);
        Keys(tags, "kuchen", LuiRankingOrder.SimilarityOnly).ShouldBe([2, 1]);
        Keys(tags, "kuchen", LuiRankingOrder.WeightOnly).ShouldBe([2, 1]);
    }

    [Fact]
    public void The_priority_comes_before_every_stage_unless_it_is_asked_to_only_make_a_candidate()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        tags.Upsert(1, "apfelkuchen", 100);
        tags.Upsert(2, "bratapfelkuchen", 0, priority: LuiPriority.System);

        tags.Query("apfelkuch").Execute().Hits.Select(h => h.Key).ShouldBe([2, 1]);
        tags.Query("apfelkuch").PriorityMode(LuiPriorityMode.CandidatesOnly).Execute().Hits.Select(h => h.Key).ShouldBe([1, 2]);
    }

    [Fact]
    public void The_priority_comes_first_in_weight_order_too_unless_it_is_asked_not_to()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        tags.Upsert(1, "kuchen a", 100);
        tags.Upsert(2, "kuchen b", 1, priority: LuiPriority.High);

        Keys(tags, "kuchen", LuiRankingOrder.WeightOnly).ShouldBe([2, 1]);
        tags.Query("kuchen").Rank(LuiRankingOrder.WeightOnly).PriorityMode(LuiPriorityMode.CandidatesOnly).Execute()
            .Hits.Select(h => h.Key).ShouldBe([1, 2]);
    }

    [Fact]
    public void The_priority_leads_the_hits_even_when_a_text_is_written_in_two_spellings()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        tags.Upsert(1, "Vegan", 900_000);
        tags.Upsert(2, "vegan", 5, priority: LuiPriority.System);
        tags.Upsert(3, "veganer kuchen", 10);

        // the far greater weight of the first entry does not beat the priority of the second one, and the
        // two spellings are two hits — the data decides which of them an application keeps (ADR-0055)
        var hits = tags.Query("vegan").Execute().Hits;
        hits.Count.ShouldBe(3);
        hits.Select(h => h.Key).ShouldBe([2, 1, 3]);
        hits.Select(h => h.Text).ShouldBe(["vegan", "Vegan", "veganer kuchen"]);
    }

    [Fact]
    public void Take_limits_the_hits_and_Candidates_limits_what_is_ranked()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        for (var i = 1; i <= 30; i++)
        {
            tags.Upsert(i, $"kuchen{i:00}", i);
        }

        var all = tags.Query("kuchen").Take(100).Execute();
        all.Hits.Count.ShouldBe(30);
        all.CandidatesConsidered.ShouldBe(30);
        all.Truncated.ShouldBeFalse();

        var take = tags.Query("kuchen").Take(5).Execute();
        take.Hits.Count.ShouldBe(5);

        var truncated = tags.Query("kuchen").Candidates(10).Take(100).Execute();
        truncated.CandidatesConsidered.ShouldBe(10);
        truncated.Truncated.ShouldBeTrue();
        truncated.Hits.Select(h => h.Weight).ShouldBe(Enumerable.Range(21, 10).Reverse().OrderByDescending(x => x), "the candidates are the heaviest ones");
    }

    [Fact]
    public void The_prefix_path_truncates_like_the_trigram_path()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        for (var i = 1; i <= 30; i++)
        {
            tags.Upsert(i, $"kuchen{i:00}", i);
        }

        var result = tags.Query("ku").Candidates(10).Take(100).Execute();

        result.CandidatesConsidered.ShouldBe(10);
        result.Truncated.ShouldBeTrue();
        result.Hits.Select(h => h.Weight).ShouldBe(Enumerable.Range(21, 10).OrderByDescending(x => x));
    }

    [Fact]
    public void The_options_object_does_what_the_fluent_query_does()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        for (var i = 1; i <= 30; i++)
        {
            tags.Upsert(i, $"kuchen{i:00}", i);
        }

        var viaOptions = tags.Search("kuchen", new LuiQueryOptions { MaxResults = 7, Rank = LuiRankingOrder.WeightOnly });
        var fluent = tags.Query("kuchen").Take(7).Rank(LuiRankingOrder.WeightOnly).Execute();

        viaOptions.Hits.Select(h => h.Key).ShouldBe(fluent.Hits.Select(h => h.Key));
        viaOptions.Hits.Count.ShouldBe(7);
    }

    [Fact]
    public void A_similarity_measure_can_be_chosen_per_query()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        tags.Upsert(1, "apfelkuchen", 10);

        var jaro = tags.Query("apfelkuchen").Execute().Hits[0].Similarity;
        var dice = tags.Query("apfelkuch").Similarity(LuiSimilarity.NgramDice).Execute().Hits[0].Similarity;
        var levenshtein = tags.Query("apfelkuch").Similarity(LuiSimilarity.Levenshtein).Execute().Hits[0].Similarity;

        jaro.ShouldBe(1.0, 1e-9);
        dice.ShouldBeInRange(0.8, 1.0);
        levenshtein.ShouldBeInRange(0.7, 1.0);
    }

    [Fact]
    public void The_default_similarity_of_the_index_can_be_replaced()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord().Similarity(new ConstantSimilarity(0.25)));
        tags.Upsert(1, "apfelkuchen", 10);

        tags.Query("apfel").Execute().Hits.ShouldHaveSingleItem().Similarity.ShouldBe(0.25);
    }

    [Fact]
    public void Timings_are_measured_only_when_asked_for()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        tags.Upsert(1, "apfelkuchen", 10);

        tags.Query("apfel").Execute().Timings.ShouldBeNull();

        var timings = tags.Query("apfel").TrackTimings().Execute().Timings.ShouldNotBeNull();
        timings.Total.ShouldBeGreaterThan(TimeSpan.Zero);
        (timings.CandidateRead + timings.Ranking + timings.Payload).ShouldBeLessThanOrEqualTo(timings.Total);
    }

    [Fact]
    public void A_cancelled_query_throws()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        tags.Upsert(1, "apfelkuchen", 10);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Should.Throw<OperationCanceledException>(() => tags.Query("apfel").Execute(cancelled.Token));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1001)]
    public void Take_outside_its_range_is_refused(int take)
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());

        Should.Throw<ArgumentOutOfRangeException>(() => tags.Query("apfel").Take(take).Execute());
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
        var tags = catalog.Create("tags", index => index.SingleWord());

        Should.Throw<ArgumentOutOfRangeException>(() => tags.Query("apfel").Candidates(candidates).Execute());
    }

    [Fact]
    public void The_result_says_what_was_asked()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());

        var result = tags.Query("Äpfel").Execute();

        result.Query.ShouldBe("Äpfel");
        result.CorrectedQuery.ShouldBeNull();
    }

    // review of pull request 6, B2: one snapshot for the candidates and the hits
    [Fact]
    public void The_hits_are_read_in_the_snapshot_of_the_candidates()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        tags.Upsert(new(1, "kirsche kuchen") { Weight = 10, Payload = new Chip("old1") });
        tags.Upsert(new(2, "zitrone kuchen") { Weight = 5, Payload = new Chip("old2") });

        // between the candidates and the hits, another writer removes the highest document and
        // adds a new one: SQLite hands the freed doc_id to the newcomer
        var interference = new OnFirstCall(() =>
        {
            tags.Remove(2);
            tags.Upsert(new(3, "himbeer kuchen") { Weight = 5, Payload = new Chip("new3") });
        });

        var hits = tags.Query("kuchen").Similarity(interference).Execute().Hits;

        interference.Called.ShouldBeTrue();
        foreach (var hit in hits)
        {
            (hit.Text, hit.Key.ToInt64()).ShouldBeOneOf(("kirsche kuchen", 1L), ("zitrone kuchen", 2L));
            hit.GetPayload<Chip>()!.Slug.ShouldBe(hit.Key == 1 ? "old1" : "old2");
        }
    }

    // review of pull request 6, B3: the hash
    [Theory]
    [InlineData("a")]
    [InlineData("ap")]
    [InlineData("apf")]
    [InlineData("apfe")]
    public void A_leading_hash_of_the_input_does_not_change_the_answer(string typed)
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        tags.Upsert(1, "apfel", 10);
        tags.Upsert(2, "#apfelkuchen", 20);
        tags.Upsert(3, "birne", 30);

        var without = tags.Query(typed).Rank(LuiRankingOrder.WeightOnly).Execute().Hits.Select(h => h.Key).ToArray();
        var with = tags.Query("#" + typed).Rank(LuiRankingOrder.WeightOnly).Execute().Hits.Select(h => h.Key).ToArray();

        without.ShouldBe([2, 1]);
        with.ShouldBe(without);
    }

    // review of pull request 6, B4: code points, not UTF-16 units
    [Fact]
    public void An_input_is_measured_in_code_points()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        tags.Upsert(1, "😀party", 5);
        tags.Upsert(2, "😀smile", 3);

        tags.Query("😀").Rank(LuiRankingOrder.WeightOnly).Execute().Hits.Select(h => h.Key).ShouldBe([1, 2]);
        tags.Query("😀p").Execute().Hits.ShouldHaveSingleItem().Key.ShouldBe(1);
        tags.Query("😀pa").Execute().Hits.ShouldHaveSingleItem().Key.ShouldBe(1);
    }

    // review of pull request 6, P6: cancellation while ranking
    [Fact]
    public void A_query_can_be_cancelled_while_it_is_being_ranked()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        using (var connection = database.Connect())
        {
            using var transaction = connection.BeginTransaction();
            for (var i = 0; i < 2_000; i++)
            {
                tags.Upsert(i, $"kuchen{i:0000}", i, transaction: transaction);
            }

            transaction.Commit();
        }

        using var cancellation = new CancellationTokenSource();
        var similarity = new OnFirstCall(cancellation.Cancel);

        Should.Throw<OperationCanceledException>(
            () => tags.Query("kuchen").Candidates(2_000).Take(5).Similarity(similarity).Execute(cancellation.Token));
    }

    // the shape of the API before 0.1.0 (ADR-0013), the options
    [Fact]
    public void A_query_option_out_of_range_fails_where_it_is_set()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        var query = tags.Query("vegan");

        // the builder throws at the call, not later in Execute()
        Should.Throw<ArgumentOutOfRangeException>(() => query.Take(0)).Message.ShouldContain("MaxResults");
        Should.Throw<ArgumentOutOfRangeException>(() => query.Candidates(100_001)).Message.ShouldContain("Candidates");
        Should.Throw<ArgumentOutOfRangeException>(() => query.Rank((LuiRankingOrder)42)).Message.ShouldContain("Rank");
        Should.Throw<ArgumentOutOfRangeException>(() => new LuiQueryOptions { PriorityMode = (LuiPriorityMode)42 })
            .Message.ShouldContain("PriorityMode");
    }

    // the shape of the API before 0.1.0 (ADR-0013), the builder and the interfaces
    [Fact]
    public void A_search_without_options_takes_the_defaults()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        tags.UpsertMany(Enumerable.Range(1, 30).Select(i => new LuiEntry(i, $"kuchen{i}") { Weight = i }));

        var result = tags.Search("kuchen");

        result.Hits.Count.ShouldBe(new LuiQueryOptions().MaxResults);
        result.Timings.ShouldBeNull();
    }

    // the shape of the API before 0.1.0 (ADR-0013), the builder and the interfaces
    [Fact]
    public void A_reader_needs_only_the_read_interface()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        tags.Upsert(1, "vegan", 5);

        ILuiIndex reader = tags;

        reader.Search("vegan").Hits.ShouldHaveSingleItem().Key.ShouldBe(1);
        reader.Count().ShouldBe(1);
    }

    private static long[] Keys(ILuiEmbeddedIndex index, string input, LuiRankingOrder order) =>
        [.. index.Query(input).Rank(order).Execute().Hits.Select(h => h.Key.ToInt64())];

    private sealed class ConstantSimilarity(double value) : ILuiSimilarity
    {
        public double Similarity(string query, string text) => value;
    }

    private sealed class OnFirstCall(Action action) : ILuiSimilarity
    {
        private int _calls;

        internal bool Called => Volatile.Read(ref _calls) > 0;

        public double Similarity(string query, string text)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                action();
            }

            return 0.5;
        }
    }
}
