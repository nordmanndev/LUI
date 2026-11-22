using Nordmann.LUI.Integration.Tests.Catalog;
using Shouldly;

namespace Nordmann.LUI.Integration.Tests.Concurrency;

/// <summary>
/// The open call under concurrency (ADR-0075, issue #265, review of pull request 286, round 2). The definitions in memory may be
/// stale — another process dropped the index and created it again — and many threads of a host take their handle at once:
/// <b>no call may throw for a stale snapshot</b> when the file holds the index that was declared (a mismatch says "drop it and
/// create it again" — a false one destroys an index), and a definition object has <b>one handle</b>, whoever replaces a handle that
/// failed. The interleavings that matter are made by seams, not by luck; the stress tests beside them only add pressure.
/// </summary>
public sealed class OpenCallTests
{
    private const int Threads = 16;

    private static readonly LuiIndexDefinition<long> Tags = new("tags", index => index.SingleWord());

    private static readonly LuiIndexDefinition<long> TagsOfWords = new("tags", index => index.MultiWord());

    // ---- a stale snapshot, a reload that another thread made first -----------------------------------------------------

    [Fact]
    public void A_mismatch_that_was_judged_on_a_snapshot_another_thread_has_replaced_is_decided_again()
    {
        using var database = new TestDatabase();
        using var first = database.Open();
        using var second = database.Open();
        first.Create(TagsOfWords);
        _ = second.Index(TagsOfWords).Count();
        first.DropIndex("tags");
        first.Create(Tags);

        // the thread that judges the stale snapshot is overtaken: another thread reloads before it looks at the counter
        second.StoredIndexes.BeforeCounterCheck = () =>
        {
            second.StoredIndexes.BeforeCounterCheck = null;
            _ = second.StoredIndexes.Reload();
        };

        second.Index(Tags).Kind.ShouldBe(LuiIndexKind.SingleWord, "the file holds the declared index; a mismatch from the stale snapshot is false");
    }

    [Fact]
    public void A_missing_name_that_was_judged_on_a_snapshot_another_thread_has_replaced_is_decided_again()
    {
        using var database = new TestDatabase();
        using var first = database.Open();
        using var second = database.Open();
        Should.Throw<LuiIndexNotFoundException>(() => second.Index(Tags));
        first.Create(Tags);

        second.StoredIndexes.BeforeCounterCheck = () =>
        {
            second.StoredIndexes.BeforeCounterCheck = null;
            _ = second.StoredIndexes.Reload();
        };

        second.Index(Tags).Name.ShouldBe("tags", "the file holds the index now; the remembered missing name is a stale answer");
    }

    [Fact]
    public void Concurrent_open_calls_on_stale_definitions_throw_no_false_mismatch_and_no_false_not_found()
    {
        for (var round = 0; round < 20; round++)
        {
            using var database = new TestDatabase();
            using var first = database.Open();
            using var second = database.Open();
            first.Create(TagsOfWords);
            _ = second.Index(TagsOfWords).Count();
            first.DropIndex("tags");
            first.Create(Tags);

            var failures = new List<Exception>();
            AtTheSameTime.Run(Threads, number =>
            {
                try
                {
                    _ = second.Index(Tags).Name + number;
                }
                catch (Exception exception)
                {
                    lock (failures)
                    {
                        failures.Add(exception);
                    }
                }
            });

            failures.ShouldBeEmpty($"round {round} (stale mismatch): " + string.Join(" | ", failures.Select(failure => failure.GetType().Name)));
        }

        for (var round = 0; round < 20; round++)
        {
            using var database = new TestDatabase();
            using var first = database.Open();
            using var second = database.Open();
            Should.Throw<LuiIndexNotFoundException>(() => second.Index(Tags));
            first.Create(Tags);

            var failures = new List<Exception>();
            AtTheSameTime.Run(Threads, number =>
            {
                try
                {
                    _ = second.Index(Tags).Name + number;
                }
                catch (Exception exception)
                {
                    lock (failures)
                    {
                        failures.Add(exception);
                    }
                }
            });

            failures.ShouldBeEmpty($"round {round} (remembered missing name): " + string.Join(" | ", failures.Select(failure => failure.GetType().Name)));
        }
    }

    // ---- one handle for a definition object --------------------------------------------------------------------------

    [Fact]
    public void A_failed_handle_that_is_replaced_by_two_calls_at_once_gives_one_handle()
    {
        using var database = new TestDatabase();
        using var first = database.Open();
        using var second = database.Open();
        first.Create(Tags);
        var failed = second.Index(Tags);
        first.DropIndex("tags");
        Should.Throw<LuiIndexNotFoundException>(() => failed.Count());
        first.Create(Tags);

        // a second call comes into the slow path while the first one is in it, and finishes completely
        LuiEmbeddedIndex<long>? nested = null;
        second.AfterSlowPathStarted = () =>
        {
            second.AfterSlowPathStarted = null;
            nested = second.Index(Tags);
        };

        var outer = second.Index(Tags);

        outer.ShouldBeSameAs(nested, "a definition object has one handle");
        second.Index(Tags).ShouldBeSameAs(outer);
        second.DropIndex("tags");
        Should.Throw<LuiIndexNotFoundException>(() => outer.Count(), "the handle that was handed out is the one DropIndex marks — no orphan");
    }

    [Fact]
    public void Concurrent_open_calls_after_a_handle_failed_give_one_handle_that_a_drop_stops()
    {
        for (var round = 0; round < 20; round++)
        {
            using var database = new TestDatabase();
            using var first = database.Open();
            using var second = database.Open();
            first.Create(Tags);
            var failed = second.Index(Tags);
            first.DropIndex("tags");
            Should.Throw<LuiIndexNotFoundException>(() => failed.Count());
            first.Create(Tags);

            var handles = new LuiEmbeddedIndex<long>[Threads];
            AtTheSameTime.Run(Threads, number => handles[number] = second.Index(Tags));

            handles.Distinct().Count().ShouldBe(1, $"round {round}: one handle for one definition object");
            second.DropIndex("tags");
            Should.Throw<LuiIndexNotFoundException>(() => handles[0].Count(), $"round {round}: no handle escaped the drop");
        }
    }

    // ---- a handle that is opened while this catalog drops the index -----------------------------------------------------

    [Fact]
    public void A_handle_that_was_judged_before_a_drop_and_made_after_it_is_not_returned_on_the_old_snapshot()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        catalog.Create(Tags).Upsert(1, "vegan");
        var fresh = new LuiIndexDefinition<long>("tags", index => index.SingleWord());

        // the call has found "tags" in the definitions of the catalog; DropIndex runs, completely, before the call takes the lock
        catalog.BeforeHandleLock = () =>
        {
            catalog.BeforeHandleLock = null;
            catalog.DropIndex("tags");
        };

        LuiEmbeddedIndex<long>? handle = null;
        var failure = Record.Exception(() => handle = catalog.Index(fresh));
        catalog.Create(Tags);

        // either the call saw the drop and said so, or the handle it returned is stale — never a handle that works on the index
        // that was created again after the drop
        if (handle is null)
        {
            failure.ShouldBeOfType<LuiIndexNotFoundException>();
        }
        else
        {
            Should.Throw<LuiIndexNotFoundException>(() => handle.Count(), "a handle that DropIndex did not mark escaped it");
        }
    }

    [Fact]
    public void No_handle_that_was_returned_before_DropIndex_returned_answers_after_the_index_was_created_again()
    {
        for (var round = 0; round < 25; round++)
        {
            using var database = new TestDatabase();
            using var catalog = database.Open();
            catalog.Create(Tags);
            var race = new OpenRace(catalog);
            race.Start(4);

            for (var spin = 0; spin < 200; spin++)
            {
                Thread.Yield();
            }

            catalog.DropIndex("tags");
            race.DropReturned();
            catalog.Create(Tags);

            foreach (var handle in race.Stop())
            {
                Should.Throw<LuiIndexNotFoundException>(() => handle.Count(), $"round {round}: a handle that was returned before DropIndex returned works on the new index");
            }
        }
    }

    /// <summary>Threads that open handles of fresh definition objects — every call a first call — and keep those returned before the drop returned.</summary>
    private sealed class OpenRace(LuiCatalog catalog)
    {
        private readonly List<LuiEmbeddedIndex<long>> _before = [];
        private readonly List<Thread> _threads = [];
        private volatile bool _stop;
        private volatile bool _dropReturned;

        internal void Start(int threads)
        {
            for (var number = 0; number < threads; number++)
            {
                var thread = new Thread(Open) { IsBackground = true };
                _threads.Add(thread);
                thread.Start();
            }
        }

        internal void DropReturned() => _dropReturned = true;

        internal LuiEmbeddedIndex<long>[] Stop()
        {
            _stop = true;
            foreach (var thread in _threads)
            {
                thread.Join(TimeSpan.FromSeconds(30)).ShouldBeTrue("the openers ended");
            }

            lock (_before)
            {
                return [.. _before];
            }
        }

        private void Open()
        {
            while (!_stop)
            {
                try
                {
                    var handle = catalog.Index(new LuiIndexDefinition<long>("tags", index => index.SingleWord()));
                    if (!_dropReturned)
                    {
                        lock (_before)
                        {
                            _before.Add(handle);
                        }
                    }
                }
                catch (LuiIndexNotFoundException)
                {
                    // after the drop, before the create
                }
            }
        }
    }
}
