using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Nordmann.LUI.Hosting;

/// <summary>
/// Creates every registered index when the host starts (LUI-Spezifikation §12.7): a definition that does not match, or a
/// linked table that does not fit, stops the start — not the first request. An index that is opened instead of created
/// has to exist, and the file stays as it is (ADR-0033). If asked, it then builds the typo dictionaries in the
/// background, so the first typo does not pay for them (ADR-0013).
/// </summary>
internal sealed partial class LuiStartup(IServiceProvider services, LuiRegistrations registrations, ILogger<LuiStartup> logger)
    : IHostedService, IDisposable
{
    private readonly CancellationTokenSource _stopping = new();
    private Task _warmUp = Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (registrations.EnsureOnStart)
        {
            foreach (var index in registrations.Indexes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                index.Create(services);
            }

            // the indexes that are opened: they have to exist, and the file stays as it is (ADR-0033)
            foreach (var index in registrations.OpenedIndexes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                index.Open(services);
            }
        }

        if (registrations.WarmUpTypoDictionariesOnStart && registrations.Indexes.Count > 0)
        {
            _warmUp = Task.Run(() => WarmUp(_stopping.Token), CancellationToken.None);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stopping.CancelAsync().ConfigureAwait(false);
        try
        {
            await _warmUp.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // the host stops; a dictionary half built is thrown away
        }
    }

    /// <summary>
    /// Cancels a warm-up that still runs — a host disposed without a stop never called <see cref="StopAsync"/> — so that the
    /// catalog, disposed after this service, does not wait for the whole build.
    /// </summary>
    public void Dispose()
    {
        _stopping.Cancel();
        _stopping.Dispose();
    }

    private void WarmUp(CancellationToken stopping)
    {
        foreach (var index in registrations.Indexes)
        {
            if (stopping.IsCancellationRequested)
            {
                return;
            }

            try
            {
                if (index.WarmUp(services, stopping))
                {
                    WarmedUp(index.Name);
                }
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // a background warm-up must not end the application; the failure is logged
            catch (Exception exception)
#pragma warning restore CA1031
            {
                WarmUpFailed(exception, index.Name);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "LUI: the typo dictionary of '{Index}' is built")]
    private partial void WarmedUp(string index);

    [LoggerMessage(Level = LogLevel.Error, Message = "LUI: the typo dictionary of '{Index}' could not be built")]
    private partial void WarmUpFailed(Exception exception, string index);
}
