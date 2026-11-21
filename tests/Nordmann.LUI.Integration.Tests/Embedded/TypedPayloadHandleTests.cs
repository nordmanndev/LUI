using Shouldly;

namespace Nordmann.LUI.Integration.Tests.Embedded;

/// <summary>
/// The payload variants of the definitions and the handles (ADR-0073, issue #269): the definition declares the type of the
/// payload, the handle writes typed payloads — and nothing else — with the serializer of the catalog. Until #262 a written
/// payload is checked through the old untyped path (<c>catalog.Index(name)</c> and <c>hit.GetPayload&lt;T&gt;()</c>).
/// </summary>
public sealed class TypedPayloadHandleTests
{
    private static readonly LuiIndexDefinition<long, Chip> Chips = new("chips", index => index.SingleWord());

    private static readonly LuiIndexDefinition<long, int> Numbers = new("numbers", index => index.SingleWord());

    private static readonly LuiIndexDefinition<long> PlainChips = new("chips", index => index.SingleWord());

    [Fact]
    public void Index_of_a_payload_definition_returns_a_payload_handle()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create(Chips);

        LuiEmbeddedIndex<long, Chip> chips = catalog.Index(Chips);
        LuiEmbeddedIndex<long, Chip> created = catalog.Create(Chips);

        chips.Name.ShouldBe("chips");
        chips.Kind.ShouldBe(LuiIndexKind.SingleWord);
        created.Name.ShouldBe("chips");
        chips.ShouldBeAssignableTo<LuiIndex<long, Chip>>();
        Chips.PayloadType.ShouldBe(typeof(Chip));
        Chips.KeyType.ShouldBe(typeof(long));
        Chips.Source.ShouldBe(LuiIndexSource.Embedded);
        Chips.ToString().ShouldBe("chips (embedded, long, Chip)");
        PlainChips.PayloadType.ShouldBeNull();
    }

    [Fact]
    public void Typed_writes_store_the_payload()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var chips = catalog.Create(Chips);

        chips.Upsert(1, "vegan", new Chip("one", "green"), weight: 5);
        chips.Upsert(new LuiEntry<long, Chip>(2, "vegetarisch", new Chip("two")) { Weight = 3, Priority = LuiPriority.High });
        chips.UpsertMany([new LuiEntry<long, Chip>(3, "veggie", new Chip("three")), new LuiEntry<long, Chip>(4, "vegetable", new Chip("four"))]).ShouldBe(2);

        LuiEntry<long, Chip>[] all =
        [
            new(1, "vegan", new Chip("one", "green")) { Weight = 5 },
            new(2, "vegetarisch", new Chip("two")) { Weight = 3, Priority = LuiPriority.High },
            new(3, "veggie", new Chip("three")),
            new(4, "vegetable", new Chip("four")),
            new(5, "vegane torte", new Chip("five")),
        ];
        chips.Sync(all).Added.ShouldBe(1);

        // the old untyped path reads what the typed handle wrote — until #262 brings hit.Payload
        var hits = catalog.Index("chips").Search("veg").Hits.ToDictionary(hit => hit.Key.ToInt64());
        hits.Keys.Order().ShouldBe([1L, 2L, 3L, 4L, 5L]);
        hits[1].GetPayload<Chip>().ShouldBe(new Chip("one", "green"));
        hits[2].GetPayload<Chip>().ShouldBe(new Chip("two"));
        hits[3].GetPayload<Chip>().ShouldBe(new Chip("three"));
        hits[4].GetPayload<Chip>().ShouldBe(new Chip("four"));
        hits[5].GetPayload<Chip>().ShouldBe(new Chip("five"));
        hits[2].Priority.ShouldBe(LuiPriority.High);
        hits[1].Weight.ShouldBe(5);
    }

    [Fact]
    public void An_async_sync_writes_the_payload_and_returns_the_report_of_the_same_entries()
    {
        using var database = new TestDatabase();
        using var other = new TestDatabase();
        using var catalog = database.Open();
        using var otherCatalog = other.Open();
        var chips = catalog.Create(Chips);
        var sameChips = otherCatalog.Create(Chips);
        LuiEntry<long, Chip>[] entries = [new(1, "vegan", new Chip("one")), new(2, "veganer", new Chip("two"))];

        var report = chips.Sync(Asynchronous(entries));

        report.ShouldBe(sameChips.Sync(entries));
        catalog.Index("chips").Search("vegan").Hits.Select(hit => hit.GetPayload<Chip>()).ShouldBe([new Chip("one"), new Chip("two")], ignoreOrder: true);
    }

    [Fact]
    public void The_typed_search_returns_typed_keys_and_the_fluent_query_runs()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var chips = catalog.Create(Chips);
        chips.Upsert(42, "vegan", new Chip("v"));

        LuiResult<long, Chip> result = chips.Search("vegan");
        LuiHit<long, Chip> hit = result.Hits.ShouldHaveSingleItem();
        hit.Key.ShouldBe(42L);
        hit.Text.ShouldBe("vegan");

        LuiResult<long, Chip> queried = chips.Query("vegan").Take(1).Candidates(100).Rank(LuiRankingOrder.WordStartThenSimilarity)
            .PriorityMode(LuiPriorityMode.First).Similarity(LuiSimilarity.NgramDice).TypoTolerance(false).TrackTimings().Execute();
        queried.Hits.ShouldHaveSingleItem().Key.ShouldBe(42L);
        queried.Timings.ShouldNotBeNull();
        chips.Count().ShouldBe(1);
    }

    [Fact]
    public void A_null_payload_throws()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var chips = catalog.Create(Chips);

        Should.Throw<ArgumentNullException>(() => new LuiEntry<long, Chip>(1, "vegan", null!)).ParamName.ShouldBe("payload");
        Should.Throw<ArgumentNullException>(() => chips.Upsert(1, "vegan", null!)).ParamName.ShouldBe("payload");
        chips.Count().ShouldBe(0);
    }

    [Fact]
    public void An_entry_with_cannot_set_a_null_payload()
    {
        // the payload has no `init`: `entry with { Payload = null }` does not compile, and a copy keeps the payload it had
        typeof(LuiEntry<long, Chip>).GetProperty(nameof(LuiEntry<long, Chip>.Payload))!.SetMethod.ShouldBeNull();
        var payload = new Chip("one");
        var entry = new LuiEntry<long, Chip>(1, "vegan", payload);

        var copy = entry with { Weight = 3, Priority = LuiPriority.High };

        copy.Payload.ShouldBeSameAs(payload);
        copy.Weight.ShouldBe(3);
        entry.ShouldBe(new LuiEntry<long, Chip>(1, "vegan", payload), "a record: value equality, the payload included");
        entry.ShouldNotBe(new LuiEntry<long, Chip>(1, "vegan", new Chip("other")));
        Should.Throw<ArgumentException>(() => new LuiEntry<long, Chip>(1, "", payload)).ParamName.ShouldBe("text");
    }

    [Fact]
    public void With_an_int_payload_Upsert_1_x_5_writes_the_payload_5()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open(new LuiCatalogOptions { PayloadSerializer = new IntSerializer() });
        var numbers = catalog.Create(Numbers);

        numbers.Upsert(1, "vegan", 5);

        var hit = catalog.Index("numbers").Search("vegan").Hits.ShouldHaveSingleItem();
        hit.GetPayload<int>().ShouldBe(5);
        hit.Weight.ShouldBe(0, "the 5 is the payload, never a weight");
    }

    [Fact]
    public void A_typed_write_without_a_serializer_throws_a_configuration_exception()
    {
        using var database = new TestDatabase();
        using var catalog = LuiCatalog.OpenOrCreate(database.FilePath);
        var chips = catalog.Create(Chips);
        var entry = new LuiEntry<long, Chip>(1, "vegan", new Chip("one"));

        Should.Throw<LuiConfigurationException>(() => chips.Upsert(1, "vegan", new Chip("one"))).Message.ShouldContain("chips", Case.Sensitive);
        Should.Throw<LuiConfigurationException>(() => chips.Upsert(entry)).Message.ShouldContain(nameof(LuiCatalogOptions.PayloadSerializer));
        Should.Throw<LuiConfigurationException>(() => chips.UpsertMany([entry])).Message.ShouldContain("chips", Case.Sensitive);
        Should.Throw<LuiConfigurationException>(() => chips.Sync([entry])).Message.ShouldContain("chips", Case.Sensitive);
        Should.Throw<LuiConfigurationException>(() => chips.Sync(Asynchronous([entry]))).Message.ShouldContain("chips", Case.Sensitive);
        chips.Count().ShouldBe(0, "nothing was written");
        chips.Search("vegan").Hits.ShouldBeEmpty("the read side needs no serializer");
    }

    [Fact]
    public void A_payload_definition_stores_the_definition_of_an_index_without_payload()
    {
        // the type of the payload is part of the stored definition from #260 on; until then the same row
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create(Chips);

        Should.NotThrow(() => catalog.Index(PlainChips));
        Should.NotThrow(() => catalog.Create(PlainChips));
        catalog.Index(Chips).Name.ShouldBe("chips");
    }

    [Fact]
    public void Payload_definitions_are_created_in_one_call_with_the_others()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();

        catalog.Create(Chips, Numbers, new LuiIndexDefinition<long>("plain", index => index.MultiWord()));

        catalog.Index(Chips).Name.ShouldBe("chips");
        catalog.Index(Numbers).Name.ShouldBe("numbers");
        Should.Throw<ArgumentException>(() => catalog.Create(Chips, Chips)).Message.ShouldContain("chips", Case.Sensitive);
    }

    [Fact]
    public void An_invalid_payload_definition_throws_a_configuration_exception_naming_the_definition()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var broken = new (string Name, Action Create)[]
        {
            ("Bad Name", () => catalog.Create(new LuiIndexDefinition<long, Chip>("Bad Name", index => index.SingleWord()))),
            ("double_keys", () => catalog.Create(new LuiIndexDefinition<double, Chip>("double_keys", index => index.SingleWord()))),
            ("with_key", () => catalog.Create(new LuiIndexDefinition<long, Chip>("with_key", index => index.SingleWord().WithKey(LuiKeyKind.Text)))),
            ("null_function", () => catalog.Create(new LuiIndexDefinition<long, Chip>("null_function", null!))),
            ("batch", () => catalog.Create(Numbers, new LuiIndexDefinition<long, Chip>("batch", index => index.SingleWord().WithKey(LuiKeyKind.Text)))),
        };

        foreach (var (name, create) in broken)
        {
            Should.Throw<LuiConfigurationException>(create).Message.ShouldContain(name, Case.Sensitive);
        }

        database.Scalar<long>("SELECT count(*) FROM lui_meta").ShouldBe(0, "a batch is all or nothing");
    }

    [Fact]
    public void An_operation_on_a_dropped_index_throws_index_not_found()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var chips = catalog.Create(Chips);
        var entry = new LuiEntry<long, Chip>(1, "vegan", new Chip("one"));
        catalog.DropIndex("chips").ShouldBeTrue();

        Should.Throw<LuiIndexNotFoundException>(() => chips.Count()).IndexName.ShouldBe("chips");
        Should.Throw<LuiIndexNotFoundException>(() => chips.Upsert(1, "vegan", new Chip("one")));
        Should.Throw<LuiIndexNotFoundException>(() => chips.UpsertMany([entry]));
        Should.Throw<LuiIndexNotFoundException>(() => chips.Search("veg"));
        Should.Throw<LuiIndexNotFoundException>(() => chips.Query("veg").Execute());
        Should.Throw<LuiIndexNotFoundException>(() => chips.Remove(1));
        Should.Throw<LuiIndexNotFoundException>(() => chips.Check());
        Should.Throw<LuiIndexNotFoundException>(() => chips.Check([entry]));
        Should.Throw<LuiIndexNotFoundException>(() => catalog.Index(Chips));
    }

    [Fact]
    public void A_write_with_a_transaction_of_the_application_on_a_dropped_index_throws_index_not_found()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var chips = catalog.Create(Chips);
        var entry = new LuiEntry<long, Chip>(1, "vegan", new Chip("one"));
        catalog.DropIndex("chips").ShouldBeTrue();

        using var connection = database.Connect();
        using var transaction = connection.BeginTransaction();

        Should.Throw<LuiIndexNotFoundException>(() => chips.Upsert(1, "vegan", new Chip("one"), transaction: transaction)).IndexName.ShouldBe("chips");
        Should.Throw<LuiIndexNotFoundException>(() => chips.Upsert(entry, transaction));
        Should.Throw<LuiIndexNotFoundException>(() => chips.UpsertMany([entry], transaction));
        Should.Throw<LuiIndexNotFoundException>(() => chips.Remove(1, transaction));
        Should.Throw<LuiIndexNotFoundException>(() => chips.UpdateWeight(1, 5, transaction));
        Should.Throw<LuiIndexNotFoundException>(() => chips.UpdatePriority(1, LuiPriority.Normal, transaction));
    }

    [Fact]
    public void An_operation_on_a_disposed_catalog_throws_object_disposed()
    {
        using var database = new TestDatabase();
        var catalog = database.Open();
        var chips = catalog.Create(Chips);
        catalog.Dispose();

        Should.Throw<ObjectDisposedException>(() => chips.Count());
        Should.Throw<ObjectDisposedException>(() => chips.Upsert(1, "vegan", new Chip("one")));
        Should.Throw<ObjectDisposedException>(() => chips.Search("veg"));
        Should.Throw<ObjectDisposedException>(() => catalog.Index(Chips));
    }

    [Fact]
    public void Writes_inside_a_transaction_of_the_application_commit_with_it()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var chips = catalog.Create(Chips);
        using var connection = database.Connect();

        using (var transaction = connection.BeginTransaction())
        {
            chips.Upsert(1, "vegan", new Chip("one"), transaction: transaction);
            transaction.Rollback();
        }

        chips.Count().ShouldBe(0);
        using (var transaction = connection.BeginTransaction())
        {
            chips.Upsert(1, "vegan", new Chip("one"), transaction: transaction);
            transaction.Commit();
        }

        chips.Count().ShouldBe(1);
        chips.UpdateWeight(1, 7).ShouldBeTrue();
        chips.UpdatePriority(1, LuiPriority.High).ShouldBeTrue();
        chips.Remove(1).ShouldBeTrue();
    }

    private static async IAsyncEnumerable<LuiEntry<long, Chip>> Asynchronous(IEnumerable<LuiEntry<long, Chip>> entries)
    {
        foreach (var entry in entries)
        {
            await Task.Yield();
            yield return entry;
        }
    }

    private sealed class IntSerializer : ILuiPayloadSerializer
    {
        public byte[] Serialize(object payload) => BitConverter.GetBytes((int)payload);

        public T Deserialize<T>(ReadOnlySpan<byte> bytes) => (T)(object)BitConverter.ToInt32(bytes);
    }
}
