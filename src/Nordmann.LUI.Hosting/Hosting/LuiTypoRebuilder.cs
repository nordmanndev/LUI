using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Nordmann.LUI.Hosting;

/// <summary>
/// Rebuilds the typo dictionaries a sync left stale, on an interval, in the background (ADR-0021): the core starts no
/// thread of its own (ADR-0018), so the host does it. A dictionary that is not stale is not touched; one that is not built
/// is not built here. A rebuild that fails is logged, and the next round tries again — the stale dictionary answers
/// meanwhile.
/// </summary>
internal sealed partial class LuiTypoRebuilder(
    IServiceProvider services,
    LuiRegistrations registrations,
    TimeProvider timeProvider,
    ILogger<LuiTypoRebuilder> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (registrations.RebuildStaleTypoDictionariesEvery is not { } every || registrations.Indexes.Count == 0)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(every, timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            foreach (var index in registrations.Indexes)
            {
                try
                {
                    if (index.RebuildIfStale(services, stoppingToken))
                    {
                        Rebuilt(index.Name);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    // the catalog or the container is gone: the application ends
                    return;
                }
#pragma warning disable CA1031 // the loop outlives one failure; the stale dictionary answers meanwhile
                catch (Exception exception)
#pragma warning restore CA1031
                {
                    Failed(exception, index.Name);
                }
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "LUI: the stale typo dictionary of '{Index}' is built anew")]
    private partial void Rebuilt(string index);

    [LoggerMessage(Level = LogLevel.Error, Message = "LUI: the stale typo dictionary of '{Index}' could not be built anew; the next round tries again")]
    private partial void Failed(Exception exception, string index);
}
