using Nordmann.LUI.Normalization;
using Nordmann.LUI.Querying;
using Shouldly;

namespace Nordmann.LUI.Tests;

/// <summary>
/// The FTS5 expression of a <c>MultiWord</c> query (LUI-Spezifikation §8.2): the words ANDed, the
/// compact form beside them, short words kept out, every term quoted.
/// </summary>
public sealed class MultiWordExpressionTests
{
    [Theory]
    [InlineData("apfel kuch", "(s_norm : (\"apfel\" AND \"kuch\")) OR (s_compact : \"apfelkuch\")")]
    [InlineData("kuchen apfel", "(s_norm : (\"kuchen\" AND \"apfel\")) OR (s_compact : \"kuchenapfel\")")]
    [InlineData("  Apfel   Kuchen  ", "(s_norm : (\"apfel\" AND \"kuchen\")) OR (s_compact : \"apfelkuchen\")")]
    [InlineData("Äpfel Müller", "(s_norm : (\"aepfel\" AND \"mueller\")) OR (s_compact : \"aepfelmueller\")")]
    [InlineData("apfel-zimt kuchen", "(s_norm : (\"apfel\" AND \"zimt\" AND \"kuchen\")) OR (s_compact : \"apfelzimtkuchen\")")]
    public void The_words_are_anded_and_the_compact_form_stands_beside_them(string typed, string expected)
    {
        Expression(typed).ShouldBe(expected);
    }

    [Theory]
    [InlineData("apfelkuch", "s_compact : \"apfelkuch\"")]
    [InlineData("Apfelkuchen", "s_compact : \"apfelkuchen\"")]
    public void One_word_is_searched_in_the_compact_column_which_holds_everything_the_other_holds(string typed, string expected)
    {
        Expression(typed).ShouldBe(expected);
    }

    [Theory]
    [InlineData("ab cd", "s_compact : \"abcd\"")]
    [InlineData("a b c", "s_compact : \"abc\"")]
    public void Words_of_fewer_than_three_characters_stay_out_of_the_expression_but_the_compact_form_has_them(
        string typed,
        string expected)
    {
        Expression(typed).ShouldBe(expected);
    }

    [Fact]
    public void A_short_word_beside_a_long_one_is_left_out_of_the_words_and_kept_in_the_compact_form()
    {
        Expression("apfel ku").ShouldBe("(s_norm : (\"apfel\")) OR (s_compact : \"apfelku\")");
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
        Query("a b").PrefixKey.ShouldBe("a ");
        Query("😀").PrefixKey.ShouldBe("😀");
    }

    [Fact]
    public void A_term_is_quoted_and_a_quotation_mark_in_it_is_doubled()
    {
        PreparedQuery.Quote("a\"b").ShouldBe("\"a\"\"b\"");
        PreparedQuery.Quote("plain").ShouldBe("\"plain\"");
    }

    [Theory]
    [InlineData("apfel\"kuchen", "(s_norm : (\"apfel\" AND \"kuchen\")) OR (s_compact : \"apfelkuchen\")")]
    [InlineData("apfel* OR kuchen", "(s_norm : (\"apfel\" AND \"kuchen\")) OR (s_compact : \"apfelorkuchen\")")]
    [InlineData("NEAR(apfel kuchen)", "(s_norm : (\"near\" AND \"apfel\" AND \"kuchen\")) OR (s_compact : \"nearapfelkuchen\")")]
    public void Nothing_of_the_input_is_copied_into_the_expression_raw(string typed, string expected)
    {
        Expression(typed).ShouldBe(expected);
    }

    private static string Expression(string typed) => Query(typed).Expression;

    private static PreparedQuery Query(string typed) =>
        PreparedQuery.Create(typed, NormalizationOptions.Default, IndexKind.MultiWord);
}
