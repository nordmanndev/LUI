using Shouldly;

namespace Nordmann.LUI.Integration.Tests.Typos;

/// <summary>
/// The typo distance is an enumeration (LUI-Spezifikation §10): the dictionary of the word beginnings grows with every
/// additional error, so the distances are the ones that were measured and a caller cannot ask for a tenth. The distance
/// belongs to the <b>index</b>: it is part of the stored definition, an index that is opened again knows how its
/// dictionary was built, and a distance of <c>None</c> builds no dictionary at all.
/// </summary>
public sealed class TypoDistanceTests
{
    [Fact]
    public void The_distance_is_part_of_the_stored_definition()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create("words", index => index.SingleWord().TypoTolerance(LuiTypoDistance.TwoErrors));

        var definition = database.Scalar<string>("SELECT definition FROM lui_meta WHERE index_name = 'words';");

        definition.ShouldContain("\"typoDistance\":\"TwoErrors\"");
    }

    [Fact]
    public void An_opened_index_keeps_its_distance_and_corrects_without_the_query_asking_for_it()
    {
        using var database = new TestDatabase();
        using (var catalog = database.Open())
        {
            var words = catalog.Create("words", index => index.SingleWord().TypoTolerance(LuiTypoDistance.TwoErrors));
            words.Upsert(1, "Kartoffel", 10);
        }

        // a new process, the same file: the distance is in the definition, so the index knows it
        using var reopened = database.Open();
        var index = reopened.Index("words");

        index.TypoDistance.ShouldBe(LuiTypoDistance.TwoErrors);

        // the application asks for the build (ADR-0042): a query never does
        index.WarmUpTypoDictionary();

        // the query does not ask for a correction either: the index forgives two errors, so it corrects them
        var result = index.Search("krttoffel");

        result.CorrectedQuery.ShouldBe("kartoffel");
        result.Hits.ShouldHaveSingleItem().Key.ShouldBe(1);
    }

    [Fact]
    public void An_index_that_forgives_nothing_builds_no_dictionary()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var words = catalog.Create("words", index => index.SingleWord().TypoTolerance(LuiTypoDistance.None));
        words.Upsert(1, "Kartoffel", 10);

        // the application asks for the build — and an index that forgives nothing has none to build
        words.WarmUpTypoDictionary();

        words.TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.NotBuilt, "the memory of a dictionary would buy no correction");
        words.Search("krttoffel").CorrectedQuery.ShouldBeNull();
        words.Search("krttoffel").Hits.ShouldBeEmpty();
    }

    [Fact]
    public void A_typo_of_two_errors_is_corrected_by_the_index_that_forgives_two_and_by_no_other()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();

        var two = catalog.Create("two", index => index.SingleWord().TypoTolerance(LuiTypoDistance.TwoErrors));
        var one = catalog.Create("one", index => index.SingleWord().TypoTolerance(LuiTypoDistance.OneError));
        two.Upsert(1, "Kartoffel", 10);
        one.Upsert(1, "Kartoffel", 10);

        // the application asks for the build (ADR-0042): a query never does
        two.WarmUpTypoDictionary();
        one.WarmUpTypoDictionary();

        // "krttoffel" is two letters away from "kartoffel" — and one error is not two
        var corrected = two.Search("krttoffel");
        corrected.CorrectedQuery.ShouldBe("kartoffel");
        corrected.Hits.ShouldHaveSingleItem().Key.ShouldBe(1);

        var refused = one.Search("krttoffel");
        refused.CorrectedQuery.ShouldBeNull("one error is not two");
        refused.Hits.ShouldBeEmpty();
    }

    [Fact]
    public void An_index_that_exists_with_another_distance_is_a_mismatch()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create("words", index => index.SingleWord().TypoTolerance(LuiTypoDistance.OneError));

        // the distance is part of the definition: an index that is created again with another one is another index
        Should.Throw<LuiDefinitionMismatchException>(
            () => catalog.Create("words", index => index.SingleWord().TypoTolerance(LuiTypoDistance.TwoErrors)));

        Should.NotThrow(() => catalog.Create("words", index => index.SingleWord().TypoTolerance(LuiTypoDistance.OneError)));
    }
}
