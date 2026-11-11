using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Nordmann.LUI.Hosting;

/// <summary>
/// The warm-up of the catalog as a service of the host (issue #68, ADR-0043): the typo dictionaries of the indexes are
/// built at the start of the host, and the outbox of the linked indexes is applied on an interval.
/// <para>
/// The service belongs to the host: the host starts it, stops it and disposes it. The library starts no thread of its
/// own. What it did, and what failed, it tells the logger of the host (ADR-0045).
/// </para>
/// </summary>
internal sealed partial class LuiWarmupService : IHostedService, IDisposable
{
    private readonly LuiCatalog _catalog;
    private readonly bool _warmUpTypoDictionaries;
    private readonly string[] _typoDictionaryIndexes;
    private readonly TimeSpan _interval;
    private readonly ILogger _logger;
    private Timer? _timer;
    private int _running;

    /// <summary>The service.</summary>
    /// <param name="catalog">The catalog of the host.</param>
    /// <param name="options">The settings of the warm-up, as they stood at the registration.</param>
    /// <param name="logger">The logger of the host.</param>
    internal LuiWarmupService(LuiCatalog catalog, LuiWarmupOptions options, ILogger<LuiWarmupService> logger)
    {
        _catalog = catalog;
        _warmUpTypoDictionaries = options.WarmUpTypoDictionaries;
        _typoDictionaryIndexes = [.. options.TypoDictionaryIndexes];
        _interval = options.OutboxInterval;
        _logger = logger;
    }

    /// <summary>Applies the outbox, builds the dictionaries of the indexes and starts the rounds of the outbox.</summary>
    /// <param name="cancellationToken">Checked between the indexes.</param>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // the rows that the triggers noted come first: the dictionary that is built afterwards knows their words
        var applied = _catalog.ApplyPending(cancellationToken);
        LogOutboxApplied(applied);

        if (_warmUpTypoDictionaries)
        {
            var watch = Stopwatch.StartNew();

            // the build of a dictionary is CPU-bound and takes seconds — a thread of its own, not the pool of the application
            await Task.Factory.StartNew(
                WarmUp,
                cancellationToken,
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default).ConfigureAwait(false);

            LogTypoDictionariesBuilt(_typoDictionaryIndexes.Length == 0 ? "every index" : string.Join(", ", _typoDictionaryIndexes), watch.Elapsed.TotalMilliseconds);
        }

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

    /// <summary>Builds the dictionaries the settings name — every index, if they name none.</summary>
    /// <param name="state">The token of the start.</param>
    private void WarmUp(object? state)
    {
        var cancellationToken = (CancellationToken)state!;
        if (_typoDictionaryIndexes.Length == 0)
        {
            _catalog.WarmUpTypoDictionaries(cancellationToken);
        }
        else
        {
            _catalog.WarmUpTypoDictionaries(_typoDictionaryIndexes, cancellationToken);
        }
    }

    /// <summary>Whether a round of the outbox runs — what the guard of <see cref="OnRound"/> reads (a test asks it, #106).</summary>
    internal bool RoundRunning => Volatile.Read(ref _running) != 0;

    /// <summary>
    /// A round of the outbox — one at a time. The callback of the timer; internal so that a test can start a round
    /// beside one that runs, without a timer and without a race (#106).
    /// </summary>
    /// <param name="state">Not used.</param>
    internal void OnRound(object? state)
    {
        // a round that is still running is not started again. The rounds of a timer may overlap, and the catalog writes
        // through one writer: a second round would not write beside the first, it would hold a thread of the pool while
        // it waits for the writer — one per tick, for as long as the first round waits for the lock of the file
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
    /// round tries again — and the logger of the host hears of the failure (ADR-0045), so that it is not left to the
    /// backlog alone.
    /// </summary>
    private void Round()
    {
        try
        {
            var applied = _catalog.ApplyPending();
            if (applied > 0)
            {
                LogOutboxApplied(applied);
            }
        }
#pragma warning disable CA1031 // a failed round is logged and tried again — a timer callback that throws ends the process
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogRoundFailed(exception, _interval.TotalSeconds);
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "LUI applied {Rows} rows of the outbox of the linked indexes.")]
    private partial void LogOutboxApplied(int rows);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "LUI built the typo dictionaries of {Indexes} in {ElapsedMilliseconds:F0} ms.")]
    private partial void LogTypoDictionariesBuilt(string indexes, double elapsedMilliseconds);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "LUI could not apply the outbox of the linked indexes; the rows stay in the outbox, the next round in {IntervalSeconds} s tries again.")]
    private partial void LogRoundFailed(Exception exception, double intervalSeconds);
}
