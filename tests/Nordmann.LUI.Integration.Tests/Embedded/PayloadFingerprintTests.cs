using Nordmann.LUI.Integration.Tests.Catalog;
using Shouldly;
using static Nordmann.LUI.Integration.Tests.Linked.LinkedTable;

namespace Nordmann.LUI.Integration.Tests.Embedded;

/// <summary>
/// The payload registry and the fingerprint of an embedded payload index (ADR-0072, issue #260): the serializer is bound to
/// the payload type through the registry of the catalog, an unregistered type is refused when the index is created or opened,
/// and the index stores the type name and the format of its payloads — so neither another type nor the same type in another
/// format is read silently.
/// </summary>
public sealed class PayloadFingerprintTests
{
    private static readonly LuiIndexDefinition<long, Chip> Chips = new("chips", index => index.SingleWord());

    private static readonly LuiIndexDefinition<long, Note> Notes = new("notes", index => index.SingleWord());

    private static readonly LuiIndexDefinition<long, Unknown> Unknowns = new("unknowns", index => index.SingleWord());

    private static readonly LuiIndexDefinition<long> PlainChips = new("chips", index => index.SingleWord());

    private static LuiPayloadRegistry Json() => new LuiPayloadRegistry().Use(ChipJsonContext.Default);

    private static LuiCatalogOptions With(LuiPayloadRegistry registry) => new() { Payloads = registry };

    // ---- the registry decides, and a type that is not there fails at once -------------------------------------------

    [Fact]
    public void An_unregistered_type_throws_a_configuration_exception_on_create_and_on_index()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open(With(Json()));

        var created = Should.Throw<LuiConfigurationException>(() => catalog.Create(Unknowns));
        var opened = Should.Throw<LuiConfigurationException>(() => catalog.Index(Unknowns));

        created.GetType().ShouldBe(typeof(LuiConfigurationException), "a configuration error is no payload error");
        foreach (var exception in new[] { created, opened })
        {
            exception.Message.ShouldContain("'unknowns'", Case.Sensitive);
            exception.Message.ShouldContain(typeof(Unknown).FullName!);
            exception.Message.ShouldContain("Use(");
        }

        opened.ShouldNotBeSameAs(created);
        database.Scalar<long>("SELECT count(*) FROM lui_meta").ShouldBe(0, "a type that cannot be written creates no index");
        Should.Throw<LuiConfigurationException>(() => catalog.Create(Unknowns)).ShouldNotBeSameAs(created, "a new exception on every use");
    }

    [Fact]
    public void A_batch_with_an_unregistered_type_names_the_definition_and_creates_nothing()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open(With(Json()));

        var exception = Should.Throw<LuiConfigurationException>(
            () => catalog.Create(Chips, new LuiIndexDefinition<long>("plain", index => index.SingleWord()), Unknowns));

        exception.Message.ShouldContain("'unknowns'", Case.Sensitive);
        database.Scalar<long>("SELECT count(*) FROM lui_meta").ShouldBe(0, "a batch is all or nothing");
    }

    [Fact]
    public void A_catalog_without_a_registry_refuses_a_payload_definition_and_says_where_to_set_it()
    {
        using var database = new TestDatabase();
        using var none = LuiCatalog.OpenOrCreate(database.FilePath);
        using var legacy = LuiCatalog.OpenOrCreate(database.FilePath, new LuiCatalogOptions { PayloadSerializer = LuiPayloadSerializer.Json(ChipJsonContext.Default) });

        foreach (var catalog in new[] { none, legacy })
        {
            var exception = Should.Throw<LuiConfigurationException>(() => catalog.Create(Chips));

            exception.Message.ShouldContain("'chips'", Case.Sensitive);
            exception.Message.ShouldContain(nameof(LuiCatalogOptions.Payloads));
            Should.Throw<LuiConfigurationException>(() => catalog.Index(Chips)).Message.ShouldContain(nameof(LuiCatalogOptions.Payloads));
        }

        database.Scalar<long>("SELECT count(*) FROM lui_meta").ShouldBe(0);
    }

    [Fact]
    public void Two_contexts_that_know_the_type_throw_on_create_and_on_index_naming_both()
    {
        using var database = new TestDatabase();
        var both = new LuiPayloadRegistry().Use(ChipJsonContext.Default).Use(FingerprintJsonContext.Default).Use(AnotherChipContext.Default);
        using var catalog = database.Open(With(both));

        foreach (var action in new Action[] { () => catalog.Create(Chips), () => catalog.Index(Chips) })
        {
            var exception = Should.Throw<LuiConfigurationException>(action);

            exception.Message.ShouldContain("'chips'", Case.Sensitive);
            exception.Message.ShouldContain(nameof(ChipJsonContext));
            exception.Message.ShouldContain(nameof(AnotherChipContext));
            exception.Message.ShouldNotContain(nameof(FingerprintJsonContext), Case.Sensitive, "a context that does not know the type is not named");
        }

        database.Scalar<long>("SELECT count(*) FROM lui_meta").ShouldBe(0);
    }

    [Fact]
    public void A_change_of_the_registry_after_the_catalog_was_opened_has_no_effect()
    {
        using var database = new TestDatabase();
        var registry = Json();
        using var catalog = database.Open(With(registry));

        _ = registry.Use(new NoteSerializer());

        Should.Throw<LuiConfigurationException>(() => catalog.Create(Notes)).Message.ShouldContain("'notes'", Case.Sensitive);
        using var later = database.Open(With(registry));
        later.Create(Notes).Upsert(1, "remember", new Note("milk"));
    }

    [Fact]
    public void Setting_Payloads_and_PayloadSerializer_throws()
    {
        using var database = new TestDatabase();
        var options = new LuiCatalogOptions { Payloads = Json(), PayloadSerializer = LuiPayloadSerializer.Json(ChipJsonContext.Default) };

        var exception = Should.Throw<LuiConfigurationException>(() => LuiCatalog.OpenOrCreate(database.FilePath, options));

        exception.Message.ShouldContain(nameof(LuiCatalogOptions.Payloads));
        exception.Message.ShouldContain(nameof(LuiCatalogOptions.PayloadSerializer));
        File.Exists(database.FilePath).ShouldBeFalse("a configuration that is refused touches no file");
        Should.Throw<LuiConfigurationException>(() => LuiCatalog.Open(database.FilePath, options));
        Should.Throw<LuiConfigurationException>(() => LuiCatalog.OpenReadOnly(database.FilePath, options));
    }

    // ---- two formats in one catalog -----------------------------------------------------------------------------------

    [Fact]
    public void Two_indexes_in_one_catalog_use_two_formats()
    {
        using var database = new TestDatabase();
        var registry = Json().Use(new NoteSerializer());
        using var catalog = database.Open(With(registry));
        var chips = catalog.Create(Chips);
        var notes = catalog.Create(Notes);

        chips.Upsert(1, "vegan", new Chip("one", "green"));
        notes.Upsert(1, "remember", new Note("milk"));

        // the bytes of each index are the ones of its own serializer
        System.Text.Encoding.UTF8.GetString(Payload(database, "chips")).ShouldBe("""{"Slug":"one","Color":"green"}""");
        System.Text.Encoding.UTF8.GetString(Payload(database, "notes")).ShouldBe("NOTE:milk");

        // and each is read with the serializer of its own type
        var chipHit = catalog.Index("chips").Search("vegan").Hits.ShouldHaveSingleItem();
        var noteHit = catalog.Index("notes").Search("remember").Hits.ShouldHaveSingleItem();
        chipHit.GetPayload<Chip>().ShouldBe(new Chip("one", "green"));
        noteHit.GetPayload<Note>().ShouldBe(new Note("milk"));

        var stored = Definition(database, "chips") + Definition(database, "notes");
        stored.ShouldContain("\"payloadFormat\":\"json-strict-v1\"");
        stored.ShouldContain("\"payloadFormat\":\"note-text-v1\"");
    }

    [Fact]
    public void The_stored_definition_holds_the_type_name_and_the_format()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open(With(Json()));

        catalog.Create(Chips);
        catalog.Create(PlainOther);

        var chips = Definition(database, "chips");
        chips.ShouldContain("\"payloadType\":\"Nordmann.LUI.Testing.Chip\"");
        chips.ShouldContain("\"payloadFormat\":\"json-strict-v1\"");
        var plain = Definition(database, "plain_other");
        plain.ShouldNotContain("payloadType");
        plain.ShouldNotContain("payloadFormat");
    }

    private static readonly LuiIndexDefinition<long> PlainOther = new("plain_other", index => index.SingleWord());

    [Fact]
    public void An_index_created_again_with_the_same_registry_is_opened_not_changed()
    {
        using var database = new TestDatabase();
        using (var first = database.Open(With(Json())))
        {
            first.Create(Chips).Upsert(1, "vegan", new Chip("one"));
        }

        using var second = database.Open(With(Json()));

        second.Create(Chips).Count().ShouldBe(1);
        second.Index(Chips).Count().ShouldBe(1);
    }

    // ---- the fingerprint: another type, another format --------------------------------------------------------------------

    [Fact]
    public void Another_payload_type_throws_a_mismatch_naming_both()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open(With(Json()));
        catalog.Create(Chips);
        var cards = new LuiIndexDefinition<long, RecipeCard>("chips", index => index.SingleWord());

        var created = Should.Throw<LuiDefinitionMismatchException>(() => catalog.Create(cards));
        var opened = Should.Throw<LuiDefinitionMismatchException>(() => catalog.Index(cards));

        foreach (var exception in new[] { created, opened })
        {
            exception.IndexName.ShouldBe("chips");
            exception.ShouldDifferIn("payloadType");
            exception.Message.ShouldContain("stored 'Nordmann.LUI.Testing.Chip', declared 'Nordmann.LUI.Testing.RecipeCard'");
            exception.Message.ShouldNotContain("LuiPayloadName", Case.Sensitive, "the namespaces are not the only difference");
        }
    }

    [Fact]
    public void A_type_that_differs_only_in_its_namespace_says_so_and_names_the_attribute()
    {
        using var database = new TestDatabase();
        var registry = new LuiPayloadRegistry().Use(FingerprintJsonContext.Default);
        using var catalog = database.Open(With(registry));
        catalog.Create(new LuiIndexDefinition<long, Alpha.Same>("things", index => index.SingleWord()));

        var exception = Should.Throw<LuiDefinitionMismatchException>(
            () => catalog.Create(new LuiIndexDefinition<long, Beta.Same>("things", index => index.SingleWord())));

        exception.Message.ShouldContain("stored '" + typeof(Alpha.Same).FullName! + "', declared '" + typeof(Beta.Same).FullName! + "'");
        exception.Message.ShouldContain("[LuiPayloadName(");
    }

    [Fact]
    public void The_attribute_name_survives_a_rename_of_the_class()
    {
        using var database = new TestDatabase();
        var registry = new LuiPayloadRegistry().Use(FingerprintJsonContext.Default);
        using var catalog = database.Open(With(registry));
        catalog.Create(new LuiIndexDefinition<long, OldRecipe>("recipes", index => index.SingleWord())).Upsert(1, "kuchen", new OldRecipe("cake"));

        // the class has another name now: the stored name is the one of the attribute, so the index opens
        var renamed = catalog.Create(new LuiIndexDefinition<long, NewRecipe>("recipes", index => index.SingleWord()));

        renamed.Count().ShouldBe(1);
        Definition(database, "recipes").ShouldContain("\"payloadType\":\"recipe\"");
        catalog.Index("recipes").Search("kuchen").Hits.ShouldHaveSingleItem().GetPayload<NewRecipe>().ShouldBe(new NewRecipe("cake"));
    }

    [Fact]
    public void The_same_type_in_another_format_throws_a_mismatch_naming_both_formats()
    {
        using var database = new TestDatabase();
        using (var json = database.Open(With(Json())))
        {
            json.Create(Chips).Upsert(1, "vegan", new Chip("one"));
        }

        using var lines = database.Open(With(Json().Use(new LinesChipSerializer())));
        var created = Should.Throw<LuiDefinitionMismatchException>(() => lines.Create(Chips));
        var opened = Should.Throw<LuiDefinitionMismatchException>(() => lines.Index(Chips));

        foreach (var exception in new[] { created, opened })
        {
            exception.IndexName.ShouldBe("chips");
            exception.ShouldDifferIn("payloadFormat");
            exception.Message.ShouldContain("the payloads of 'chips' were written in format 'json-strict-v1', the registered serializer writes 'lines-v1'");
        }

        using var other = database.Open(With(Json().Use(new LinesChipSerializer("lines-v2"))));
        Should.Throw<LuiDefinitionMismatchException>(() => other.Index(Chips)).Message.ShouldContain("writes 'lines-v2'");
        database.Open(With(Json())).Index(Chips).Count().ShouldBe(1, "the index is still there and still read as it was written");
    }

    [Fact]
    public void An_index_with_a_payload_type_cannot_be_opened_without_one_and_vice_versa()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open(With(Json()));
        catalog.Create(Chips);
        catalog.Create(PlainOther);
        var plainChips = PlainChips;
        var typedOther = new LuiIndexDefinition<long, Chip>("plain_other", index => index.SingleWord());

        var withoutPayload = Should.Throw<LuiDefinitionMismatchException>(() => catalog.Index(plainChips));
        var createdWithout = Should.Throw<LuiDefinitionMismatchException>(() => catalog.Create(plainChips));
        var withPayload = Should.Throw<LuiDefinitionMismatchException>(() => catalog.Index(typedOther));
        var createdWith = Should.Throw<LuiDefinitionMismatchException>(() => catalog.Create(typedOther));

        foreach (var exception in new[] { withoutPayload, createdWithout })
        {
            exception.IndexName.ShouldBe("chips");
            exception.ShouldDifferIn("payloadType");
            exception.Message.ShouldContain("LuiIndexDefinition<TKey, TPayload>");
        }

        foreach (var exception in new[] { withPayload, createdWith })
        {
            exception.IndexName.ShouldBe("plain_other");
            exception.ShouldDifferIn("payloadType");
            exception.Message.ShouldContain("LuiIndexDefinition<TKey>");
        }
    }

    [Fact]
    public void A_handle_of_an_index_that_another_process_created_again_in_another_format_throws_a_mismatch()
    {
        using var database = new TestDatabase();
        using var first = database.Open(With(Json()));
        using var second = database.Open(With(Json().Use(new LinesChipSerializer())));
        first.Create(Chips);
        var old = first.Index(Chips);
        old.Upsert(1, "vegan", new Chip("one"));

        second.DropIndex("chips");
        var recreated = second.Create(Chips);

        // the old handle writes with the JSON serializer: it must never write into an index that holds lines
        var thrown = Should.Throw<LuiDefinitionMismatchException>(() => old.Upsert(2, "vegane", new Chip("two")));
        thrown.ShouldDifferIn("payloadFormat");
        recreated.Count().ShouldBe(0, "nothing was written through the stale definition");
    }

    // ---- a linked index needs no registry --------------------------------------------------------------------------------

    [Fact]
    public void A_linked_payload_index_needs_no_registry_and_stores_no_type()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        using var catalog = LuiCatalog.OpenOrCreate(database.FilePath);
        var linked = new LuiLinkedIndexDefinition<long, Chip>(
            "linked_chips",
            index => index.SingleWord().LinkTo("tag", "id", "name", weight: "usage_count").WithPayload(["slug"], row => new Chip(row.GetString("slug"))));
        Run(database, "INSERT INTO tag(id, name, usage_count, slug, color) VALUES (1, 'vegan', 5, 'v', NULL);");

        var handle = catalog.Create(linked);
        handle.Sync().Indexed.ShouldBe(1);

        Definition(database, "linked_chips").ShouldNotContain("payloadType");
        Definition(database, "linked_chips").ShouldNotContain("payloadFormat");
        catalog.LinkedIndex("linked_chips", row => new Chip(row.GetString("slug"))).Search("vegan").Hits
            .ShouldHaveSingleItem().GetPayload<Chip>().ShouldBe(new Chip("v"));
        catalog.Index(linked).ShouldBeSameAs(handle);
    }

    // ---- the old untyped path ---------------------------------------------------------------------------------------------

    [Fact]
    public void The_old_read_path_resolves_through_the_registry()
    {
        using var database = new TestDatabase();
        var lines = new LinesChipSerializer();
        using var catalog = database.Open(With(new LuiPayloadRegistry().Use(lines)));
        catalog.Create(Chips).Upsert(1, "vegan", new Chip("one", "green"));

        var hit = catalog.Index("chips").Search("vegan").Hits.ShouldHaveSingleItem();

        hit.GetPayload<Chip>().ShouldBe(new Chip("one", "green"));
        lines.Deserialized.ShouldBe(1, "the serializer of the registry read it");
        hit.TryGetPayload<Chip>(out var payload).ShouldBeTrue();
        payload.ShouldBe(new Chip("one", "green"));
    }

    [Fact]
    public void The_old_read_path_asks_the_registry_for_the_type_and_a_missing_one_is_a_configuration_error_not_a_payload_error()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open(With(Json()));
        catalog.Create(Chips).Upsert(1, "vegan", new Chip("one"));
        var hit = catalog.Index("chips").Search("vegan").Hits.ShouldHaveSingleItem();

        var missing = Should.Throw<LuiConfigurationException>(() => hit.GetPayload<Uri>());

        missing.Message.ShouldContain("'chips'", Case.Sensitive);
        missing.Message.ShouldContain("System.Uri");
        Should.Throw<LuiConfigurationException>(() => hit.TryGetPayload<Uri>(out _));

        // a type that is registered and cannot read the bytes is a payload error, and says which index, key and type
        var unreadable = Should.Throw<LuiPayloadException>(() => hit.GetPayload<RecipeCard>());
        unreadable.IndexName.ShouldBe("chips");
        unreadable.Key.ShouldBe((LuiKey)1);
        unreadable.RequestedType.ShouldBe(typeof(RecipeCard));
    }

    [Fact]
    public void A_payload_that_cannot_be_read_is_a_payload_exception_and_not_a_configuration_exception()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open(With(Json()));
        catalog.Create(Chips).Upsert(1, "vegan", new Chip("one"));
        using (var connection = database.Connect())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE lui_chips SET payload = x'00ff00';";
            command.ExecuteNonQuery();
        }

        var hit = catalog.Index("chips").Search("vegan").Hits.ShouldHaveSingleItem();

        var exception = Should.Throw<LuiPayloadException>(() => hit.GetPayload<Chip>());
        exception.IndexName.ShouldBe("chips");
        exception.RequestedType.ShouldBe(typeof(Chip));
    }

    [Fact]
    public void With_a_registry_an_untyped_entry_cannot_carry_a_payload()
    {
        // there is no untyped payload: an index has a declared, registered payload type, or none
        using var database = new TestDatabase();
        using var catalog = database.Open(With(Json()));
        var tags = catalog.Create("tags", index => index.SingleWord());

        var exception = Should.Throw<LuiConfigurationException>(() => tags.Upsert(new LuiEntry(1, "vegan") { Payload = new Chip("v") }));

        exception.Message.ShouldContain("'tags'", Case.Sensitive);
        exception.Message.ShouldContain("LuiIndexDefinition<TKey, TPayload>");
        tags.Count().ShouldBe(0);
        Should.NotThrow(() => tags.Upsert(2, "ohne"), "an entry without a payload needs no serializer");
    }

    // ---- the lookup happens once --------------------------------------------------------------------------------------------

    [Fact]
    public void The_serializer_is_looked_up_once_per_catalog_and_the_hot_path_does_not_look_again()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open(With(Json()));

        var chips = catalog.Create(Chips);
        for (var i = 1; i <= 50; i++)
        {
            chips.Upsert(i, "vegan " + i, new Chip("c" + i));
            _ = catalog.Index(Chips);
        }

        chips.Sync([new LuiEntry<long, Chip>(100, "vegan hundred", new Chip("c100"))]);
        _ = catalog.Index("chips").Search("vegan").Hits[0].GetPayload<Chip>();

        catalog.PayloadLookups.ShouldBe(1, "one type, looked up once, however often it is written or read");
    }

    private static byte[] Payload(TestDatabase database, string index)
    {
        using var connection = database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT payload FROM lui_{index};";
        return (byte[])command.ExecuteScalar()!;
    }

    private static string Definition(TestDatabase database, string index)
    {
        using var connection = database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT definition FROM lui_meta WHERE index_name = $name;";
        command.Parameters.AddWithValue("$name", index);
        return (string)command.ExecuteScalar()!;
    }
}
