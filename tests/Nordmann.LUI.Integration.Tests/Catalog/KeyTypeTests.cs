using Shouldly;
using static Nordmann.LUI.Integration.Tests.Linked.LinkedTable;

namespace Nordmann.LUI.Integration.Tests.Catalog;

/// <summary>
/// The key types of a definition (ADR-0073, issue #270): <c>long</c>, <c>int</c>, <c>string</c> and <see cref="Guid"/> for an
/// embedded index, <c>long</c> and <c>int</c> for a linked one. The stored definition names the exact type, a definition with
/// another type is a changed definition, a type that is none of them is a configuration error, and a stored key that does
/// not convert fails the read with a <see cref="LuiKeyException"/>.
/// </summary>
public sealed class KeyTypeTests
{
    private static readonly Guid FirstGuid = new("0F8FAD5B-D9CB-469F-A165-70867728950E");
    private static readonly Guid SecondGuid = new("9B1DEB4D-3B7D-4BAD-9BDD-2B0D7B3DCB6D");

    // ---- embedded: every allowed type ------------------------------------------------------------------------------

    [Theory]
    [InlineData("long")]
    [InlineData("int")]
    [InlineData("string")]
    [InlineData("guid")]
    public void Embedded_keys_of_every_allowed_type_round_trip(string stored)
    {
        switch (stored)
        {
            case "long":
                RoundTrip(1L, 3_000_000_000L, stored);
                break;
            case "int":
                RoundTrip(1, int.MaxValue, stored);
                break;
            case "string":
                RoundTrip("01JC0FFEE", "slug-2", stored);
                break;
            default:
                RoundTrip(FirstGuid, SecondGuid, stored);
                break;
        }
    }

    private static void RoundTrip<TKey>(TKey first, TKey second, string stored)
        where TKey : notnull
    {
        using var database = new TestDatabase();
        var definition = new LuiIndexDefinition<TKey>("things", index => index.SingleWord());

        using (var catalog = database.Open())
        {
            var things = catalog.Create(definition);
            things.Upsert(first, "vegan", weight: 5);
            things.UpsertMany([new LuiEntry<TKey>(second, "vegetarian") { Weight = 3 }]);

            var hits = things.Query("veg").Rank(LuiRankingOrder.WeightOnly).Execute().Hits;
            hits.Select(hit => hit.Key).ShouldBe([first, second]);
            ((object)hits[0].Key).ShouldBeOfType<TKey>("hit.Key has the key type of the definition");

            things.UpdateWeight(second, 9).ShouldBeTrue();
            things.Query("veg").Rank(LuiRankingOrder.WeightOnly).Execute().Hits.Select(hit => hit.Key).ShouldBe([second, first]);
            things.UpdatePriority(first, LuiPriority.High).ShouldBeTrue();
            things.Query("veg").Rank(LuiRankingOrder.WeightOnly).Execute().Hits.Select(hit => hit.Key).ShouldBe([first, second]);

            LuiEntry<TKey>[] entries = [new(first, "vegan") { Weight = 5, Priority = LuiPriority.High }, new(second, "vegetarian") { Weight = 9 }];
            things.Sync(entries).Unchanged.ShouldBe(2);
            things.Check(entries).IsHealthy.ShouldBeTrue();
            things.Check().IsHealthy.ShouldBeTrue();

            things.Remove(first).ShouldBeTrue();
            things.Remove(first).ShouldBeFalse();
            things.Count().ShouldBe(1);
        }

        database.Scalar<string>("SELECT definition FROM lui_meta WHERE index_name = 'things';")
            .ShouldContain($"\"keyType\":\"{stored}\"", Case.Sensitive);

        // the next start opens the same index with the same definition
        using var again = database.Open();
        again.Index(definition).Search("veg").Hits.ShouldHaveSingleItem().Key.ShouldBe(second);
        again.Create(definition).Count().ShouldBe(1);
    }

    [Fact]
    public void A_guid_key_is_stored_in_the_canonical_lower_case_D_form()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var ids = catalog.Create(new LuiIndexDefinition<Guid>("ids", index => index.SingleWord()));

        ids.Upsert(FirstGuid, "vegan");
        ids.Upsert(FirstGuid, "vegan, again");

        database.Scalar<string>("SELECT key FROM lui_ids;").ShouldBe("0f8fad5b-d9cb-469f-a165-70867728950e");
        ids.Count().ShouldBe(1, "the same GUID is one key");
        ids.Remove(Guid.Parse(FirstGuid.ToString("N").ToUpperInvariant())).ShouldBeTrue();
        ids.Count().ShouldBe(0);
    }

    [Fact]
    public void An_int_key_is_stored_as_its_digits()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var counts = catalog.Create(new LuiIndexDefinition<int>("counts", index => index.SingleWord()));

        counts.Upsert(-7, "vegan");
        counts.Upsert(int.MinValue, "vegetarian");

        database.Scalar<string>("SELECT group_concat(key, '|') FROM (SELECT key FROM lui_counts ORDER BY doc_id);").ShouldBe("-7|-2147483648");
        counts.Search("veg").Hits.Select(hit => hit.Key).Order().ShouldBe([int.MinValue, -7]);
    }

    [Fact]
    public void The_stored_definition_names_the_exact_key_type_of_every_definition()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        using var catalog = database.Open();

        catalog.Create(
            new LuiIndexDefinition<long>("e_long", index => index.SingleWord()),
            new LuiIndexDefinition<int>("e_int", index => index.SingleWord()),
            new LuiIndexDefinition<string>("e_string", index => index.SingleWord()),
            new LuiIndexDefinition<Guid>("e_guid", index => index.SingleWord()),
            new LuiLinkedIndexDefinition<long>("l_long", index => index.SingleWord().LinkTo("tag", "id", "name")),
            new LuiLinkedIndexDefinition<int>("l_int", index => index.SingleWord().LinkTo("tag", "id", "name")));

        database.Scalar<string>(
                "SELECT group_concat(index_name || '=' || json_extract(definition, '$.keyType'), ',') FROM (SELECT * FROM lui_meta ORDER BY index_name);")
            .ShouldBe("e_guid=guid,e_int=int,e_long=long,e_string=string,l_int=int,l_long=long");
    }

    [Fact]
    public void A_string_definition_opens_an_index_the_old_calls_created_with_text_keys()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create("words", index => index.SingleWord().WithKey(LuiKeyKind.Text)).Upsert("01JC0FFEE", "vegan");

        var words = catalog.Index(new LuiIndexDefinition<string>("words", index => index.SingleWord()));

        words.Search("veg").Hits.ShouldHaveSingleItem().Key.ShouldBe("01JC0FFEE");
        Should.NotThrow(() => catalog.Create(new LuiIndexDefinition<string>("words", index => index.SingleWord())));
    }

    [Fact]
    public void An_index_of_the_old_calls_with_int_or_guid_keys_reads_as_numbers_and_as_texts()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var counts = catalog.Create(new LuiIndexDefinition<int>("counts", index => index.SingleWord()));
        var ids = catalog.Create(new LuiIndexDefinition<Guid>("ids", index => index.SingleWord()));
        counts.Upsert(42, "vegan");
        ids.Upsert(FirstGuid, "vegan");

        var untypedCounts = catalog.Index("counts");
        var untypedIds = catalog.Index("ids");

        untypedCounts.KeyKind.ShouldBe(LuiKeyKind.Number);
        untypedCounts.Search("veg").Hits.ShouldHaveSingleItem().Key.ShouldBe((LuiKey)42L);
        untypedIds.KeyKind.ShouldBe(LuiKeyKind.Text);
        untypedIds.Search("veg").Hits.ShouldHaveSingleItem().Key.ShouldBe((LuiKey)"0f8fad5b-d9cb-469f-a165-70867728950e");
        Should.Throw<ArgumentException>(() => untypedCounts.Upsert(3_000_000_000L, "too big")).Message.ShouldContain("'counts'");
        Should.Throw<ArgumentException>(() => untypedIds.Upsert("not a guid", "refused")).Message.ShouldContain("'ids'");
        untypedIds.Upsert("0F8FAD5B-D9CB-469F-A165-70867728950E", "same key, other case");
        untypedIds.Count().ShouldBe(1);
    }

    // ---- the handle of a dropped index, whatever the key type -------------------------------------------------------

    [Fact]
    public void A_handle_of_an_int_or_guid_index_that_was_dropped_throws_not_found_also_with_a_transaction_of_the_application()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var counts = catalog.Create(new LuiIndexDefinition<int>("counts", index => index.SingleWord()));
        var ids = catalog.Create(new LuiIndexDefinition<Guid>("ids", index => index.SingleWord()));
        catalog.DropIndex("counts");
        catalog.DropIndex("ids");

        using var connection = database.Connect();
        using var transaction = connection.BeginTransaction();

        Should.Throw<LuiIndexNotFoundException>(() => counts.Count()).IndexName.ShouldBe("counts");
        Should.Throw<LuiIndexNotFoundException>(() => counts.Search("veg"));
        Should.Throw<LuiIndexNotFoundException>(() => counts.Upsert(1, "vegan"));
        Should.Throw<LuiIndexNotFoundException>(() => counts.Upsert(1, "vegan", transaction: transaction));
        Should.Throw<LuiIndexNotFoundException>(() => counts.Remove(1, transaction));
        Should.Throw<LuiIndexNotFoundException>(() => counts.UpdateWeight(1, 2, transaction));
        Should.Throw<LuiIndexNotFoundException>(() => ids.Count()).IndexName.ShouldBe("ids");
        Should.Throw<LuiIndexNotFoundException>(() => ids.Upsert(FirstGuid, "vegan", transaction: transaction));
        Should.Throw<LuiIndexNotFoundException>(() => ids.UpsertMany([new LuiEntry<Guid>(FirstGuid, "vegan")], transaction));
        Should.Throw<LuiIndexNotFoundException>(() => ids.Remove(FirstGuid, transaction));
        transaction.Rollback();
    }

    // ---- linked: int ----------------------------------------------------------------------------------------------------

    [Fact]
    public void A_linked_int_index_works()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Insert(database, (1, "vegan", 5), (2, "vegetarian", 3), (int.MaxValue, "veggie", 1));
        var definition = new LuiLinkedIndexDefinition<int>("tags", index => index.SingleWord().LinkTo("tag", "id", "name", weight: "usage_count"));
        using var catalog = database.Open();

        var tags = catalog.Create(definition);
        var report = tags.Sync();

        report.Indexed.ShouldBe(3);
        report.KeyOutOfRange.ShouldBe(0);
        var hits = tags.Search("veg").Hits;
        hits.Select(hit => hit.Key).Order().ShouldBe([1, 2, int.MaxValue]);
        ((object)hits[0].Key).ShouldBeOfType<int>();
        database.Scalar<string>("SELECT json_extract(definition, '$.keyType') FROM lui_meta WHERE index_name = 'tags';").ShouldBe("int");

        Run(database, "INSERT INTO tag(id, name, usage_count) VALUES (7, 'vegetable', 2);");
        tags.Refresh(7).ShouldBeTrue();
        tags.RefreshMany([1, 2]).ShouldBe(2);
        tags.Search("vegetable").Hits.ShouldHaveSingleItem().Key.ShouldBe(7);
        tags.Check().IsHealthy.ShouldBeTrue();

        tags.CreateTriggers();
        Run(database, "INSERT INTO tag(id, name, usage_count) VALUES (8, 'vegetal', 2);");
        tags.ApplyPending().ShouldBe(1);
        tags.Search("vegetal").Hits.ShouldHaveSingleItem().Key.ShouldBe(8);
        catalog.Index(definition).Search("vegetal").Hits.ShouldHaveSingleItem().Key.ShouldBe(8);
    }

    [Fact]
    public void A_linked_int_index_leaves_keys_outside_the_int_range_out_and_counts_them()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Insert(
            database,
            (1, "vegan", 1),
            (int.MaxValue, "vegetarian", 1),
            (2_147_483_648, "veggie outside the int range", 1),
            (3_000_000_000, "vegetable outside the int range", 1),
            (-1, "vegetal outside every range", 1),
            (4, null, 1));
        using var catalog = database.Open();

        var report = catalog.Create(new LuiLinkedIndexDefinition<int>("tags", index => index.SingleWord().LinkTo("tag", "id", "name"))).Sync();

        report.Indexed.ShouldBe(2);
        report.KeyOutOfRange.ShouldBe(3);
        report.WithoutText.ShouldBe(1);

        var tags = catalog.Index(new LuiLinkedIndexDefinition<int>("tags", index => index.SingleWord().LinkTo("tag", "id", "name")));
        tags.Search("veg").Hits.Select(hit => hit.Key).Order().ShouldBe([1, int.MaxValue]);
        tags.Check().IsHealthy.ShouldBeTrue("a row that cannot be indexed is neither missing nor extra");
        tags.Refresh(1).ShouldBeTrue();

        // the same rows in a linked long index: the document id is the only range there
        var longTags = catalog.Create(new LuiLinkedIndexDefinition<long>("long_tags", index => index.SingleWord().LinkTo("tag", "id", "name")));
        var longReport = longTags.Sync();
        longReport.Indexed.ShouldBe(4);
        longReport.KeyOutOfRange.ShouldBe(1);
        longTags.Search("veg").Hits.Select(hit => hit.Key).Order().ShouldBe([1L, int.MaxValue, 2_147_483_648L, 3_000_000_000L]);
    }

    [Fact]
    public void A_linked_int_index_leaves_a_key_outside_the_int_range_out_of_the_triggers_too()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        using var catalog = database.Open();
        var tags = catalog.Create(new LuiLinkedIndexDefinition<int>("tags", index => index.SingleWord().LinkTo("tag", "id", "name")));
        tags.Sync();
        tags.CreateTriggers();

        Insert(database, (5, "vegan", 1), (3_000_000_000, "vegetarian outside the int range", 1));

        tags.Pending().ShouldBe(2);
        tags.ApplyPending();
        tags.Pending().ShouldBe(0, "a key that cannot be indexed only leaves the outbox");
        tags.Search("veg").Hits.ShouldHaveSingleItem().Key.ShouldBe(5);
        tags.Check().IsHealthy.ShouldBeTrue();
    }

    // ---- another key type is another definition -----------------------------------------------------------------

    [Theory]
    [InlineData("long", "int")]
    [InlineData("int", "long")]
    [InlineData("string", "guid")]
    [InlineData("guid", "string")]
    [InlineData("long", "string")]
    [InlineData("int", "guid")]
    public void Opening_with_another_key_type_throws_a_mismatch(string created, string opened)
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        Declare(created).Create(catalog);

        var open = Declare(opened);
        Should.Throw<LuiDefinitionMismatchException>(() => open.Index(catalog)).ShouldDifferIn("keyType");
        Should.Throw<LuiDefinitionMismatchException>(() => open.Create(catalog)).ShouldDifferIn("keyType");
        database.Scalar<long>("SELECT count(*) FROM lui_meta;").ShouldBe(1);
    }

    [Fact]
    public void Opening_a_linked_index_with_another_key_type_throws_a_mismatch()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        using var catalog = database.Open();
        catalog.Create(new LuiLinkedIndexDefinition<long>("tags", index => index.SingleWord().LinkTo("tag", "id", "name")));
        var asInt = new LuiLinkedIndexDefinition<int>("tags", index => index.SingleWord().LinkTo("tag", "id", "name"));

        Should.Throw<LuiDefinitionMismatchException>(() => catalog.Index(asInt)).ShouldDifferIn("keyType");
        Should.Throw<LuiDefinitionMismatchException>(() => catalog.Create(asInt)).ShouldDifferIn("keyType");
        Should.Throw<LuiDefinitionMismatchException>(() => catalog.Create(asInt, new LuiIndexDefinition<long>("other", index => index.SingleWord())))
            .IndexName.ShouldBe("tags");
        database.Scalar<long>("SELECT count(*) FROM lui_meta;").ShouldBe(1, "the batch is all or none");
    }

    private static Declared Declare(string stored) => new(stored);

    /// <summary>The embedded definition of a stored key type name, called "things" — to vary the type without a generic test.</summary>
    private readonly record struct Declared(string Stored)
    {
        internal void Create(LuiCatalog catalog)
        {
            switch (Stored)
            {
                case "long":
                    catalog.Create(new LuiIndexDefinition<long>("things", index => index.SingleWord()));
                    break;
                case "int":
                    catalog.Create(new LuiIndexDefinition<int>("things", index => index.SingleWord()));
                    break;
                case "string":
                    catalog.Create(new LuiIndexDefinition<string>("things", index => index.SingleWord()));
                    break;
                default:
                    catalog.Create(new LuiIndexDefinition<Guid>("things", index => index.SingleWord()));
                    break;
            }
        }

        internal void Index(LuiCatalog catalog)
        {
            switch (Stored)
            {
                case "long":
                    catalog.Index(new LuiIndexDefinition<long>("things", index => index.SingleWord()));
                    break;
                case "int":
                    catalog.Index(new LuiIndexDefinition<int>("things", index => index.SingleWord()));
                    break;
                case "string":
                    catalog.Index(new LuiIndexDefinition<string>("things", index => index.SingleWord()));
                    break;
                default:
                    catalog.Index(new LuiIndexDefinition<Guid>("things", index => index.SingleWord()));
                    break;
            }
        }
    }

    // ---- a key type that is none of the four ------------------------------------------------------------------------

    [Fact]
    public void Another_key_type_throws_a_configuration_exception_naming_the_allowed_types()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        using var catalog = database.Open();
        LuiIndexDefinitionBase[] definitions =
        [
            new LuiIndexDefinition<double>("doubles", index => index.SingleWord()),
            new LuiIndexDefinition<short>("shorts", index => index.SingleWord()),
            new LuiIndexDefinition<decimal>("decimals", index => index.SingleWord()),
            new LuiIndexDefinition<object>("objects", index => index.SingleWord()),
            new LuiIndexDefinition<DateTime>("dates", index => index.SingleWord()),
            new LuiLinkedIndexDefinition<double>("linked_doubles", index => index.SingleWord().LinkTo("tag", "id", "name")),
            new LuiIndexDefinition<double, string>("doubles_with_payload", index => index.SingleWord()),
        ];

        foreach (var definition in definitions)
        {
            var first = Should.Throw<LuiConfigurationException>(() => catalog.Create(definition));
            var second = Should.Throw<LuiConfigurationException>(() => catalog.Create(definition));

            first.Message.ShouldContain(definition.Name, Case.Sensitive);
            first.Message.ShouldContain(definition.KeyType.Name, Case.Insensitive);
            foreach (var allowed in new[] { "long", "int", "string", "Guid" })
            {
                first.Message.ShouldContain(allowed, Case.Sensitive);
            }

            second.ShouldNotBeSameAs(first, "the failure is kept, the exception is new");
            second.Message.ShouldBe(first.Message);
        }

        Should.Throw<LuiConfigurationException>(() => catalog.Index(new LuiIndexDefinition<double>("doubles", index => index.SingleWord())));
        Should.Throw<LuiConfigurationException>(() => catalog.Create(new LuiIndexDefinition<long>("fine", index => index.SingleWord()), definitions[0]))
            .Message.ShouldContain("doubles", Case.Sensitive);
        database.Scalar<long>("SELECT count(*) FROM lui_meta;").ShouldBe(0, "nothing is created when a definition of the batch is not valid");
    }

    [Fact]
    public void A_linked_string_or_guid_definition_names_issue_267()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        using var catalog = database.Open();
        LuiIndexDefinitionBase[] definitions =
        [
            new LuiLinkedIndexDefinition<string>("linked_texts", index => index.SingleWord().LinkTo("tag", "id", "name")),
            new LuiLinkedIndexDefinition<Guid>("linked_guids", index => index.SingleWord().LinkTo("tag", "id", "name")),
            new LuiLinkedIndexDefinition<string, string>(
                "linked_texts_with_payload", index => index.SingleWord().LinkTo("tag", "id", "name").WithPayload(["slug"], row => row.GetString("slug"))),
            new LuiLinkedIndexDefinition<Guid, string>(
                "linked_guids_with_payload", index => index.SingleWord().LinkTo("tag", "id", "name").WithPayload(["slug"], row => row.GetString("slug"))),
        ];

        foreach (var definition in definitions)
        {
            var exception = Should.Throw<LuiConfigurationException>(() => catalog.Create(definition));

            exception.Message.ShouldContain("#267");
            exception.Message.ShouldContain(definition.Name, Case.Sensitive);
            exception.Message.ShouldContain("long", Case.Sensitive);
            exception.Message.ShouldContain("int", Case.Sensitive);
            Should.Throw<LuiConfigurationException>(() => catalog.Create(definition)).ShouldNotBeSameAs(exception);
        }

        database.Scalar<long>("SELECT count(*) FROM lui_meta;").ShouldBe(0);
    }

    // ---- a stored key that does not convert -------------------------------------------------------------------------

    [Theory]
    [InlineData("int", "abc")]
    [InlineData("int", "2147483648")]
    [InlineData("long", "abc")]
    [InlineData("guid", "not-a-guid")]
    public void A_damaged_stored_key_fails_the_read_with_a_key_exception_naming_the_index_and_the_key(string stored, string damaged)
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var things = stored switch
        {
            "int" => Search(catalog.Create(new LuiIndexDefinition<int>("things", index => index.SingleWord())), 1),
            "long" => Search(catalog.Create(new LuiIndexDefinition<long>("things", index => index.SingleWord())), 1L),
            _ => Search(catalog.Create(new LuiIndexDefinition<Guid>("things", index => index.SingleWord())), FirstGuid),
        };
        Run(database, $"UPDATE lui_things SET key = '{damaged}';");

        var exception = Should.Throw<LuiKeyException>(things);

        exception.IndexName.ShouldBe("things");
        exception.StoredKey.ShouldBe(damaged);
        exception.Message.ShouldContain("'things'");
        exception.Message.ShouldContain($"'{damaged}'");
        exception.ShouldBeAssignableTo<LuiException>();

        // the untyped read of the same index fails the same way
        Should.Throw<LuiKeyException>(() => catalog.Index("things").Search("veg"));
    }

    private static Action Search<TKey>(LuiEmbeddedIndex<TKey> things, TKey key)
        where TKey : notnull
    {
        things.Upsert(key, "vegan");
        return () => things.Search("veg");
    }
}
