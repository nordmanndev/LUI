using Shouldly;
using static Nordmann.LUI.Integration.Tests.Linked.LinkedTable;

namespace Nordmann.LUI.Integration.Tests.Concurrency;

/// <summary>
/// The check of a handle and its operation are one step (ADR-0075, issue #265, review of pull request 286): an index that
/// another catalog — another process — drops and creates again <b>between</b> the read of the schema counter and the work of
/// the operation must not be read or written through the old definition, and must not surface as a bare SQLite error. The
/// writes check inside the transaction that holds the write lock (the other catalog's drop waits for it), the reads inside
/// their snapshot, and what cannot share one transaction checks inside each of its transactions.
/// <para>
/// The interleaving is not a matter of luck: the seam <c>Database.AfterSchemaCheck</c> runs a swap of the index — a drop and a
/// create with another definition, by another catalog — at the very place between the check and the work, and waits a moment
/// for it. Whether the swap can get in is the question; the answer has to be the same one in every order: the operation
/// either completes on the index it checked, or throws the proper exception — and nothing lands in the new index.
/// </para>
/// </summary>
public sealed class AtomicCheckTests
{
    private static readonly LuiIndexDefinition<long> Tags = new("tags", index => index.SingleWord());

    private static readonly LuiIndexDefinition<long> TagsOfWords = new("tags", index => index.MultiWord());

    private static readonly LuiLinkedIndexDefinition<long> LinkedTags =
        new("linked_tags", index => index.SingleWord().LinkTo("tag", "id", "name", weight: "usage_count"));

    private static readonly LuiLinkedIndexDefinition<long> LinkedOfWords =
        new("linked_tags", index => index.MultiWord().LinkTo("tag", "id", "name", weight: "usage_count"));

    /// <summary>How long the operation waits for the swap — the swap is blocked by the write lock of the operation, or it is not.</summary>
    private static readonly TimeSpan Wait = TimeSpan.FromMilliseconds(400);

    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);

    // ---- the writes ------------------------------------------------------------------------------------------------

    [Fact]
    public void Upsert_never_lands_in_an_index_that_was_created_again_between_the_check_and_the_write() =>
        AWriteMeetsTheSwap(tags => tags.Upsert(99, "vegetable"));

    [Fact]
    public void UpdateWeight_never_lands_in_an_index_that_was_created_again_between_the_check_and_the_write() =>
        AWriteMeetsTheSwap(tags => tags.UpdateWeight(1, 5));

    [Fact]
    public void UpdatePriority_never_lands_in_an_index_that_was_created_again_between_the_check_and_the_write() =>
        AWriteMeetsTheSwap(tags => tags.UpdatePriority(1, LuiPriority.High));

    [Fact]
    public void Remove_never_lands_in_an_index_that_was_created_again_between_the_check_and_the_write() =>
        AWriteMeetsTheSwap(tags => tags.Remove(1));

    [Fact]
    public void UpsertMany_never_lands_in_an_index_that_was_created_again_between_the_check_and_the_write() =>
        AWriteMeetsTheSwap(tags => tags.UpsertMany([new LuiEntry<long>(98, "vegetable"), new LuiEntry<long>(99, "vegetarisch")]));

    [Fact]
    public void Sync_never_lands_in_an_index_that_was_created_again_between_the_check_and_the_work() =>
        AWriteMeetsTheSwap(tags => tags.Sync(
            Enumerable.Range(10, 6).Select(key => new LuiEntry<long>(key, "vegetable " + key)),
            new LuiSyncOptions { BatchSize = 2 }));

    [Fact]
    public void Rebuild_and_Optimize_say_so_when_the_index_was_created_again_between_the_check_and_the_work()
    {
        AWriteMeetsTheSwap(tags => tags.Rebuild());
        AWriteMeetsTheSwap(tags => tags.Optimize());
    }

    // ---- the reads -------------------------------------------------------------------------------------------------

    [Fact]
    public void Count_answers_for_the_index_it_checked_or_throws_and_never_reads_the_new_one()
    {
        var seen = 0L;
        var failure = AnEmbeddedOperationMeetsTheSwap(tags => seen = tags.Count());

        IsProper(failure).ShouldBeTrue(Describe(failure));
        if (failure is null)
        {
            seen.ShouldBe(1, "the count of the snapshot the check was made in, not of the index that was created again");
        }
    }

    [Fact]
    public void Search_answers_for_the_index_it_checked_or_throws()
    {
        var hits = -1;
        var failure = AnEmbeddedOperationMeetsTheSwap(tags => hits = tags.Search("veg").Hits.Count);

        IsProper(failure).ShouldBeTrue(Describe(failure));
        if (failure is null)
        {
            hits.ShouldBe(1);
        }
    }

    [Fact]
    public void A_linked_read_answers_for_the_index_it_checked_or_throws_and_never_reads_the_new_one()
    {
        using var database = new TestDatabase();
        Run(database, Table);
        Insert(database, (1, "vegan", 1));
        using var first = database.Open();
        using var second = database.Open();
        first.Create(LinkedTags).Sync();
        var linked = second.Index(LinkedTags);
        _ = linked.Count();
        var counted = -1L;
        var pending = -1L;

        var failure = MeetTheSwap(second, () => first.DropIndex("linked_tags"), () => first.Create(LinkedOfWords), () => counted = linked.Count());
        IsProper(failure).ShouldBeTrue(Describe(failure));
        if (failure is null)
        {
            counted.ShouldBe(1);
        }

        failure = Record.Exception(() => pending = linked.Pending());
        IsProper(failure).ShouldBeTrue(Describe(failure));
        _ = pending;
    }

    // ---- many operations, the index swapped under them --------------------------------------------------------------

    [Fact]
    public async Task Operations_beside_a_catalog_that_drops_and_creates_the_index_again_end_well_or_in_the_proper_exception()
    {
        using var database = new TestDatabase();
        using var first = database.Open();
        using var second = database.Open();
        first.Create(Tags);
        using var stop = new CancellationTokenSource(Deadline);
        var stray = new List<Exception>();

        var workers = Enumerable.Range(0, 3).Select(number => Task.Factory.StartNew(
            () =>
            {
                var round = 0;
                do
                {
                    try
                    {
                        // a handle per request: after the swap a new handle is the one that works
                        var tags = second.Index(Tags);
                        _ = number switch
                        {
                            0 => tags.Count(),
                            1 => tags.Search("veg").Hits.Count,
                            _ => WriteOne(tags, round),
                        };
                    }
                    catch (Exception exception) when (IsProper(exception) || exception is ObjectDisposedException)
                    {
                        // a swap in between: the proper answer
                    }
                    catch (Exception exception)
                    {
                        lock (stray)
                        {
                            stray.Add(exception);
                        }
                    }

                    round++;
                }
                while (!stop.IsCancellationRequested && round < 400);
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default)).ToArray();

        for (var swap = 0; swap < 15; swap++)
        {
            first.DropIndex("tags");
            first.Create(swap % 2 == 0 ? TagsOfWords : Tags);
        }

        await stop.CancelAsync();
        await Task.WhenAll(workers).WaitAsync(Deadline);
        stray.ShouldBeEmpty(string.Join(Environment.NewLine, stray.Select(Describe)));
    }

    // ---- the driver -----------------------------------------------------------------------------------------------

    private static void AWriteMeetsTheSwap(Action<LuiEmbeddedIndex<long>> write)
    {
        var failure = AnEmbeddedOperationMeetsTheSwap(write, out var database, out var first);
        using (database)
        using (first)
        {
            IsProper(failure).ShouldBeTrue(Describe(failure));
            first.Index(TagsOfWords).Count().ShouldBe(0, "nothing was written through the old definition into the index that was created again");
        }
    }

    private static Exception? AnEmbeddedOperationMeetsTheSwap(Action<LuiEmbeddedIndex<long>> operation)
    {
        var failure = AnEmbeddedOperationMeetsTheSwap(operation, out var database, out var first);
        first.Dispose();
        database.Dispose();
        return failure;
    }

    private static Exception? AnEmbeddedOperationMeetsTheSwap(
        Action<LuiEmbeddedIndex<long>> operation,
        out TestDatabase database,
        out LuiCatalog first)
    {
        database = new TestDatabase();
        first = database.Open();
        var second = database.Open();
        first.Create(Tags).Upsert(1, "vegan");
        var tags = second.Index(Tags);
        _ = tags.Count();

        var other = first;
        return MeetTheSwap(second, () => other.DropIndex("tags"), () => other.Create(TagsOfWords), () => operation(tags));
    }

    /// <summary>
    /// Runs the operation; at the place between its check and its work the swap — a drop and a create by another catalog — is
    /// started on a thread of its own and given a moment to finish. Returns what the operation threw, and waits for the swap.
    /// </summary>
    private static Exception? MeetTheSwap(LuiCatalog operating, Action drop, Action create, Action operation)
    {
        Task? swap = null;
        operating.Database.AfterSchemaCheck = () =>
        {
            operating.Database.AfterSchemaCheck = null;
            swap = Task.Run(() =>
            {
                drop();
                create();
            });
            _ = swap.Wait(Wait);
        };

        var failure = Record.Exception(operation);
        operating.Database.AfterSchemaCheck = null;

        var ran = swap ?? throw new InvalidOperationException("The operation did not check the schema counter: the seam did not run.");
        ran.Wait(Deadline).ShouldBeTrue("the swap ends once the operation let go of the write lock");
        return failure;
    }

    /// <summary>One write of a handle, for the loop of many operations.</summary>
    private static int WriteOne(LuiEmbeddedIndex<long> tags, int round)
    {
        tags.Upsert(1000 + (round % 50), "vegetable " + round);
        return round;
    }

    private static bool IsProper(Exception? exception) =>
        exception is null or LuiIndexNotFoundException or LuiDefinitionMismatchException;

    private static string Describe(Exception? exception) =>
        exception is null ? "no exception" : $"{exception.GetType().Name}: {exception.Message}";
}
