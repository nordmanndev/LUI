using Shouldly;

namespace Nordmann.LUI.Integration.Tests.Querying;

/// <summary>
/// The answer in the type of the application (issue #59, ADR-0044, ADR-0073): the handle of a definition with a payload type answers
/// with <c>hit.Payload</c> in the type of the application, as the handle of a definition without one answers with the same data —
/// the typed view over an untyped index (<c>index.As&lt;Chip&gt;()</c>) is gone, the definition names the type once.
/// </summary>
public sealed class TypedIndexTests
{
    private static readonly LuiIndexDefinition<long, Chip> Chips = new("tags", index => index.SingleWord());

    [Fact]
    public void The_handle_of_a_payload_definition_reads_the_payload_in_the_type_of_the_application()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create(Chips);
        tags.Upsert(new LuiEntry<long, Chip>(1, "vegan", new Chip("vegan", "#0a0")));

        var opened = catalog.Index(Chips);
        opened.Name.ShouldBe("tags");
        opened.Count().ShouldBe(1);

        var hit = opened.Search("vegan").Hits.ShouldHaveSingleItem();
        hit.Payload.ShouldBe(new Chip("vegan", "#0a0"));
        hit.Key.ShouldBe(1);
    }

    [Fact]
    public void The_handle_with_a_payload_answers_as_the_handle_without_one_answers()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var withPayload = catalog.Create(Chips);
        var without = catalog.Create(new LuiIndexDefinition<long>("plain", index => index.SingleWord()));
        withPayload.Upsert(new LuiEntry<long, Chip>(1, "vegan", new Chip("vegan")));
        withPayload.Upsert(new LuiEntry<long, Chip>(2, "vegetarian", new Chip("vegetarian")));
        without.Upsert(new LuiEntry<long>(1, "vegan"));
        without.Upsert(new LuiEntry<long>(2, "vegetarian"));

        var plain = without.Search("vegan");
        var typed = withPayload.Search("vegan");

        typed.Query.ShouldBe(plain.Query);
        typed.CandidatesConsidered.ShouldBe(plain.CandidatesConsidered);
        typed.Truncated.ShouldBe(plain.Truncated);
        typed.CorrectedQuery.ShouldBe(plain.CorrectedQuery);
        typed.CorrectionSkipped.ShouldBe(plain.CorrectionSkipped);
        typed.Hits.Count.ShouldBe(plain.Hits.Count);
        typed.Hits.Select(hit => hit.Key).ShouldBe(plain.Hits.Select(hit => hit.Key));
        typed.Hits.Select(hit => hit.Text).ShouldBe(plain.Hits.Select(hit => hit.Text));
        typed.Hits.Select(hit => hit.Weight).ShouldBe(plain.Hits.Select(hit => hit.Weight));
        typed.Hits.Select(hit => hit.Priority).ShouldBe(plain.Hits.Select(hit => hit.Priority));
        typed.Hits.Select(hit => hit.Similarity).ShouldBe(plain.Hits.Select(hit => hit.Similarity));
        typed.Hits.Select(hit => hit.AtWordStart).ShouldBe(plain.Hits.Select(hit => hit.AtWordStart));
    }

    [Fact]
    public void The_handle_with_a_payload_describes_the_index_as_the_catalog_does()
    {
        // issue #107 (the coverage of #80, G5): the pass-throughs of the handle were read by no test. The index differs from
        // the defaults in every one of them, so that a handle that answered a constant would show.
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var titles = catalog.Create(new LuiIndexDefinition<string, Chip>("titles", index => index.MultiWord().TypoTolerance()));
        titles.Upsert(new LuiEntry<string, Chip>("a", "Apfelkuchen mit Sahne", new Chip("apfel")));
        titles.WarmUpTypoDictionary();

        titles.Name.ShouldBe("titles");
        titles.Kind.ShouldBe(LuiIndexKind.MultiWord);
        catalog.Indexes().ShouldHaveSingleItem().KeyType.ShouldBe(LuiKeyType.String);
        titles.TypoDistance.ShouldBe(LuiTypoDistance.OneError);
        titles.TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.Current);
        titles.Count().ShouldBe(1);
    }

    [Fact]
    public void The_fluent_path_ends_in_the_answer_with_the_payload_too()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create(Chips);
        tags.Upsert(new LuiEntry<long, Chip>(1, "vegan", new Chip("vegan", "#0a0")) { Weight = 10 });
        tags.Upsert(new LuiEntry<long, Chip>(2, "vegetarian", new Chip("vegetarian")) { Weight = 5 });

        var typed = tags.Query("veg").Take(2).Execute();

        typed.Hits.Select(hit => hit.Text).ShouldBe(["vegan", "vegetarian"]);
        typed.Hits[0].Payload.ShouldBe(new Chip("vegan", "#0a0"));
        typed.Hits[1].Payload.ShouldBe(new Chip("vegetarian"));
    }
}
