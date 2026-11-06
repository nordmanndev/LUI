using Nordmann.LUI.Schema;
using Shouldly;

namespace Nordmann.LUI.Integration.Tests;

/// <summary>
/// The shape of the API before 0.1.0 (ADR-0013): the entry, the batch, the options that check themselves,
/// the read and write interfaces.
/// </summary>
public sealed class ApiShapeTests
{
    // ---- the entry -----------------------------------------------------------------------------

    [Fact]
    public void An_entry_checks_its_values_where_they_are_set()
    {
        Should.Throw<ArgumentException>(() => new LuiEntry(1, string.Empty))
            .Message.ShouldContain("Text");
        Should.Throw<ArgumentNullException>(() => new LuiEntry(1, null!));
        Should.Throw<ArgumentOutOfRangeException>(() => new LuiEntry(1, "a") { Weight = -1 });
        Should.Throw<ArgumentOutOfRangeException>(() => new LuiEntry(1, "a") { Weight = 536_870_912 });
        Should.Throw<ArgumentOutOfRangeException>(() => new LuiEntry(1, "a") { Priority = (LuiPriority)7 });

        var entry = new LuiEntry(1, "a");
        Should.Throw<ArgumentException>(() => entry with { Text = string.Empty });
    }

    [Fact]
    public void An_entry_is_normal_and_weightless_unless_it_says_otherwise()
    {
        var entry = new LuiEntry(1, "vegan");

        entry.Weight.ShouldBe(0);
        entry.Priority.ShouldBe(LuiPriority.Normal);
    }

    [Fact]
    public void The_short_form_of_the_upsert_replaces_the_whole_entry_and_clears_the_payload()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());

        tags.Upsert(new LuiEntry(1, "vegan") { Weight = 5, Payload = new Chip("v"), Priority = LuiPriority.System });
        tags.Upsert(1, "vegan", 7);

        var hit = tags.Search("vegan").Hits.ShouldHaveSingleItem();
        hit.HasPayload.ShouldBeFalse();
        hit.Weight.ShouldBe(7);
        hit.Priority.ShouldBe(LuiPriority.Normal);
    }

    // ---- the batch -----------------------------------------------------------------------------

    [Fact]
    public void A_batch_writes_every_entry_and_says_how_many()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());

        var written = tags.UpsertMany(Enumerable.Range(1, 1_000).Select(
            i => new LuiEntry(i, $"kuchen{i}") { Weight = i, Payload = new Chip($"k{i}") }));

        written.ShouldBe(1_000);
        tags.Count().ShouldBe(1_000);
        tags.Search("kuchen1000").Hits[0].GetPayload<Chip>().ShouldBe(new Chip("k1000"));
    }

    [Fact]
    public void A_key_that_comes_twice_in_a_batch_keeps_the_last_entry()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());

        tags.UpsertMany(
        [
            new LuiEntry(1, "apfel") { Weight = 1 },
            new LuiEntry(1, "birne") { Weight = 2 },
        ]);

        tags.Count().ShouldBe(1);
        tags.Search("apfel").Hits.ShouldBeEmpty();
        tags.Search("birne").Hits.ShouldHaveSingleItem().Weight.ShouldBe(2);
    }

    [Fact]
    public void A_batch_in_its_own_transaction_writes_all_or_nothing()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());

        Should.Throw<InvalidOperationException>(() => tags.UpsertMany(FailingAfter(10)));

        tags.Count().ShouldBe(0);

        // the writer is free again: a single write and another batch go through
        tags.Upsert(1, "apfel", 1);
        tags.UpsertMany([new LuiEntry(2, "birne")]).ShouldBe(1);
        tags.Count().ShouldBe(2);
    }

    [Fact]
    public void A_cancelled_batch_rolls_its_own_transaction_back()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        using var cancellation = new CancellationTokenSource();

        IEnumerable<LuiEntry> Entries()
        {
            for (var i = 1; i <= 100; i++)
            {
                if (i == 50)
                {
                    cancellation.Cancel();
                }

                yield return new LuiEntry(i, $"kuchen{i}");
            }
        }

        Should.Throw<OperationCanceledException>(() => tags.UpsertMany(Entries(), cancellationToken: cancellation.Token));

        tags.Count().ShouldBe(0);
    }

    [Fact]
    public void A_batch_in_the_transaction_of_the_application_is_part_of_it()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());

        using (var connection = database.Connect())
        using (var transaction = connection.BeginTransaction())
        {
            tags.UpsertMany([new LuiEntry(1, "apfel"), new LuiEntry(2, "birne")], transaction)
                .ShouldBe(2);
            transaction.Rollback();
        }

        tags.Count().ShouldBe(0);

        using (var connection = database.Connect())
        using (var transaction = connection.BeginTransaction())
        {
            tags.UpsertMany([new LuiEntry(1, "apfel")], transaction);
            transaction.Commit();
        }

        tags.Search("apfel").Hits.ShouldHaveSingleItem();
    }

    [Fact]
    public void A_batch_teaches_the_typo_dictionary_its_new_words_as_a_single_upsert_does()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var batch = catalog.Create("batch", index => index.SingleWord().TypoTolerance());
        var single = catalog.Create("single", index => index.SingleWord().TypoTolerance());
        foreach (var index in new[] { batch, single })
        {
            index.Upsert(1, "Apfelkuchen", 10);
            index.WarmUpTypoDictionary();
        }

        batch.UpsertMany([new LuiEntry(2, "Himbeere") { Weight = 50 }]);
        single.Upsert(2, "Himbeere", 50);

        var learned = batch.Search("himbere");
        learned.CorrectedQuery.ShouldNotBeNull();
        learned.CorrectedQuery.ShouldBe(single.Search("himbere").CorrectedQuery);
        learned.Hits.ShouldHaveSingleItem().Key.ShouldBe(2);
    }

    [Fact]
    public async Task A_batch_runs_beside_single_writes_of_other_indexes()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var batch = catalog.Create("batch", index => index.SingleWord());
        var single = catalog.Create("single", index => index.SingleWord());

        var writer = Task.Run(() =>
        {
            for (var i = 1; i <= 200; i++)
            {
                single.Upsert(i, $"torte{i}", i);
            }
        });

        for (var round = 0; round < 10; round++)
        {
            batch.UpsertMany(Enumerable.Range((round * 100) + 1, 100).Select(i => new LuiEntry(i, $"kuchen{i}")));
        }

        await writer.WaitAsync(TimeSpan.FromSeconds(30));
        batch.Count().ShouldBe(1_000);
        single.Count().ShouldBe(200);
    }

    // ---- the options ---------------------------------------------------------------------------

    [Fact]
    public void A_query_option_out_of_range_fails_where_it_is_set()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        var query = tags.Query("vegan");

        // the builder throws at the call, not later in Execute()
        Should.Throw<ArgumentOutOfRangeException>(() => query.Take(0)).Message.ShouldContain("MaxResults");
        Should.Throw<ArgumentOutOfRangeException>(() => query.Candidates(100_001)).Message.ShouldContain("Candidates");
        Should.Throw<ArgumentOutOfRangeException>(() => query.Rank((LuiRankingOrder)42)).Message.ShouldContain("Rank");
        Should.Throw<ArgumentOutOfRangeException>(() => new LuiQueryOptions { PriorityMode = (LuiPriorityMode)42 })
            .Message.ShouldContain("PriorityMode");
    }

    [Fact]
    public void A_catalog_option_out_of_range_fails_where_it_is_set()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new LuiCatalogOptions { CacheSizeMegabytes = 0 })
            .Message.ShouldContain("CacheSizeMegabytes");
        Should.Throw<ArgumentOutOfRangeException>(() => new LuiCatalogOptions { MemoryMapMegabytes = -1 })
            .Message.ShouldContain("MemoryMapMegabytes");
        Should.Throw<ArgumentOutOfRangeException>(() => new LuiCatalogOptions { BusyTimeout = TimeSpan.FromSeconds(-1) })
            .Message.ShouldContain("BusyTimeout");
        Should.Throw<ArgumentNullException>(() => new LuiCatalogOptions { TimeProvider = null! });

        Should.NotThrow(() => new LuiCatalogOptions { CacheSizeMegabytes = null, MemoryMapMegabytes = 1 });
    }

    [Fact]
    public void The_exception_of_a_wrong_value_names_the_property_as_its_parameter()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new LuiQueryOptions { MaxResults = 0 }).ParamName.ShouldBe("MaxResults");
        Should.Throw<ArgumentOutOfRangeException>(() => new LuiQueryOptions { Candidates = 0 }).ParamName.ShouldBe("Candidates");
        Should.Throw<ArgumentOutOfRangeException>(() => new LuiQueryOptions { Rank = (LuiRankingOrder)42 }).ParamName.ShouldBe("Rank");
        Should.Throw<ArgumentOutOfRangeException>(() => new LuiCatalogOptions { CacheSizeMegabytes = 0 }).ParamName.ShouldBe("CacheSizeMegabytes");
        Should.Throw<ArgumentOutOfRangeException>(() => new LuiCatalogOptions { BusyTimeout = TimeSpan.FromSeconds(-1) }).ParamName.ShouldBe("BusyTimeout");
        Should.Throw<ArgumentNullException>(() => new LuiCatalogOptions { TimeProvider = null! }).ParamName.ShouldBe("TimeProvider");
        Should.Throw<ArgumentOutOfRangeException>(() => new LuiEntry(1, "a") { Weight = -1 }).ParamName.ShouldBe("Weight");
        Should.Throw<ArgumentOutOfRangeException>(() => new LuiEntry(1, "a") { Priority = (LuiPriority)7 }).ParamName.ShouldBe("Priority");
        Should.Throw<ArgumentException>(() => new LuiEntry(1, string.Empty)).ParamName.ShouldBe("Text");
    }

    [Fact]
    public void The_catalog_options_reach_the_connections()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open(new LuiCatalogOptions { CacheSizeMegabytes = 3, BusyTimeout = TimeSpan.FromMilliseconds(1234) });
        using var connection = catalog.Database.OpenConnection();
        using var command = connection.CreateCommand();

        command.CommandText = "PRAGMA cache_size;";
        ((long)command.ExecuteScalar()!).ShouldBe(-3 * 1024);
        command.CommandText = "PRAGMA busy_timeout;";
        ((long)command.ExecuteScalar()!).ShouldBe(1234);
    }

    // ---- the builder and the interfaces --------------------------------------------------------

    [Fact]
    public void An_undefined_kind_of_key_fails_where_it_is_chosen()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();

        Should.Throw<ArgumentOutOfRangeException>(
            () => catalog.Create("tags", index => index.SingleWord().WithKey((LuiKeyKind)7))).ParamName.ShouldBe("kind");
    }

    [Fact]
    public void A_linked_index_has_the_numbers_of_its_table_as_keys()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();

        Should.Throw<InvalidOperationException>(
            () => catalog.Create("tags", index => index.SingleWord().WithKey(LuiKeyKind.Text).LinkTo("tag", "id", "name")))
            .Message.ShouldContain("INTEGER PRIMARY KEY");
    }

    [Fact]
    public void A_payload_keeps_the_settings_made_before_it()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var words = catalog.Create("words", index => index.SingleWord().TypoTolerance().GermanTransliteration(false).Similarity(new Constant(0.25)));
        words.Upsert(new LuiEntry(1, "Apfelkuchen") { Weight = 10, Payload = new Chip("a") });

        // the typo tolerance
        words.Search("apflekuchen").CorrectedQuery.ShouldBe("apfelkuchen");

        // the similarity
        words.Search("apfelkuch").Hits.ShouldHaveSingleItem().Similarity.ShouldBe(0.25);

        // the transliteration: it is part of the definition, so an index with it on is another index
        Should.Throw<LuiDefinitionMismatchException>(
            () => catalog.Create("words", index => index.SingleWord()));
    }

    [Fact]
    public void A_rolled_back_write_leaves_its_beginnings_in_the_typo_dictionary_until_it_is_rebuilt()
    {
        // A documented limit (review of pull request 12): the library does not own the transaction of the
        // application and learns its words when they are written. The rollback itself is correct.
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var titles = catalog.Create("titles", index => index.MultiWord().TypoTolerance());
        titles.Upsert(1, "Schifffahrt", 10);
        titles.WarmUpTypoDictionary();
        titles.Search("schiffb").CorrectedQuery.ShouldBe("schifff");

        using (var connection = database.Connect())
        using (var transaction = connection.BeginTransaction())
        {
            titles.UpsertMany([new LuiEntry(2, "Schiffbruecke")], transaction);
            transaction.Rollback();
        }

        titles.Count().ShouldBe(1);
        var stale = titles.Search("schiffb");
        stale.CorrectedQuery.ShouldBeNull("to the dictionary, schiffb is a beginning of the index now");
        stale.Hits.ShouldBeEmpty();

        titles.RebuildTypoDictionary();

        var rebuilt = titles.Search("schiffb");
        rebuilt.CorrectedQuery.ShouldBe("schifff");
        rebuilt.Hits.ShouldHaveSingleItem().Key.ShouldBe(1);
    }

    [Fact]
    public void A_search_without_options_takes_the_defaults()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        tags.UpsertMany(Enumerable.Range(1, 30).Select(i => new LuiEntry(i, $"kuchen{i}") { Weight = i }));

        var result = tags.Search("kuchen");

        result.Hits.Count.ShouldBe(new LuiQueryOptions().MaxResults);
        result.Timings.ShouldBeNull();
    }

    [Fact]
    public void A_reader_needs_only_the_read_interface()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        tags.Upsert(1, "vegan", 5);

        ILuiIndex reader = tags;

        reader.Search("vegan").Hits.ShouldHaveSingleItem().Key.ShouldBe(1);
        reader.Count().ShouldBe(1);
    }

    [Fact]
    public void The_exceptions_of_the_library_can_be_caught_as_one_and_still_as_an_InvalidOperationException()
    {
        // Review of 2026-11-04, A.8: a caller that separates LUI's conditions from its own errors had no type for it
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create("words", index => index.SingleWord());

        var mismatch = Record.Exception(() => catalog.Create("words", index => index.MultiWord()));

        mismatch.ShouldBeOfType<LuiDefinitionMismatchException>();
        mismatch.ShouldBeAssignableTo<LuiException>();
        mismatch.ShouldBeAssignableTo<InvalidOperationException>("a catch that exists keeps catching it");
        new LuiEnvironmentException("too old").ShouldBeAssignableTo<LuiException>();
        new LuiLinkedTableException("no such table").ShouldBeAssignableTo<LuiException>();
    }

    [Fact]
    public void Every_step_that_changes_the_key_or_the_payload_keeps_every_setting()
    {
        // review of 2026-11-04, C.1 (#40): one settings record instead of three fields copied by hand in four places
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var similarity = new Constant(0.25);
        var expected = new IndexOptions(similarity, GermanTransliteration: false, TypoTolerance: true);

        // the builder is the description of an index, not the way into it (ADR-0033): the catalog builds it, the test
        // asks for it directly to look at the settings of every step.
        var described = new LuiIndexKindBuilder(catalog, "words").SingleWord().Similarity(similarity).GermanTransliteration(false).TypoTolerance();

        // the default, pinned: comparing with IndexOptions.Default alone would let a wrong constant pass (review of PR 45, M3)
        IndexOptions.Default.ShouldBe(new IndexOptions(Similarity: null, GermanTransliteration: true, TypoTolerance: false));
        new LuiIndexKindBuilder(catalog, "plain").SingleWord().Options.ShouldBe(IndexOptions.Default);
        described.Options.ShouldBe(expected);
        described.WithKey(LuiKeyKind.Number).Options.ShouldBe(expected, "WithKey");

        var linked = described.LinkTo("tag", "id", "name");
        linked.Options.ShouldBe(expected, "LinkTo");
        linked.WithPayload(["name"], row => new Chip(row.GetString("name"))).Options.ShouldBe(expected, "the linked WithPayload");
    }

    [Fact]
    public void A_dropped_index_is_gone_and_a_drop_of_a_missing_one_says_so()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create("tags", index => index.SingleWord()).Upsert(1, "vegan", 5);

        catalog.DropIndex("tags").ShouldBeTrue();
        catalog.DropIndex("tags").ShouldBeFalse();
        Should.Throw<ArgumentException>(() => catalog.DropIndex("Not A Name"));
        database.Scalar<long>("SELECT count(*) FROM sqlite_master WHERE name LIKE 'lui_tags%'").ShouldBe(0);
    }

    private sealed class Constant(double value) : ILuiSimilarity
    {
        public double Similarity(string query, string text) => value;
    }

    /// <summary>A serialiser of the application — the way to describe a payload that is not JSON.</summary>
    private sealed class ChipSerializer : ILuiPayloadSerializer
    {
        public byte[] Serialize(object payload) => System.Text.Encoding.UTF8.GetBytes(((Chip)payload).Slug);

        public T Deserialize<T>(ReadOnlySpan<byte> bytes) => (T)(object)new Chip(System.Text.Encoding.UTF8.GetString(bytes));
    }

    private static IEnumerable<LuiEntry> FailingAfter(int count)
    {
        for (var i = 1; i <= count; i++)
        {
            yield return new LuiEntry(i, $"kuchen{i}");
        }

        throw new InvalidOperationException("the source failed");
    }
}
