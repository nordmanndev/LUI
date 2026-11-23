using Shouldly;

namespace Nordmann.LUI.Integration.Tests.Embedded;

/// <summary>
/// The payload of an <c>Embedded</c> index (ADR-0046, ADR-0072, ADR-0074): stored as bytes by the serializer the registry of the
/// catalog holds for its type, loaded with the query for the hits it returns and for no others, and refused when nobody can
/// serialize it — when the index is created, before anything is written.
/// </summary>
public sealed class EmbeddedPayloadTests
{
    private static readonly LuiIndexDefinition<long, Chip> Chips = new("tags", index => index.SingleWord());

    [Fact]
    public void A_payload_whose_type_the_registry_does_not_know_is_refused_when_the_index_is_created()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();

        var exception = Should.Throw<LuiConfigurationException>(
            () => catalog.Create(new LuiIndexDefinition<long, Uri>("tags", index => index.SingleWord())));

        exception.Message.ShouldContain("System.Uri");
        database.Scalar<long>("SELECT count(*) FROM lui_meta;").ShouldBe(0, "nothing was written");
    }

    [Fact]
    public void A_payload_in_a_catalog_without_a_registry_is_refused_and_an_index_without_a_payload_needs_none()
    {
        using var database = new TestDatabase();
        using var catalog = LuiCatalog.OpenOrCreate(database.FilePath);

        var exception = Should.Throw<LuiConfigurationException>(() => catalog.Create(Chips));

        exception.Message.ShouldContain(nameof(LuiCatalogOptions.Payloads));
        database.Scalar<long>("SELECT count(*) FROM lui_meta;").ShouldBe(0, "the index was not created");
        var plain = catalog.Create(new LuiIndexDefinition<long>("plain", index => index.SingleWord()));
        Should.NotThrow(() => plain.Upsert(3, "ohne"), "an entry without a payload needs no serializer");
    }

    [Fact]
    public void Two_hits_of_the_same_entry_are_equal_also_with_a_payload()
    {
        // review of PR 60, M4: a hit is a value — its equality includes the payload (ADR-0074: the payload's own equality)
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create(Chips);
        tags.Upsert(new LuiEntry<long, Chip>(1, "vegan", new Chip("v", "green")));

        var first = tags.Search("vegan").Hits.ShouldHaveSingleItem();
        var second = tags.Search("vegan").Hits.ShouldHaveSingleItem();
        var opened = catalog.Index(Chips).Search("vegan").Hits.ShouldHaveSingleItem();

        first.ShouldBe(second);
        first.GetHashCode().ShouldBe(second.GetHashCode());
        first.ShouldBe(opened, "another instance of the index reads the same entry");

        tags.Upsert(new LuiEntry<long, Chip>(1, "vegan", new Chip("v", "teal")));
        tags.Search("vegan").Hits.ShouldHaveSingleItem().ShouldNotBe(first, "another payload is another hit");
    }

    [Fact]
    public void A_payload_comes_back_with_the_hit()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create(Chips);

        tags.Upsert(new LuiEntry<long, Chip>(1, "vegan", new Chip("vegan", "#0a0")) { Weight = 5 });
        tags.Upsert(2, "veganer-kuchen", new Chip("kuchen"), 4);

        var hits = tags.Query("vegan").Rank(LuiRankingOrder.WeightOnly).Execute().Hits;
        hits[0].Payload.ShouldBe(new Chip("vegan", "#0a0"));
        hits[1].Payload.ShouldBe(new Chip("kuchen"));
    }

    [Fact]
    public void The_payloads_of_the_hits_are_read_with_the_query_and_those_of_the_candidates_are_not()
    {
        // ADR-0074 (it replaces "a payload is read when it is asked for and not before"): the query deserializes the payloads of the
        // hits it returns — five of the two hundred candidates — and reading hit.Payload deserializes nothing
        using var database = new TestDatabase();
        var serializer = new CountingSerializer();
        using var catalog = database.Open(new LuiCatalogOptions { Payloads = new LuiPayloadRegistry().Use(serializer) });
        var tags = catalog.Create(Chips);
        for (var i = 1; i <= 200; i++)
        {
            tags.Upsert(new LuiEntry<long, Chip>(i, $"kuchen{i:000}", new Chip($"k{i}")) { Weight = i });
        }

        var result = tags.Query("kuchen").Take(5).Execute();

        result.CandidatesConsidered.ShouldBe(200);
        result.Hits.Count.ShouldBe(5);
        serializer.Deserialized.ShouldBe(5, "the query deserializes the payloads of its five hits");

        result.Hits[0].Payload.ShouldNotBeNull();
        serializer.Deserialized.ShouldBe(5, "reading the payload of a hit reads nothing more");
    }

    // review of pull request 6, B1: a value-type payload
    [Fact]
    public void An_index_without_a_payload_takes_the_number_after_the_text_as_its_weight()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var nums = catalog.Create(new LuiIndexDefinition<long>("nums", index => index.SingleWord()));

        Should.NotThrow(() => nums.Upsert(1, "vegan", 5));

        nums.Query("vegan").Execute().Hits.ShouldHaveSingleItem().Weight.ShouldBe(5);
        database.Scalar<long>("SELECT count(*) FROM lui_nums WHERE payload IS NOT NULL").ShouldBe(0);
    }

    // review of pull request 6, B1: a value-type payload
    [Fact]
    public void A_value_type_payload_is_stored_even_when_it_is_its_default()
    {
        // ADR-0035: only null means "none" — a 0 is a payload like any other
        using var database = new TestDatabase();
        using var catalog = database.Open(new LuiCatalogOptions { Payloads = new LuiPayloadRegistry().Use(new IntSerializer()) });
        var nums = catalog.Create(new LuiIndexDefinition<long, int>("nums", index => index.SingleWord()));

        nums.Upsert(new LuiEntry<long, int>(1, "vegan", 42) { Weight = 5 });
        nums.Upsert(new LuiEntry<long, int>(2, "veganer", 0) { Weight = 4 });

        var hits = nums.Query("vegan").Rank(LuiRankingOrder.WeightOnly).Execute().Hits;
        hits[0].Payload.ShouldBe(42);
        hits[1].Payload.ShouldBe(0);
        database.Scalar<long>("SELECT count(*) FROM lui_nums WHERE payload IS NULL").ShouldBe(0);
    }

    private sealed class CountingSerializer : ILuiPayloadSerializer<Chip>
    {
        internal int Deserialized { get; private set; }

        public string Format => "counting-v1";

        public byte[] Serialize(Chip payload) => System.Text.Encoding.UTF8.GetBytes(payload.Slug);

        public Chip Deserialize(ReadOnlySpan<byte> bytes)
        {
            Deserialized++;
            return new Chip(System.Text.Encoding.UTF8.GetString(bytes));
        }
    }

    private sealed class IntSerializer : ILuiPayloadSerializer<int>
    {
        public string Format => "int-v1";

        public byte[] Serialize(int payload) => BitConverter.GetBytes(payload);

        public int Deserialize(ReadOnlySpan<byte> bytes) => BitConverter.ToInt32(bytes);
    }
}
