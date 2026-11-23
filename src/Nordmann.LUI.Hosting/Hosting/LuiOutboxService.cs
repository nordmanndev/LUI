using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Nordmann.LUI.Hosting;

/// <summary>
/// The outbox of the linked indexes as a service of the host (ADR-0076): it calls <c>catalog.ApplyPending()</c> every <see cref="LuiOutboxOptions.Interval"/> and stops with
/// the host. It does <b>nothing at the start</b> — creating the triggers, the first sync and the typo dictionaries are <c>catalog.Initialize()</c>, which the application calls
/// (LUI has no hosted service that does).
/// <para>
/// <b>A failing round never stops the host.</b> It is logged through the logger of the host — a warning, with the exception —, the next round waits with exponential backoff
/// up to <see cref="LuiOutboxOptions.MaxBackoff"/>, and the first round that works goes back to the interval. Every process with the registration runs its own loop; that is
/// safe — the outbox is applied by one writer at a time (ADR-0046) — and a second loop only finds less to do.
/// </para>
/// <para>
/// A round is a task of the service: the host that stops the service waits for the task of the round (the fix of issue #185), not for a flag that clears a moment
/// before the round is over. A catalog that was opened with <c>OpenReadOnly</c> cannot apply an outbox: the host does not start.
/// </para>
/// </summary>
internal sealed partial class LuiOutboxService : BackgroundService
{
    /// <summary>The time a test waits for a signal of the service — a stop for a hang, not a reading of the clock.</summary>
    private static readonly TimeSpan HangStop = TimeSpan.FromSeconds(30);

    private readonly LuiCatalog _catalog;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _maxBackoff;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;

    // the signals of the loop, counted: a waiter that comes late finds every one of them (issue #185 — a claim must stay true)
    private readonly SemaphoreSlim _armed = new(0);
    private readonly SemaphoreSlim _began = new(0);
    private readonly SemaphoreSlim _finished = new(0);
    private int _running;
    private int _roundsBegun;
    private int _roundsFinished;
    private int _failures;
    private long _nextDelayTicks;

    /// <summary>The service.</summary>
    /// <param name="catalog">The catalog of the host.</param>
    /// <param name="options">The settings, as they stood at the registration.</param>
    /// <param name="time">The clock of the waits: the one of the host.</param>
    /// <param name="logger">The logger of the host.</param>
    internal LuiOutboxService(LuiCatalog catalog, LuiOutboxOptions options, TimeProvider time, ILogger<LuiOutboxService> logger)
    {
        _catalog = catalog;
        _interval = options.Interval;
        _maxBackoff = options.MaxBackoff < options.Interval ? options.Interval : options.MaxBackoff;
        _time = time;
        _logger = logger;
    }

    /// <summary>How many rounds in a row have failed; 0 after a round that worked.</summary>
    internal int Failures => Volatile.Read(ref _failures);

    /// <summary>How many rounds have begun — a claim that stays true.</summary>
    internal int RoundsBegun => Volatile.Read(ref _roundsBegun);

    /// <summary>How many rounds are over — worked or failed.</summary>
    internal int RoundsFinished => Volatile.Read(ref _roundsFinished);

    /// <summary>Whether a round runs now.</summary>
    internal bool RoundRunning => Volatile.Read(ref _running) != 0;

    /// <summary>Waits until the loop waits for its next round — its timer exists — and tells how long it waits. One signal per wait; a test moves the clock after it.</summary>
    /// <returns>The delay of the wait that began.</returns>
    internal async Task<TimeSpan> ArmedAsync()
    {
        await Signalled(_armed).ConfigureAwait(false);
        return TimeSpan.FromTicks(Interlocked.Read(ref _nextDelayTicks));
    }

    /// <summary>Waits until a round has begun. One signal per round.</summary>
    /// <returns>The task that ends with the signal.</returns>
    internal Task BeganAsync() => Signalled(_began);

    /// <summary>Waits until a round is over. One signal per round.</summary>
    /// <returns>The task that ends with the signal.</returns>
    internal Task FinishedAsync() => Signalled(_finished);

    /// <summary>Refuses to start on a catalog that cannot apply an outbox, then starts the loop.</summary>
    /// <param name="cancellationToken">The token of the start.</param>
    /// <returns>The task of the start.</returns>
    /// <exception cref="InvalidOperationException">The catalog was opened with <c>OpenReadOnly</c>.</exception>
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        if (_catalog.IsReadOnly)
        {
            throw new InvalidOperationException(
                "AddLuiOutbox: a read-only catalog cannot apply the outbox — the rows the triggers noted are applied by the process that writes the file. "
                + "Register AddLuiOutbox in that process, and open this catalog with LuiCatalog.Open or OpenOrCreate if it is the one that writes.");
        }

        return base.StartAsync(cancellationToken);
    }

    /// <summary>Stops the loop — and waits for a round that runs: nothing of LUI runs after the service is gone.</summary>
    public override void Dispose()
    {
        base.Dispose();
        try
        {
            ExecuteTask?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // the loop ended with the stop that Dispose asked for
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>The loop: wait, run a round, wait again — until the host stops it.</summary>
    /// <param name="stoppingToken">Cancelled when the host stops the service.</param>
    /// <returns>The task of the loop; the host waits for it.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = _interval;
        while (!stoppingToken.IsCancellationRequested)
        {
            // the timer exists before the loop says that it waits: a test that moves the clock after the signal moves a clock that is being waited for
            var wait = Task.Delay(delay, _time, stoppingToken);
            Interlocked.Exchange(ref _nextDelayTicks, delay.Ticks);
            _ = _armed.Release();
            try
            {
                await wait.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            delay = await Round(stoppingToken).ConfigureAwait(false);
        }
    }

    /// <summary>One round of the outbox, as a task of the service. A round that fails does not take the host with it.</summary>
    /// <param name="stoppingToken">Cancelled when the host stops the service.</param>
    /// <returns>The wait before the next round.</returns>
    private async Task<TimeSpan> Round(CancellationToken stoppingToken)
    {
        Volatile.Write(ref _running, 1);
        _ = Interlocked.Increment(ref _roundsBegun);
        _ = _began.Release();
        try
        {
            // the work is synchronous SQLite: a thread of the pool, not the one of the loop; it looks at the token between the indexes and the batches
            var applied = await Task.Run(() => _catalog.ApplyPending(stoppingToken), CancellationToken.None).ConfigureAwait(false);
            if (applied > 0)
            {
                LogOutboxApplied(applied);
            }

            if (Interlocked.Exchange(ref _failures, 0) > 0)
            {
                LogRecovered(_interval.TotalSeconds);
            }

            return _interval;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return _interval;
        }
#pragma warning disable CA1031 // a failed round is logged and tried again — a loop that throws ends the service, and the host with it
        catch (Exception exception)
#pragma warning restore CA1031
        {
            var failures = Interlocked.Increment(ref _failures);
            var next = Backoff(failures);
            LogRoundFailed(exception, failures, next.TotalSeconds);
            return next;
        }
        finally
        {
            Volatile.Write(ref _running, 0);
            _ = Interlocked.Increment(ref _roundsFinished);
            _ = _finished.Release();
        }
    }

    /// <summary>The wait after the <paramref name="failures"/>-th failed round in a row: the interval doubled that many times, cut at the maximum.</summary>
    /// <param name="failures">How many rounds in a row have failed, at least one.</param>
    /// <returns>The wait.</returns>
    private TimeSpan Backoff(int failures)
    {
        // 2^failures, with the exponent held where a double and the ticks stay far from overflow: the maximum cuts it long before
        var ticks = _interval.Ticks * Math.Pow(2, Math.Min(failures, 40));
        return ticks >= _maxBackoff.Ticks ? _maxBackoff : TimeSpan.FromTicks((long)ticks);
    }

    private static async Task Signalled(SemaphoreSlim signal)
    {
        if (!await signal.WaitAsync(HangStop).ConfigureAwait(false))
        {
            throw new TimeoutException("The outbox service did not give the signal in thirty seconds.");
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "LUI could not apply the outbox of the linked indexes (round {Failures} in a row failed); the rows stay in the outbox, the next round in {DelaySeconds} s tries again.")]
    private partial void LogRoundFailed(Exception exception, int failures, double delaySeconds);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug, Message = "LUI applied {Rows} rows of the outbox of the linked indexes.")]
    private partial void LogOutboxApplied(int rows);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "LUI applied the outbox again after failed rounds; the next round in {IntervalSeconds} s.")]
    private partial void LogRecovered(double intervalSeconds);
}
