using Microsoft.Extensions.Hosting;

namespace Nordmann.LUI.Hosting;

/// <summary>
/// The warm-up of the catalog as a service of the host (issue #68, ADR-0043): the typo dictionaries of the indexes are
/// built at the start of the host, and the outbox of the linked indexes is applied on an interval.
/// <para>
/// The service belongs to the host: the host starts it, stops it and disposes it. The library starts no thread of its
/// own.
/// </para>
/// </summary>
internal sealed class LuiWarmupService : IHostedService, IDisposable
{
    /// <summary>How long the service waits between two rounds of the outbox, if the application says nothing else.</summary>
    internal static readonly TimeSpan DefaultOutboxInterval = TimeSpan.FromSeconds(30);

    private readonly LuiCatalog _catalog;
    private readonly TimeSpan _interval;
    private Timer? _timer;
    private int _running;

    /// <summary>The service.</summary>
    /// <param name="catalog">The catalog of the host.</param>
    /// <param name="interval">How long the service waits between two rounds of the outbox.</param>
    internal LuiWarmupService(LuiCatalog catalog, TimeSpan interval)
    {
        _catalog = catalog;
        _interval = interval;
    }

    /// <summary>Applies the outbox, builds the dictionaries of the indexes and starts the rounds of the outbox.</summary>
    /// <param name="cancellationToken">Checked between the indexes.</param>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // the rows that the triggers noted come first: the dictionary that is built afterwards knows their words
        _catalog.ApplyPending(cancellationToken);

        // the build of a dictionary is CPU-bound and takes seconds — a thread of its own, not the pool of the application
        await Task.Factory.StartNew(
            () => _catalog.WarmUpTypoDictionaries(cancellationToken),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default).ConfigureAwait(false);

        _timer = new Timer(OnRound, null, _interval, _interval);
    }

    /// <summary>Stops the rounds of the outbox.</summary>
    /// <param name="cancellationToken">Not used — the rounds stop with the timer.</param>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        return Task.CompletedTask;
    }

    /// <summary>Stops the timer of the rounds.</summary>
    public void Dispose()
    {
        _timer?.Dispose();
    }

    /// <summary>A round of the outbox — one at a time.</summary>
    /// <param name="state">Not used.</param>
    private void OnRound(object? state)
    {
        // a round that is still running is not started again — the rounds of a timer may overlap
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            return;
        }

        try
        {
            Round();
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    /// <summary>
    /// One round of the outbox. A round that fails does not take the host with it: the outbox keeps the rows, the next
    /// round tries again, and the application sees the backlog with <c>Pending()</c>.
    /// </summary>
    private void Round()
    {
        try
        {
            _catalog.ApplyPending();
        }
        catch (Exception)
        {
            // the rows stay in the outbox — the next round tries again
        }
    }
}
