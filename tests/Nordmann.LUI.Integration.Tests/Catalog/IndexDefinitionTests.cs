using Shouldly;
using static Nordmann.LUI.Integration.Tests.Linked.LinkedTable;

namespace Nordmann.LUI.Integration.Tests.Catalog;

/// <summary>
/// The index definition is the single declaration of an index (ADR-0073, issue #261): the catalog creates and opens
/// from it, compares it with the stored one, and a handle that comes out of it is typed. The old calls of the catalog
/// are a thin layer over the same path.
/// </summary>
public sealed class IndexDefinitionTests
{
    private static readonly LuiIndexDefinition<long> Tags = new("tags", index => index.SingleWord());

    private static readonly LuiLinkedIndexDefinition<long> LinkedTags =
        new("linked_tags", index => index.SingleWord().LinkTo("tag", "id", "name", weight: "usage_count"));

    // ---- one declaration, one way in -------------------------------------------------------------------------------

    [Fact]
    public void Index_of_an_embedded_definition_returns_an_embedded_handle_without_type_arguments()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create(Tags);

        LuiEmbeddedIndex<long> tags = catalog.Index(Tags);

        tags.Name.ShouldBe("tags");
        tags.Kind.ShouldBe(LuiIndexKind.SingleWord);
        tags.ShouldBeAssignableTo<LuiIndex<long>>();
    }

    [Fact]
    public void Index_of_a_linked_definition_returns_a_linked_handle()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        using var catalog = database.Open();
        catalog.Create(LinkedTags);

        LuiLinkedIndex<long> tags = catalog.Index(LinkedTags);

        tags.Name.ShouldBe("linked_tags");
        tags.ShouldBeAssignableTo<LuiIndex<long>>();
    }

    [Fact]
    public void Create_returns_the_handle_of_the_index()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        using var catalog = database.Open();

        LuiEmbeddedIndex<long> embedded = catalog.Create(Tags);
        LuiLinkedIndex<long> linked = catalog.Create(LinkedTags);

        embedded.Name.ShouldBe("tags");
        linked.Name.ShouldBe("linked_tags");
    }

    [Fact]
    public void A_definition_describes_and_knows_no_catalog()
    {
        Tags.Name.ShouldBe("tags");
        Tags.Source.ShouldBe(LuiIndexSource.Embedded);
        Tags.KeyType.ShouldBe(typeof(long));
        Tags.PayloadType.ShouldBeNull();
        Tags.ToString().ShouldBe("tags (embedded, long)");

        LinkedTags.Source.ShouldBe(LuiIndexSource.Linked);
        LinkedTags.ToString().ShouldBe("linked_tags (linked, long)");
    }

    // ---- create: safe on every start -------------------------------------------------------------------------------

    [Fact]
    public void Create_again_with_the_same_definition_opens_the_index()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create(Tags).Upsert(1, "vegan");

        var again = catalog.Create(Tags);

        again.Count().ShouldBe(1);
        database.Scalar<long>("SELECT count(*) FROM lui_meta").ShouldBe(1);
    }

    [Fact]
    public void Create_with_another_stored_definition_throws_a_mismatch_naming_the_fields()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create(Tags);

        var other = new LuiIndexDefinition<long>("tags", index => index.SingleWord().GermanTransliteration(false).TypoTolerance());
        var exception = Should.Throw<LuiDefinitionMismatchException>(() => catalog.Create(other));

        exception.IndexName.ShouldBe("tags");
        exception.Message.ShouldContain("germanTransliteration");
        exception.Message.ShouldContain("typoDistance");
        exception.Message.ShouldNotContain("kind,");
        exception.Message.ShouldContain("DropIndex", Case.Sensitive, "the message points at the recipe for a changed definition");
        exception.Message.ShouldContain("recipes_v2", Case.Sensitive, "or a new name");
    }

    [Fact]
    public void Create_of_several_definitions_creates_or_opens_all_of_them()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        using var catalog = database.Open();
        catalog.Create(Tags);

        catalog.Create(Tags, LinkedTags, new LuiIndexDefinition<long>("words", index => index.MultiWord()));

        database.Scalar<long>("SELECT count(*) FROM lui_meta").ShouldBe(3);
    }

    [Fact]
    public void Create_of_several_definitions_is_all_or_nothing()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create(new LuiIndexDefinition<long>("b", index => index.SingleWord()));

        var a = new LuiIndexDefinition<long>("a", index => index.SingleWord());
        var b = new LuiIndexDefinition<long>("b", index => index.MultiWord());
        var c = new LuiIndexDefinition<long>("c", index => index.SingleWord());

        var exception = Should.Throw<LuiDefinitionMismatchException>(() => catalog.Create(a, b, c));

        exception.IndexName.ShouldBe("b", "the exception names the definition that failed");
        database.Scalar<long>("SELECT count(*) FROM lui_meta").ShouldBe(1);
        database.Scalar<long>("SELECT count(*) FROM sqlite_master WHERE name LIKE 'lui_a%' OR name LIKE 'lui_c%'").ShouldBe(0);
    }

    [Fact]
    public void Create_of_a_duplicate_name_throws_before_any_write()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();

        var first = new LuiIndexDefinition<long>("dup", index => index.SingleWord());
        var second = new LuiIndexDefinition<long>("dup", index => index.MultiWord());

        Should.Throw<ArgumentException>(() => catalog.Create(first, new LuiIndexDefinition<long>("other", index => index.SingleWord()), second));

        database.Scalar<long>("SELECT count(*) FROM lui_meta").ShouldBe(0);
    }

    [Fact]
    public void Create_of_a_linked_index_syncs_nothing_and_installs_no_triggers()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Insert(database, (1, "vegan", 5), (2, "vegetarisch", 3));
        using var catalog = database.Open();

        var tags = catalog.Create(LinkedTags);

        tags.Count().ShouldBe(0);
        database.Scalar<long>("SELECT count(*) FROM sqlite_master WHERE type = 'trigger'").ShouldBe(0);
    }

    [Fact]
    public void Create_of_a_linked_index_over_a_missing_table_throws_the_linked_table_exception()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();

        Should.Throw<LuiLinkedTableException>(() => catalog.Create(LinkedTags));
        database.Scalar<long>("SELECT count(*) FROM lui_meta").ShouldBe(0);
    }

    [Fact]
    public void Create_names_the_definition_whose_linked_table_is_broken()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        using var catalog = database.Open();

        var good = new LuiLinkedIndexDefinition<long>("good", index => index.SingleWord().LinkTo("tag", "id", "name"));
        var broken = new LuiLinkedIndexDefinition<long>("broken", index => index.SingleWord().LinkTo("tag", "id", "no_such_column"));

        var exception = Should.Throw<LuiLinkedTableException>(() => catalog.Create(good, broken));

        exception.Message.ShouldContain("broken", Case.Sensitive, "the exception names the definition that failed");
        exception.Message.ShouldContain("no_such_column", Case.Sensitive, "and keeps the detail of the table");
        exception.InnerException.ShouldBeOfType<LuiLinkedTableException>();
        database.Scalar<long>("SELECT count(*) FROM lui_meta").ShouldBe(0);
    }

    [Fact]
    public void Create_names_the_definition_whose_table_is_missing_when_both_link_to_it()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();

        var first = new LuiLinkedIndexDefinition<long>("first", index => index.SingleWord().LinkTo("nowhere", "id", "name"));
        var second = new LuiLinkedIndexDefinition<long>("second", index => index.SingleWord().LinkTo("nowhere", "id", "name"));

        Should.Throw<LuiLinkedTableException>(() => catalog.Create(first, second)).Message.ShouldContain("first", Case.Sensitive);
    }

    [Fact]
    public void A_read_only_refusal_names_the_definition_whose_write_was_refused()
    {
        using var database = new TestDatabase();
        using (var writer = database.Open())
        {
            writer.Create(Tags);
        }

        using var reader = LuiCatalog.OpenReadOnly(database.FilePath);
        var second = new LuiIndexDefinition<long>("second", index => index.SingleWord());

        var exception = Should.Throw<LuiReadOnlyException>(() => reader.Create(Tags, second));

        exception.Message.ShouldContain("second", Case.Sensitive, "tags exists and needs no write: the refused write is the one of 'second'");
        exception.Message.ShouldNotContain("'tags'", Case.Sensitive);
    }

    // ---- what is compared ------------------------------------------------------------------------------------------

    [Fact]
    public void Changing_only_the_similarity_raises_no_mismatch()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        using var catalog = database.Open();
        catalog.Create(Tags);
        catalog.Create(LinkedTags);

        var other = new LuiIndexDefinition<long>("tags", index => index.SingleWord().Similarity(LuiSimilarity.Levenshtein));
        var otherLinked = new LuiLinkedIndexDefinition<long>(
            "linked_tags",
            index => index.SingleWord().LinkTo("tag", "id", "name", weight: "usage_count").Similarity(LuiSimilarity.Levenshtein));

        Should.NotThrow(() => catalog.Create(other));
        Should.NotThrow(() => catalog.Index(other));
        Should.NotThrow(() => catalog.Create(otherLinked));
        Should.NotThrow(() => catalog.Index(otherLinked));
    }

    [Theory]
    [InlineData("kind")]
    [InlineData("germanTransliteration")]
    [InlineData("typoDistance")]
    public void Changing_a_compared_field_raises_a_mismatch(string field)
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create(Tags);

        var changed = field switch
        {
            "kind" => new LuiIndexDefinition<long>("tags", index => index.MultiWord()),
            "germanTransliteration" => new LuiIndexDefinition<long>("tags", index => index.SingleWord().GermanTransliteration(false)),
            _ => new LuiIndexDefinition<long>("tags", index => index.SingleWord().TypoTolerance(LuiTypoDistance.TwoErrors)),
        };

        Should.Throw<LuiDefinitionMismatchException>(() => catalog.Create(changed)).ShouldDifferIn(field);
        Should.Throw<LuiDefinitionMismatchException>(() => catalog.Index(changed)).ShouldDifferIn(field);
    }

    [Theory]
    [InlineData("link.table", "tag2", "id", "name", "usage_count", null)]
    [InlineData("link.text", "tag", "id", "slug", "usage_count", null)]
    [InlineData("link.weight", "tag", "id", "name", null, null)]
    [InlineData("link.priority", "tag", "id", "name", "usage_count", "priority")]
    public void Changing_a_compared_field_of_a_linked_definition_raises_a_mismatch(
        string field, string table, string key, string text, string? weight, string? priority)
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Run(database, "CREATE TABLE tag2 (id INTEGER PRIMARY KEY, name TEXT, usage_count INTEGER, slug TEXT, priority INTEGER);");
        using var catalog = database.Open();
        catalog.Create(LinkedTags);

        var changed = new LuiLinkedIndexDefinition<long>(
            "linked_tags", index => index.SingleWord().LinkTo(table, key, text, weight, priority));

        Should.Throw<LuiDefinitionMismatchException>(() => catalog.Create(changed)).ShouldDifferIn(field);
        Should.Throw<LuiDefinitionMismatchException>(() => catalog.Index(changed)).ShouldDifferIn(field);
    }

    // The two fields below cannot be varied through a definition of this version (its key type is `long`, and the payload
    // columns of a definition belong to the payload variants, whose tests are in TypedLinkedPayloadTests), so the stored side is
    // written by the old path, which can. They are not rows of
    // the theories above: their arrangement differs.
    [Fact]
    public void Changing_the_key_type_raises_a_mismatch()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create(new LuiIndexDefinition<string>("tags", index => index.SingleWord()));

        Should.Throw<LuiDefinitionMismatchException>(() => catalog.Index(Tags)).ShouldDifferIn("keyType");
        Should.Throw<LuiDefinitionMismatchException>(() => catalog.Create(Tags)).ShouldDifferIn("keyType");
    }

    [Fact]
    public void Changing_the_payload_columns_raises_a_mismatch()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        using var catalog = database.Open();
        catalog.Create(new LuiLinkedIndexDefinition<long, string>("linked_tags", index => index.SingleWord()
            .LinkTo("tag", "id", "name", weight: "usage_count")
            .WithPayload(["slug"], row => row.GetString("slug"))));

        Should.Throw<LuiDefinitionMismatchException>(() => catalog.Index(LinkedTags)).ShouldDifferIn("link.payloadColumns");
        Should.Throw<LuiDefinitionMismatchException>(() => catalog.Create(LinkedTags)).ShouldDifferIn("link.payloadColumns");
    }

    [Fact]
    public void A_definition_of_the_other_source_raises_a_mismatch_naming_the_source()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        using var catalog = database.Open();
        catalog.Create(Tags);

        var linked = new LuiLinkedIndexDefinition<long>("tags", index => index.SingleWord().LinkTo("tag", "id", "name"));

        Should.Throw<LuiDefinitionMismatchException>(() => catalog.Index(linked)).Message.ShouldContain("source");
        Should.Throw<LuiDefinitionMismatchException>(() => catalog.Create(linked)).Message.ShouldContain("source");
    }

    [Fact]
    public void Index_of_a_missing_index_throws_not_found()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();

        Should.Throw<LuiIndexNotFoundException>(() => catalog.Index(Tags)).IndexName.ShouldBe("tags");
    }

    [Fact]
    public void Index_of_a_definition_writes_nothing_and_opens_on_a_read_only_catalog()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        using (var writer = database.Open())
        {
            writer.Create(Tags).Upsert(1, "vegan");
        }

        using var reader = LuiCatalog.OpenReadOnly(database.FilePath);
        var tags = reader.Index(Tags);

        tags.Search("vegan").Hits.ShouldHaveSingleItem().Key.ShouldBe(1);
        Should.Throw<LuiReadOnlyException>(() => tags.Upsert(2, "vegetarisch"));
    }

    // ---- the old path is a thin layer ------------------------------------------------------------------------------

    [Fact]
    public void The_old_path_and_the_definition_store_the_same_definition()
    {
        using var old = new TestDatabase();
        using var current = new TestDatabase();
        Run(old, Table);
        Run(current, Table);

        using (var catalog = old.Open())
        {
            catalog.Create(new LuiIndexDefinition<long>("tags", index => index.MultiWord().GermanTransliteration(false).TypoTolerance()));
            catalog.Create(new LuiLinkedIndexDefinition<long>("linked_tags", index => index.SingleWord().LinkTo("tag", "id", "name", "usage_count", "priority")));
        }

        using (var catalog = current.Open())
        {
            catalog.Create(
                new LuiIndexDefinition<long>("tags", index => index.MultiWord().GermanTransliteration(false).TypoTolerance()),
                new LuiLinkedIndexDefinition<long>("linked_tags", index => index.SingleWord().LinkTo("tag", "id", "name", "usage_count", "priority")));
        }

        const string Sql = "SELECT group_concat(index_name || kind || source || definition || schema_version, '|') FROM (SELECT * FROM lui_meta ORDER BY index_name);";
        current.Scalar<string>(Sql).ShouldBe(old.Scalar<string>(Sql));
    }

    [Fact]
    public void The_old_path_and_the_definition_open_the_same_index()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create(new LuiIndexDefinition<long>("tags", index => index.SingleWord())).Upsert(1, "vegan");

        catalog.Create(Tags).Search("vegan").Hits.ShouldHaveSingleItem().Text.ShouldBe("vegan");
        catalog.Index(Tags).Count().ShouldBe(1);
    }

    [Fact]
    public void A_builder_describes_and_the_catalog_creates()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        using var catalog = database.Open();
        catalog.Create(new LuiIndexDefinition<long>("tags", index => index.SingleWord())).Name.ShouldBe("tags");
        catalog.Create(new LuiLinkedIndexDefinition<long>("linked_tags", index => index.SingleWord().LinkTo("tag", "id", "name"))).Name.ShouldBe("linked_tags");
    }

    // ---- an invalid definition fails on first use, not at the field ------------------------------------------------

    [Fact]
    public void An_invalid_definition_throws_a_configuration_exception_on_first_use_not_at_field_initialization()
    {
        // a static readonly field that threw would surface as a TypeInitializationException
        var holder = Should.NotThrow(() => InvalidFields.BadName);
        holder.Name.ShouldBe("Bad Name");

        using var database = new TestDatabase();
        using var catalog = database.Open();

        Should.Throw<LuiConfigurationException>(() => catalog.Create(InvalidFields.BadName)).ShouldBeAssignableTo<LuiException>();
        Should.Throw<LuiConfigurationException>(() => catalog.Create(InvalidFields.UnsupportedKey));
        Should.Throw<LuiConfigurationException>(() => catalog.Index(InvalidFields.BadName));
        Should.Throw<LuiConfigurationException>(() => catalog.Create(InvalidFields.BadLink));
        database.Scalar<long>("SELECT count(*) FROM lui_meta").ShouldBe(0);
    }

    [Fact]
    public void A_builder_function_that_returns_null_or_throws_anything_is_a_configuration_exception()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var returnsNull = new LuiIndexDefinition<long>("nothing", _ => null!);
        var throws = new LuiIndexDefinition<long>("boom", _ => throw new FormatException("the application's mistake"));
        var linkedNull = new LuiLinkedIndexDefinition<long>("linked_nothing", _ => null!);

        foreach (var definition in new LuiIndexDefinitionBase[] { returnsNull, throws, linkedNull })
        {
            var first = Should.Throw<LuiConfigurationException>(() => catalog.Create(definition));
            var second = Should.Throw<LuiConfigurationException>(() => catalog.Create(definition));

            first.Message.ShouldContain(definition.Name, Case.Sensitive);
            first.InnerException.ShouldNotBeNull();
            second.ShouldNotBeSameAs(first);
            second.InnerException.ShouldBeSameAs(first.InnerException);
        }

        Should.Throw<LuiConfigurationException>(() => catalog.Index(throws)).InnerException.ShouldBeOfType<FormatException>();
    }

    [Fact]
    public void Every_configuration_failure_names_the_definition_also_in_a_batch()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        using var catalog = database.Open();
        var good = new LuiIndexDefinition<long>("good", index => index.SingleWord());
        LuiIndexDefinitionBase[] broken =
        [
            new LuiIndexDefinition<double>("doubles", index => index.SingleWord()),
            new LuiLinkedIndexDefinition<long>("bad_link", index => index.SingleWord().LinkTo("1table", "id", "name")),
            // `WithPayload` inside a LuiLinkedIndexDefinition<long> no longer compiles (issue #269); the payload variants are
            // definitions of their own, and a key type that is not allowed fails there, too
            new LuiLinkedIndexDefinition<double, string>(
                "with_payload", index => index.SingleWord().LinkTo("tag", "id", "name").WithPayload(["slug"], row => row.GetString("slug"))),
            new LuiIndexDefinition<long>("null_function", (Func<LuiIndexKindBuilder, LuiEmbeddedIndexBuilder>)null!),
            new LuiIndexDefinition<long>("own_failure", _ => throw new LuiConfigurationException("my own words")),
        ];

        foreach (var definition in broken)
        {
            Should.Throw<LuiConfigurationException>(() => catalog.Create(good, definition))
                .Message.ShouldContain(definition.Name, Case.Sensitive);
            Should.Throw<LuiConfigurationException>(() => catalog.Create(definition))
                .Message.ShouldContain(definition.Name, Case.Sensitive);
        }

        Should.Throw<LuiConfigurationException>(() => catalog.Create(good, broken[4])).Message.ShouldContain("my own words");
        database.Scalar<long>("SELECT count(*) FROM lui_meta").ShouldBe(0);
    }

    [Fact]
    public void The_not_found_message_of_a_dropped_handle_points_at_the_definition()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create(Tags);
        catalog.DropIndex("tags");

        var message = Should.Throw<LuiIndexNotFoundException>(() => tags.Count()).Message;

        message.ShouldContain("Create(definition)", Case.Sensitive);
        message.ShouldNotContain("Create(name", Case.Sensitive);
    }

    [Fact]
    public void An_invalid_definition_keeps_its_failure_and_throws_a_new_exception_each_time()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var definition = new LuiIndexDefinition<long>("Bad Name", index => index.SingleWord());

        var first = Should.Throw<LuiConfigurationException>(() => catalog.Create(definition));
        var second = Should.Throw<LuiConfigurationException>(() => catalog.Create(definition));

        second.ShouldNotBeSameAs(first, "one exception object thrown from many threads would have its stack trace overwritten");
        second.Message.ShouldBe(first.Message);
        first.InnerException.ShouldNotBeNull();
        second.InnerException.ShouldBeSameAs(first.InnerException, "the cause is kept, it is the failure of the one run");
    }

    [Fact]
    public void The_builder_function_runs_once_under_concurrent_first_use()
    {
        using var database = new TestDatabase();
        using var other = new TestDatabase();
        using var catalog = database.Open();
        using var second = other.Open();
        var runs = 0;
        var definition = new LuiIndexDefinition<long>("tags", index =>
        {
            Interlocked.Increment(ref runs);
            Thread.SpinWait(5_000_000);
            return index.SingleWord();
        });

        using var barrier = new Barrier(8);
        Parallel.For(0, 8, new ParallelOptions { MaxDegreeOfParallelism = 8 }, number =>
        {
            barrier.SignalAndWait();
            _ = (number % 2 == 0 ? catalog : second).Create(definition);
        });

        runs.ShouldBe(1);
        database.Scalar<long>("SELECT count(*) FROM lui_meta").ShouldBe(1);
        other.Scalar<long>("SELECT count(*) FROM lui_meta").ShouldBe(1);
    }

    // ---- the lifetime of a handle ----------------------------------------------------------------------------------

    [Fact]
    public void An_operation_on_a_dropped_index_throws_index_not_found()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        using var catalog = database.Open();
        var tags = catalog.Create(Tags);
        var linked = catalog.Create(LinkedTags);
        catalog.DropIndex("tags").ShouldBeTrue();
        catalog.DropIndex("linked_tags").ShouldBeTrue();

        Should.Throw<LuiIndexNotFoundException>(() => tags.Count()).IndexName.ShouldBe("tags");
        Should.Throw<LuiIndexNotFoundException>(() => tags.Upsert(1, "vegan"));
        Should.Throw<LuiIndexNotFoundException>(() => tags.Search("veg"));
        Should.Throw<LuiIndexNotFoundException>(() => tags.Query("veg").Execute());
        Should.Throw<LuiIndexNotFoundException>(() => tags.Remove(1));
        Should.Throw<LuiIndexNotFoundException>(() => tags.Check());
        Should.Throw<LuiIndexNotFoundException>(() => linked.Count()).IndexName.ShouldBe("linked_tags");
        Should.Throw<LuiIndexNotFoundException>(() => linked.Search("veg"));
        Should.Throw<LuiIndexNotFoundException>(() => linked.Sync());
        Should.Throw<LuiIndexNotFoundException>(() => linked.Refresh(1));
    }

    [Fact]
    public void TriggerScript_is_pure_and_answers_on_a_handle_of_a_dropped_index()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        using var catalog = database.Open();
        var tags = catalog.Create(LinkedTags);
        var script = tags.TriggerScript();

        catalog.DropIndex("linked_tags");

        tags.TriggerScript().ShouldBe(script, "it builds the SQL from the definition and reads no database");
    }

    [Fact]
    public void A_write_with_a_transaction_of_the_application_on_a_dropped_index_throws_index_not_found()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        using var catalog = database.Open();
        var tags = catalog.Create(Tags);
        var linked = catalog.Create(LinkedTags);
        catalog.DropIndex("tags").ShouldBeTrue();
        catalog.DropIndex("linked_tags").ShouldBeTrue();

        using var connection = database.Connect();
        using var transaction = connection.BeginTransaction();
        var entry = new LuiEntry<long>(1, "vegan");

        Should.Throw<LuiIndexNotFoundException>(() => tags.Upsert(1, "vegan", transaction: transaction)).IndexName.ShouldBe("tags");
        Should.Throw<LuiIndexNotFoundException>(() => tags.Upsert(entry, transaction));
        Should.Throw<LuiIndexNotFoundException>(() => tags.UpsertMany([entry], transaction));
        Should.Throw<LuiIndexNotFoundException>(() => tags.Remove(1, transaction));
        Should.Throw<LuiIndexNotFoundException>(() => tags.UpdateWeight(1, 5, transaction));
        Should.Throw<LuiIndexNotFoundException>(() => tags.UpdatePriority(1, LuiPriority.Normal, transaction));
        Should.Throw<LuiIndexNotFoundException>(() => linked.Refresh(1, transaction)).IndexName.ShouldBe("linked_tags");
        Should.Throw<LuiIndexNotFoundException>(() => linked.RefreshMany([1L], transaction));
    }

    [Fact]
    public void An_operation_on_a_disposed_catalog_throws_object_disposed()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        var catalog = database.Open();
        var tags = catalog.Create(Tags);
        var linked = catalog.Create(LinkedTags);
        catalog.Dispose();

        Should.Throw<ObjectDisposedException>(() => tags.Count());
        Should.Throw<ObjectDisposedException>(() => tags.Upsert(1, "vegan"));
        Should.Throw<ObjectDisposedException>(() => tags.Search("veg"));
        Should.Throw<ObjectDisposedException>(() => linked.Count());
        Should.Throw<ObjectDisposedException>(() => linked.Sync());
        Should.Throw<ObjectDisposedException>(() => catalog.Index(Tags));
        Should.Throw<ObjectDisposedException>(() => catalog.Create(Tags));
    }

    private static void Run(TestDatabase database, string sql)
    {
        using var connection = database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>Invalid definitions as <c>static readonly</c> fields — the way an application declares them.</summary>
    private static class InvalidFields
    {
        public static readonly LuiIndexDefinition<long> BadName = new("Bad Name", index => index.SingleWord());

        public static readonly LuiIndexDefinition<double> UnsupportedKey = new("doubles", index => index.SingleWord());

        public static readonly LuiLinkedIndexDefinition<long> BadLink =
            new("links", index => index.SingleWord().LinkTo("1table", "id", "name"));
    }
}
