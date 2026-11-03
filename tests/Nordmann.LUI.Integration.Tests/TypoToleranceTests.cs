using Shouldly;

namespace Nordmann.LUI.Integration.Tests;

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
        using var index = Recipes(database);

        var result = index.Index.Query("apflekuchen").Execute();

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
        using var index = Recipes(database);

        index.Index.Query(typed).Execute().CorrectedQuery.ShouldBeNull();
    }

    [Fact]
    public void A_typo_in_one_word_of_several_is_corrected_and_the_other_words_stay()
    {
        using var database = new TestDatabase();
        using var index = Recipes(database);

        var result = index.Index.Query("apflekuchen sahne").Execute();

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
        using var index = Recipes(database);

        var result = index.Index.Query(typed).Execute();

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
        using var index = Recipes(database);

        var result = index.Index.Query(typed).Execute();

        result.CorrectedQuery.ShouldBeNull();
        result.Hits.ShouldBeEmpty();
    }

    [Fact]
    public void The_german_spelling_without_the_e_is_a_typo_of_one_edit()
    {
        // §6.3: „muller" is not „mueller" in the index, and one edit away from it
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("people").MultiWord().TypoTolerance().Ensure();
        index.Upsert(1, "Anna Müller", 10);
        index.Upsert(2, "Hans Schmidt", 20);

        var result = index.Query("muller").Execute();

        result.CorrectedQuery.ShouldBe("mueller");
        result.Hits.ShouldHaveSingleItem().Key.ShouldBe(1);
    }

    [Fact]
    public void The_trigger_is_no_word_start_not_no_hits()
    {
        // „apflekuchen" has a hit in the middle of another word — 34 % of the typos do (§10.2)
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("words").SingleWord().TypoTolerance().Ensure();
        index.Upsert(1, "Apfelkuchen", 10);
        index.Upsert(2, "Teigapflekuchen", 20);

        var result = index.Query("apflekuchen").Execute();

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
        var index = catalog.Index("words").SingleWord().Ensure();
        index.Upsert(1, "Apfelkuchen", 10);

        var off = index.Query("apflekuchen").Execute();
        off.Hits.ShouldBeEmpty();
        off.CorrectedQuery.ShouldBeNull();

        var on = index.Query("apflekuchen").TypoTolerance().Execute();
        on.CorrectedQuery.ShouldBe("apfelkuchen");
        on.Hits.ShouldHaveSingleItem().Key.ShouldBe(1);
    }

    [Fact]
    public void A_query_can_turn_the_tolerance_of_the_index_off()
    {
        using var database = new TestDatabase();
        using var index = Recipes(database);

        var result = index.Index.Query("apflekuchen").TypoTolerance(false).Execute();

        result.Hits.ShouldBeEmpty();
        result.CorrectedQuery.ShouldBeNull();
    }

    [Fact]
    public void The_options_object_takes_the_setting_too()
    {
        using var database = new TestDatabase();
        using var index = Recipes(database);

        index.Index.Search("apflekuchen", new LuiQueryOptions { TypoTolerance = false }).Hits.ShouldBeEmpty();
        index.Index.Search("apflekuchen", new LuiQueryOptions { TypoTolerance = true }).CorrectedQuery.ShouldBe("apfelkuchen");
        index.Index.Search("apflekuchen", new LuiQueryOptions()).CorrectedQuery.ShouldBe("apfelkuchen", "null: as the index says");
    }

    [Theory]
    [InlineData("a")]
    [InlineData("ap")]
    [InlineData("xy")]
    public void One_and_two_letters_are_never_corrected(string typed)
    {
        using var database = new TestDatabase();
        using var index = Recipes(database);

        index.Index.Query(typed).Execute().CorrectedQuery.ShouldBeNull();
    }

    [Fact]
    public void A_compound_typed_together_is_corrected_only_if_such_a_word_exists()
    {
        // the dictionary holds the beginnings of the *words* of the titles (§10.1); „Apfel Kuchen" has the words
        // apfel and kuchen, and no word apfelkuchen
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("titles").MultiWord().TypoTolerance().Ensure();
        index.Upsert(1, "Apfel Kuchen", 10);

        var together = index.Query("apflekuchen").Execute();
        together.Hits.ShouldBeEmpty();
        together.CorrectedQuery.ShouldBeNull();

        var apart = index.Query("apfle kuchen").Execute();
        apart.CorrectedQuery.ShouldBe("apfel kuchen");
        apart.Hits.ShouldHaveSingleItem().Key.ShouldBe(1);
    }

    [Fact]
    public void A_word_with_nothing_near_it_is_left_alone()
    {
        using var database = new TestDatabase();
        using var index = Recipes(database);

        var result = index.Index.Query("qqqqqqqq").Execute();

        result.Hits.ShouldBeEmpty();
        result.CorrectedQuery.ShouldBeNull();
    }

    [Fact]
    public void A_word_longer_than_ten_letters_is_corrected_by_its_first_ten()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("words").SingleWord().TypoTolerance().Ensure();
        index.Upsert(1, "Donaudampfschifffahrtsgesellschaft", 10);

        var result = index.Query("donaudampgschifffahrtsgesellschaft").Execute();

        result.CorrectedQuery.ShouldBe("donaudampfschifffahrtsgesellschaft");
        result.Hits.ShouldHaveSingleItem().Key.ShouldBe(1);
    }

    [Fact]
    public void Upsert_adds_the_new_word_beginnings()
    {
        using var database = new TestDatabase();
        using var index = Recipes(database);
        index.Index.Query("apflekuchen").Execute();   // builds the dictionary

        index.Index.Upsert(20, "Himbeere", 50);

        var result = index.Index.Query("himbere").Execute();
        result.CorrectedQuery.ShouldNotBeNull();
        result.Hits.ShouldHaveSingleItem().Key.ShouldBe(20);
    }

    [Fact]
    public void A_word_that_is_removed_is_still_a_correction_that_finds_nothing_until_the_dictionary_is_rebuilt()
    {
        using var database = new TestDatabase();
        using var index = Recipes(database);
        index.Index.Upsert(20, "Himbeere", 50);
        index.Index.Query("himbere").Execute().Hits.ShouldHaveSingleItem();

        index.Index.Remove(20);

        var before = index.Index.Query("himbere").Execute();
        before.Hits.ShouldBeEmpty("the beginnings stay (§10.1), the correction finds nothing");

        index.Index.RebuildTypoDictionary();

        var after = index.Index.Query("himbere").Execute();
        after.Hits.ShouldBeEmpty();
        after.CorrectedQuery.ShouldBeNull("the removed word is no longer in the dictionary");
    }

    [Fact]
    public void The_last_switch_of_the_tolerance_wins()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("words").SingleWord().TypoTolerance().TypoTolerance(false).Ensure();
        index.Upsert(1, "Apfelkuchen", 10);

        index.Query("apflekuchen").Execute().CorrectedQuery.ShouldBeNull();
    }

    [Fact]
    public void The_tolerance_is_a_default_of_the_instance_and_not_part_of_the_definition()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Index("words").SingleWord().TypoTolerance().Ensure();

        Should.NotThrow(() => catalog.Index("words").SingleWord().Ensure());
    }

    [Fact]
    public void The_priority_and_the_ranking_options_work_with_the_corrected_input()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("tags").SingleWord().TypoTolerance().Ensure();
        index.Upsert(1, "vegan", 0, priority: LuiPriority.System);
        index.Upsert(2, "veganer kuchen", 900);

        var result = index.Query("vegna").Execute();

        result.CorrectedQuery.ShouldNotBeNull();
        result.Hits[0].Key.ShouldBe(1, "the system tag stays first");
    }

    [Fact]
    public void Timings_show_the_time_of_the_correction()
    {
        using var database = new TestDatabase();
        using var index = Recipes(database);

        var corrected = index.Index.Query("apflekuchen").TrackTimings().Execute();
        var plain = index.Index.Query("apfelkuchen").TrackTimings().Execute();

        corrected.Timings!.Correction.ShouldBeGreaterThan(TimeSpan.Zero);
        plain.Timings!.Correction.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public async Task Parallel_corrections_beside_a_writer_neither_fail_nor_lose_the_words()
    {
        // SymSpell is not made for reading while writing (§15): lookups share the lock, additions have it alone
        using var database = new TestDatabase();
        using var index = Recipes(database);
        index.Index.Query("apflekuc").Execute();   // builds the dictionary

        var writer = Task.Run(() =>
        {
            for (var i = 0; i < 300; i++)
            {
                index.Index.Upsert(100 + i, $"Neuwort{i:000} Torte", i);
            }
        });

        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            for (var round = 0; round < 150; round++)
            {
                var result = index.Index.Query("apflekuchen").Execute();
                result.CorrectedQuery.ShouldBe("apfelkuchen");
                result.Hits.Count.ShouldBeGreaterThan(0);
            }
        })).ToArray();

        await Task.WhenAll(readers);
        await writer;

        // and the words the writer added are corrected towards afterwards
        var late = index.Index.Query("neuwrt299").Execute();
        late.Hits.ShouldContain(h => h.Key == 100 + 299);
    }

    private static Fixture Recipes(TestDatabase database)
    {
        var catalog = database.Open();
        var index = catalog.Index("recipes").MultiWord().TypoTolerance().Ensure();
        index.Upsert(1, "Apfelkuchen", 900);
        index.Upsert(2, "Gedeckter Apfelkuchen", 800);
        index.Upsert(3, "Apfel Kuchen", 700);
        index.Upsert(4, "Apfelkuchen mit Sahne", 400);
        index.Upsert(5, "Bratapfelkuchen", 9_000);
        index.Upsert(6, "Apfelstrudel", 5_000);
        index.Upsert(7, "Apfelkuchen mit Äpfeln", 300);
        index.Upsert(8, "Hauptstraße", 100);
        return new Fixture(index);
    }

    private sealed class Fixture(ILuiOwnedIndex<long, LuiNoPayload> index) : IDisposable
    {
        internal ILuiOwnedIndex<long, LuiNoPayload> Index => index;

        public void Dispose()
        {
        }
    }
}
