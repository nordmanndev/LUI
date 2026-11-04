namespace Nordmann.LUI.Hosting;

/// <summary>
/// The loop of the periodic hosted services — <see cref="LuiPendingApplier"/> and <see cref="LuiTypoRebuilder"/> — in one
/// place (review of 2026-11-04, C.3, issue #40): wait an interval; run a round; a round that fails is reported and the
/// loop goes on; the stop of the host and a catalog that is gone end it quietly. The services keep what is theirs — the
/// work and the log messages — and stay two services: a failed round of the outbox must not hold up a rebuild.
/// </summary>
internal static class PeriodicRound
{
    /// <summary>Waits <paramref name="every"/>; says whether the loop goes on (<see langword="false"/>: the host stops).</summary>
    internal static async Task<bool> Wait(TimeSpan every, TimeProvider timeProvider, CancellationToken stopping)
    {
        try
        {
            await Task.Delay(every, timeProvider, stopping).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// Runs one piece of work of a round. A failure goes to <paramref name="failed"/> and the loop goes on; says whether it
    /// does — <see langword="false"/> when the host stops or the catalog or the container is gone, which is the end of the
    /// application, not a failed round.
    /// </summary>
    internal static bool Run(Action work, Action<Exception> failed, CancellationToken stopping)
    {
        try
        {
            work();
            return true;
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            return false;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
#pragma warning disable CA1031 // the loop outlives one failure; the failure is reported and the next round tries again
        catch (Exception exception)
#pragma warning restore CA1031
        {
            failed(exception);
            return true;
        }
    }
}
