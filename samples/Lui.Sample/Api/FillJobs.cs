using System.Collections.Concurrent;
using Nordmann.LUI.Sample.Indexes;

namespace Nordmann.LUI.Sample.Api;

/// <summary>
/// The fills of the bulk indexes, one at a time per index (ADR-0029). A fill runs in the background — a fill of a million
/// entries takes minutes, and a request that waited for it would time out — and the panel watches it through
/// <see cref="FillJob.Status"/>.
/// </summary>
internal sealed class FillJobs
{
    /// <summary>The most entries one fill may ask for.</summary>
    internal const int Limit = 1_000_000;

    private readonly ConcurrentDictionary<string, FillJob> _jobs = new(StringComparer.Ordinal);

    /// <summary>
    /// Starts a fill of an index. A fill that runs already is cancelled first — the index is loaded from the source anyway, so
    /// the second fill is the one that counts.
    /// </summary>
    /// <param name="bulk">The index to fill.</param>
    /// <param name="requested">The number of entries the index should hold.</param>
    /// <returns>The state of the fill that has just started.</returns>
    /// <exception cref="ArgumentException">The number is not one the index can hold.</exception>
    internal FillStatus Start(IBulkSampleIndex bulk, int requested)
    {
        Validate(bulk, requested);

        var job = new FillJob(bulk.Name, requested);

        if (_jobs.TryGetValue(bulk.Name, out var running))
        {
            running.Cancel();
        }

        _jobs[bulk.Name] = job;
        _ = Task.Run(() => Run(bulk, job));

        return job.Status;
    }

    /// <summary>The state of the fill of an index, or <see langword="null"/> when there was none.</summary>
    internal FillStatus? Status(string index) => _jobs.TryGetValue(index, out var job) ? job.Status : null;

    /// <summary>Stops the fill of an index; <see langword="false"/> when there was none.</summary>
    internal bool Cancel(string index)
    {
        if (!_jobs.TryGetValue(index, out var job))
        {
            return false;
        }

        job.Cancel();
        return true;
    }

    private static void Validate(IBulkSampleIndex bulk, int requested)
    {
        if (requested < 1)
        {
            throw new ArgumentException("The number of entries must be at least one.", nameof(requested));
        }

        if (requested > Limit)
        {
            throw new ArgumentException($"The largest fill of the sample holds {Limit:N0} entries.", nameof(requested));
        }

        if (bulk.Capacity is { } capacity && requested > capacity)
        {
            throw new ArgumentException($"The source of '{bulk.Name}' holds {capacity:N0} entries.", nameof(requested));
        }
    }

    private static void Run(IBulkSampleIndex bulk, FillJob job)
    {
        try
        {
            var report = bulk.Fill(job.Requested, job, job.Cancellation.Token);
            job.MarkDone(report);
        }
        catch (OperationCanceledException)
        {
            job.MarkCancelled();
        }
        catch (Exception e)
        {
            job.MarkFailed(e.Message);
        }
        finally
        {
            job.Cancellation.Dispose();
        }
    }
}
