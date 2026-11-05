using System.Diagnostics;

namespace Nordmann.LUI.Sample.Api;

/// <summary>
/// One fill of a bulk index, and what it has done so far. The job is the progress of the sync itself: the library tells the
/// report after every batch (ADR-0016), and the job keeps the last one.
/// </summary>
internal sealed class FillJob : IProgress<LuiSyncReport>
{
    private readonly Lock _gate = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private string _state = "running";
    private LuiSyncReport _report = new();
    private string? _error;

    internal FillJob(string index, int requested)
    {
        Index = index;
        Requested = requested;
    }

    /// <summary>The index that is being filled.</summary>
    internal string Index { get; }

    /// <summary>The number of entries the user asked for.</summary>
    internal int Requested { get; }

    /// <summary>Stops the fill when it is cancelled.</summary>
    internal CancellationTokenSource Cancellation { get; } = new();

    /// <summary>Stops the fill — if it is still running. A fill that is done has nothing to stop.</summary>
    internal void Cancel()
    {
        try
        {
            Cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // the fill is over and has released its source: there is nothing left to cancel
        }
    }

    /// <summary>Told by the sync after every batch.</summary>
    public void Report(LuiSyncReport report)
    {
        lock (_gate)
        {
            _report = report;
        }
    }

    /// <summary>The fill is done — with the report the library returned, the one that counts what was removed.</summary>
    internal void MarkDone(LuiSyncReport report)
    {
        lock (_gate)
        {
            _report = report;
            _state = "done";
            _clock.Stop();
        }
    }

    /// <summary>The fill was cancelled; the batches that were committed stay (ADR-0016).</summary>
    internal void MarkCancelled()
    {
        lock (_gate)
        {
            _state = "cancelled";
            _clock.Stop();
        }
    }

    /// <summary>The fill failed; the reason is what the panel shows.</summary>
    internal void MarkFailed(string error)
    {
        lock (_gate)
        {
            _state = "failed";
            _error = error;
            _clock.Stop();
        }
    }

    /// <summary>What the fill has done so far.</summary>
    internal FillStatus Status
    {
        get
        {
            lock (_gate)
            {
                return new FillStatus(
                    Index,
                    _state,
                    Requested,
                    _report.Read,
                    _report.Added,
                    _report.Changed,
                    _report.Unchanged,
                    _report.Removed,
                    _report.Batches,
                    _clock.Elapsed.TotalMilliseconds,
                    _error);
            }
        }
    }
}
