using Nordmann.LUI.Normalization;
using Shouldly;

namespace Nordmann.LUI.Unit.Tests;
/// <summary>
/// The buffer path and the string path of the search form must be the same form:
/// the ranking measures the candidates in the buffer (issue #30), and a different
/// form would change the order of the hits without anyone noticing.
/// </summary>
public sealed class SearchFormParityTests
{
    public static TheoryData<string> Texts() => new()
    {
        "",
        "Apfel",
        "Apfel Kuchen",
        "APFELKUCHEN",
        "Müller",
        "Müller",
        "Straße",
        "ẞ",
        "Große Grütze",
        "   ",
        "a-b_c",
        "Éclair",
        "CAFÉ",
        "Öl",
        "Äpfel und Birnen",
        "日本",
        "mixed 123 text!",
    };

    [Theory]
    [MemberData(nameof(Texts))]
    public void The_buffer_and_the_string_form_are_the_same(string text)
    {
        var expected = Normalizer.ToSearchForm(text);
        var buffer = new char[Math.Max(1, text.Length * 3 + 1)];

        var written = Normalizer.TryWriteSearchForm(text, buffer, out var count);

        Assert.True(written);
        Assert.Equal(expected, new string(buffer[..count]));
    }

    [Theory]
    [MemberData(nameof(Texts))]
    public void The_buffer_and_the_string_form_are_the_same_without_the_transliteration(string text)
    {
        var options = new NormalizationOptions { GermanTransliteration = false };
        var expected = Normalizer.ToSearchForm(text, options);
        var buffer = new char[Math.Max(1, text.Length * 3 + 1)];

        var written = Normalizer.TryWriteSearchForm(text, buffer, out var count, options);

        Assert.True(written);
        Assert.Equal(expected, new string(buffer[..count]));
    }

    [Fact]
    public void A_buffer_that_is_too_small_says_so()
    {
        Assert.False(Normalizer.TryWriteSearchForm("Apfel Kuchen", new char[3], out var count));
        Assert.True(count > 3);
    }
}
