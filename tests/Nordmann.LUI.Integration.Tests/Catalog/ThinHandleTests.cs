using Nordmann.LUI.Integration.Tests.Concurrency;
using Nordmann.LUI.Storage;
using Shouldly;
using static Nordmann.LUI.Integration.Tests.Linked.LinkedTable;

namespace Nordmann.LUI.Integration.Tests.Catalog;

/// <summary>
/// The handle is thin and the runtime lives in the catalog (ADR-0075, issue #265): the catalog reads the stored definitions
/// of the file once, when it is opened, keeps the runtime of an index — its source, its statements, its query runner — and
/// hands out the <b>same handle</b> for the same definition object. After the first call <c>catalog.Index(definition)</c> is
/// a lookup: it allocates nothing and executes no SQL, a request can take its handle where it needs it.
/// <para>
/// What is measured is a number of bytes and a number of commands, which are the same on every machine, so they are held in
/// tests (<c>docs/testing.md</c>); how long a call takes is the business of the performance analysis of the release (#275).
/// </para>
/// </summary>
public sealed class ThinHandleTests
{
    private const int Warmup = 200;
    private const int Calls = 1_000;

    private static readonly LuiIndexDefinition<long> Recipes = new("recipes", index => index.SingleWord());

    private static readonly LuiIndexDefinition<long, Chip> Chips = new("chips", index => index.SingleWord());

    private static readonly LuiLinkedIndexDefinition<long> LinkedTags =
        new("linked_tags", index => index.SingleWord().LinkTo("tag", "id", "name", weight: "usage_count"));

    private static readonly LuiLinkedIndexDefinition<long, string> Slugs =
        new("slugs", index => index.SingleWord().LinkTo("tag", "id", "name").WithPayload(["slug"], row => row.GetString("slug")));

    private static LuiCatalog CreateAll(TestDatabase database)
    {
        Run(database, Table);
        var catalog = database.Open();
        catalog.Create(Recipes, Chips, LinkedTags, Slugs);
        return catalog;
    }

    /// <summary>The four kinds of definition, as calls that take the handle from the catalog.</summary>
    private static (string Name, Func<object> Open)[] Opens(LuiCatalog catalog) =>
    [
        ("embedded", () => catalog.Index(Recipes)),
        ("embedded with a payload", () => catalog.Index(Chips)),
        ("linked", () => catalog.Index(LinkedTags)),
        ("linked with a payload", () => catalog.Index(Slugs)),
    ];

    // ---- the open call is a lookup ---------------------------------------------------------------------------------

    [Fact]
    public void Index_of_a_definition_allocates_nothing_after_the_first_call()
    {
        using var database = new TestDatabase();
        using var catalog = CreateAll(database);

        foreach (var (name, open) in Opens(catalog))
        {
            _ = open();
            var bytes = Allocated(open);

            bytes.ShouldBe(0, $"catalog.Index(definition) of {name} allocated {bytes:N0} B in {Calls:N0} calls — a thin handle is a lookup");
        }
    }

    [Fact]
    public void Index_of_a_definition_creates_no_command_after_the_first_call()
    {
        using var database = new TestDatabase();
        using var catalog = CreateAll(database);
        var commands = new CommandCounter();
        catalog.Database.CommandCounter = commands;

        foreach (var (name, open) in Opens(catalog))
        {
            _ = open();
            var before = commands.Count;
            for (var call = 0; call < Calls; call++)
            {
                _ = open();
            }

            commands.Count.ShouldBe(before, $"catalog.Index(definition) of {name} created a SqliteCommand — it executes no SQL");
        }
    }

    [Fact]
    public void The_first_call_after_the_open_reads_nothing_either_because_the_definitions_are_in_memory()
    {
        using var database = new TestDatabase();
        using (var first = CreateAll(database))
        {
            first.Index(Recipes).Upsert(1, "kuchen");
        }

        using var catalog = LuiCatalog.Open(database.FilePath);
        var commands = new CommandCounter();
        catalog.Database.CommandCounter = commands;
        var reads = catalog.StoredIndexes.Reloads;

        foreach (var (name, open) in Opens(catalog))
        {
            _ = open();
            commands.Count.ShouldBe(0, $"the first catalog.Index(definition) of {name} after Open read the file again");
        }

        catalog.StoredIndexes.Reloads.ShouldBe(reads, "the stored definitions are read once, when the catalog is opened");
    }

    [Fact]
    public void Index_returns_the_same_instance_for_the_same_definition()
    {
        using var database = new TestDatabase();
        using var catalog = CreateAll(database);

        foreach (var (name, open) in Opens(catalog))
        {
            open().ShouldBeSameAs(open(), $"the handle of {name} is cached per definition");
        }

        // the handle that Create gives is the one Index gives
        catalog.Create(Recipes).ShouldBeSameAs(catalog.Index(Recipes));
        catalog.Create(Chips).ShouldBeSameAs(catalog.Index(Chips));
        catalog.Create(LinkedTags).ShouldBeSameAs(catalog.Index(LinkedTags));
        catalog.Create(Slugs).ShouldBeSameAs(catalog.Index(Slugs));
    }

    [Fact]
    public void Two_definition_objects_of_one_index_each_get_their_own_handle_over_one_runtime()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var again = new LuiIndexDefinition<long>("recipes", index => index.SingleWord());
        catalog.Create(Recipes);

        var one = catalog.Index(Recipes);
        var other = catalog.Index(again);

        other.ShouldNotBeSameAs(one, "a handle is cached per definition object, not per name");
        one.Upsert(1, "kuchen");
        other.Count().ShouldBe(1, "both are views of the one index");
    }

    [Fact]
    public void The_handles_of_two_catalogs_are_not_shared()
    {
        using var database = new TestDatabase();
        using var first = database.Open();
        using var second = database.Open();
        first.Create(Recipes);

        first.Index(Recipes).ShouldNotBeSameAs(second.Index(Recipes), "the cache belongs to the catalog");
    }

    [Fact]
    public void A_catalog_that_is_disposed_refuses_the_open_call_even_for_a_handle_that_is_cached()
    {
        using var database = new TestDatabase();
        var catalog = CreateAll(database);
        _ = catalog.Index(Recipes);
        catalog.Dispose();

        Should.Throw<ObjectDisposedException>(() => catalog.Index(Recipes));
    }

    // ---- a request takes its handle where it needs it --------------------------------------------------------------

    [Fact]
    public void A_query_with_a_handle_per_request_allocates_as_one_with_a_held_handle()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var recipes = catalog.Create(Recipes);
        recipes.UpsertMany(Enumerable.Range(1, 2_000).Select(key => new LuiEntry<long>(key, "kuchen " + key) { Weight = key }));

        var held = Allocated(() => recipes.Search("kuc"));
        var perRequest = Allocated(() => catalog.Index(Recipes).Search("kuc"));

        // the handle adds nothing: the difference is the noise of the allocation of a query (the tiers of the compiler, a buffer
        // of a thread that was grown), a few bytes per call at most — the open call itself is 0 B
        (perRequest <= held * 1.01 + (16L * Calls))
            .ShouldBeTrue($"a query with a handle per request allocated {perRequest / Calls:N0} B per call, one with a held handle {held / Calls:N0} B");
    }

    // ---- the unknown name ------------------------------------------------------------------------------------------

    [Fact]
    public void An_unknown_name_reloads_once_then_throws_and_does_not_reload_again_while_the_counter_is_unchanged()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create(Recipes);
        var ghost = new LuiIndexDefinition<long>("ghost", index => index.SingleWord());
        var reloads = catalog.StoredIndexes.Reloads;

        Should.Throw<LuiIndexNotFoundException>(() => catalog.Index(ghost)).IndexName.ShouldBe("ghost");
        catalog.StoredIndexes.Reloads.ShouldBe(reloads + 1, "an unknown name reloads the definitions once");

        for (var ask = 0; ask < 5; ask++)
        {
            Should.Throw<LuiIndexNotFoundException>(() => catalog.Index(ghost));
        }

        catalog.StoredIndexes.Reloads.ShouldBe(reloads + 1, "the missing name is remembered while the SQLite schema counter is unchanged");

        // the schema changes — a table of the application is enough: a harmless reload, and the name is looked for again
        Run(database, "CREATE TABLE somewhere_else(id INTEGER PRIMARY KEY);");
        Should.Throw<LuiIndexNotFoundException>(() => catalog.Index(ghost));
        catalog.StoredIndexes.Reloads.ShouldBe(reloads + 2, "a changed schema counter reloads again");
        Should.Throw<LuiIndexNotFoundException>(() => catalog.Index(ghost));
        catalog.StoredIndexes.Reloads.ShouldBe(reloads + 2);
    }

    [Fact]
    public void The_unknown_name_is_found_when_another_catalog_creates_it_in_the_meantime()
    {
        using var database = new TestDatabase();
        using var first = database.Open();
        using var second = database.Open();
        first.Create(Recipes);
        var late = new LuiIndexDefinition<long>("late", index => index.SingleWord());
        Should.Throw<LuiIndexNotFoundException>(() => second.Index(late));

        first.Create(late);

        second.Index(late).Name.ShouldBe("late", "the missing name is remembered only until the schema counter changes");
    }

    // ---- the check of every operation -----------------------------------------------------------------------------

    [Fact]
    public void A_change_of_the_schema_reloads_the_definitions_once_at_the_next_operation()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var recipes = catalog.Create(Recipes);
        recipes.Upsert(1, "kuchen");
        var reloads = catalog.StoredIndexes.Reloads;

        for (var call = 0; call < 20; call++)
        {
            recipes.Count().ShouldBe(1);
            recipes.Search("kuc").Hits.Count.ShouldBe(1);
        }

        catalog.StoredIndexes.Reloads.ShouldBe(reloads, "an unchanged schema counter reloads nothing, and writing content does not change it");

        Run(database, "CREATE TABLE somewhere_else(id INTEGER PRIMARY KEY);");
        recipes.Count().ShouldBe(1);
        catalog.StoredIndexes.Reloads.ShouldBe(reloads + 1, "the schema of the file changed: one reload");

        for (var call = 0; call < 20; call++)
        {
            recipes.Search("kuc").Hits.Count.ShouldBe(1);
        }

        catalog.StoredIndexes.Reloads.ShouldBe(reloads + 1);
    }

    [Fact]
    public void Every_operation_of_a_handle_reads_the_schema_counter_before_its_first_statement()
    {
        using var database = new TestDatabase();
        using var catalog = CreateAll(database);
        var commands = new CommandCounter();
        catalog.Database.CommandCounter = commands;
        var recipes = catalog.Index(Recipes);
        recipes.Upsert(1, "kuchen");
        for (var warm = 0; warm < 5; warm++)
        {
            _ = recipes.Search("kuc");
            _ = recipes.Count();
        }

        // an operation is its own statements plus the one that reads the schema counter: with the check, a query that finds
        // nothing to read still creates a command
        var before = commands.Count;
        _ = recipes.Count();
        (commands.Count - before).ShouldBe(4, "Count: the snapshot (BEGIN), PRAGMA schema_version, the count, the end of the snapshot — the check and the count are one step");

        before = commands.Count;
        _ = recipes.Search("kuc");
        (commands.Count - before).ShouldBeGreaterThanOrEqualTo(2, "Search: the schema counter first, then the statements of the query");
    }

    // ---- the lifetime of a handle in the catalog that dropped the index -------------------------------------------

    [Fact]
    public void A_handle_of_an_index_that_this_catalog_dropped_stays_dropped_and_the_open_call_gives_a_new_one()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var before = catalog.Create(Recipes);
        before.Upsert(1, "kuchen");

        catalog.DropIndex("recipes");
        Should.Throw<LuiIndexNotFoundException>(() => catalog.Index(Recipes), "the open call looks at the definitions, which the drop updated");
        var after = catalog.Create(Recipes);

        after.ShouldNotBeSameAs(before, "the runtime of the dropped index is gone, the handle is a new one");
        after.Count().ShouldBe(0, "the new index is empty");
        Should.Throw<LuiIndexNotFoundException>(() => before.Count(), "an index object that was created before the drop does not survive it (DropIndex)");
        catalog.Index(Recipes).ShouldBeSameAs(after);
    }

    [Fact]
    public void The_typo_dictionary_of_a_dropped_index_is_not_the_one_of_the_next()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var corrected = new LuiIndexDefinition<long>("corrected", index => index.SingleWord().TypoTolerance());
        var before = catalog.Create(corrected);
        before.Upsert(1, "kuchen");
        before.WarmUpTypoDictionary();
        before.TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.Current);

        catalog.DropIndex("corrected");
        var after = catalog.Create(corrected);

        after.TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.NotBuilt, "the words of the dropped index are not the words of the next one");
    }

    // ---- many threads ----------------------------------------------------------------------------------------------

    [Fact]
    public void Concurrent_first_calls_of_one_definition_get_one_handle()
    {
        using var database = new TestDatabase();
        using (var creator = CreateAll(database))
        {
            creator.Index(Recipes).Upsert(1, "kuchen");
        }

        using var catalog = LuiCatalog.Open(database.FilePath);
        var handles = new LuiEmbeddedIndex<long>[8];

        AtTheSameTime.Run(8, number =>
        {
            handles[number] = catalog.Index(Recipes);
        });

        handles.Distinct().Count().ShouldBe(1, "a definition has one handle, also when its first calls come at once");
    }

    [Fact]
    public async Task Operations_run_while_the_schema_of_the_file_changes_under_them()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var recipes = catalog.Create(Recipes);
        recipes.UpsertMany(Enumerable.Range(1, 500).Select(key => new LuiEntry<long>(key, "kuchen " + key)));
        using var stop = new CancellationTokenSource();

        // every change of the schema is a new counter for every handle: a reload, once, by whoever comes first
        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            var rounds = 0;
            do
            {
                recipes.Count().ShouldBeInRange(500, 501, "the writer writes one entry beside the readers and takes it out again");
                recipes.Search("kuchen 4").Hits.ShouldNotBeEmpty();
                catalog.Index(Recipes).ShouldBeSameAs(recipes);
                rounds++;
            }
            while (!stop.IsCancellationRequested);

            return rounds;
        })).ToArray();

        for (var table = 0; table < 40; table++)
        {
            Run(database, $"CREATE TABLE churn_{table}(id INTEGER PRIMARY KEY);");
            recipes.Upsert(1000 + table, "neu " + table);
            Run(database, $"DROP TABLE churn_{table};");
            recipes.Remove(1000 + table);
        }

        await stop.CancelAsync();
        (await Task.WhenAll(readers)).ShouldAllBe(rounds => rounds > 0);
        recipes.Count().ShouldBe(500);
    }

    // ---- helpers -----------------------------------------------------------------------------------------------------

    /// <summary>The bytes the current thread allocates in <see cref="Calls"/> calls of the action, after a warm-up.</summary>
    private static long Allocated(Func<object> action)
    {
        for (var call = 0; call < Warmup; call++)
        {
            _ = action();
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var call = 0; call < Calls; call++)
        {
            _ = action();
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
