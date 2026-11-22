using Nordmann.LUI.Integration.Tests.Embedded;
using Shouldly;
using static Nordmann.LUI.Integration.Tests.Linked.LinkedTable;

namespace Nordmann.LUI.Integration.Tests.Maintenance;

/// <summary>
/// Maintenance by name (ADR-0076, issue #273): a tool, an admin endpoint or a health check inspects and maintains every index of a
/// catalog <b>without its definitions and without a payload type</b> — <c>Indexes()</c>, <c>Count(name)</c>, <c>Check(name)</c>,
/// <c>Optimize(name)</c>, <c>Rebuild(name)</c>. They open the internal runtime from the stored definition, return no handle, read no
/// payload and call no map, and they meet the same guards as a typed handle: the check of the schema counter and the work are one
/// step, an index that is gone is a <see cref="LuiIndexNotFoundException"/> that names it, and a name that is not known reloads the
/// definitions once and is remembered as missing until the schema counter changes.
/// </summary>
public sealed class MaintenanceByNameTests
{
    private const string Corrupt = "X'7B226F6C64223A317D'";

    private static readonly LuiIndexDefinition<long> Recipes = new("recipes", index => index.SingleWord());

    private static readonly LuiIndexDefinition<long> RecipesOfWords = new("recipes", index => index.MultiWord());

    private static readonly LuiIndexDefinition<string, Chip> Chips = new("chips", index => index.MultiWord().TypoTolerance(LuiTypoDistance.TwoErrors));

    private static readonly LuiLinkedIndexDefinition<int> LinkedTags =
        new("linked_tags", index => index.SingleWord().LinkTo("tag", "id", "name", weight: "usage_count"));

    private static readonly LuiLinkedIndexDefinition<long, string> Slugs =
        new("slugs", index => index.SingleWord().LinkTo("tag", "id", "name").WithPayload(["slug"], row => row.GetString("slug")));

    private static readonly string[] AllNames = ["recipes", "chips", "linked_tags", "slugs"];

    private static LuiCatalogOptions Lines(LinesChipSerializer serializer) => new() { Payloads = new LuiPayloadRegistry().Use(serializer) };

    /// <summary>The four kinds of index in one file — embedded and linked, with and without a payload type — written by a catalog that knows their definitions.</summary>
    private static void WriteAllKinds(TestDatabase database)
    {
        Run(database, Table);
        Insert(database, (1, "vegan", 5), (2, "veganer", 4), (3, "vegetarisch", 3));
        Run(database, "UPDATE tag SET slug = name;");
        using var catalog = database.Open(Lines(new LinesChipSerializer()));
        var recipes = catalog.Create(Recipes);
        recipes.Upsert(1, "kuchen");
        recipes.Upsert(2, "torte");
        var chips = catalog.Create(Chips);
        chips.Upsert("alpha", "vegan", new Chip("one"));
        chips.Upsert("beta", "veganer", new Chip("two"));
        chips.Upsert("gamma", "vegetarisch", new Chip("three"));
        _ = catalog.Create(LinkedTags).Sync();
        _ = catalog.Create(Slugs).Sync();
    }

    private static string StoredPayloadType(TestDatabase database) =>
        database.Scalar<string>("SELECT json_extract(definition, '$.payloadTypeName') FROM lui_meta WHERE index_name = 'chips';");

    // ---- every kind, no definition in the calling code ------------------------------------------------------------

    [Theory]
    [InlineData("recipes", LuiIndexSource.Embedded, 2)]
    [InlineData("chips", LuiIndexSource.Embedded, 3)]
    [InlineData("linked_tags", LuiIndexSource.Linked, 3)]
    [InlineData("slugs", LuiIndexSource.Linked, 3)]
    public void Indexes_Count_Check_Optimize_work_for_every_kind(string name, LuiIndexSource source, long count)
    {
        using var database = new TestDatabase();
        WriteAllKinds(database);

        // a catalog that knows no definition, no payload type and no serializer: what a tool or an admin endpoint has
        using var tool = LuiCatalog.Open(database.FilePath);

        var info = tool.Indexes().Single(index => string.Equals(index.Name, name, StringComparison.Ordinal));
        info.Source.ShouldBe(source);
        tool.Count(name).ShouldBe(count);
        var report = tool.Check(name);
        report.IsHealthy.ShouldBeTrue(string.Join("; ", report.Problems));
        Should.NotThrow(() => tool.Optimize(name));
        tool.Count(name).ShouldBe(count, "optimizing changes no entry");
        tool.Check(name).IsHealthy.ShouldBeTrue();
    }

    [Fact]
    public void Indexes_describes_every_index_by_what_is_stored()
    {
        using var database = new TestDatabase();
        WriteAllKinds(database);
        using var tool = LuiCatalog.Open(database.FilePath);

        var indexes = tool.Indexes();

        indexes.Select(index => index.Name).ShouldBe(["chips", "linked_tags", "recipes", "slugs"], "in the order of the names");
        indexes.ShouldBe(
            [
                new LuiIndexInfo
                {
                    Name = "chips",
                    Source = LuiIndexSource.Embedded,
                    Kind = LuiIndexKind.MultiWord,
                    PayloadType = StoredPayloadType(database),
                    PayloadFormat = "lines-v1",
                    KeyType = LuiKeyType.String,
                    TypoDistance = LuiTypoDistance.TwoErrors,
                    TypoDictionaryState = LuiTypoDictionaryState.NotBuilt,
                },
                new LuiIndexInfo
                {
                    Name = "linked_tags",
                    Source = LuiIndexSource.Linked,
                    Kind = LuiIndexKind.SingleWord,
                    PayloadType = null,
                    PayloadFormat = null,
                    KeyType = LuiKeyType.Int,
                    TypoDistance = LuiTypoDistance.None,
                    TypoDictionaryState = LuiTypoDictionaryState.NotBuilt,
                },
                new LuiIndexInfo
                {
                    Name = "recipes",
                    Source = LuiIndexSource.Embedded,
                    Kind = LuiIndexKind.SingleWord,
                    PayloadType = null,
                    PayloadFormat = null,
                    KeyType = LuiKeyType.Long,
                    TypoDistance = LuiTypoDistance.None,
                    TypoDictionaryState = LuiTypoDictionaryState.NotBuilt,
                },
                new LuiIndexInfo
                {
                    Name = "slugs",
                    Source = LuiIndexSource.Linked,
                    Kind = LuiIndexKind.SingleWord,
                    PayloadType = null,
                    PayloadFormat = null,
                    KeyType = LuiKeyType.Long,
                    TypoDistance = LuiTypoDistance.None,
                    TypoDictionaryState = LuiTypoDictionaryState.NotBuilt,
                },
            ]);
        StoredPayloadType(database).ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void An_empty_catalog_has_no_index()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();

        catalog.Indexes().ShouldBeEmpty();
    }

    [Fact]
    public void The_typo_dictionary_state_is_read_when_Indexes_is_called()
    {
        using var database = new TestDatabase();
        WriteAllKinds(database);
        using var tool = LuiCatalog.Open(database.FilePath);
        tool.Indexes().ShouldAllBe(index => index.TypoDictionaryState == LuiTypoDictionaryState.NotBuilt);

        tool.WarmUpTypoDictionaries(["chips"]);

        var states = tool.Indexes().ToDictionary(index => index.Name, index => index.TypoDictionaryState, StringComparer.Ordinal);
        states["chips"].ShouldBe(LuiTypoDictionaryState.Current, "the state of the dictionary of this catalog, at the call");
        states["recipes"].ShouldBe(LuiTypoDictionaryState.NotBuilt);
    }

    // ---- no SQL for the definition fields; no payload, no map ------------------------------------------------------

    [Fact]
    public void Indexes_executes_no_SQL_for_the_definition_fields()
    {
        using var database = new TestDatabase();
        WriteAllKinds(database);
        using var tool = LuiCatalog.Open(database.FilePath);
        var commands = new Nordmann.LUI.Storage.CommandCounter();
        tool.Database.CommandCounter = commands;
        var reloads = tool.StoredIndexes.Reloads;

        for (var call = 0; call < 20; call++)
        {
            tool.Indexes().Count.ShouldBe(4);
        }

        tool.StoredIndexes.Reloads.ShouldBe(reloads, "the definitions are those of the catalog's memory: lui_meta is not read while the schema counter is unchanged");
        commands.Count.ShouldBe(20, "exactly the statement that asks whether the schema counter moved, once per call — no statement for a field");

        // the file moved: one read of the definitions, and then no more
        Run(database, "CREATE TABLE somewhere_else(id INTEGER PRIMARY KEY);");
        _ = tool.Indexes();
        tool.StoredIndexes.Reloads.ShouldBe(reloads + 1);
        _ = tool.Indexes();
        tool.StoredIndexes.Reloads.ShouldBe(reloads + 1);
    }

    [Fact]
    public void Indexes_finds_an_index_that_another_catalog_created_after_the_open()
    {
        using var database = new TestDatabase();
        using var first = database.Open(Lines(new LinesChipSerializer()));
        using var second = database.Open();
        _ = first.Create(Recipes);
        second.Indexes().Select(index => index.Name).ShouldBe(["recipes"]);

        _ = first.Create(Chips);
        first.DropIndex("recipes");

        second.Indexes().Select(index => index.Name).ShouldBe(["chips"], "the stored definitions are not a stale list: the schema counter moved");
    }

    [Fact]
    public void Check_by_name_reads_no_payload_and_calls_no_map()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Insert(database, (1, "vegan", 5), (2, "veganer", 4));
        Run(database, "UPDATE tag SET slug = name;");
        var serializer = new LinesChipSerializer();
        using var catalog = database.Open(Lines(serializer));
        var chips = catalog.Create(Chips);
        chips.Upsert("alpha", "vegan", new Chip("one"));
        chips.Upsert("beta", "veganer", new Chip("two"));
        Run(database, $"UPDATE lui_chips SET payload = {Corrupt} WHERE key = 'beta';");
        var calls = 0;
        var linked = catalog.Create(
            new LuiLinkedIndexDefinition<long, Chip>(
                "linked_chips",
                index => index.SingleWord().LinkTo("tag", "id", "name", weight: "usage_count")
                    .WithPayload(["slug"], row =>
                    {
                        Interlocked.Increment(ref calls);
                        return new Chip(row.GetString("slug"));
                    })));
        _ = linked.Sync();
        calls = 0;

        catalog.Check("chips").IsHealthy.ShouldBeTrue("the structural check does not look at a payload");
        catalog.Check("linked_chips").IsHealthy.ShouldBeTrue();
        catalog.Count("chips").ShouldBe(2);
        catalog.Count("linked_chips").ShouldBe(2);
        catalog.Optimize("chips");
        catalog.Optimize("linked_chips");
        _ = catalog.Indexes();

        serializer.Deserialized.ShouldBe(0, "no operation by name reads a payload");
        calls.ShouldBe(0, "no operation by name calls a map");
        chips.CheckPayloads().Unreadable.ShouldHaveSingleItem().Key.ShouldBe("beta", "the payload is damaged — the check by name just does not look");
    }

    // ---- a missing name -------------------------------------------------------------------------------------------

    [Fact]
    public void A_missing_name_asked_twice_reloads_once()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        _ = catalog.Create(Recipes);
        var reloads = catalog.StoredIndexes.Reloads;

        Should.Throw<LuiIndexNotFoundException>(() => catalog.Count("ghost")).IndexName.ShouldBe("ghost");
        catalog.StoredIndexes.Reloads.ShouldBe(reloads + 1, "an unknown name reloads the definitions once");

        Should.Throw<LuiIndexNotFoundException>(() => catalog.Count("ghost"));
        Should.Throw<LuiIndexNotFoundException>(() => catalog.Check("ghost")).IndexName.ShouldBe("ghost");
        Should.Throw<LuiIndexNotFoundException>(() => catalog.Optimize("ghost")).IndexName.ShouldBe("ghost");
        catalog.StoredIndexes.Reloads.ShouldBe(reloads + 1, "the missing name is remembered while the SQLite schema counter is unchanged");

        // the schema changes — a table of the application is enough: a harmless reload, and the name is looked for again
        Run(database, "CREATE TABLE somewhere_else(id INTEGER PRIMARY KEY);");
        Should.Throw<LuiIndexNotFoundException>(() => catalog.Count("ghost"));
        catalog.StoredIndexes.Reloads.ShouldBe(reloads + 2, "a changed schema counter reloads again");
        Should.Throw<LuiIndexNotFoundException>(() => catalog.Check("ghost"));
        catalog.StoredIndexes.Reloads.ShouldBe(reloads + 2);
    }

    [Fact]
    public void A_name_that_another_catalog_creates_in_the_meantime_is_found()
    {
        using var database = new TestDatabase();
        using var first = database.Open(Lines(new LinesChipSerializer()));
        using var second = database.Open();
        _ = first.Create(Recipes);
        Should.Throw<LuiIndexNotFoundException>(() => second.Count("chips"));

        var chips = first.Create(Chips);
        chips.Upsert("alpha", "vegan", new Chip("one"));

        second.Count("chips").ShouldBe(1, "the missing name is remembered only until the schema counter changes");
    }

    // ---- the guards of the operation -------------------------------------------------------------------------------

    [Fact]
    public void An_index_that_another_catalog_dropped_is_not_found_and_the_exception_names_it()
    {
        using var database = new TestDatabase();
        using var first = database.Open();
        using var second = database.Open();
        first.Create(Recipes).Upsert(1, "kuchen");
        second.Count("recipes").ShouldBe(1);

        first.DropIndex("recipes");

        Should.Throw<LuiIndexNotFoundException>(() => second.Count("recipes")).IndexName.ShouldBe("recipes");
        Should.Throw<LuiIndexNotFoundException>(() => second.Check("recipes")).IndexName.ShouldBe("recipes");
        Should.Throw<LuiIndexNotFoundException>(() => second.Optimize("recipes")).IndexName.ShouldBe("recipes");
    }

    [Fact]
    public void An_index_that_was_created_again_with_another_definition_is_used_as_it_is_stored_and_throws_no_mismatch()
    {
        using var database = new TestDatabase();
        using var first = database.Open();
        using var second = database.Open();
        first.Create(Recipes).Upsert(1, "kuchen");
        second.Count("recipes").ShouldBe(1);
        second.Indexes().Single().Kind.ShouldBe(LuiIndexKind.SingleWord);

        // another process: the same name, another definition and other content — the memory of `second` still holds the old definition
        first.DropIndex("recipes");
        var words = first.Create(RecipesOfWords);
        words.Upsert(1, "gedeckter apfelkuchen");
        words.Upsert(2, "rhabarbertorte");

        second.Count("recipes").ShouldBe(2, "a call by name declares no definition: there is nothing to mismatch");
        second.Check("recipes").IsHealthy.ShouldBeTrue();
        second.Indexes().Single().Kind.ShouldBe(LuiIndexKind.MultiWord);
    }

    [Fact]
    public void Count_answers_for_the_snapshot_it_checked_when_the_index_is_swapped_between_the_check_and_the_count()
    {
        using var database = new TestDatabase();
        using var first = database.Open();
        using var second = database.Open();
        first.Create(Recipes).Upsert(1, "kuchen");
        second.Count("recipes").ShouldBe(1);

        // the swap is by another catalog, at the place between the check of the schema counter and the work
        second.Database.AfterSchemaCheck = () =>
        {
            second.Database.AfterSchemaCheck = null;
            first.DropIndex("recipes");
            _ = first.Create(RecipesOfWords);
        };
        var counted = -1L;
        var failure = Record.Exception(() => counted = second.Count("recipes"));

        if (failure is null)
        {
            counted.ShouldBe(1, "the count of the snapshot the check was made in — never of the index that was created again");
        }
        else
        {
            failure.ShouldBeOfType<LuiIndexNotFoundException>().IndexName.ShouldBe("recipes");
        }
    }

    [Fact]
    public void Every_operation_by_name_reads_the_schema_counter_in_the_snapshot_of_its_work()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create(Recipes).Upsert(1, "kuchen");
        for (var warm = 0; warm < 3; warm++)
        {
            _ = catalog.Count("recipes");
        }

        var commands = new Nordmann.LUI.Storage.CommandCounter();
        catalog.Database.CommandCounter = commands;

        _ = catalog.Count("recipes");

        commands.Count.ShouldBe(4, "Count: the snapshot (BEGIN), PRAGMA schema_version, the count, the end of the snapshot — the check and the count are one step, and the name costs no statement");
    }

    // ---- the rest of the contract ---------------------------------------------------------------------------------

    [Fact]
    public void A_name_that_is_no_index_name_is_an_argument_exception()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();

        Should.Throw<ArgumentException>(() => catalog.Count("Not A Name")).ParamName.ShouldBe("name");
        Should.Throw<ArgumentException>(() => catalog.Check("")).ParamName.ShouldBe("name");
        Should.Throw<ArgumentException>(() => catalog.Optimize("1abc")).ParamName.ShouldBe("name");
    }

    [Fact]
    public void A_disposed_catalog_refuses_the_operations_by_name()
    {
        using var database = new TestDatabase();
        var catalog = database.Open();
        _ = catalog.Create(Recipes);
        catalog.Dispose();

        Should.Throw<ObjectDisposedException>(() => catalog.Indexes());
        Should.Throw<ObjectDisposedException>(() => catalog.Count("recipes"));
        Should.Throw<ObjectDisposedException>(() => catalog.Check("recipes"));
        Should.Throw<ObjectDisposedException>(() => catalog.Optimize("recipes"));
    }

    [Fact]
    public void A_read_only_catalog_lists_and_counts_and_refuses_what_writes_naming_the_index()
    {
        using var database = new TestDatabase();
        WriteAllKinds(database);
        using var reader = LuiCatalog.OpenReadOnly(database.FilePath);

        reader.Indexes().Count.ShouldBe(4);
        foreach (var name in AllNames)
        {
            reader.Count(name).ShouldBeGreaterThan(0);
            Should.Throw<LuiReadOnlyException>(() => reader.Optimize(name)).Message.ShouldContain($"'{name}'");
            Should.Throw<LuiReadOnlyException>(() => reader.Check(name)).Message.ShouldContain($"'{name}'", customMessage: "the integrity check of FTS5 is an insert that is rolled back");
        }
    }

    [Fact]
    public void Optimize_and_Check_change_no_entry_and_the_index_still_answers()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var recipes = catalog.Create(Recipes);
        recipes.UpsertMany(Enumerable.Range(1, 50).Select(key => new LuiEntry<long>(key, "kuchen" + key)));

        catalog.Optimize("recipes");
        catalog.Check("recipes").IsHealthy.ShouldBeTrue();

        catalog.Count("recipes").ShouldBe(50);
        recipes.Search("kuchen7").Hits.ShouldNotBeEmpty();
    }
}
