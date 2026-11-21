using Shouldly;

namespace Nordmann.LUI.Integration.Tests.Concurrency;

/// <summary>
/// Runs a piece of work on several threads that start together. The threads are threads of their own — a thread pool that is busy
/// with the tests of other classes would never start them all, and a barrier would wait for ever — and both the barrier and the
/// end of the work have a deadline, so a start that does not come is a failure with a message and not a hang.
/// </summary>
internal static class AtTheSameTime
{
    private static readonly TimeSpan StartDeadline = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan EndDeadline = TimeSpan.FromSeconds(120);

    /// <summary>Runs <paramref name="work"/> once per number from 0 to <paramref name="count"/> - 1, all of them released at once.</summary>
    internal static void Run(int count, Action<int> work)
    {
        using var barrier = new Barrier(count);
        var tasks = Enumerable.Range(0, count).Select(number => Task.Factory.StartNew(
            () =>
            {
                barrier.SignalAndWait(StartDeadline).ShouldBeTrue("all the threads of the test started");
                work(number);
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default)).ToArray();

        var all = Task.WhenAll(tasks);
        all.Wait(EndDeadline).ShouldBeTrue("the work of the threads ended");
        all.GetAwaiter().GetResult();
    }
}
