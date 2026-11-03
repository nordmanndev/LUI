using Shouldly;

namespace Nordmann.LUI.Tests;

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
        Should.Throw<ArgumentException>(() => new LuiEntry<long, LuiNoPayload>(1, string.Empty))
            .Message.ShouldContain("Text");
        Should.Throw<ArgumentNullException>(() => new LuiEntry<long, LuiNoPayload>(1, null!));
        Should.Throw<ArgumentOutOfRangeException>(() => new LuiEntry<long, LuiNoPayload>(1, "a") { Weight = -1 });
        Should.Throw<ArgumentOutOfRangeException>(() => new LuiEntry<long, LuiNoPayload>(1, "a") { Weight = 536_870_912 });
        Should.Throw<ArgumentOutOfRangeException>(() => new LuiEntry<long, LuiNoPayload>(1, "a") { Priority = (LuiPriority)7 });

        var entry = new LuiEntry<long, LuiNoPayload>(1, "a");
        Should.Throw<ArgumentException>(() => entry with { Text = string.Empty });
    }

    [Fact]
    public void An_entry_is_normal_and_weightless_unless_it_says_otherwise()
    {
        var entry = new LuiEntry<long, LuiNoPayload>(1, "vegan");

        entry.Weight.ShouldBe(0);
        entry.Priority.ShouldBe(LuiPriority.Normal);
    }

    [Fact]
    public void The_short_form_of_the_upsert_replaces_the_whole_entry_and_clears_the_payload()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("tags").SingleWord().Owned<long>().WithPayload(ChipJsonContext.Default.Chip).Ensure();

        index.Upsert(new LuiEntry<long, Chip>(1, "vegan") { Weight = 5, Payload = new Chip("v"), Priority = LuiPriority.System });
        index.Upsert(1, "vegan", 7);

        var hit = index.Search("vegan").Hits.ShouldHaveSingleItem();
        hit.Payload.ShouldBeNull();
        hit.Weight.ShouldBe(7);
        hit.Priority.ShouldBe(LuiPriority.Normal);
    }

    // ---- the batch -----------------------------------------------------------------------------

    [Fact]
    public void A_batch_writes_every_entry_and_says_how_many()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("tags").SingleWord().Owned<long>().WithPayload(ChipJsonContext.Default.Chip).Ensure();

        var written = index.UpsertMany(Enumerable.Range(1, 1_000).Select(
            i => new LuiEntry<long, Chip>(i, $"kuchen{i}") { Weight = i, Payload = new Chip($"k{i}") }));

        written.ShouldBe(1_000);
        index.Count().ShouldBe(1_000);
        index.Search("kuchen1000").Hits[0].Payload.ShouldBe(new Chip("k1000"));
    }

    [Fact]
    public void A_key_that_comes_twice_in_a_batch_keeps_the_last_entry()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("tags").SingleWord().Owned<long>().Ensure();

        index.UpsertMany(
        [
            new LuiEntry<long, LuiNoPayload>(1, "apfel") { Weight = 1 },
            new LuiEntry<long, LuiNoPayload>(1, "birne") { Weight = 2 },
        ]);

        index.Count().ShouldBe(1);
        index.Search("apfel").Hits.ShouldBeEmpty();
        index.Search("birne").Hits.ShouldHaveSingleItem().Weight.ShouldBe(2);
    }

    [Fact]
    public void A_batch_in_its_own_transaction_writes_all_or_nothing()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("tags").SingleWord().Owned<long>().Ensure();

        Should.Throw<InvalidOperationException>(() => index.UpsertMany(FailingAfter(10)));

        index.Count().ShouldBe(0);

        // the writer is free again: a single write and another batch go through
        index.Upsert(1, "apfel", 1);
        index.UpsertMany([new LuiEntry<long, LuiNoPayload>(2, "birne")]).ShouldBe(1);
        index.Count().ShouldBe(2);
    }

    [Fact]
    public void A_cancelled_batch_rolls_its_own_transaction_back()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("tags").SingleWord().Owned<long>().Ensure();
        using var cancellation = new CancellationTokenSource();

        IEnumerable<LuiEntry<long, LuiNoPayload>> Entries()
        {
            for (var i = 1; i <= 100; i++)
            {
                if (i == 50)
                {
                    cancellation.Cancel();
                }

                yield return new LuiEntry<long, LuiNoPayload>(i, $"kuchen{i}");
            }
        }

        Should.Throw<OperationCanceledException>(() => index.UpsertMany(Entries(), cancellationToken: cancellation.Token));

        index.Count().ShouldBe(0);
    }

    [Fact]
    public void A_batch_in_the_transaction_of_the_application_is_part_of_it()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("tags").SingleWord().Owned<long>().Ensure();

        using (var connection = database.Connect())
        using (var transaction = connection.BeginTransaction())
        {
            index.UpsertMany([new LuiEntry<long, LuiNoPayload>(1, "apfel"), new LuiEntry<long, LuiNoPayload>(2, "birne")], transaction)
                .ShouldBe(2);
            transaction.Rollback();
        }

        index.Count().ShouldBe(0);

        using (var connection = database.Connect())
        using (var transaction = connection.BeginTransaction())
        {
            index.UpsertMany([new LuiEntry<long, LuiNoPayload>(1, "apfel")], transaction);
            transaction.Commit();
        }

        index.Search("apfel").Hits.ShouldHaveSingleItem();
    }

    [Fact]
    public void A_batch_teaches_the_typo_dictionary_its_new_words_as_a_single_upsert_does()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var batch = catalog.Index("batch").SingleWord().Owned<long>().TypoTolerance().Ensure();
        var single = catalog.Index("single").SingleWord().Owned<long>().TypoTolerance().Ensure();
        foreach (var index in new[] { batch, single })
        {
            index.Upsert(1, "Apfelkuchen", 10);
            index.WarmUpTypoDictionary();
        }

        batch.UpsertMany([new LuiEntry<long, LuiNoPayload>(2, "Himbeere") { Weight = 50 }]);
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
        var batch = catalog.Index("batch").SingleWord().Owned<long>().Ensure();
        var single = catalog.Index("single").SingleWord().Owned<long>().Ensure();

        var writer = Task.Run(() =>
        {
            for (var i = 1; i <= 200; i++)
            {
                single.Upsert(i, $"torte{i}", i);
            }
        });

        for (var round = 0; round < 10; round++)
        {
            batch.UpsertMany(Enumerable.Range((round * 100) + 1, 100).Select(i => new LuiEntry<long, LuiNoPayload>(i, $"kuchen{i}")));
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
        var index = catalog.Index("tags").SingleWord().Owned<long>().Ensure();
        var query = index.Query("vegan");

        // the builder throws at the call, not later in Execute()
        Should.Throw<ArgumentOutOfRangeException>(() => query.Take(0)).Message.ShouldContain("MaxResults");
        Should.Throw<ArgumentOutOfRangeException>(() => query.Candidates(100_001)).Message.ShouldContain("Candidates");
        Should.Throw<ArgumentOutOfRangeException>(() => query.Rank((LuiRankingOrder)42)).Message.ShouldContain("Rank");
        Should.Throw<ArgumentOutOfRangeException>(() => new LuiQueryOptions { Priorities = (LuiPriorityRanking)42 })
            .Message.ShouldContain("Priorities");
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
        Should.Throw<ArgumentOutOfRangeException>(() => new LuiEntry<long, LuiNoPayload>(1, "a") { Weight = -1 }).ParamName.ShouldBe("Weight");
        Should.Throw<ArgumentOutOfRangeException>(() => new LuiEntry<long, LuiNoPayload>(1, "a") { Priority = (LuiPriority)7 }).ParamName.ShouldBe("Priority");
        Should.Throw<ArgumentException>(() => new LuiEntry<long, LuiNoPayload>(1, string.Empty)).ParamName.ShouldBe("Text");
    }

    [Fact]
    public void The_catalog_options_reach_the_connections()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open(new LuiCatalogOptions { CacheSizeMegabytes = 3, BusyTimeout = TimeSpan.FromMilliseconds(1234) });
        using var connection = catalog.OpenConnection();
        using var command = connection.CreateCommand();

        command.CommandText = "PRAGMA cache_size;";
        ((long)command.ExecuteScalar()!).ShouldBe(-3 * 1024);
        command.CommandText = "PRAGMA busy_timeout;";
        ((long)command.ExecuteScalar()!).ShouldBe(1234);
    }

    // ---- the builder and the interfaces --------------------------------------------------------

    [Fact]
    public void An_unsupported_key_type_fails_where_it_is_chosen()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var source = catalog.Index("tags").SingleWord();

        Should.Throw<NotSupportedException>(() => source.Owned<Guid>()).Message.ShouldContain("long or a string");
    }

    [Fact]
    public void A_payload_keeps_the_settings_made_before_it()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("words").SingleWord().Owned<long>()
            .TypoTolerance()
            .GermanTransliteration(false)
            .Similarity(new Constant(0.25))
            .WithPayload(ChipJsonContext.Default.Chip)
            .Ensure();
        index.Upsert(new LuiEntry<long, Chip>(1, "Apfelkuchen") { Weight = 10, Payload = new Chip("a") });

        // the typo tolerance
        index.Search("apflekuchen").CorrectedQuery.ShouldBe("apfelkuchen");

        // the similarity
        index.Search("apfelkuch").Hits.ShouldHaveSingleItem().Similarity.ShouldBe(0.25);

        // the transliteration: it is part of the definition, so an index with it on is another index
        Should.Throw<LuiDefinitionMismatchException>(
            () => catalog.Index("words").SingleWord().Owned<long>().WithPayload(ChipJsonContext.Default.Chip).Ensure());
    }

    [Fact]
    public void A_rolled_back_write_leaves_its_beginnings_in_the_typo_dictionary_until_it_is_rebuilt()
    {
        // A documented limit (review of pull request 12): the library does not own the transaction of the
        // application and learns its words when they are written. The rollback itself is correct.
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("titles").MultiWord().Owned<long>().TypoTolerance().Ensure();
        index.Upsert(1, "Schifffahrt", 10);
        index.WarmUpTypoDictionary();
        index.Search("schiffb").CorrectedQuery.ShouldBe("schifff");

        using (var connection = database.Connect())
        using (var transaction = connection.BeginTransaction())
        {
            index.UpsertMany([new LuiEntry<long, LuiNoPayload>(2, "Schiffbruecke")], transaction);
            transaction.Rollback();
        }

        index.Count().ShouldBe(1);
        var stale = index.Search("schiffb");
        stale.CorrectedQuery.ShouldBeNull("to the dictionary, schiffb is a beginning of the index now");
        stale.Hits.ShouldBeEmpty();

        index.RebuildTypoDictionary();

        var rebuilt = index.Search("schiffb");
        rebuilt.CorrectedQuery.ShouldBe("schifff");
        rebuilt.Hits.ShouldHaveSingleItem().Key.ShouldBe(1);
    }

    [Fact]
    public void A_search_without_options_takes_the_defaults()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("tags").SingleWord().Owned<long>().Ensure();
        index.UpsertMany(Enumerable.Range(1, 30).Select(i => new LuiEntry<long, LuiNoPayload>(i, $"kuchen{i}") { Weight = i }));

        var result = index.Search("kuchen");

        result.Hits.Count.ShouldBe(new LuiQueryOptions().MaxResults);
        result.Timings.ShouldBeNull();
    }

    [Fact]
    public void A_reader_needs_only_the_read_interface()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("tags").SingleWord().Owned<long>().Ensure();
        index.Upsert(1, "vegan", 5);

        ILuiIndex<long, LuiNoPayload> reader = index;

        reader.Search("vegan").Hits.ShouldHaveSingleItem().Key.ShouldBe(1);
        reader.Count().ShouldBe(1);
    }

    [Fact]
    public void A_dropped_index_is_gone_and_a_drop_of_a_missing_one_says_so()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Index("tags").SingleWord().Owned<long>().Ensure().Upsert(1, "vegan", 5);

        catalog.DropIndex("tags").ShouldBeTrue();
        catalog.DropIndex("tags").ShouldBeFalse();
        Should.Throw<ArgumentException>(() => catalog.DropIndex("Not A Name"));
        database.Scalar<long>("SELECT count(*) FROM sqlite_master WHERE name LIKE 'lui_tags%'").ShouldBe(0);
    }

    private sealed class Constant(double value) : ILuiSimilarity
    {
        public string Name => "constant";

        public double Similarity(string query, string text) => value;
    }

    private static IEnumerable<LuiEntry<long, LuiNoPayload>> FailingAfter(int count)
    {
        for (var i = 1; i <= count; i++)
        {
            yield return new LuiEntry<long, LuiNoPayload>(i, $"kuchen{i}");
        }

        throw new InvalidOperationException("the source failed");
    }
}
