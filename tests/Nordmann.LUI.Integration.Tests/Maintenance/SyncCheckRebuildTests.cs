using Shouldly;

namespace Nordmann.LUI.Integration.Tests.Maintenance;

/// <summary>
/// Sync, check, rebuild and optimize (LUI-Spezifikation §12.4, §12.6, §14, ADR-0016): an <c>Embedded</c> index against a
/// source and against itself, a <c>Linked</c> one against the application's table.
/// </summary>
public sealed class SyncCheckRebuildTests
{
    // ---- the sync of an embedded index -----------------------------------------------------------------------------

    [Fact]
    public void A_sync_fills_an_empty_index_and_says_what_it_added()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var words = Words(catalog);

        var report = words.Sync(Entries("vegan", "vegetarisch", "tofu"));

        report.ShouldBe(new LuiSyncReport { Read = 3, Added = 3, Batches = 1, Merged = true });
        words.Count().ShouldBe(3);
        words.Search("veg").Hits.Select(hit => hit.Text).ShouldBe(["vegetarisch", "vegan"], ignoreOrder: true);
    }

    [Fact]
    public void A_sync_adds_what_is_missing_changes_what_differs_keeps_what_is_the_same_and_removes_the_rest()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var words = Words(catalog);
        words.UpsertMany([Entry(1, "apfel"), Entry(2, "birne", 5), Entry(3, "kirsche")]);

        var report = words.Sync([Entry(2, "birne", 9), Entry(3, "kirsche"), Entry(4, "pflaume")]);

        report.Added.ShouldBe(1);
        report.Changed.ShouldBe(1);
        report.Unchanged.ShouldBe(1);
        report.Removed.ShouldBe(1);
        words.Search("apfel").Hits.ShouldBeEmpty();
        words.Search("birne").Hits.ShouldHaveSingleItem().Weight.ShouldBe(9);
        words.Search("pflaume").Hits.ShouldHaveSingleItem().Key.ShouldBe(4);
    }

    [Fact]
    public void A_second_sync_with_the_same_source_writes_nothing_and_merges_nothing()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var words = Words(catalog);
        var source = Entries("vegan", "tofu", "seitan");
        words.Sync(source);

        var report = words.Sync(source);

        report.ShouldBe(new LuiSyncReport { Read = 3, Unchanged = 3, Batches = 1 });
    }

    [Fact]
    public void A_sync_compares_the_payload_too()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var tags = catalog.Create("tags", index => index.SingleWord());
        tags.Sync([new LuiEntry(1, "vegan") { Payload = new Chip("v", "green") }]);

        var report = tags.Sync([new LuiEntry(1, "vegan") { Payload = new Chip("v", "red") }]);

        report.Changed.ShouldBe(1);
        tags.Search("vegan").Hits.ShouldHaveSingleItem().GetPayload<Chip>().ShouldBe(new Chip("v", "red"));
    }

    [Fact]
    public void A_sync_works_in_batches_and_reports_after_each_one()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var words = Words(catalog);
        var reports = new List<LuiSyncReport>();

        var report = words.Sync(
            Entries("a1", "a2", "a3", "a4", "a5"),
            new LuiSyncOptions { BatchSize = 2, Progress = new Synchronous<LuiSyncReport>(reports.Add) });

        report.Batches.ShouldBe(3);
        reports.Select(r => r.Read).ShouldBe([2, 4, 5, 5]);
        reports[^1].ShouldBe(report);
        words.Count().ShouldBe(5);
    }

    [Fact]
    public void A_cancelled_sync_keeps_its_committed_batches_removes_nothing_and_a_second_run_finishes_the_work()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var words = Words(catalog);
        words.Upsert(100, "alt");
        var source = Enumerable.Range(1, 10).Select(i => Entry(i, $"wort{i}")).ToList();
        using var cancellation = new CancellationTokenSource();

        Should.Throw<OperationCanceledException>(() => words.Sync(
            CancelAfter(source, 5, cancellation),
            new LuiSyncOptions { BatchSize = 3 },
            cancellation.Token));

        // the first batch is committed, the second rolled back, the removal never ran
        words.Count().ShouldBe(4);
        words.Search("alt").Hits.ShouldHaveSingleItem();

        var resumed = words.Sync(source, new LuiSyncOptions { BatchSize = 3 });

        resumed.Unchanged.ShouldBe(3);
        resumed.Added.ShouldBe(7);
        resumed.Removed.ShouldBe(1);
        words.Count().ShouldBe(10);
        words.Check(source).IsHealthy.ShouldBeTrue();
    }

    [Fact]
    public void A_source_that_fails_leaves_the_removal_undone()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var words = Words(catalog);
        words.Upsert(100, "alt");

        Should.Throw<InvalidOperationException>(() => words.Sync(Failing(Entries("vegan", "tofu")), new LuiSyncOptions { BatchSize = 1 }));

        words.Search("alt").Hits.ShouldHaveSingleItem();
        words.Count().ShouldBe(3);
    }

    [Fact]
    public void A_key_that_comes_twice_keeps_its_last_entry_in_a_batch_and_across_batches()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var words = Words(catalog);

        // in one batch: the sort by weight must not change which one is last
        words.Sync([Entry(1, "erste", 9), Entry(1, "zweite", 1)]);
        words.Search("erste").Hits.ShouldBeEmpty();
        words.Search("zweite").Hits.ShouldHaveSingleItem();

        var report = words.Sync([Entry(1, "dritte"), Entry(1, "vierte")], new LuiSyncOptions { BatchSize = 1 });
        report.Changed.ShouldBe(2);
        words.Search("vierte").Hits.ShouldHaveSingleItem();
        words.Count().ShouldBe(1);
    }

    [Fact]
    public void A_key_twice_in_two_batches_counts_as_a_change_even_in_an_empty_index()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var words = Words(catalog);

        var report = words.Sync([Entry(1, "apfel"), Entry(2, "birne"), Entry(1, "apfelkuchen")], new LuiSyncOptions { BatchSize = 2 });

        (report.Read, report.Added, report.Changed).ShouldBe((3L, 2L, 1L));
        words.Search("apfelkuchen").Hits.ShouldHaveSingleItem().Key.ShouldBe(1);
    }

    [Fact]
    public void A_sync_answers_as_the_same_entries_upserted_one_by_one()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var random = new Random(7);
        var entries = Enumerable.Range(1, 600)
            .Select(i => Entry(i, $"{(char)('a' + random.Next(5))}{(char)('a' + random.Next(5))}wort{i}", random.Next(0, 50), (LuiPriority)random.Next(0, 4)))
            .ToList();
        var synced = catalog.Create("synced", index => index.SingleWord());
        var upserted = catalog.Create("upserted", index => index.SingleWord());

        synced.Sync(entries, new LuiSyncOptions { BatchSize = 97 });
        foreach (var entry in entries)
        {
            upserted.Upsert(entry);
        }

        foreach (var input in new[] { "a", "ab", "wort", "bawort1", "rt5", "ccw" })
        {
            synced.Search(input).Hits.Select(hit => (hit.Key, hit.Text)).ShouldBe(
                upserted.Search(input).Hits.Select(hit => (hit.Key, hit.Text)), input);
        }
    }

    [Fact]
    public void A_sync_turns_the_incremental_merge_back_on_and_leaves_a_sound_index()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var words = Words(catalog);

        words.Sync(Entries("vegan", "tofu", "seitan"), new LuiSyncOptions { BatchSize = 1 });

        database.Scalar<long>("SELECT v FROM lui_words_fts_config WHERE k = 'automerge'").ShouldBe(4);
        words.Check().IsHealthy.ShouldBeTrue();
    }

    [Fact]
    public void An_asynchronous_source_is_synced_the_same_way()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var words = Words(catalog);

        var report = words.Sync(Asynchronous(Entries("vegan", "tofu")));

        report.Added.ShouldBe(2);
        words.Search("tofu").Hits.ShouldHaveSingleItem();
    }

    [Fact]
    public void A_text_key_is_synced_by_its_text()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var slugs = catalog.Create("slugs", index => index.SingleWord().WithKey(LuiKeyKind.Text));
        slugs.Upsert("alt", "alt");

        var report = slugs.Sync([new LuiEntry("vegan", "vegan")]);

        report.Added.ShouldBe(1);
        report.Removed.ShouldBe(1);
        slugs.Search("vegan").Hits.ShouldHaveSingleItem().Key.ShouldBe("vegan");
    }

    [Fact]
    public void A_key_the_application_adds_during_a_sync_stays_and_a_key_that_was_there_goes_even_when_written_again()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var words = Words(catalog);
        words.UpsertMany([Entry(1, "apfel"), Entry(2, "birne")]);
        var once = false;

        // between the batches — the progress is told outside the write lock — the application writes
        var report = words.Sync(
            [Entry(1, "apfel"), Entry(3, "kirsche"), Entry(4, "pflaume")],
            new LuiSyncOptions
            {
                BatchSize = 1,
                Progress = new Synchronous<LuiSyncReport>(_ =>
                {
                    if (!once)
                    {
                        once = true;
                        words.Upsert(50, "neu");
                        words.Remove(2);
                        words.Upsert(2, "birne wieder");
                    }
                }),
            });

        words.Search("neu").Hits.ShouldHaveSingleItem();
        words.Search("birne").Hits.ShouldBeEmpty();
        report.Removed.ShouldBe(1);
        words.Count().ShouldBe(4);
    }

    [Fact]
    public void The_empty_text_key_is_synced_and_removed_like_any_other()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var slugs = catalog.Create("slugs", index => index.SingleWord().WithKey(LuiKeyKind.Text));
        slugs.Upsert(string.Empty, "leer");
        slugs.Upsert("a", "voll");

        slugs.Sync([new LuiEntry("a", "voll")]).Removed.ShouldBe(1);

        slugs.Search("leer").Hits.ShouldBeEmpty();
        slugs.Count().ShouldBe(1);
    }

    [Fact]
    public void A_built_typo_dictionary_follows_the_sync()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var words = catalog.Create("words", index => index.SingleWord().TypoTolerance());
        words.Sync(Entries("kartoffel"));
        words.WarmUpTypoDictionary();

        words.Sync(Entries("tomate"));

        // the removed word is not a correction any more, the new one is
        words.Search("kartofel").CorrectedQuery.ShouldBeNull();
        words.Search("tomahe").Hits.ShouldHaveSingleItem().Text.ShouldBe("tomate");
    }

    [Fact]
    public void The_options_of_a_sync_check_their_values_where_they_are_set()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new LuiSyncOptions { BatchSize = 0 }).ParamName.ShouldBe("BatchSize");
        Should.Throw<ArgumentOutOfRangeException>(() => new LuiSyncOptions { BatchSize = LuiSyncOptions.MaxBatchSize + 1 });
        new LuiSyncOptions().BatchSize.ShouldBe(10_000);
    }

    // ---- the check of an embedded index against a source -------------------------------------------------------------

    [Fact]
    public void A_check_against_the_source_counts_and_names_what_differs_and_changes_nothing()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var words = Words(catalog);
        words.UpsertMany([Entry(1, "apfel"), Entry(2, "birne", 5), Entry(3, "kirsche")]);

        var report = words.Check([Entry(2, "birne", 9), Entry(3, "kirsche"), Entry(4, "pflaume")]);

        report.IsHealthy.ShouldBeFalse();
        report.Missing.ShouldBe(1);
        report.Extra.ShouldBe(1);
        report.Different.ShouldBe(1);
        report.Problems.Count.ShouldBe(3);
        report.Problems.ShouldContain(problem => problem.Contains("'4'", StringComparison.Ordinal) && problem.Contains("Sync(source)", StringComparison.Ordinal));
        report.Problems.ShouldContain(problem => problem.Contains("'1'", StringComparison.Ordinal));
        report.Problems.ShouldContain(problem => problem.Contains("'2'", StringComparison.Ordinal));
        words.Count().ShouldBe(3);
        words.Search("birne").Hits.ShouldHaveSingleItem().Weight.ShouldBe(5);
    }

    [Fact]
    public void A_check_against_the_source_after_a_sync_finds_nothing_and_counts_the_last_entry_of_a_key()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var words = Words(catalog);
        LuiEntry[] source = [Entry(1, "erste"), Entry(1, "zweite"), Entry(2, "tofu")];
        words.Sync(source);

        var report = words.Check(source);

        report.Problems.ShouldBeEmpty();
        (report.Missing, report.Extra, report.Different).ShouldBe((0L, 0L, 0L));
    }

    [Fact]
    public void A_check_names_a_few_keys_and_says_there_are_more()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var words = Words(catalog);

        var report = words.Check(Enumerable.Range(1, 8).Select(i => Entry(i, $"wort{i}")));

        report.Missing.ShouldBe(8);
        report.Problems.ShouldHaveSingleItem().ShouldContain("'5', …");
    }

    // ---- the check of an embedded index against itself, and the rebuild ------------------------------------------------

    [Fact]
    public void A_fresh_index_is_healthy()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var words = Words(catalog);
        words.UpsertMany(Entries("vegan", "tofu"));

        words.Check().ShouldBe(new LuiIntegrityReport { Problems = [] }, new IntegrityComparer());
    }

    [Fact]
    public void A_missing_trigger_and_a_row_it_did_not_carry_are_found_and_the_rebuild_repairs_both()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var words = Words(catalog);
        words.Upsert(1, "vegan");
        Run(database, "DROP TRIGGER lui_words_ai;");
        words.Upsert(2, "tofu");

        var report = words.Check();

        report.Problems.Count.ShouldBe(2);
        report.Problems.ShouldContain(problem => problem.Contains("lui_words_ai", StringComparison.Ordinal) && problem.Contains("Rebuild()", StringComparison.Ordinal));
        report.Problems.ShouldContain(problem => problem.Contains("integrity check", StringComparison.Ordinal));
        words.Search("tofu").Hits.ShouldBeEmpty();

        words.Rebuild();

        words.Check().IsHealthy.ShouldBeTrue();
        words.Search("tofu").Hits.ShouldHaveSingleItem();
        words.Upsert(3, "seitan");
        words.Search("seitan").Hits.ShouldHaveSingleItem();
    }

    [Fact]
    public void A_search_index_that_lost_an_entry_fails_the_integrity_check()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var words = Words(catalog);
        words.UpsertMany(Entries("vegan", "tofu"));
        Run(database, "INSERT INTO lui_words_fts(lui_words_fts, rowid, s_norm) SELECT 'delete', s_rowid, s_norm FROM lui_words WHERE text = 'tofu';");

        words.Check().Problems.ShouldHaveSingleItem().ShouldContain("Rebuild()");

        words.Rebuild();
        words.Check().IsHealthy.ShouldBeTrue();
        words.Search("tofu").Hits.ShouldHaveSingleItem();
    }

    [Fact]
    public void A_changed_trigger_is_found()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var words = Words(catalog);
        Run(database, "DROP TRIGGER lui_words_ad; CREATE TRIGGER lui_words_ad AFTER DELETE ON lui_words BEGIN SELECT 1; END;");

        words.Check().Problems.ShouldHaveSingleItem().ShouldContain("not the ones of this version");

        words.Rebuild();
        words.Check().IsHealthy.ShouldBeTrue();
    }

    [Fact]
    public void An_instance_whose_index_was_dropped_or_ensured_anew_is_told_so()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var old = Words(catalog);

        catalog.DropIndex("words");
        old.Check().Problems.ShouldHaveSingleItem().ShouldContain("dropped");

        catalog.Create("words", index => index.MultiWord());
        old.Check().Problems.ShouldHaveSingleItem().ShouldContain("another definition");
    }

    [Fact]
    public void Optimize_merges_and_keeps_the_answers()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var words = Words(catalog);
        foreach (var entry in Entries("vegan", "vegetarisch", "tofu"))
        {
            words.Upsert(entry);
        }

        var before = words.Search("veg").Hits.Select(hit => hit.Key).ToList();
        words.Optimize();

        words.Search("veg").Hits.Select(hit => hit.Key).ShouldBe(before);
        words.Check().IsHealthy.ShouldBeTrue();
    }

    // ---- the check of a linked index --------------------------------------------------------------------------------

    [Fact]
    public void A_synced_linked_index_is_healthy()
    {
        using var database = new TestDatabase();
        Run(database, TagTable + " INSERT INTO tag(id, name, usage_count) VALUES (1, 'vegan', 3), (2, 'tofu', 1), (3, NULL, 0);");
        using var catalog = database.Open();
        var tags = Tags(catalog);
        tags.Sync();

        tags.Check().ShouldBe(new LuiIntegrityReport { Problems = [] }, new IntegrityComparer());
    }

    [Fact]
    public void A_linked_check_finds_rows_the_index_was_not_told_of_and_the_sync_repairs_them()
    {
        using var database = new TestDatabase();
        Run(database, TagTable + " INSERT INTO tag(id, name, usage_count) VALUES (1, 'vegan', 3), (2, 'tofu', 1), (3, 'seitan', 0);");
        using var catalog = database.Open();
        var tags = Tags(catalog);
        tags.Sync();
        Run(database, "INSERT INTO tag(id, name) VALUES (4, 'tempeh'); DELETE FROM tag WHERE id = 2; UPDATE tag SET usage_count = 99 WHERE id = 3;");

        var report = tags.Check();

        report.Missing.ShouldBe(1);
        report.Extra.ShouldBe(1);
        report.Different.ShouldBe(1);
        report.Problems.ShouldContain(problem => problem.Contains("'4'", StringComparison.Ordinal) && problem.Contains("Sync()", StringComparison.Ordinal));
        report.Problems.ShouldContain(problem => problem.Contains("'2'", StringComparison.Ordinal));
        report.Problems.ShouldContain(problem => problem.Contains("'3'", StringComparison.Ordinal));

        tags.Sync();
        tags.Check().IsHealthy.ShouldBeTrue();
    }

    [Fact]
    public void A_linked_check_points_to_the_outbox_when_the_triggers_noted_the_rows()
    {
        using var database = new TestDatabase();
        Run(database, TagTable);
        using var catalog = database.Open();
        var tags = Tags(catalog);
        tags.CreateTriggers();
        Run(database, "INSERT INTO tag(id, name) VALUES (1, 'vegan');");

        tags.Check().Problems.ShouldHaveSingleItem().ShouldContain("ApplyPending()");

        tags.ApplyPending();
        tags.Check().IsHealthy.ShouldBeTrue();
    }

    [Fact]
    public void A_linked_check_finds_stale_triggers_and_a_table_that_changed()
    {
        using var database = new TestDatabase();
        Run(database, TagTable);
        using var catalog = database.Open();
        var tags = Tags(catalog);
        tags.CreateTriggers();
        Run(database, "DROP TRIGGER lui_tags_ad; CREATE TRIGGER lui_tags_ad AFTER DELETE ON tag BEGIN SELECT 1; END;");

        tags.Check().Problems.ShouldHaveSingleItem().ShouldContain("CreateTriggers()");

        tags.DropTriggers();
        Run(database, "ALTER TABLE tag RENAME COLUMN name TO title;");
        var problem = tags.Check().Problems.ShouldHaveSingleItem();
        problem.ShouldContain("'name'");
        problem.ShouldContain("drop the index");
    }

    // ---- helpers ----------------------------------------------------------------------------------------------------

    private const string TagTable = "CREATE TABLE tag (id INTEGER PRIMARY KEY, name TEXT, usage_count INTEGER NOT NULL DEFAULT 0);";

    private static ILuiEmbeddedIndex Words(LuiCatalog catalog) => catalog.Create("words", index => index.SingleWord());

    private static ILuiLinkedIndex Tags(LuiCatalog catalog) =>
        catalog.Create("tags", index => index.SingleWord().LinkTo("tag", "id", "name", weight: "usage_count"));

    private static LuiEntry Entry(long key, string text, int weight = 0, LuiPriority priority = LuiPriority.Normal) =>
        new(key, text) { Weight = weight, Priority = priority };

    private static List<LuiEntry> Entries(params string[] texts) =>
        [.. texts.Select((text, i) => Entry(i + 1, text))];

    private static IEnumerable<LuiEntry> CancelAfter(
        IEnumerable<LuiEntry> source,
        int count,
        CancellationTokenSource cancellation)
    {
        var given = 0;
        foreach (var entry in source)
        {
            if (given++ == count)
            {
                cancellation.Cancel();
            }

            yield return entry;
        }
    }

    private static IEnumerable<LuiEntry> Failing(IEnumerable<LuiEntry> source)
    {
        foreach (var entry in source)
        {
            yield return entry;
        }

        throw new InvalidOperationException("the source failed");
    }

    private static async IAsyncEnumerable<LuiEntry> Asynchronous(IEnumerable<LuiEntry> source)
    {
        foreach (var entry in source)
        {
            await Task.Yield();
            yield return entry;
        }
    }

    private static void Run(TestDatabase database, string sql)
    {
        using var connection = database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>A progress that reports on the calling thread: <see cref="Progress{T}"/> would post to the pool.</summary>
    private sealed class Synchronous<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    /// <summary>Two reports are equal when their counts and findings are.</summary>
    private sealed class IntegrityComparer : IEqualityComparer<LuiIntegrityReport>
    {
        public bool Equals(LuiIntegrityReport? x, LuiIntegrityReport? y) =>
            x is not null && y is not null && x.Problems.SequenceEqual(y.Problems, StringComparer.Ordinal)
            && x.Missing == y.Missing && x.Extra == y.Extra && x.Different == y.Different;

        public int GetHashCode(LuiIntegrityReport obj) => obj.Problems.Count;
    }
}
