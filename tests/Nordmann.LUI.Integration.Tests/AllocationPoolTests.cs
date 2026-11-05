using Shouldly;

namespace Nordmann.LUI.Integration.Tests;

/// <summary>
/// The pool of the buffers of the candidates (issue #30, ADR-0029): the buffers of a query are lent
/// again on the same thread, so a query allocates almost nothing — but only as long as the lengths
/// fit. The length of the big buffers follows the <b>candidate budget</b>, and the budget is a query
/// option: a service that serves several budgets needs several lengths. A pool that keeps twelve
/// buffers of the exact length and <b>discards</b> a buffer when no place is left fills up with the
/// lengths of the past — and then every query allocates everything anew, more than before the pool
/// (the review of PR #53).
/// </summary>
public sealed class AllocationPoolTests
{
    /// <summary>The budgets that run before the measured one: three lengths, then the fourth.</summary>
    private static readonly int[] Budgets = [500, 600, 1000];

    /// <summary>
    /// The allocation of a query must not depend on the budgets that ran before it on the same
    /// thread: three different candidate budgets, then a fourth — the fourth must not cost the sum
    /// of all the buffers.
    /// </summary>
    [Fact]
    public void The_allocation_of_a_query_stays_low_after_the_candidate_budget_changed()
    {
        using var database = new TestDatabase();
        using var catalog = database.Open();
        var index = catalog.Index("tags").SingleWord().TypoTolerance().Ensure();

        // a small corpus is enough: the buffers are lent by the budget, not by the number of the
        // candidates that are read
        index.Upsert(1, "Apfel", 10);
        for (var i = 0; i < 20; i++)
        {
            index.Upsert(100 + i, $"Apfel{i}", 10);
        }

        index.WarmUpTypoDictionary();

        // The pool of the buffers is thread-static: the scenario runs on a thread of its own,
        // otherwise the pool that a test before has left behind would decide the number — the
        // guard would be flaky, and a flaky guard proves nothing (the review of PR #53).
        var allocated = OnOwnThread(() =>
        {
            // three different budgets, one after the other, on the same thread
            foreach (var budget in Budgets)
            {
                Search(index, budget);
            }

            // the fourth: its buffers do not fit into the pool any more
            return Measure(index, 700);
        });

        // 64 KB: the query allocates 39.8 KB in this state; before the correction (the review of PR #53) it
        // allocated 95.4 KB — the pool had filled up with the lengths of the past and every query
        // allocated the big buffers anew. The target of §16 is p95 ≤ 100 KB; this is the same claim
        // for a single query, measured on the same machine.
        allocated.ShouldBeLessThan(
            64 * 1024,
            $"A query allocates {allocated / 1024.0:F1} KB after the candidate budget changed — the pool of " +
            "the buffers holds the lengths of the past and every query allocates the big buffers anew.");
    }

    /// <summary>Runs the action on a thread of its own: the pool of the buffers is thread-static.</summary>
    private static long OnOwnThread(Func<long> action)
    {
        var result = 0L;
        var thread = new Thread(() => result = action());
        thread.Start();
        thread.Join();
        return result;
    }

    /// <summary>A query with a typo: the union of the corrections is the biggest buffer of a query.</summary>
    private static void Search(ILuiIndex<long, LuiNoPayload> index, int budget) =>
        index.Search("apfael", new LuiQueryOptions { Candidates = budget, MaxResults = 20, TypoTolerance = true });

    /// <summary>The allocation of one query, after a warm-up query with the same options.</summary>
    private static long Measure(ILuiIndex<long, LuiNoPayload> index, int budget)
    {
        Search(index, budget);

        var before = GC.GetAllocatedBytesForCurrentThread();
        Search(index, budget);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
