using Shouldly;
using static Nordmann.LUI.Integration.Tests.Linked.LinkedTable;

namespace Nordmann.LUI.Integration.Tests.Linked;

/// <summary>
/// The linked definition with a payload and the typed linked builder (ADR-0073, issue #269): <c>WithPayload</c> returns a
/// <c>LuiLinkedIndexBuilder&lt;TPayload&gt;</c> that has the same option methods in any order, each definition keeps its own map,
/// and the payload comes with the hit of the handle that <c>Create</c> or <c>Index</c> returns.
/// </summary>
public sealed class TypedLinkedPayloadTests
{
    private const string Rows = "INSERT INTO tag(id, name, usage_count, slug, color) VALUES (1, 'vegan', 5, 'vegan', '#0a0'), (2, 'veganer', 4, 'veganer', NULL);";

    private static readonly LuiLinkedIndexDefinition<long, Chip> Chips =
        new("chips", index => index.SingleWord().LinkTo("tag", "id", "name", weight: "usage_count")
            .WithPayload(["slug", "color"], row => new Chip(row.GetString("slug"), row.IsNull("color") ? null : row.GetString("color"))));

    [Fact]
    public void Index_of_a_linked_payload_definition_returns_a_linked_payload_handle()
    {
        using var database = Prepared();
        using var catalog = database.Open();
        catalog.Create(Chips);

        LuiLinkedIndex<long, Chip> chips = catalog.Index(Chips);
        LuiLinkedIndex<long, Chip> created = catalog.Create(Chips);

        chips.Name.ShouldBe("chips");
        created.Name.ShouldBe("chips");
        chips.ShouldBeAssignableTo<LuiIndex<long, Chip>>();
        Chips.PayloadType.ShouldBe(typeof(Chip));
        Chips.Source.ShouldBe(LuiIndexSource.Linked);
        Chips.ToString().ShouldBe("chips (linked, long, Chip)");
    }

    [Fact]
    public void The_typed_linked_handle_syncs_and_searches_with_typed_keys()
    {
        using var database = Prepared();
        using var catalog = database.Open();
        var chips = catalog.Create(Chips);
        chips.Count().ShouldBe(0, "Create syncs nothing");

        chips.Sync().Indexed.ShouldBe(2);

        LuiResult<long, Chip> result = chips.Search("vegan");
        result.Hits.Select(hit => hit.Key).ShouldBe([1L, 2L]);
        result.Hits[0].Text.ShouldBe("vegan");
        chips.Query("vegan").Take(1).Execute().Hits.ShouldHaveSingleItem().Key.ShouldBe(1L);
        chips.Refresh(1).ShouldBeTrue();
        chips.RefreshMany([1L, 2L]).ShouldBe(2);
        chips.Pending().ShouldBe(0);
        chips.Triggers().ShouldBe(LuiTriggerState.None);
        chips.TriggerScript().ShouldContain("tag");
    }

    [Fact]
    public void WithPayload_and_options_in_any_order()
    {
        using var database = Prepared();
        using var catalog = database.Open();
        var before = new LuiLinkedIndexDefinition<long, Chip>(
            "chips", index => index.MultiWord().LinkTo("tag", "id", "name", weight: "usage_count")
                .TypoTolerance(LuiTypoDistance.TwoErrors).GermanTransliteration(false).Similarity(LuiSimilarity.NgramDice)
                .WithPayload(["slug"], row => new Chip(row.GetString("slug"))));
        var after = new LuiLinkedIndexDefinition<long, Chip>(
            "chips", index => index.MultiWord().LinkTo("tag", "id", "name", weight: "usage_count")
                .WithPayload(["slug"], row => new Chip(row.GetString("slug")))
                .TypoTolerance(LuiTypoDistance.TwoErrors).GermanTransliteration(false).Similarity(LuiSimilarity.NgramDice));

        var created = catalog.Create(before);

        created.TypoDistance.ShouldBe(LuiTypoDistance.TwoErrors);
        catalog.Index(after).TypoDistance.ShouldBe(LuiTypoDistance.TwoErrors);
        Should.NotThrow(() => catalog.Create(after), "the stored definition is the same in either order");
    }

    [Fact]
    public void Two_definitions_with_the_same_view_type_use_their_own_maps()
    {
        using var database = Prepared();
        using var catalog = database.Open();
        var calls = new List<string>();
        var plain = new LuiLinkedIndexDefinition<long, Chip>(
            "plain_chips", index => index.SingleWord().LinkTo("tag", "id", "name").WithPayload(["slug"], row => Mark(calls, "plain", row)));
        var loud = new LuiLinkedIndexDefinition<long, Chip>(
            "loud_chips", index => index.SingleWord().LinkTo("tag", "id", "name").WithPayload(["slug"], row => Mark(calls, "loud", row)));
        var plainChips = catalog.Create(plain);
        var loudChips = catalog.Create(loud);
        plainChips.Sync();
        loudChips.Sync();

        plainChips.Search("vegan");
        calls.ShouldBe(["plain:vegan", "plain:veganer"], ignoreOrder: true);
        calls.Clear();

        loudChips.Search("vegan");
        calls.ShouldBe(["loud:vegan", "loud:veganer"], ignoreOrder: true);
        calls.Clear();

        // opened again, each handle still uses the map of its own definition
        catalog.Index(plain).Search("vegan");
        calls.ShouldAllBe(call => call.StartsWith("plain:", StringComparison.Ordinal));
        calls.Count.ShouldBe(2);
    }

    [Fact]
    public void A_linked_definition_with_WithPayload_creates_the_handle_and_the_definition_opens_it()
    {
        using var database = Prepared();
        using var catalog = database.Open();

        var tags = catalog.Create(new LuiLinkedIndexDefinition<long, Chip>("chips", index => index.SingleWord().LinkTo("tag", "id", "name", weight: "usage_count")
            .WithPayload(["slug", "color"], row => new Chip(row.GetString("slug"), row.IsNull("color") ? null : row.GetString("color")))));
        tags.Sync();

        tags.Search("vegan").Hits[0].Payload.ShouldBe(new Chip("vegan", "#0a0"));
        tags.Search("vegan").Hits[1].Payload.ShouldBe(new Chip("veganer"));

        // the same stored definition, and the handle of the definition opens the index
        var typed = catalog.Index(Chips);
        typed.Search("vegan").Hits.Select(hit => hit.Key).ShouldBe([1L, 2L]);

        // the options may come after WithPayload, too
        var options = catalog.Create(new LuiLinkedIndexDefinition<long, string>("other", index => index.SingleWord().LinkTo("tag", "id", "name")
            .WithPayload(["slug"], row => row.GetString("slug")).TypoTolerance()));
        options.TypoDistance.ShouldBe(LuiTypoDistance.OneError);
    }

    [Fact]
    public void Changing_the_payload_columns_raises_a_mismatch_and_changing_only_the_map_does_not()
    {
        using var database = Prepared();
        using var catalog = database.Open();
        catalog.Create(Chips);

        var otherMap = new LuiLinkedIndexDefinition<long, Chip>(
            "chips", index => index.SingleWord().LinkTo("tag", "id", "name", weight: "usage_count")
                .WithPayload(["slug", "color"], row => new Chip("other", row.IsNull("color") ? null : row.GetString("color"))));
        var otherColumns = new LuiLinkedIndexDefinition<long, Chip>(
            "chips", index => index.SingleWord().LinkTo("tag", "id", "name", weight: "usage_count")
                .WithPayload(["slug"], row => new Chip(row.GetString("slug"))));
        var noPayload = new LuiLinkedIndexDefinition<long>("chips", index => index.SingleWord().LinkTo("tag", "id", "name", weight: "usage_count"));

        Should.NotThrow(() => catalog.Index(otherMap));
        Should.NotThrow(() => catalog.Create(otherMap));
        Should.Throw<LuiDefinitionMismatchException>(() => catalog.Index(otherColumns)).Message.ShouldContain("link.payloadColumns");
        Should.Throw<LuiDefinitionMismatchException>(() => catalog.Create(otherColumns)).Message.ShouldContain("link.payloadColumns");
        Should.Throw<LuiDefinitionMismatchException>(() => catalog.Index(noPayload)).Message.ShouldContain("link.payloadColumns");
    }

    [Fact]
    public void A_missing_payload_column_throws_a_linked_table_exception_naming_the_index()
    {
        using var database = Prepared();
        using var catalog = database.Open();
        var missing = new LuiLinkedIndexDefinition<long, string>(
            "missing_column", index => index.SingleWord().LinkTo("tag", "id", "name").WithPayload(["shade"], row => row.GetString("shade")));

        Should.Throw<LuiLinkedTableException>(() => catalog.Create(missing)).Message.ShouldContain("missing_column", Case.Sensitive);
        database.Scalar<long>("SELECT count(*) FROM lui_meta").ShouldBe(0);
    }

    [Fact]
    public void An_invalid_linked_payload_definition_throws_a_configuration_exception_naming_the_definition()
    {
        using var database = Prepared();
        using var catalog = database.Open();
        var duplicate = new LuiLinkedIndexDefinition<long, string>(
            "dup_columns", index => index.SingleWord().LinkTo("tag", "id", "name").WithPayload(["slug", "Slug"], row => row.GetString("slug")));
        var doubleKey = new LuiLinkedIndexDefinition<double, string>(
            "double_keys", index => index.SingleWord().LinkTo("tag", "id", "name").WithPayload(["slug"], row => row.GetString("slug")));

        Should.Throw<LuiConfigurationException>(() => catalog.Create(duplicate)).Message.ShouldContain("dup_columns", Case.Sensitive);
        Should.Throw<LuiConfigurationException>(() => catalog.Index(doubleKey)).Message.ShouldContain("double_keys", Case.Sensitive);
        Should.Throw<LuiConfigurationException>(() => catalog.Create(Chips, duplicate)).Message.ShouldContain("dup_columns", Case.Sensitive);
    }

    [Fact]
    public void An_operation_on_a_dropped_index_throws_index_not_found()
    {
        using var database = Prepared();
        using var catalog = database.Open();
        var chips = catalog.Create(Chips);
        catalog.DropIndex("chips").ShouldBeTrue();

        Should.Throw<LuiIndexNotFoundException>(() => chips.Count()).IndexName.ShouldBe("chips");
        Should.Throw<LuiIndexNotFoundException>(() => chips.Search("veg"));
        Should.Throw<LuiIndexNotFoundException>(() => chips.Sync());
        Should.Throw<LuiIndexNotFoundException>(() => chips.Refresh(1));

        using var connection = database.Connect();
        using var transaction = connection.BeginTransaction();
        Should.Throw<LuiIndexNotFoundException>(() => chips.Refresh(1, transaction)).IndexName.ShouldBe("chips");
        Should.Throw<LuiIndexNotFoundException>(() => chips.RefreshMany([1L], transaction));
    }

    [Fact]
    public void An_operation_on_a_disposed_catalog_throws_object_disposed()
    {
        using var database = Prepared();
        var catalog = database.Open();
        var chips = catalog.Create(Chips);
        catalog.Dispose();

        Should.Throw<ObjectDisposedException>(() => chips.Count());
        Should.Throw<ObjectDisposedException>(() => chips.Sync());
        Should.Throw<ObjectDisposedException>(() => catalog.Index(Chips));
    }

    private static Chip Mark(List<string> calls, string who, LuiPayloadRow row)
    {
        var slug = row.GetString("slug");
        lock (calls)
        {
            calls.Add($"{who}:{slug}");
        }

        return new Chip(slug);
    }

    private static TestDatabase Prepared()
    {
        var database = new TestDatabase();
        Run(database, Table);
        Run(database, Rows);
        return database;
    }
}
