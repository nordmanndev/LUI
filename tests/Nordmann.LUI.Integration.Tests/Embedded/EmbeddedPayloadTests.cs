using Shouldly;

namespace Nordmann.LUI.Integration.Tests.Embedded;

/// <summary>
/// The payload of an <c>Embedded</c> index (ADR-0046, ADR-0035): stored as bytes by the serialiser of the
/// catalog, read when it is asked for and not before, and refused on the write when nobody can serialise it.
/// </summary>
public sealed class EmbeddedPayloadTests
{
    [Fact]
    public void A_payload_whose_type_the_serialiser_does_not_know_is_refused_on_the_write()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());

        var exception = Should.Throw<NotSupportedException>(
            () => tags.Upsert(new LuiEntry(1, "vegan") { Payload = new Uri("https://example.org") }));

        exception.Message.ShouldContain("System.Uri");
        tags.Count().ShouldBe(0, "nothing was written");
    }

    [Fact]
    public void A_payload_in_a_catalog_without_a_serialiser_is_refused_on_the_write()
    {
        using var database = new TestDatabase();
        using var catalog = LuiCatalog.Open(database.ConnectionString);
        var tags = catalog.Create("tags", index => index.SingleWord());

        var exception = Should.Throw<InvalidOperationException>(
            () => tags.UpsertMany([new LuiEntry(1, "vegan"), new LuiEntry(2, "veganer") { Payload = new Chip("v") }]));

        exception.Message.ShouldContain(nameof(LuiCatalogOptions.PayloadSerializer));
        tags.Count().ShouldBe(0, "the batch is one transaction");
        Should.NotThrow(() => tags.Upsert(3, "ohne"), "an entry without a payload needs no serialiser");
    }

    [Fact]
    public void Two_hits_of_the_same_entry_are_equal_also_with_a_payload()
    {
        // review of PR 60, M4: a hit is a value — its equality compares the stored payload by its bytes, not by the
        // array that holds them, as it compared the deserialised payload before ADR-0035
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        tags.Upsert(new LuiEntry(1, "vegan") { Payload = new Chip("v", "green") });

        var first = tags.Search("vegan").Hits.ShouldHaveSingleItem();
        var second = tags.Search("vegan").Hits.ShouldHaveSingleItem();
        var opened = catalog.Index("tags").Search("vegan").Hits.ShouldHaveSingleItem();

        first.ShouldBe(second);
        first.GetHashCode().ShouldBe(second.GetHashCode());
        first.ShouldBe(opened, "another instance of the index reads the same entry");

        tags.Upsert(new LuiEntry(1, "vegan") { Payload = new Chip("v", "teal") });
        tags.Search("vegan").Hits.ShouldHaveSingleItem().ShouldNotBe(first, "another payload is another hit");
    }

    [Fact]
    public void A_payload_comes_back_with_the_hit()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());

        tags.Upsert(new(1, "vegan") { Weight = 5, Payload = new Chip("vegan", "#0a0") });
        tags.Upsert(2, "veganer-kuchen", 4);

        var hits = tags.Query("vegan").Rank(LuiRankingOrder.WeightOnly).Execute().Hits;
        hits[0].GetPayload<Chip>().ShouldBe(new Chip("vegan", "#0a0"));
        hits[1].HasPayload.ShouldBeFalse();
    }

    [Fact]
    public void A_payload_is_read_when_it_is_asked_for_and_not_before()
    {
        // ADR-0035: the query hands out the stored bytes; the hit deserialises them when the application asks
        using var database = new TestDatabase();
        var serializer = new CountingSerializer();
        using var catalog = database.Open(new LuiCatalogOptions { PayloadSerializer = serializer });
        var tags = catalog.Create("tags", index => index.SingleWord());
        for (var i = 1; i <= 200; i++)
        {
            tags.Upsert(new(i, $"kuchen{i:000}") { Weight = i, Payload = new Chip($"k{i}") });
        }

        var result = tags.Query("kuchen").Take(5).Execute();

        result.CandidatesConsidered.ShouldBe(200);
        result.Hits.Count.ShouldBe(5);
        serializer.Deserialized.ShouldBe(0, "the query deserialises nothing");

        result.Hits[0].GetPayload<Chip>().ShouldNotBeNull();
        serializer.Deserialized.ShouldBe(1, "one hit was asked");
    }

    // review of pull request 6, B1: a value-type payload
    [Fact]
    public void An_index_without_a_payload_accepts_an_upsert_whose_payload_is_a_value_type()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var nums = catalog.Create("nums", index => index.SingleWord());

        Should.NotThrow(() => nums.Upsert(1, "vegan", 5));

        nums.Query("vegan").Execute().Hits.ShouldHaveSingleItem().HasPayload.ShouldBeFalse();
    }

    // review of pull request 6, B1: a value-type payload
    [Fact]
    public void A_value_type_payload_is_stored_even_when_it_is_its_default()
    {
        // ADR-0035: the payload is an object, and only null means "none" — a 0 is a payload like any other
        using var database = new TestDatabase();
        using var catalog = database.Open(new LuiCatalogOptions { PayloadSerializer = new IntSerializer() });
        var nums = catalog.Create("nums", index => index.SingleWord());

        nums.Upsert(new(1, "vegan") { Weight = 5, Payload = 42 });
        nums.Upsert(new(2, "veganer") { Weight = 4, Payload = 0 });
        nums.Upsert(3, "vegane torte", 3);

        var hits = nums.Query("vegan").Rank(LuiRankingOrder.WeightOnly).Execute().Hits;
        hits[0].GetPayload<int>().ShouldBe(42);
        hits[1].HasPayload.ShouldBeTrue();
        hits[1].GetPayload<int>().ShouldBe(0);
        hits[2].HasPayload.ShouldBeFalse();
        database.Scalar<long>("SELECT count(*) FROM lui_nums WHERE payload IS NULL").ShouldBe(1);
    }

    private sealed class CountingSerializer : ILuiPayloadSerializer
    {
        internal int Deserialized { get; private set; }

        public byte[] Serialize(object payload) => System.Text.Encoding.UTF8.GetBytes(((Chip)payload).Slug);

        public T Deserialize<T>(ReadOnlySpan<byte> bytes)
        {
            Deserialized++;
            return (T)(object)new Chip(System.Text.Encoding.UTF8.GetString(bytes));
        }
    }

    private sealed class IntSerializer : ILuiPayloadSerializer
    {
        public byte[] Serialize(object payload) => BitConverter.GetBytes((int)payload);

        public T Deserialize<T>(ReadOnlySpan<byte> bytes) => (T)(object)BitConverter.ToInt32(bytes);
    }
}
