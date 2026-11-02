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
    [InlineData("apfel\"kuchen", "s_compact : (\"apfel\" AND \"kuchen\")")]
    [InlineData("apfel* OR kuchen", "s_compact : (\"apfel\" AND \"kuchen\")")]
    [InlineData("NEAR(apfel kuchen)", "s_compact : (\"near\" AND \"apfel\" AND \"kuchen\")")]
    public void Nothing_of_the_input_is_copied_into_the_expression_raw(string typed, string expected)
    {
        Expression(typed).ShouldBe(expected);
    }

    private static string Expression(string typed) => Query(typed).Expression;

    private static PreparedQuery Query(string typed) =>
        PreparedQuery.Create(typed, NormalizationOptions.Default, IndexKind.MultiWord);
}
