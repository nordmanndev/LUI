using Microsoft.Data.Sqlite;
using Nordmann.LUI.Schema;
using Shouldly;
using static Nordmann.LUI.Integration.Tests.Linked.LinkedTable;

namespace Nordmann.LUI.Integration.Tests.Linked;

/// <summary>
/// The linked sync in batches (#271, ADR-0077, extending ADR-0016): the rows of the application's table are read in the order of their
/// key, <c>BatchSize</c> at a time, each batch in a transaction of its own — there is no clear at the start. What is left of the previous
/// state is removed at the end by its <b>generation</b>, in batches too, and the search index is merged in steps. One sync of an index
/// runs at a time: it holds a <b>lease</b> in <c>lui_meta</c> that it renews with every transaction and that another sync takes over
/// only when it has expired. A sync that is cancelled or fails keeps the batches it committed, removes nothing that was not removed yet,
/// does not finish the merge, leaves <c>NeedsInitialSync</c> as it was and releases its lease.
/// <para>
/// The sync is driven from inside: <c>LuiSyncOptions.Progress</c> is called after every batch, outside of every lock, on the thread
/// of the sync — a test that wants something to happen between two batches does it there, and nothing waits for a clock. The
/// expiry of a lease is the <c>TimeProvider</c> of the catalog; the tests move it by hand.
/// </para>
/// </summary>
public sealed class LinkedBatchSyncTests
{
    private static readonly LuiLinkedIndexDefinition<long> LinkedTags =
        new("tags", index => index.SingleWord().LinkTo("tag", "id", "name", weight: "usage_count"));

    private static readonly LuiLinkedIndexDefinition<long> LinkedTagsOfWords =
        new("tags", index => index.MultiWord().LinkTo("tag", "id", "name", weight: "usage_count"));

    private static readonly LuiLinkedIndexDefinition<long> Corrected =
        new("tags", index => index.SingleWord().TypoTolerance().LinkTo("tag", "id", "name", weight: "usage_count"));

    private static readonly LuiQueryOptions Everything = new() { MaxResults = 1_000 };

    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);

    // ---- the batches ------------------------------------------------------------------------------------------------

    [Fact]
    public void A_search_during_the_sync_never_sees_an_empty_index()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Fill(database, 60);
        using var catalog = database.Open();
        var tags = catalog.Create(LinkedTags);
        tags.Sync();

        var counts = new List<long>();
        var hits = new List<int>();
        tags.Sync(new LuiSyncOptions
        {
            BatchSize = 10,
            Progress = Hear(_ =>
            {
                counts.Add(tags.Count());
                hits.Add(tags.Search("kuchen", Everything).Hits.Count);
            }),
        });

        counts.Count.ShouldBeGreaterThanOrEqualTo(6, "the index is looked at after every batch");
        counts.ShouldAllBe(count => count == 60, "the old entries stay until the new ones replace them: the index is never empty and never short");
        hits.ShouldAllBe(found => found == 60, "and every search finds every row — from the first batch to the merge");
    }

    [Fact]
    public void During_the_sync_the_index_is_partly_old_and_partly_new_after_it_all_new()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Fill(database, 40);
        using var catalog = database.Open();
        var tags = catalog.Create(LinkedTags);
        tags.Sync();
        Run(database, "UPDATE tag SET name = 'torte' || id;");

        var newOnes = new List<int>();
        tags.Sync(new LuiSyncOptions
        {
            BatchSize = 10,
            Progress = Hear(report =>
            {
                if (report.Batches > 0 && report.Batches <= 4)
                {
                    newOnes.Add(tags.Search("torte", Everything).Hits.Count);
                    tags.Count().ShouldBe(40, "the entries of the old text are still there for the rows of the batches to come");
                }
            }),
        });

        newOnes.Take(4).ShouldBe([10, 20, 30, 40], "a batch makes its rows new, the rest is the old state — a linked sync is not atomic");
        tags.Search("torte", Everything).Hits.Count.ShouldBe(40);
        tags.Search("kuchen", Everything).Hits.ShouldBeEmpty();
    }

    [Fact]
    public void After_the_sync_the_index_holds_exactly_the_rows_of_the_table()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Fill(database, 45);
        Run(database, "UPDATE tag SET name = NULL WHERE id IN (3, 17); UPDATE tag SET name = '' WHERE id = 30;");
        using var catalog = database.Open();
        var tags = catalog.Create(LinkedTags);

        var report = tags.Sync(new LuiSyncOptions { BatchSize = 7 });

        report.Indexed.ShouldBe(42);
        report.WithoutText.ShouldBe(3);
        tags.Count().ShouldBe(42);
        tags.Check().IsHealthy.ShouldBeTrue();

        Run(database, "DELETE FROM tag WHERE id % 5 = 0; UPDATE tag SET name = 'apfel' || id WHERE id % 4 = 0; INSERT INTO tag(id, name) VALUES (100, 'neu'), (101, 'neuer');");

        var again = tags.Sync(new LuiSyncOptions { BatchSize = 7 });

        again.Indexed.ShouldBe(tags.Count());
        tags.Check().IsHealthy.ShouldBeTrue("every row of the table is in the index as it is now, and nothing else");
        tags.Search("apfel", Everything).Hits.Select(hit => hit.Key).Order().ShouldBe([4, 8, 12, 16, 24, 28, 32, 36, 44]);
        tags.Search("neu", Everything).Hits.Select(hit => hit.Key).Order().ShouldBe([100, 101]);
    }

    [Fact]
    public void The_report_counts_the_batches_what_was_removed_and_the_longest_lock()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Fill(database, 25);
        using var catalog = database.Open();
        var tags = catalog.Create(LinkedTags);

        var first = tags.Sync(new LuiSyncOptions { BatchSize = 10 });

        first.Batches.ShouldBe(3);
        first.Removed.ShouldBe(0);
        first.Indexed.ShouldBe(25);
        first.LongestLock.ShouldBeGreaterThan(TimeSpan.Zero);
        first.LongestLock.ShouldBeLessThan(TimeSpan.FromMinutes(1));

        Run(database, "DELETE FROM tag WHERE id IN (2, 4, 6, 24);");
        var second = tags.Sync(new LuiSyncOptions { BatchSize = 10 });

        second.Batches.ShouldBe(3);
        second.Removed.ShouldBe(4, "the four entries whose rows are gone: the ones that kept the generation of the sync before");
        second.Indexed.ShouldBe(21);
        tags.Sync(new LuiSyncOptions { BatchSize = 1 }).Batches.ShouldBe(21, "a batch is BatchSize rows of the table");
    }

    [Fact]
    public void A_key_out_of_range_is_counted_once_wherever_the_batches_end()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Fill(database, 10);
        Run(database, "INSERT INTO tag(id, name) VALUES (-3, 'negativ'), (5000000000, 'riesig'), (6000000000, 'riesiger'), (-4, NULL);");
        using var catalog = database.Open();
        var tags = catalog.Create(LinkedTags);

        var report = tags.Sync(new LuiSyncOptions { BatchSize = 3 });

        report.KeyOutOfRange.ShouldBe(3);
        report.WithoutText.ShouldBe(1, "the row without a name is left out for that, whatever its key; the others for their keys");
        report.Indexed.ShouldBe(10);
        tags.Check().IsHealthy.ShouldBeTrue();
    }

    [Fact]
    public void Progress_is_told_after_every_batch_and_at_the_end_with_the_merge_done()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Fill(database, 25);
        using var catalog = database.Open();
        var tags = catalog.Create(LinkedTags);
        var reports = new List<LuiSyncReport>();

        tags.Sync(new LuiSyncOptions { BatchSize = 10, Progress = Hear(reports.Add) });

        reports.Take(3).Select(report => (report.Read, report.Batches)).ShouldBe([(10L, 1), (20L, 2), (25L, 3)]);
        reports.Take(3).ShouldAllBe(report => !report.Merged);
        reports[^1].Merged.ShouldBeTrue("the last report says that the sync is through, the merge included");
        reports[^1].Batches.ShouldBe(3);
    }

    // ---- the generation ---------------------------------------------------------------------------------------------

    [Fact]
    public void Every_sync_takes_the_next_generation_and_every_row_of_the_index_carries_it()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Fill(database, 30);
        using var catalog = database.Open();
        var tags = catalog.Create(LinkedTags);

        Generation(database).ShouldBe(0, "no sync yet");
        tags.Sync(new LuiSyncOptions { BatchSize = 8 });
        Generation(database).ShouldBe(1);
        Entries(database, "SELECT count(*) FROM lui_tags WHERE gen = 1").ShouldBe(30);

        tags.Sync(new LuiSyncOptions { BatchSize = 8 });
        Generation(database).ShouldBe(2);
        Entries(database, "SELECT count(*) FROM lui_tags WHERE gen = 2").ShouldBe(30, "two syncs in a row: the second one rewrote every row");
        Entries(database, "SELECT count(*) FROM lui_tags WHERE gen <> 2").ShouldBe(0);
    }

    [Fact]
    public void The_table_of_LUI_has_a_generation_column_that_cannot_be_null_and_an_index_on_it()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        using var catalog = database.Open();
        _ = catalog.Create(LinkedTags);

        database.Scalar<string>("SELECT type || ' ' || \"notnull\" FROM pragma_table_info('lui_tags') WHERE name = 'gen'").ShouldBe("INTEGER 1");
        Entries(database, "SELECT count(*) FROM sqlite_master WHERE type = 'index' AND tbl_name = 'lui_tags' AND name = 'lui_tags_gen'").ShouldBe(1);
        database.Scalar<string>("SELECT sql FROM sqlite_master WHERE name = 'lui_tags_gen'").ShouldContain("(gen)");
    }

    [Fact]
    public void A_row_that_a_refresh_wrote_beside_the_sync_stays_when_the_old_generation_is_removed()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Fill(database, 30);
        using var catalog = database.Open();
        var tags = catalog.Create(LinkedTags);
        tags.Sync();

        tags.Sync(new LuiSyncOptions
        {
            BatchSize = 10,
            Progress = Hear(report =>
            {
                if (report.Batches == 1)
                {
                    // a key below the ones the first batch took: its range is done, and no batch of this sync will see it
                    Insert(database, (0, "frisch", 5));
                    tags.Refresh(0).ShouldBeTrue();
                }
            }),
        });

        tags.Search("frisch").Hits.ShouldHaveSingleItem().Key.ShouldBe(0, "the row carries the generation of the sync beside which it was written");
        tags.Count().ShouldBe(31);
    }

    // ---- cancellation and failure -----------------------------------------------------------------------------------

    [Fact]
    public void A_cancelled_sync_keeps_its_batches_leaves_NeedsInitialSync_true_and_releases_the_lease()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Fill(database, 55);
        using var catalog = database.Open();
        var tags = catalog.Create(LinkedTags);
        using var cancellation = new CancellationTokenSource();
        var batches = 0;

        Should.Throw<OperationCanceledException>(() => tags.Sync(
            new LuiSyncOptions
            {
                BatchSize = 10,
                Progress = Hear(_ =>
                {
                    if (++batches == 2)
                    {
                        cancellation.Cancel();
                    }
                }),
            },
            cancellation.Token));

        tags.Count().ShouldBe(20, "the two batches that were committed stay");
        tags.NeedsInitialSync.ShouldBeTrue("a sync that was not complete does not clear the flag");
        Lease(database).Owner.ShouldBeNull("the lease is released, also when the sync is cancelled");

        tags.Sync(new LuiSyncOptions { BatchSize = 10 }).Indexed.ShouldBe(55);
        tags.NeedsInitialSync.ShouldBeFalse();
        Lease(database).Owner.ShouldBeNull();
        tags.Check().IsHealthy.ShouldBeTrue();
    }

    [Fact]
    public void A_cancelled_sync_deletes_no_row_of_the_previous_generation()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Fill(database, 40);
        using var catalog = database.Open();
        var tags = catalog.Create(LinkedTags);
        tags.Sync();
        Run(database, "DELETE FROM tag WHERE id <= 5;");
        using var cancellation = new CancellationTokenSource();

        Should.Throw<OperationCanceledException>(() => tags.Sync(
            new LuiSyncOptions { BatchSize = 10, Progress = Hear(report => { if (report.Batches == 1) { cancellation.Cancel(); } }) },
            cancellation.Token));

        Entries(database, "SELECT count(*) FROM lui_tags").ShouldBe(40, "the five entries whose rows are gone are removed at the end of a complete sync — not before");
        Entries(database, "SELECT count(*) FROM lui_tags WHERE gen = 2").ShouldBe(10, "the first batch is ten rows of the table, 6 … 15: they carry the new generation");
        Entries(database, "SELECT count(*) FROM lui_tags WHERE gen = 1").ShouldBe(30);
        Entries(database, "SELECT count(*) FROM lui_tags_fts_docsize").ShouldBe(40, "and neither does the search index lose an entry");

        tags.Sync();

        Generation(database).ShouldBe(3, "the generation of the cancelled sync is spent: the next one takes the next number");
        Entries(database, "SELECT count(*) FROM lui_tags").ShouldBe(35);
        Entries(database, "SELECT count(DISTINCT gen) FROM lui_tags").ShouldBe(1);
        Entries(database, "SELECT min(gen) FROM lui_tags").ShouldBe(3);
        tags.Check().IsHealthy.ShouldBeTrue();
    }

    [Fact]
    public void A_sync_that_fails_in_a_batch_keeps_the_earlier_ones_and_leaves_the_flag_and_no_lease()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Fill(database, 30);
        using var catalog = database.Open();
        var tags = catalog.Create(LinkedTags);

        var failure = Should.Throw<LuiLinkedTableException>(() => tags.Sync(new LuiSyncOptions
        {
            BatchSize = 10,
            Progress = Hear(report =>
            {
                if (report.Batches == 1)
                {
                    Run(database, "ALTER TABLE tag RENAME COLUMN name TO title;");
                }
            }),
        }));

        failure.Message.ShouldContain("'tags'");
        tags.Count().ShouldBe(10);
        tags.NeedsInitialSync.ShouldBeTrue();
        Lease(database).Owner.ShouldBeNull();
    }

    // ---- rows that are deleted ---------------------------------------------------------------------------------------

    [Fact]
    public void A_row_deleted_before_the_sync_is_gone_after_it()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Fill(database, 30);
        using var catalog = database.Open();
        var tags = catalog.Create(LinkedTags);
        tags.Sync();
        tags.Search("kuchen12").Hits.ShouldHaveSingleItem();

        Run(database, "DELETE FROM tag WHERE id = 12;");
        tags.Sync(new LuiSyncOptions { BatchSize = 10 });

        tags.Search("kuchen12").Hits.ShouldBeEmpty();
        tags.Count().ShouldBe(29);
        tags.Check().IsHealthy.ShouldBeTrue();
    }

    [Fact]
    public void A_row_deleted_during_the_sync_is_gone_after_ApplyPending()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Fill(database, 30);
        using var catalog = database.Open();
        var tags = catalog.Create(LinkedTags);
        tags.CreateTriggers();
        tags.Sync();

        tags.Sync(new LuiSyncOptions
        {
            BatchSize = 10,
            Progress = Hear(report =>
            {
                if (report.Batches == 1)
                {
                    // 3 is in the range the first batch did; 25 is in one that is still to come
                    Run(database, "DELETE FROM tag WHERE id IN (3, 25);");
                }
            }),
        });

        tags.Search("kuchen25").Hits.ShouldBeEmpty("the batch of its range did not find the row, and the old entry went with the old generation");
        tags.Pending().ShouldBe(2, "the triggers noted both");
        tags.ApplyPending().ShouldBe(2);
        tags.Search("kuchen3", Everything).Hits.Select(hit => hit.Key).ShouldNotContain(3);
        tags.Count().ShouldBe(28);
        tags.Check().IsHealthy.ShouldBeTrue();
    }

    [Fact]
    public void A_row_deleted_during_the_sync_without_triggers_is_gone_after_the_next_sync()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Fill(database, 30);
        using var catalog = database.Open();
        var tags = catalog.Create(LinkedTags);
        tags.Sync();

        tags.Sync(new LuiSyncOptions
        {
            BatchSize = 10,
            Progress = Hear(report =>
            {
                if (report.Batches == 1)
                {
                    Run(database, "DELETE FROM tag WHERE id IN (3, 25);");
                }
            }),
        });

        tags.Search("kuchen25").Hits.ShouldBeEmpty();
        tags.Count().ShouldBe(29, "the entry of 3 is in a range that was done: nothing noted the delete");
        tags.Check().Extra.ShouldBe(1);

        tags.Sync();

        tags.Count().ShouldBe(28);
        tags.Check().IsHealthy.ShouldBeTrue();
    }

    [Fact]
    public void A_row_that_a_refresh_wrote_beside_the_sync_and_that_is_deleted_before_its_batch_is_gone_after_the_sync()
    {
        // review of #291, B1: the refresh writes an entry for a row in a range the sync has not reached; the row goes before the batch comes
        using var database = new TestDatabase();
        Run(database, Table);
        Fill(database, 30);
        using var catalog = database.Open();
        var tags = catalog.Create(LinkedTags);
        tags.Sync();

        tags.Sync(new LuiSyncOptions
        {
            BatchSize = 10,
            Progress = Hear(report =>
            {
                if (report.Batches == 1 && !report.Merged)
                {
                    tags.Refresh(25).ShouldBeTrue();
                    tags.Refresh(26).ShouldBeTrue();
                    Run(database, "DELETE FROM tag WHERE id = 25; UPDATE tag SET name = NULL WHERE id = 26;");
                }
            }),
        });

        tags.Count().ShouldBe(28, "neither the deleted row nor the one without a text is in the index: the batch of their range did not take them, and the entries a refresh wrote are not 'reached by the sync'");
        tags.Search("kuchen25").Hits.ShouldBeEmpty();
        tags.Search("kuchen26").Hits.ShouldBeEmpty();
        tags.Check().IsHealthy.ShouldBeTrue();
    }

    [Fact]
    public void A_row_that_ApplyPending_wrote_beside_the_sync_and_that_is_deleted_before_its_batch_is_gone_after_ApplyPending()
    {
        // review of #291, B1, with the triggers: the outbox must still hold the key when the sync is through
        using var database = new TestDatabase();
        Run(database, Table);
        Fill(database, 30);
        using var catalog = database.Open();
        var tags = catalog.Create(LinkedTags);
        tags.CreateTriggers();
        tags.Sync();

        tags.Sync(new LuiSyncOptions
        {
            BatchSize = 10,
            Progress = Hear(report =>
            {
                if (report.Batches == 1 && !report.Merged)
                {
                    Run(database, "UPDATE tag SET name = 'frisch' WHERE id = 25;");
                    tags.ApplyPending().ShouldBe(1);
                    Run(database, "DELETE FROM tag WHERE id = 25;");
                }
            }),
        });

        tags.Pending().ShouldBe(1, "the delete is noted, and nothing the batches did forgets the note: no batch wrote an entry for the row");
        tags.ApplyPending().ShouldBe(1);
        tags.Count().ShouldBe(29);
        tags.Check().IsHealthy.ShouldBeTrue();
        tags.Search("frisch").Hits.ShouldBeEmpty();
    }

    // ---- one sync at a time: the lease ------------------------------------------------------------------------------

    [Fact]
    public void A_second_concurrent_sync_throws_sync_in_progress_naming_the_owner_and_the_expiry()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Fill(database, 30);
        var clock = new ManualClock();
        using var first = database.Open(new LuiCatalogOptions { TimeProvider = clock });
        using var second = database.Open(new LuiCatalogOptions { TimeProvider = clock });
        var tags = first.Create(LinkedTags);
        var other = second.Index(LinkedTags);
        var refused = new List<LuiSyncInProgressException>();
        string? owner = null;

        tags.Sync(new LuiSyncOptions
        {
            BatchSize = 10,
            Progress = Hear(report =>
            {
                if (report.Batches != 1)
                {
                    return;
                }

                owner = Lease(database).Owner;
                refused.Add(Should.Throw<LuiSyncInProgressException>(() => other.Sync(), "another catalog on the file"));
                refused.Add(Should.Throw<LuiSyncInProgressException>(() => tags.Sync(), "and the catalog that runs the sync itself: it is the run that holds the lease"));
            }),
        });

        owner.ShouldNotBeNullOrWhiteSpace();
        refused.Count.ShouldBe(2);
        refused.ShouldAllBe(exception => string.Equals(exception.IndexName, "tags", StringComparison.Ordinal) && string.Equals(exception.Owner, owner, StringComparison.Ordinal));
        refused.ShouldAllBe(exception => exception.ExpiresAt == clock.GetUtcNow().AddSeconds(60));
        refused[0].Message.ShouldContain("'tags'");
        refused[0].Message.ShouldContain(owner!);
        Lease(database).Owner.ShouldBeNull();
        other.Sync().Indexed.ShouldBe(30, "the lease is released: the next sync runs");
    }

    [Fact]
    public void A_valid_lease_of_another_owner_stops_the_sync_before_it_writes_anything()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Fill(database, 10);
        var clock = new ManualClock();
        using var catalog = database.Open(new LuiCatalogOptions { TimeProvider = clock });
        var tags = catalog.Create(LinkedTags);
        PlantLease(database, "another-process", clock.GetUtcNow().AddSeconds(1));

        var refused = Should.Throw<LuiSyncInProgressException>(() => tags.Sync());

        refused.Owner.ShouldBe("another-process");
        refused.ExpiresAt.ShouldBe(clock.GetUtcNow().AddSeconds(1));
        refused.IndexName.ShouldBe("tags");
        tags.Count().ShouldBe(0);
        tags.NeedsInitialSync.ShouldBeTrue();
        Generation(database).ShouldBe(0, "a sync that is refused takes no generation");
        Lease(database).Owner.ShouldBe("another-process", "and it does not touch the lease of the other");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void An_expired_lease_is_taken_over(int millisecondsFromNow)
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Fill(database, 10);
        var clock = new ManualClock();
        using var catalog = database.Open(new LuiCatalogOptions { TimeProvider = clock });
        var tags = catalog.Create(LinkedTags);
        PlantLease(database, "crashed-process", clock.GetUtcNow().AddMilliseconds(millisecondsFromNow));

        tags.Sync().Indexed.ShouldBe(10);

        tags.NeedsInitialSync.ShouldBeFalse();
        Lease(database).Owner.ShouldBeNull("the sync took the lease over and released it");
    }

    [Fact]
    public void A_lease_is_a_row_of_lui_meta_in_utc_and_it_is_renewed_with_every_batch()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Fill(database, 30);

        // a clock whose local time is not UTC: the expiry is a point in time, not a wall-clock reading
        var clock = new ManualClock();
        using var catalog = database.Open(new LuiCatalogOptions { TimeProvider = clock });
        var tags = catalog.Create(LinkedTags);
        var expiries = new List<long>();
        var starts = new List<long>();

        tags.Sync(new LuiSyncOptions
        {
            BatchSize = 10,
            Progress = Hear(report =>
            {
                if (report.Batches is >= 1 and <= 3 && !report.Merged)
                {
                    clock.Advance(TimeSpan.FromSeconds(10));
                    starts.Add(clock.GetUtcNow().ToUnixTimeMilliseconds());
                    expiries.Add(Lease(database).Expires ?? -1);
                }
            }),
        });

        // the lease read right after a batch is the one that batch wrote: its clock reading plus the 60 seconds of a lease
        expiries.Count.ShouldBe(3);
        expiries[0].ShouldBe(clock.Start.ToUnixTimeMilliseconds() + 60_000, "the first batch ran at the start of the clock");
        expiries[1].ShouldBe(starts[0] + 60_000, "the second batch renewed it, 10 seconds on");
        expiries[2].ShouldBe(starts[1] + 60_000);
    }

    [Fact]
    public async Task A_lease_that_was_taken_over_stops_the_old_owner_and_the_old_owner_leaves_the_lease_of_the_new_one_alone()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Fill(database, 40);
        var clock = new ManualClock();
        using var first = database.Open(new LuiCatalogOptions { TimeProvider = clock });
        using var second = database.Open(new LuiCatalogOptions { TimeProvider = clock });
        var old = first.Create(LinkedTags);
        var taker = second.Index(LinkedTags);
        using var oldPaused = new ManualResetEventSlim();
        using var oldMayGo = new ManualResetEventSlim();
        using var takerPaused = new ManualResetEventSlim();
        using var takerMayGo = new ManualResetEventSlim();

        var oldRun = Task.Run(() => Record.Exception(() => old.Sync(PausingAfterTheFirstBatch(oldPaused, oldMayGo))));
        oldPaused.Wait(Deadline).ShouldBeTrue();

        // the old owner stops renewing — it is stuck — and its lease runs out; another sync takes it over and is in the middle of its work
        clock.Advance(TimeSpan.FromSeconds(61));
        var takerRun = Task.Run(() => Record.Exception(() => taker.Sync(PausingAfterTheFirstBatch(takerPaused, takerMayGo))));
        takerPaused.Wait(Deadline).ShouldBeTrue();
        var takersLease = Lease(database).Owner;
        takersLease.ShouldNotBeNull();

        oldMayGo.Set();
        var stopped = await oldRun.WaitAsync(Deadline);

        var refusal = stopped.ShouldBeOfType<LuiSyncInProgressException>("the old owner notices at its next batch and stops");
        refusal.Owner.ShouldBe(takersLease, "the exception names the one that holds the lease now");
        refusal.IndexName.ShouldBe("tags");
        Lease(database).Owner.ShouldBe(takersLease, "the old owner releases only its own lease: it let the lease of the new owner alone");
        Entries(database, "SELECT count(*) FROM lui_tags").ShouldBe(
            10,
            "the old owner wrote nothing after it lost the lease: the rows 1 … 10 are the ones its first batch wrote and the new owner's first batch wrote again");

        takerMayGo.Set();
        (await takerRun.WaitAsync(Deadline)).ShouldBeNull("the new owner completes");
        Lease(database).Owner.ShouldBeNull();
        taker.NeedsInitialSync.ShouldBeFalse();
        taker.Count().ShouldBe(40);
        taker.Check().IsHealthy.ShouldBeTrue();
    }

    [Fact]
    public void The_lease_of_the_old_owner_that_lost_it_while_nobody_holds_it_is_a_sync_in_progress_exception_with_no_owner()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Fill(database, 30);
        var clock = new ManualClock();
        using var first = database.Open(new LuiCatalogOptions { TimeProvider = clock });
        using var second = database.Open(new LuiCatalogOptions { TimeProvider = clock });
        var old = first.Create(LinkedTags);
        var taker = second.Index(LinkedTags);

        var failure = Should.Throw<LuiSyncInProgressException>(() => old.Sync(new LuiSyncOptions
        {
            BatchSize = 10,
            Progress = Hear(report =>
            {
                if (report.Batches == 1)
                {
                    clock.Advance(TimeSpan.FromSeconds(61));
                    taker.Sync().Indexed.ShouldBe(30);
                }
            }),
        }));

        failure.IndexName.ShouldBe("tags");
        failure.Owner.ShouldBeNull("the sync that took it over has finished and released it");
        failure.ExpiresAt.ShouldBeNull();
        failure.Message.ShouldContain("'tags'");
        taker.NeedsInitialSync.ShouldBeFalse("what the old owner did not finish does not undo what the new one completed");
        Entries(database, "SELECT count(*) FROM lui_tags WHERE gen <> (SELECT sync_generation FROM lui_meta WHERE index_name = 'tags')").ShouldBe(0);
    }

    // ---- the index that is swapped under the sync -------------------------------------------------------------------

    [Fact]
    public void An_index_that_was_created_again_between_two_batches_stops_the_sync_and_gets_nothing_from_it()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Fill(database, 40);
        using var catalog = database.Open();
        using var other = database.Open();
        var tags = catalog.Create(LinkedTags);

        var failure = Should.Throw<LuiSyncInProgressException>(
            () => tags.Sync(new LuiSyncOptions { BatchSize = 10, Progress = Hear(report => SwapOnce(report, other, LinkedTags)) }),
            "the same definition is no stale definition for a handle — but the sync that was running belongs to the index that is gone");

        failure.IndexName.ShouldBe("tags");
        var created = other.Index(LinkedTags);
        created.Count().ShouldBe(0, "nothing of the old sync landed in the new index");
        created.NeedsInitialSync.ShouldBeTrue();
        Entries(database, "SELECT count(*) FROM lui_tags").ShouldBe(0);
        Lease(database).Owner.ShouldBeNull();
    }

    [Fact]
    public void An_index_created_again_with_another_definition_between_two_batches_is_a_definition_mismatch()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Fill(database, 40);
        using var catalog = database.Open();
        using var other = database.Open();
        var tags = catalog.Create(LinkedTags);

        Should.Throw<LuiDefinitionMismatchException>(
            () => tags.Sync(new LuiSyncOptions { BatchSize = 10, Progress = Hear(report => SwapOnce(report, other, LinkedTagsOfWords)) })).IndexName.ShouldBe("tags");

        Entries(database, "SELECT count(*) FROM lui_tags").ShouldBe(0, "no batch was written into the index that was created again");
        other.Index(LinkedTagsOfWords).NeedsInitialSync.ShouldBeTrue();
    }

    [Fact]
    public void An_index_that_was_dropped_between_two_batches_is_an_index_not_found()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Fill(database, 40);
        using var catalog = database.Open();
        using var other = database.Open();
        var tags = catalog.Create(LinkedTags);

        Should.Throw<LuiIndexNotFoundException>(() => tags.Sync(new LuiSyncOptions
        {
            BatchSize = 10,
            Progress = Hear(report =>
            {
                if (report.Batches == 1)
                {
                    other.DropIndex("tags");
                }
            }),
        })).IndexName.ShouldBe("tags");
    }

    [Fact]
    public async Task The_swap_between_the_check_and_the_work_of_a_batch_cannot_get_in_and_the_next_batch_says_so()
    {
        // ADR-0075: the check is made inside the write transaction of the batch, so the drop and the create of the other catalog wait for
        // its commit; the batch lands in the index that it checked, and the next batch finds the index another one
        using var database = new TestDatabase();
        Run(database, Table);
        Fill(database, 40);
        using var catalog = database.Open();
        using var other = database.Open();
        var tags = catalog.Create(LinkedTags);
        var checks = 0;
        Task? swap = null;
        catalog.Database.AfterSchemaCheck = () =>
        {
            // the transactions of the sync: the lease, the first batch, the second batch — the swap starts inside the second one
            if (++checks != 3)
            {
                return;
            }

            catalog.Database.AfterSchemaCheck = null;
            swap = Task.Run(() =>
            {
                other.DropIndex("tags");
                other.Create(LinkedTagsOfWords);
            });
            _ = swap.Wait(TimeSpan.FromMilliseconds(400));
        };

        // the other writer gets the file when the batch has committed — and the next batch, started right after it, would take the lock before
        // the swap could (SQLite has no queue): the sync is held between the batches until the swap is through
        var failure = Record.Exception(() => tags.Sync(new LuiSyncOptions
        {
            BatchSize = 10,
            Progress = Hear(report =>
            {
                if (report.Batches == 2 && swap is not null)
                {
                    swap.Wait(Deadline).ShouldBeTrue();
                }
            }),
        }));
        catalog.Database.AfterSchemaCheck = null;

        await (swap ?? throw new InvalidOperationException("the seam did not run")).WaitAsync(Deadline);
        failure.ShouldBeOfType<LuiDefinitionMismatchException>();
        Entries(database, "SELECT count(*) FROM lui_tags").ShouldBe(0, "the new index is empty: the batches before the swap went into the index that was dropped");
    }

    // ---- the merge and the settings of FTS5 -------------------------------------------------------------------------

    [Fact]
    public void The_merge_runs_in_steps_each_in_a_transaction_of_its_own()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Fill(database, 3_000);
        var phases = new List<string>();

        using (var small = database.Open())
        {
            small.Database.MergePages = 1;
            small.Database.AfterSyncTransaction = phases.Add;
            var tags = small.Create(LinkedTags);

            tags.Sync(new LuiSyncOptions { BatchSize = 500 });
        }

        Count(phases, "lease").ShouldBe(1);
        Count(phases, "batch").ShouldBe(6);
        Count(phases, "merge").ShouldBeGreaterThan(2, "one page per step: the merge of six segments is several transactions");
        phases.IndexOf("merge").ShouldBeGreaterThan(phases.LastIndexOf("removal"), "the merge comes after the batches and the removal");
        phases.LastIndexOf("batch").ShouldBeLessThan(phases.IndexOf("removal"));

        var steps = Count(phases, "merge");
        var fewer = new List<string>();
        using var big = new TestDatabase();
        Run(big, Table);
        Fill(big, 3_000);
        using (var catalog = big.Open())
        {
            catalog.Database.AfterSyncTransaction = fewer.Add;
            catalog.Create(LinkedTags).Sync(new LuiSyncOptions { BatchSize = 500 });
        }

        Count(fewer, "merge").ShouldBeLessThan(steps, "with the pages of a step at their default the same merge takes fewer steps");
        Count(fewer, "merge").ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void The_merge_is_done_when_the_sync_returns_and_the_flag_is_cleared_by_its_last_step()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Fill(database, 3_000);
        using var catalog = database.Open();
        catalog.Database.MergePages = 4;
        var tags = catalog.Create(LinkedTags);
        var flags = new List<bool>();
        catalog.Database.AfterSyncTransaction = phase =>
        {
            if (string.Equals(phase, "merge", StringComparison.Ordinal))
            {
                flags.Add(tags.NeedsInitialSync);
            }
        };

        tags.Sync(new LuiSyncOptions { BatchSize = 500 });

        flags.Count.ShouldBeGreaterThan(2);
        flags.Take(flags.Count - 1).ShouldAllBe(flag => flag, "the flag is still true after every step but the last");
        flags[^1].ShouldBeFalse("the last step — the one that found nothing left — cleared it in its own transaction");
        using var connection = database.Connect();
        using var merge = connection.CreateCommand();
        merge.CommandText = LinkedSource.MergeSql("lui_tags_fts");
        merge.Parameters.AddWithValue("$pages", 4_096);
        var before = Scalar(connection, LinkedSource.TotalChangesSql);
        merge.ExecuteNonQuery();
        (Scalar(connection, LinkedSource.TotalChangesSql) - before).ShouldBe(1, "one more step finds nothing left");
    }

    [Fact]
    public void The_automatic_merge_of_FTS5_is_back_on_after_every_batch_and_every_step_of_the_removal()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Fill(database, 60);
        using var catalog = database.Open();
        var tags = catalog.Create(LinkedTags);
        tags.Sync();
        Run(database, "DELETE FROM tag WHERE id % 2 = 0;");
        var seen = new List<long>();

        var removals = new List<long>();
        tags.Sync(new LuiSyncOptions
        {
            BatchSize = 10,
            Progress = Hear(report =>
            {
                var value = database.Scalar<long>("SELECT v FROM lui_tags_fts_config WHERE k = 'automerge'");
                (report.Removed > 0 ? removals : seen).Add(value);
            }),
        });

        seen.Count.ShouldBe(3, "thirty rows are left in three batches");
        seen.ShouldAllBe(value => value == 4, "a batch turns it off while it loads and sets the default again before its commit");
        removals.ShouldNotBeEmpty("thirty entries are removed, ten at a time");
        removals.ShouldAllBe(value => value == 4, "and so does a step of the removal: its deletes write into the search index, too");
        database.Scalar<long>("SELECT v FROM lui_tags_fts_config WHERE k = 'automerge'").ShouldBe(4);
    }

    // ---- the catalog and the sync ------------------------------------------------------------------------------------

    [Fact]
    public async Task A_catalog_that_is_disposed_during_the_sync_waits_for_it_and_the_lease_is_released_at_its_end()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Fill(database, 30);
        var catalog = database.Open();
        var tags = catalog.Create(LinkedTags);
        using var paused = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();

        var sync = Task.Run(() => tags.Sync(PausingAfterTheFirstBatch(paused, resume)));
        paused.Wait(Deadline).ShouldBeTrue();

        var disposing = Task.Run(catalog.Dispose);
        SpinWait.SpinUntil(() => Record.Exception(() => tags.Count()) is ObjectDisposedException, Deadline).ShouldBeTrue("a call after Dispose is refused");
        resume.Set();

        (await sync.WaitAsync(Deadline)).Indexed.ShouldBe(30, "the sync that was running is let finish: Dispose waits for it");
        await disposing.WaitAsync(Deadline);
        using var reopened = LuiCatalog.Open(database.FilePath);
        reopened.Index(LinkedTags).NeedsInitialSync.ShouldBeFalse();
        Lease(database).Owner.ShouldBeNull();
    }

    [Fact]
    public void The_typo_dictionary_is_rebuilt_once_after_the_batches_and_not_while_they_run()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Insert(database, (1, "kuchen", 5), (2, "torte", 5));
        using var catalog = database.Open();
        var tags = catalog.Create(Corrected);
        tags.Sync();
        tags.WarmUpTypoDictionary();
        Run(database, "UPDATE tag SET name = 'zimtstern' WHERE id = 1; INSERT INTO tag(id, name) VALUES (3, 'kuchen2');");
        var states = new List<LuiTypoDictionaryState>();

        tags.Sync(new LuiSyncOptions { BatchSize = 1, Progress = Hear(_ => states.Add(tags.TypoDictionaryState)) });

        states.ShouldAllBe(state => state == LuiTypoDictionaryState.Current, "the batches do not touch the dictionary — it is rebuilt after them, from the table as it is then");
        tags.TypoDictionaryState.ShouldBe(LuiTypoDictionaryState.Current);
        tags.Search("zimtsten", new LuiQueryOptions { TypoTolerance = true }).Hits.Select(hit => hit.Text).Contains("zimtstern", StringComparer.Ordinal).ShouldBeTrue("the new word is in the dictionary again");
    }

    // ---- helpers ----------------------------------------------------------------------------------------------------

    /// <summary>The first call of the progress after the first batch: another catalog drops the index and creates it again — a swap, between two batches.</summary>
    private static void SwapOnce(LuiSyncReport report, LuiCatalog other, LuiLinkedIndexDefinition<long> definition)
    {
        if (report.Batches != 1)
        {
            return;
        }

        other.DropIndex("tags");
        _ = other.Create(definition);
    }

    /// <summary>Batches of 10; after the first one the sync tells the test and waits — outside of every lock — until the test lets it go.</summary>
    private static LuiSyncOptions PausingAfterTheFirstBatch(ManualResetEventSlim paused, ManualResetEventSlim mayGo) =>
        new()
        {
            BatchSize = 10,
            Progress = Hear(report =>
            {
                if (report.Batches == 1)
                {
                    paused.Set();
                    mayGo.Wait(Deadline).ShouldBeTrue();
                }
            }),
        };

    private static int Count(List<string> phases, string phase) => phases.Count(seen => string.Equals(seen, phase, StringComparison.Ordinal));

    private static Inline<LuiSyncReport> Hear(Action<LuiSyncReport> action) => new(action);

    /// <summary>A progress that runs on the thread of the sync, right where it is told: <c>Progress&lt;T&gt;</c> would post it to the pool.</summary>
    private sealed class Inline<T>(Action<T> action) : IProgress<T>
    {
        public void Report(T value) => action(value);
    }

    /// <summary>A clock that stands still until the test moves it, and whose local time is not UTC.</summary>
    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 22, 9, 0, 0, TimeSpan.Zero);

        public DateTimeOffset Start { get; } = new(2026, 11, 22, 9, 0, 0, TimeSpan.Zero);

        public override TimeZoneInfo LocalTimeZone { get; } = TimeZoneInfo.CreateCustomTimeZone("lui-test-zone", TimeSpan.FromHours(14), "lui-test-zone", "lui-test-zone");

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private static void Fill(TestDatabase database, int count, string prefix = "kuchen") =>
        Run(
            database,
            $"WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n WHERE i < {count}) "
            + $"INSERT INTO tag(id, name, usage_count) SELECT i, '{prefix}' || i, i % 7 FROM n;");

    private static long Generation(TestDatabase database) =>
        database.Scalar<long>("SELECT sync_generation FROM lui_meta WHERE index_name = 'tags'");

    private static long Entries(TestDatabase database, string sql) => database.Scalar<long>(sql);

    private static (string? Owner, long? Expires) Lease(TestDatabase database)
    {
        using var connection = database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sync_lease_owner, sync_lease_expires FROM lui_meta WHERE index_name = 'tags';";
        using var reader = command.ExecuteReader();
        reader.Read().ShouldBeTrue("the index has its row in lui_meta");
        return (reader.IsDBNull(0) ? null : reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetInt64(1));
    }

    private static void PlantLease(TestDatabase database, string owner, DateTimeOffset expires)
    {
        using var connection = database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE lui_meta SET sync_lease_owner = $owner, sync_lease_expires = $expires WHERE index_name = 'tags';";
        command.Parameters.AddWithValue("$owner", owner);
        command.Parameters.AddWithValue("$expires", expires.ToUnixTimeMilliseconds());
        command.ExecuteNonQuery();
    }

    private static long Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)command.ExecuteScalar()!;
    }
}
