using Nordmann.LUI.Schema;
using Nordmann.LUI.Normalization;
using Nordmann.LUI.Querying;
using Shouldly;

namespace Nordmann.LUI.Unit.Tests.Querying;

/// <summary>
/// The FTS5 expression of a <c>MultiWord</c> query (ADR-0008): the words ANDed, the
/// compact form beside them, short words kept out, every term quoted.
/// </summary>
public sealed class MultiWordExpressionTests
{
    [Theory]
    [InlineData("apfel kuch", "s_compact : (\"apfel\" AND \"kuch\")")]
    [InlineData("kuchen apfel", "s_compact : (\"kuchen\" AND \"apfel\")")]
    [InlineData("  Apfel   Kuchen  ", "s_compact : (\"apfel\" AND \"kuchen\")")]
    [InlineData("Äpfel Müller", "s_compact : (\"aepfel\" AND \"mueller\")")]
    [InlineData("apfel-zimt kuchen", "s_compact : (\"apfel\" AND \"zimt\" AND \"kuchen\")")]
    public void The_words_are_anded_in_the_compact_column(string typed, string expected)
    {
        Expression(typed).ShouldBe(expected);
    }

    [Theory]
    [InlineData("apfelkuch", "s_compact : \"apfelkuch\"")]
    [InlineData("Apfelkuchen", "s_compact : \"apfelkuchen\"")]
    public void One_word_is_one_phrase_in_the_compact_column(string typed, string expected)
    {
        Expression(typed).ShouldBe(expected);
    }

    [Theory]
    [InlineData("ab cd", "s_compact : \"abcd\"")]
    [InlineData("a b c", "s_compact : \"abc\"")]
    public void Words_of_fewer_than_three_characters_stay_out_of_the_words_but_the_compact_form_has_them(
        string typed,
        string expected)
    {
        Expression(typed).ShouldBe(expected);
    }

    [Fact]
    public void A_short_word_beside_a_long_one_is_left_out_of_the_expression()
    {
        Expression("apfel ku").ShouldBe("s_compact : \"apfel\"");
    }

    [Theory]
    [InlineData("a")]
    [InlineData("ap")]
    [InlineData("a b")]
    [InlineData("a-")]
    [InlineData("#a")]
    public void An_input_with_fewer_than_three_letters_takes_the_prefix_path(string typed)
    {
        Query(typed).IsPrefixPath.ShouldBeTrue();
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("ab cd")]
    [InlineData("apfel ku")]
    public void An_input_with_three_letters_takes_the_trigram_path(string typed)
    {
        Query(typed).IsPrefixPath.ShouldBeFalse();
    }

    [Fact]
    public void The_prefix_key_of_a_short_input_is_at_most_two_code_points()
    {
        Query("ap").PrefixKey.ShouldBe("ap");
        Query("😀").PrefixKey.ShouldBe("😀");
        Query("😀p").PrefixKey.ShouldBe("😀p");
    }

    [Theory]
    [InlineData("a b", "ab")]
    [InlineData("a-b", "ab")]
    [InlineData("a.b", "ab")]
    [InlineData("-a", "a")]
    [InlineData("#a", "a")]
    [InlineData("A  B", "ab")]
    public void The_prefix_key_of_a_multi_word_index_is_the_compact_form_spaces_and_punctuation_do_not_count(
        string typed,
        string expected)
    {
        Query(typed).PrefixKey.ShouldBe(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("-")]
    [InlineData("- -")]
    [InlineData("#")]
    [InlineData(" . ")]
    public void An_input_of_separators_alone_is_empty(string typed)
    {
        Query(typed).IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Only_the_longest_eight_words_go_into_the_expression()
    {
        // twelve words of 3 … 14 letters: the eight longest stay, in the order they were typed
        var typed = string.Join(' ', Enumerable.Range(3, 12).Select(n => new string((char)('a' + (n % 26)), n)));

        var expression = Expression(typed);

        expression.Split(" AND ").Length.ShouldBe(8);
        expression.ShouldNotContain("\"ddd\"");
        expression.ShouldContain(new string('o', 14));
        expression.ShouldStartWith("s_compact : (");
    }

    [Fact]
    public void An_input_of_a_hundred_words_is_a_valid_expression_and_a_query_that_returns()
    {
        var typed = string.Join(' ', Enumerable.Range(0, 100).Select(i => $"wort{i:000}"));

        Expression(typed).Split(" AND ").Length.ShouldBe(8);
    }

    [Fact]
    public void A_term_is_quoted_and_a_quotation_mark_in_it_is_doubled()
    {
        IndexKindRules.Quote("a\"b").ShouldBe("\"a\"\"b\"");
        IndexKindRules.Quote("plain").ShouldBe("\"plain\"");
    }

    [Theory]
    [InlineData("apfel\"kuchen", "s_compact : (\"apfel\" AND \"kuchen\")")]
    [InlineData("apfel* OR kuchen", "s_compact : (\"apfel\" AND \"kuchen\")")]
    [InlineData("NEAR(apfel kuchen)", "s_compact : (\"near\" AND \"apfel\" AND \"kuchen\")")]
    public void Nothing_of_the_input_is_copied_into_the_expression_raw(string typed, string expected)
    {
        Expression(typed).ShouldBe(expected);
    }

    private static string Expression(string typed) => Query(typed).Expression;

    private static PreparedQuery Query(string typed) =>
        PreparedQuery.Create(typed, NormalizationOptions.Default, MultiWordRules.Instance);
}
