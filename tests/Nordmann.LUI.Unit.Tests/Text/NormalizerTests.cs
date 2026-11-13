using Nordmann.LUI.Normalization;
using Shouldly;

namespace Nordmann.LUI.Unit.Tests.Text;

/// <summary>
/// The normalisation rule, as the specification states it
/// (ADR-0005).
/// </summary>
public sealed class NormalizerTests
{
    [Theory]
    [InlineData("Gedeckter Apfelkuchen mit Äpfeln", "gedeckter apfelkuchen mit aepfeln", "gedeckterapfelkuchenmitaepfeln")]
    [InlineData("Straße", "strasse", "strasse")]
    [InlineData("Café Crème", "cafe creme", "cafecreme")]
    [InlineData("Apfel-Zimt-Kuchen", "apfel-zimt-kuchen", "apfelzimtkuchen")]
    public void A_text_becomes_its_search_form_and_its_compact_form(
        string text,
        string expectedSearchForm,
        string expectedCompactForm)
    {
        var normalized = Normalizer.Normalize(text);

        normalized.SearchForm.ShouldBe(expectedSearchForm);
        normalized.CompactForm.ShouldBe(expectedCompactForm);
    }

    [Theory]
    [InlineData("MÜLLER", "mueller")]
    [InlineData("Müller", "mueller")]
    [InlineData("müller", "mueller")]
    [InlineData("Gebrüht", "gebrueht")]
    [InlineData("Größe", "groesse")]
    [InlineData("ẞ", "ss")]
    // Every letter of the table, in both cases — the mutation check found the
    // two that were missing: a lower-case "ä" and an upper-case "Ö" changed
    // nothing in the suite.
    [InlineData("Bäcker", "baecker")]
    [InlineData("BÄCKER", "baecker")]
    [InlineData("Öl", "oel")]
    [InlineData("ÖL", "oel")]
    [InlineData("Löffel", "loeffel")]
    public void The_german_transliteration_applies_to_both_cases(string text, string expected)
    {
        Normalizer.ToSearchForm(text).ShouldBe(expected);
    }

    [Fact]
    public void The_german_transliteration_can_be_turned_off()
    {
        var without = NormalizationOptions.Default with { GermanTransliteration = false };

        Normalizer.ToSearchForm("Müller", without).ShouldBe("muller");

        // Without the German transliteration the sharp s stays a sharp s —
        // the tokenizer does not fold it either.
        Normalizer.ToSearchForm("Straße", without).ShouldBe("straße");
    }

    [Fact]
    public void The_german_transliteration_is_on_by_default()
    {
        Normalizer.ToSearchForm("Müller").ShouldBe("mueller");
    }

    [Theory]
    [InlineData("Café", "cafe")]
    [InlineData("Crème", "creme")]
    [InlineData("Nîmes", "nimes")]
    public void The_diacritics_are_removed(string text, string expected)
    {
        Normalizer.ToSearchForm(text).ShouldBe(expected);
    }

    [Fact]
    public void The_combining_marks_are_removed()
    {
        // The tokenizer drops the combining marks — and so does the C# side,
        // or a decomposed "é" would not be found by a composed "é".
        Normalizer.ToSearchForm("e\u0301cole").ShouldBe("ecole");
    }

    [Fact]
    public void The_compact_form_drops_the_separators()
    {
        Normalizer.ToCompactForm("Apfel Kuchen").ShouldBe("apfelkuchen");
        Normalizer.ToCompactForm("Apfel-Kuchen").ShouldBe("apfelkuchen");
        Normalizer.ToCompactForm("Apfel.Kuchen").ShouldBe("apfelkuchen");
    }

    [Theory]
    [InlineData("\t")]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\u00A0")]
    [InlineData("\u2009")]
    [InlineData("\u202F")]
    [InlineData("\u3000")]
    [InlineData("\u00B7")]
    [InlineData("\u2022")]
    [InlineData("\u00AB")]
    [InlineData("\u00BB")]
    public void The_compact_form_drops_the_unicode_word_separators(string separator)
    {
        var separatorCharacter = separator[0];

        Normalizer.Separators.Contains(separatorCharacter).ShouldBeTrue(
            $"U+{(int)separatorCharacter:X4} separates words and has to go");

        Normalizer.ToCompactForm($"Apfel{separator}Kuchen").ShouldBe("apfelkuchen");
    }

    [Theory]
    [InlineData("Mu\u0308ller", "mueller")]
    [InlineData("A\u0308pfel", "aepfel")]
    [InlineData("O\u0308l", "oel")]
    [InlineData("U\u0308bung", "uebung")]
    [InlineData("Gru\u0308\u00dfe", "gruesse")]
    public void A_decomposed_umlaut_is_transliterated_like_the_composed_one(string text, string expected)
    {
        // macOS file names and some clipboards deliver the decomposed form.
        // Without this, the two spellings would be two different words.
        Normalizer.ToSearchForm(text).ShouldBe(expected);
        Normalizer.ToCompactForm(text).ShouldBe(expected);
    }

    [Fact]
    public void A_decomposed_umlaut_can_be_transliterated_off()
    {
        var without = NormalizationOptions.Default with { GermanTransliteration = false };

        Normalizer.ToSearchForm("Mu\u0308ller", without).ShouldBe("muller");
    }

    [Fact]
    public void The_compact_form_keeps_the_letters_and_the_digits()
    {
        Normalizer.ToCompactForm("3D-Druck").ShouldBe("3ddruck");
    }

    [Fact]
    public void The_offsets_point_back_into_the_original_text()
    {
        // "Müller" becomes "mueller" — six characters from five, and the
        // position of the match in the original text cannot be guessed from
        // the search form.
        var normalized = Normalizer.Normalize("Müller");

        normalized.SearchForm.ShouldBe("mueller");
        normalized.SourceOffsets.ShouldBe([0, 1, 1, 2, 3, 4, 5]);
        normalized.SourceOffset(0).ShouldBe(0);
        normalized.SourceOffset(1).ShouldBe(1);
        normalized.SourceOffset(6).ShouldBe(5);
        normalized.SourceOffset(7).ShouldBe(-1);
    }

    [Fact]
    public void The_offsets_survive_a_removed_character()
    {
        // The combining mark disappears — the characters behind it move up.
        var normalized = Normalizer.Normalize("e\u0301cole");

        normalized.SearchForm.ShouldBe("ecole");

        // The combining mark is gone — the characters behind it moved up.
        normalized.SourceOffsets.ShouldBe([0, 2, 3, 4, 5]);
    }

    [Fact]
    public void The_text_survives_a_round_trip_through_the_offsets()
    {
        const string text = "Gedeckter Apfelkuchen mit Äpfeln";

        var normalized = Normalizer.Normalize(text);

        for (var i = 0; i < normalized.SearchForm.Length; i++)
        {
            text[normalized.SourceOffset(i)].ShouldNotBe('\0');
        }
    }

    [Fact]
    public void An_empty_text_stays_empty()
    {
        var normalized = Normalizer.Normalize(string.Empty);

        normalized.SearchForm.ShouldBeEmpty();
        normalized.CompactForm.ShouldBeEmpty();
        normalized.SourceOffsets.ShouldBeEmpty();
    }
}
