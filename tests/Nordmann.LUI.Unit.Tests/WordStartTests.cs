using Nordmann.LUI.Normalization;
using Nordmann.LUI.Querying;
using Shouldly;

namespace Nordmann.LUI.Unit.Tests;

/// <summary>
/// The word-start rule — the table of LUI-Spezifikation §9.1.
/// </summary>
public sealed class WordStartTests
{
    [Theory]
    [InlineData("apfelkuch", "Apfelkuchen", true)]
    [InlineData("apfelkuch", "Gedeckter Apfelkuchen", true)]
    [InlineData("apfelkuch", "Apfel Kuchen", true)]
    [InlineData("apfel kuch", "Apfelkuchen", true)]
    [InlineData("apfelkuch", "Bratapfelkuchen", false)]
    [InlineData("kuch", "Kuchenblech", true)]
    [InlineData("kuch", "Apfelkuchen", false)]
    public void The_table_of_the_specification_holds(string query, string text, bool expected)
    {
        Matches(query, text).ShouldBe(expected);
    }

    [Theory]
    [InlineData("bahn", "#Bahnhof", true)]
    [InlineData("hof", "#Bahnhof", false)]
    [InlineData("zimt", "Apfel-Zimt-Kuchen", true)]
    [InlineData("kuchen", "Apfel-Zimt-Kuchen", true)]
    [InlineData("aepfel", "Äpfel", true)]
    public void A_leading_hash_does_not_count_and_a_hyphen_separates_words(string query, string text, bool expected)
    {
        Matches(query, text).ShouldBe(expected);
    }

    [Fact]
    public void The_position_is_where_the_matching_word_begins()
    {
        var searchForm = Normalizer.ToSearchForm("Gedeckter Apfelkuchen");

        WordStart.Find(searchForm, "apfelkuch").ShouldBe(10);
    }

    [Fact]
    public void A_hash_is_not_part_of_the_word()
    {
        WordStart.Find(Normalizer.ToSearchForm("#Bahnhof"), "bahn").ShouldBe(1);
    }

    [Fact]
    public void An_empty_input_stands_at_no_word_start()
    {
        WordStart.Find("apfel", "").ShouldBe(-1);
    }

    private static bool Matches(string query, string text) =>
        WordStart.Find(Normalizer.ToSearchForm(text), Normalizer.ToCompactForm(query)) >= 0;
}
