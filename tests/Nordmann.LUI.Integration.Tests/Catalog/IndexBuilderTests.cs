using Nordmann.LUI.Schema;
using Shouldly;

namespace Nordmann.LUI.Integration.Tests.Catalog;

/// <summary>
/// The description of an index (ADR-0013, ADR-0033): the builder checks a value where it is set, and every step of it
/// keeps the settings of the steps before.
/// </summary>
public sealed class IndexBuilderTests
{
    // the shape of the API before 0.1.0 (ADR-0013), the builder and the interfaces
    [Fact]
    public void An_undefined_kind_of_key_fails_where_it_is_chosen()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();

        Should.Throw<ArgumentOutOfRangeException>(
            () => catalog.Create("tags", index => index.SingleWord().WithKey((LuiKeyKind)7))).ParamName.ShouldBe("kind");
    }

    // the shape of the API before 0.1.0 (ADR-0013), the builder and the interfaces
    [Fact]
    public void A_payload_keeps_the_settings_made_before_it()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var words = catalog.Create("words", index => index.SingleWord().TypoTolerance().GermanTransliteration(false).Similarity(new Constant(0.25)));
        words.Upsert(new LuiEntry(1, "Apfelkuchen") { Weight = 10, Payload = new Chip("a") });

        // the typo tolerance: the application asks for the build (ADR-0042), a query never does
        words.WarmUpTypoDictionary();
        words.Search("apflekuchen").CorrectedQuery.ShouldBe("apfelkuchen");

        // the similarity
        words.Search("apfelkuch").Hits.ShouldHaveSingleItem().Similarity.ShouldBe(0.25);

        // the transliteration: it is part of the definition, so an index with it on is another index
        Should.Throw<LuiDefinitionMismatchException>(
            () => catalog.Create("words", index => index.SingleWord()));
    }

    // the shape of the API before 0.1.0 (ADR-0013), the builder and the interfaces
    [Fact]
    public void Every_step_that_changes_the_key_or_the_payload_keeps_every_setting()
    {
        // review of 2026-11-04, C.1 (#40): one settings record instead of three fields copied by hand in four places
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var similarity = new Constant(0.25);
        var expected = new IndexOptions(similarity, GermanTransliteration: false, TypoDistance: LuiTypoDistance.OneError);

        // the builder is the description of an index, not the way into it (ADR-0033): the catalog builds it, the test
        // asks for it directly to look at the settings of every step.
        var described = new LuiIndexKindBuilder(catalog, "words").SingleWord().Similarity(similarity).GermanTransliteration(false).TypoTolerance();

        // the default, pinned: comparing with IndexOptions.Default alone would let a wrong constant pass (review of PR 45, M3)
        IndexOptions.Default.ShouldBe(new IndexOptions(Similarity: null, GermanTransliteration: true, TypoDistance: LuiTypoDistance.None));
        new LuiIndexKindBuilder(catalog, "plain").SingleWord().Options.ShouldBe(IndexOptions.Default);
        described.Options.ShouldBe(expected);
        described.WithKey(LuiKeyKind.Number).Options.ShouldBe(expected, "WithKey");

        var linked = described.LinkTo("tag", "id", "name");
        linked.Options.ShouldBe(expected, "LinkTo");
        linked.WithPayload(["name"], row => new Chip(row.GetString("name"))).Options.ShouldBe(expected, "the linked WithPayload");
    }

    private sealed class Constant(double value) : ILuiSimilarity
    {
        public double Similarity(string query, string text) => value;
    }
}
