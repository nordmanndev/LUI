using Shouldly;

namespace Nordmann.LUI.Integration.Tests;

/// <summary>
/// The typed view of an index (issue #59, ADR-0044): <c>index.As&lt;Chip&gt;()</c> gives the answer in the type of the
/// application — the payload is read where it is used, and the compiler checks the type. The untyped way stays:
/// <c>hit.GetPayload&lt;Chip&gt;()</c> is still there, and still the one way for the application that does not want a
/// view.
/// </summary>
public sealed class TypedIndexTests
{
    [Fact]
    public void The_typed_view_reads_the_payload_in_the_type_of_the_application()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        tags.Upsert(new LuiEntry(1, "vegan") { Payload = new Chip("vegan", "#0a0") });

        var view = tags.As<Chip>();
        view.Name.ShouldBe("tags");
        view.Count().ShouldBe(1);

        var hit = view.Search("vegan").Hits.ShouldHaveSingleItem();
        hit.GetPayload().ShouldBe(new Chip("vegan", "#0a0"));
        hit.HasPayload.ShouldBeTrue();
        hit.Key.ShouldBe(1);
    }

    [Fact]
    public void The_typed_view_answers_as_the_index_answers()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        tags.Upsert(new LuiEntry(1, "vegan") { Payload = new Chip("vegan") });
        tags.Upsert(new LuiEntry(2, "vegetarian"));

        var untyped = tags.Search("vegan");
        var typed = tags.As<Chip>().Search("vegan");

        typed.Query.ShouldBe(untyped.Query);
        typed.CandidatesConsidered.ShouldBe(untyped.CandidatesConsidered);
        typed.Truncated.ShouldBe(untyped.Truncated);
        typed.CorrectedQuery.ShouldBe(untyped.CorrectedQuery);
        typed.CorrectionSkipped.ShouldBe(untyped.CorrectionSkipped);
        typed.Hits.Count.ShouldBe(untyped.Hits.Count);
        typed.Hits.Select(hit => hit.Key).ShouldBe(untyped.Hits.Select(hit => hit.Key));
        typed.Hits.Select(hit => hit.Text).ShouldBe(untyped.Hits.Select(hit => hit.Text));
        typed.Hits.Select(hit => hit.Weight).ShouldBe(untyped.Hits.Select(hit => hit.Weight));
        typed.Hits.Select(hit => hit.Priority).ShouldBe(untyped.Hits.Select(hit => hit.Priority));
        typed.Hits.Select(hit => hit.Similarity).ShouldBe(untyped.Hits.Select(hit => hit.Similarity));
        typed.Hits.Select(hit => hit.AtWordStart).ShouldBe(untyped.Hits.Select(hit => hit.AtWordStart));
    }

    [Fact]
    public void The_fluent_path_ends_in_the_typed_answer_too()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        tags.Upsert(new LuiEntry(1, "vegan") { Weight = 10, Payload = new Chip("vegan", "#0a0") });
        tags.Upsert(new LuiEntry(2, "vegetarian") { Weight = 5 });

        // the fluent query ends in an untyped answer — and the answer names the type where it is used (ADR-0044)
        var typed = tags.Query("veg").Take(2).Execute().As<Chip>();

        typed.Hits.Select(hit => hit.Text).ShouldBe(["vegan", "vegetarian"]);
        typed.Hits[0].TryGetPayload(out var chip).ShouldBeTrue();
        chip.ShouldBe(new Chip("vegan", "#0a0"));
        typed.Hits[1].TryGetPayload(out _).ShouldBeFalse("the entry has no payload");
        typed.Hits[1].GetPayload().ShouldBeNull();
    }
}
