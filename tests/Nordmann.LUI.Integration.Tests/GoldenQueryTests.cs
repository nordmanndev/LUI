using Shouldly;

namespace Nordmann.LUI.Integration.Tests;

/// <summary>
/// The golden queries of a single-word index — Anhang B of the specification
/// as fixed expectations (§19), and the system tag of E10.
/// </summary>
public sealed class GoldenQueryTests
{
    [Fact]
    public void An_input_at_a_word_start_beats_a_more_popular_entry_that_has_it_in_the_middle()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var rezepte = catalog.Create("rezepte", index => index.SingleWord());
        rezepte.Upsert(1, "Apfelkuchen", 900);
        rezepte.Upsert(2, "Gedeckter Apfelkuchen", 800);
        rezepte.Upsert(3, "Apfelkuchen mit Sahne", 400);
        rezepte.Upsert(4, "Bratapfelkuchen", 9_000);
        rezepte.Upsert(5, "Apfelstrudel", 5_000);

        var texts = rezepte.Query("apfelkuch").Execute().Hits.Select(h => h.Text).ToArray();

        texts.Length.ShouldBe(4);
        texts[0].ShouldBe("Apfelkuchen");
        texts.Take(3).ShouldBe(["Apfelkuchen", "Gedeckter Apfelkuchen", "Apfelkuchen mit Sahne"], ignoreOrder: true);
        texts[3].ShouldBe("Bratapfelkuchen", "behind all the word starts, although it is more popular");
    }

    [Fact]
    public void A_shorter_input_finds_the_same_family_of_entries()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var rezepte = catalog.Create("rezepte", index => index.SingleWord());
        rezepte.Upsert(1, "Apfelkuchen", 900);
        rezepte.Upsert(2, "Gedeckter Apfelkuchen", 800);
        rezepte.Upsert(3, "Bratapfelkuchen", 9_000);
        rezepte.Upsert(4, "Apfelstrudel", 5_000);

        var texts = rezepte.Query("apfelk").Execute().Hits.Select(h => h.Text).ToArray();

        texts.Take(2).ShouldBe(["Apfelkuchen", "Gedeckter Apfelkuchen"], ignoreOrder: true);
        texts[2].ShouldBe("Bratapfelkuchen");
        texts.Contains("Apfelstrudel", StringComparer.Ordinal).ShouldBeFalse();
    }

    [Theory]
    [InlineData("äpfeln")]
    [InlineData("aepfeln")]
    [InlineData("ÄPFELN")]
    public void The_umlaut_and_its_spelling_find_the_same_entry(string input)
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var rezepte = catalog.Create("rezepte", index => index.SingleWord());
        rezepte.Upsert(1, "Gedeckter Apfelkuchen mit Äpfeln", 10);
        rezepte.Upsert(2, "Gedeckter Apfelkuchen", 20);

        rezepte.Query(input).Execute().Hits.ShouldHaveSingleItem().Key.ShouldBe(1);
    }

    [Theory]
    [InlineData("strasse")]
    [InlineData("Straße")]
    public void Sharp_s_and_double_s_are_the_same(string input)
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var orte = catalog.Create("orte", index => index.SingleWord());
        orte.Upsert(1, "Hauptstraße", 10);
        orte.Upsert(2, "Hauptstrasse", 5);

        orte.Query(input).Execute().Hits.Select(h => h.Key).ShouldBe([1, 2], ignoreOrder: true);
    }

    [Fact]
    public void One_and_two_letters_give_the_most_popular_entries_that_begin_so()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var rezepte = catalog.Create("rezepte", index => index.SingleWord());
        rezepte.Upsert(1, "Apfelkuchen", 300);
        rezepte.Upsert(2, "Ananas-Torte", 700);
        rezepte.Upsert(3, "Aprikosenkuchen", 500);
        rezepte.Upsert(4, "Bratapfel", 9_000);

        rezepte.Query("a").Execute().Hits.Select(h => h.Text).ShouldBe(["Ananas-Torte", "Aprikosenkuchen", "Apfelkuchen"]);
        rezepte.Query("ap").Execute().Hits.Select(h => h.Text).ShouldBe(["Aprikosenkuchen", "Apfelkuchen"]);
    }

    [Fact]
    public void The_system_tag_is_first_for_every_input_that_matches_it_even_among_thirty_thousand_hits()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        LoadTagCorpus(database, tags);

        foreach (var input in new[] { "gan", "veg", "vega", "vegan" })
        {
            var result = tags.Query(input).Execute();

            result.Hits[0].Text.ShouldBe("vegan", $"for '{input}'");
            result.Hits[0].Priority.ShouldBe(LuiPriority.System);
        }

        var wide = tags.Query("gan").Execute();
        wide.Truncated.ShouldBeTrue();
        wide.CandidatesConsidered.ShouldBe(500);
        database.Scalar<long>("SELECT count(*) FROM lui_tags WHERE s_norm LIKE '%gan%'").ShouldBeGreaterThan(30_000);

        // the same entry as an ordinary tag is not among the five hundred candidates — that is why the priority sits in the rowid (E10)
        tags.UpdatePriority(1, LuiPriority.Normal);
        tags.Query("gan").Execute().Hits.Select(h => h.Key).ShouldNotContain(1L);
    }

    [Fact]
    public void An_ordinary_tag_with_the_same_text_is_a_duplicate_the_caller_can_hide()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        tags.Upsert(1, "vegan", 0, priority: LuiPriority.System);
        tags.Upsert(2, "Vegan", 800_000);
        tags.Upsert(3, "vegane torte", 700_000);

        tags.Query("veg").Execute().Hits.Select(h => h.Key).ShouldBe([1, 2, 3]);
        tags.Query("veg").DistinctText().Execute().Hits.Select(h => h.Key).ShouldBe([1, 3]);
    }

    private static void LoadTagCorpus(TestDatabase database, ILuiEmbeddedIndex index)
    {
        // 35,000 tags that hold "gan", 1,100 that begin with "veg", and the system tag
        var random = new Random(20261102);
        string[] stems = ["organ", "gans", "gangster", "mogano"];

        using var connection = database.Connect();
        using var transaction = connection.BeginTransaction();

        index.Upsert(1, "vegan", 5, priority: LuiPriority.System, transaction: transaction);

        for (var i = 0; i < 35_000; i++)
        {
            index.Upsert(100 + i, $"{stems[i % stems.Length]}{i}", random.Next(1, 1_000_000), transaction: transaction);
        }

        for (var i = 0; i < 1_100; i++)
        {
            index.Upsert(100_000 + i, $"vegetarisch{i}", random.Next(1, 1_000_000), transaction: transaction);
        }

        transaction.Commit();
    }
}
