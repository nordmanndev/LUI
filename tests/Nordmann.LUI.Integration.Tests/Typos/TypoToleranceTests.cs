using Shouldly;

namespace Nordmann.LUI.Integration.Tests.Typos;

/// <summary>
/// Typo tolerance (LUI-Spezifikation §10, Anhang B): a typo must not lead nowhere. SymSpell over the word
/// beginnings of an index; corrections when no candidate stands at a word start.
/// </summary>
public sealed class TypoToleranceTests
{
    [Fact]
    public void A_typo_in_a_word_finds_the_title_and_says_what_was_searched()
    {
        using var database = new TestDatabase();
        using var recipes = Recipes(database);

        var result = recipes.Index.Query("apflekuchen").Execute();

        result.CorrectedQuery.ShouldBe("apfelkuchen");
        result.Hits.Select(h => h.Text).Take(3).ShouldBe(
            ["Apfelkuchen", "Gedeckter Apfelkuchen", "Apfel Kuchen"],
            ignoreOrder: true);
        result.Hits[0].AtWordStart.ShouldBeTrue("the ranking ran with the corrected input");
    }

    [Theory]
    [InlineData("apfelkuch")]
    [InlineData("apfel kuchen")]
    [InlineData("kuchen")]
    [InlineData("gedeckter apfel")]
    public void An_input_without_a_typo_is_not_corrected(string typed)
    {
        using var database = new TestDatabase();
        using var recipes = Recipes(database);

        recipes.Index.Query(typed).Execute().CorrectedQuery.ShouldBeNull();
    }

    [Fact]
    public void A_typo_in_one_word_of_several_is_corrected_and_the_other_words_stay()
    {
        using var database = new TestDatabase();
        using var recipes = Recipes(database);

        var result = recipes.Index.Query("apflekuchen sahne").Execute();

        result.CorrectedQuery.ShouldBe("apfelkuchen sahne");
        result.Hits.ShouldHaveSingleItem().Text.ShouldBe("Apfelkuchen mit Sahne");
    }

    [Theory]
    [InlineData("apflekuc", "apfelkuc")]    // two letters swapped
    [InlineData("apelkuc", "apfelkuc")]     // a letter left out
    [InlineData("apfelkuuc", "apfelkuc")]   // a letter too many
    [InlineData("aqfelkuc", "apfelkuc")]    // a letter wrong
    [InlineData("apfelkcuhen", "apfelkuchen")]
    public void Every_kind_of_typo_of_one_edit_is_found(string typed, string expected)
    {
        using var database = new TestDatabase();
        using var recipes = Recipes(database);

        var result = recipes.Index.Query(typed).Execute();

        result.CorrectedQuery.ShouldBe(expected);
        result.Hits.Select(h => h.Text).Contains("Apfelkuchen", StringComparer.Ordinal).ShouldBeTrue("for '" + typed + "'");
    }

    [Theory]
    [InlineData("apfelkuchenn")]
    [InlineData("apfelkuchem")]
    public void A_typo_after_the_tenth_letter_is_not_corrected(string typed)
    {
        // the dictionary holds the beginnings of 3 … 10 letters (§10.1); a longer word is looked up by its first ten
        using var database = new TestDatabase();
        using var recipes = Recipes(database);

        var result = recipes.Index.Query(typed).Execute();

        result.CorrectedQuery.ShouldBeNull();
        result.Hits.ShouldBeEmpty();
    }

    [Fact]
    public void The_german_spelling_without_the_e_is_a_typo_of_one_edit()
    {
        // §6.3: „muller" is not „mueller" in the index, and one edit away from it
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var people = catalog.Create("people", index => index.MultiWord().TypoTolerance());
        people.Upsert(1, "Anna Müller", 10);
        people.Upsert(2, "Hans Schmidt", 20);

        // the application asks for the build (ADR-0042): a query never does
        people.WarmUpTypoDictionary();

        var result = people.Query("muller").Execute();

        result.CorrectedQuery.ShouldBe("mueller");
        result.Hits.ShouldHaveSingleItem().Key.ShouldBe(1);
    }

    [Fact]
    public void The_trigger_is_no_word_start_not_no_hits()
    {
        // „apflekuchen" has a hit in the middle of another word — 34 % of the typos do (§10.2)
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var words = catalog.Create("words", index => index.SingleWord().TypoTolerance());
        words.Upsert(1, "Apfelkuchen", 10);
        words.Upsert(2, "Teigapflekuchen", 20);

        // the application asks for the build (ADR-0042): a query never does
        words.WarmUpTypoDictionary();

        var result = words.Query("apflekuchen").Execute();

        result.CorrectedQuery.ShouldBe("apfelkuchen");
        result.Hits.Select(h => h.Text).Contains("Apfelkuchen", StringComparer.Ordinal).ShouldBeTrue();
        result.Hits.Select(h => h.Text).Contains("Teigapflekuchen", StringComparer.Ordinal).ShouldBeTrue("the candidates of the original input stay");
        result.Hits[0].Text.ShouldBe("Apfelkuchen", "the word start of the corrected input comes first");
    }

    [Fact]
    public void Typo_tolerance_is_off_unless_the_index_or_the_query_asks_for_it()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var words = catalog.Create("words", index => index.SingleWord());
        words.Upsert(1, "Apfelkuchen", 10);

        // the application asks for the build (ADR-0042): a query never does — the query asks for the
        // correction, the application pays for the dictionary
        words.WarmUpTypoDictionary();

        var off = words.Query("apflekuchen").Execute();
        off.Hits.ShouldBeEmpty();
        off.CorrectedQuery.ShouldBeNull();

        var on = words.Query("apflekuchen").TypoTolerance().Execute();
        on.CorrectedQuery.ShouldBe("apfelkuchen");
        on.Hits.ShouldHaveSingleItem().Key.ShouldBe(1);
    }

    [Fact]
    public void A_query_can_turn_the_tolerance_of_the_index_off()
    {
        using var database = new TestDatabase();
        using var recipes = Recipes(database);

        var result = recipes.Index.Query("apflekuchen").TypoTolerance(false).Execute();

        result.Hits.ShouldBeEmpty();
        result.CorrectedQuery.ShouldBeNull();
    }

    [Fact]
    public void The_options_object_takes_the_setting_too()
    {
        using var database = new TestDatabase();
        using var recipes = Recipes(database);

        recipes.Index.Search("apflekuchen", new LuiQueryOptions { TypoTolerance = false }).Hits.ShouldBeEmpty();
        recipes.Index.Search("apflekuchen", new LuiQueryOptions { TypoTolerance = true }).CorrectedQuery.ShouldBe("apfelkuchen");
        recipes.Index.Search("apflekuchen", new LuiQueryOptions()).CorrectedQuery.ShouldBe("apfelkuchen", "null: as the index says");
    }

    [Theory]
    [InlineData("a")]
    [InlineData("ap")]
    [InlineData("xy")]
    public void One_and_two_letters_are_never_corrected(string typed)
    {
        using var database = new TestDatabase();
        using var recipes = Recipes(database);

        recipes.Index.Query(typed).Execute().CorrectedQuery.ShouldBeNull();
    }

    [Fact]
    public void A_compound_typed_together_is_corrected_only_if_such_a_word_exists()
    {
        // the dictionary holds the beginnings of the *words* of the titles (§10.1); „Apfel Kuchen" has the words
        // apfel and kuchen, and no word apfelkuchen
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var titles = catalog.Create("titles", index => index.MultiWord().TypoTolerance());
        titles.Upsert(1, "Apfel Kuchen", 10);

        // the application asks for the build (ADR-0042): a query never does
        titles.WarmUpTypoDictionary();

        var together = titles.Query("apflekuchen").Execute();
        together.Hits.ShouldBeEmpty();
        together.CorrectedQuery.ShouldBeNull();

        var apart = titles.Query("apfle kuchen").Execute();
        apart.CorrectedQuery.ShouldBe("apfel kuchen");
        apart.Hits.ShouldHaveSingleItem().Key.ShouldBe(1);
    }

    [Fact]
    public void A_word_with_nothing_near_it_is_left_alone()
    {
        using var database = new TestDatabase();
        using var recipes = Recipes(database);

        var result = recipes.Index.Query("qqqqqqqq").Execute();

        result.Hits.ShouldBeEmpty();
        result.CorrectedQuery.ShouldBeNull();
    }

    [Fact]
    public void A_word_longer_than_ten_letters_is_corrected_by_its_first_ten()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var words = catalog.Create("words", index => index.SingleWord().TypoTolerance());
        words.Upsert(1, "Donaudampfschifffahrtsgesellschaft", 10);

        // the application asks for the build (ADR-0042): a query never does
        words.WarmUpTypoDictionary();

        var result = words.Query("donaudampgschifffahrtsgesellschaft").Execute();

        result.CorrectedQuery.ShouldBe("donaudampfschifffahrtsgesellschaft");
        result.Hits.ShouldHaveSingleItem().Key.ShouldBe(1);
    }

    [Fact]
    public void Upsert_adds_the_new_word_beginnings()
    {
        using var database = new TestDatabase();
        using var recipes = Recipes(database);
        recipes.Index.Query("apflekuchen").Execute();   // the dictionary is built (the helper warms it up)

        recipes.Index.Upsert(20, "Himbeere", 50);

        var result = recipes.Index.Query("himbere").Execute();
        result.CorrectedQuery.ShouldNotBeNull();
        result.Hits.ShouldHaveSingleItem().Key.ShouldBe(20);
    }

    [Fact]
    public void A_word_that_is_removed_is_still_a_correction_that_finds_nothing_until_the_dictionary_is_rebuilt()
    {
        using var database = new TestDatabase();
        using var recipes = Recipes(database);
        recipes.Index.Upsert(20, "Himbeere", 50);
        recipes.Index.Query("himbere").Execute().Hits.ShouldHaveSingleItem();

        recipes.Index.Remove(20);

        var before = recipes.Index.Query("himbere").Execute();
        before.Hits.ShouldBeEmpty("the beginnings stay (§10.1), the correction finds nothing");

        recipes.Index.RebuildTypoDictionary();

        var after = recipes.Index.Query("himbere").Execute();
        after.Hits.ShouldBeEmpty();
        after.CorrectedQuery.ShouldBeNull("the removed word is no longer in the dictionary");
    }

    [Fact]
    public void The_last_switch_of_the_tolerance_wins()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var words = catalog.Create("words", index => index.SingleWord().TypoTolerance().TypoTolerance(false));
        words.Upsert(1, "Apfelkuchen", 10);

        words.Query("apflekuchen").Execute().CorrectedQuery.ShouldBeNull();
    }

    [Fact]
    public void The_tolerance_is_a_default_of_the_instance_and_not_part_of_the_definition()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create("words", index => index.SingleWord().TypoTolerance());

        Should.NotThrow(() => catalog.Create("words", index => index.SingleWord()));
    }

    [Fact]
    public void The_priority_and_the_ranking_options_work_with_the_corrected_input()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord().TypoTolerance());
        tags.Upsert(1, "vegan", 0, priority: LuiPriority.System);
        tags.Upsert(2, "veganer kuchen", 900);

        // the application asks for the build (ADR-0042): a query never does
        tags.WarmUpTypoDictionary();

        var result = tags.Query("vegna").Execute();

        result.CorrectedQuery.ShouldNotBeNull();
        result.Hits[0].Key.ShouldBe(1, "the system tag stays first");
    }

    [Fact]
    public void Timings_show_the_time_of_the_correction()
    {
        using var database = new TestDatabase();
        using var recipes = Recipes(database);

        var corrected = recipes.Index.Query("apflekuchen").TrackTimings().Execute();
        var plain = recipes.Index.Query("apfelkuchen").TrackTimings().Execute();

        corrected.Timings!.Correction.ShouldBeGreaterThan(TimeSpan.Zero);
        plain.Timings!.Correction.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public async Task Parallel_corrections_beside_a_writer_neither_fail_nor_lose_the_words()
    {
        // SymSpell is not made for reading while writing (§15): lookups share the lock, additions have it alone
        using var database = new TestDatabase();
        using var recipes = Recipes(database);
        recipes.Index.Query("apflekuc").Execute();   // builds the dictionary

        var writer = Task.Run(() =>
        {
            for (var i = 0; i < 300; i++)
            {
                recipes.Index.Upsert(100 + i, $"Neuwort{i:000} Torte", i);
            }
        });

        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            for (var round = 0; round < 150; round++)
            {
                var result = recipes.Index.Query("apflekuchen").Execute();
                result.CorrectedQuery.ShouldBe("apfelkuchen");
                result.Hits.Count.ShouldBeGreaterThan(0);
            }
        })).ToArray();

        await Task.WhenAll(readers);
        await writer;

        // and the words the writer added are corrected towards afterwards
        var late = recipes.Index.Query("neuwrt299").Execute();
        late.Hits.ShouldContain(h => h.Key == 100 + 299);
    }

    // review of pull request 10, M2: the cap keeps the candidates of the correction
    [Theory]
    [InlineData(50)]
    [InlineData(5)]
    public void A_full_budget_of_hits_in_the_middle_of_words_does_not_push_the_corrected_candidates_out(int candidates)
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var words = catalog.Create("words", index => index.SingleWord().TypoTolerance());
        for (var i = 1; i <= 10; i++)
        {
            words.Upsert(i, $"Teigapfle{i:00}", 1_000);
        }

        words.Upsert(100, "Apfelkuchen", 1);

        // the application asks for the build (ADR-0042): a query never does
        words.WarmUpTypoDictionary();

        var result = words.Query("apfle").Candidates(candidates).Execute();

        result.CorrectedQuery.ShouldBe("apfel");
        result.Hits.Select(h => h.Text).Contains("Apfelkuchen", StringComparer.Ordinal).ShouldBeTrue();
        result.Hits[0].Text.ShouldBe("Apfelkuchen", "the word start of the corrected input comes first");
    }

    // review of pull request 10, M3: the correction survives a text that is written in two spellings
    [Fact]
    public void The_correction_is_not_swallowed_by_a_text_that_is_written_in_two_spellings()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var titles = catalog.Create("titles", index => index.MultiWord().TypoTolerance());
        titles.Upsert(1, "Bratapfle Apfel", 10);
        titles.Upsert(2, "bratapfle apfel", 10);

        // the application asks for the build (ADR-0042): a query never does
        titles.WarmUpTypoDictionary();

        var result = titles.Query("apfle").Execute();

        // the correction is there, and both spellings of the text are hits: one hit per entry (ADR-0055)
        result.CorrectedQuery.ShouldBe("apfel");
        result.Hits.Count.ShouldBe(2);
        result.Hits.Select(h => h.Text).ShouldBe(["Bratapfle Apfel", "bratapfle apfel"]);
        result.Hits.ShouldAllBe(hit => hit.AtWordStart);
    }

    // review of pull request 10, M3: the correction reaches over the two spellings of a text
    [Fact]
    public void The_input_and_its_correction_keep_the_two_spellings_of_a_text()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var titles = catalog.Create("titles", index => index.SingleWord().TypoTolerance());
        titles.Upsert(1, "Teigapfle", 10);
        titles.Upsert(2, "teigapfle", 20);
        titles.Upsert(3, "Apfelkuchen", 5);

        // the application asks for the build (ADR-0042): a query never does
        titles.WarmUpTypoDictionary();

        var result = titles.Query("apfle").Execute();

        // the two spellings are two hits, and the correction still reaches the title that has the word start
        result.CorrectedQuery.ShouldBe("apfel");
        result.Hits.Count(h => string.Equals(h.Text, "Teigapfle", StringComparison.OrdinalIgnoreCase)).ShouldBe(2);
        result.Hits.Select(h => h.Text).Contains("Apfelkuchen", StringComparer.Ordinal).ShouldBeTrue();
    }

    // review of pull request 10, N1: the builder takes a bool too
    [Fact]
    public void The_builder_takes_a_bool_as_well_as_a_number_of_edits()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var on = catalog.Create("a", index => index.SingleWord().TypoTolerance());
        var off = catalog.Create("b", index => index.SingleWord().TypoTolerance(true).TypoTolerance(false));
        on.Upsert(1, "Apfelkuchen", 1);
        off.Upsert(1, "Apfelkuchen", 1);

        // the application asks for the build (ADR-0042): a query never does
        on.WarmUpTypoDictionary();
        off.WarmUpTypoDictionary();

        on.Query("apflekuchen").Execute().CorrectedQuery.ShouldBe("apfelkuchen");
        off.Query("apflekuchen").Execute().CorrectedQuery.ShouldBeNull();
    }

    private static Fixture Recipes(TestDatabase database)
    {
        var catalog = database.Open();
        var recipes = catalog.Create("recipes", index => index.MultiWord().TypoTolerance());
        recipes.Upsert(1, "Apfelkuchen", 900);
        recipes.Upsert(2, "Gedeckter Apfelkuchen", 800);
        recipes.Upsert(3, "Apfel Kuchen", 700);
        recipes.Upsert(4, "Apfelkuchen mit Sahne", 400);
        recipes.Upsert(5, "Bratapfelkuchen", 9_000);
        recipes.Upsert(6, "Apfelstrudel", 5_000);
        recipes.Upsert(7, "Apfelkuchen mit Äpfeln", 300);
        recipes.Upsert(8, "Hauptstraße", 100);

        // the application asks for the build (ADR-0042): a query never does
        recipes.WarmUpTypoDictionary();
        return new Fixture(recipes);
    }

    private sealed class Fixture(ILuiEmbeddedIndex index) : IDisposable
    {
        internal ILuiEmbeddedIndex Index => index;

        public void Dispose()
        {
        }
    }
}
