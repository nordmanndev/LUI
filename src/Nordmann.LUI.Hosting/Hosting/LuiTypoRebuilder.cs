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

        while (await PeriodicRound.Wait(every, timeProvider, stoppingToken).ConfigureAwait(false))
        {
            foreach (var index in registrations.Indexes)
            {
                // each index on its own: one that fails does not hold up the others; the stale dictionary answers meanwhile
                var goesOn = PeriodicRound.Run(
                    () =>
                    {
                        if (index.RebuildIfStale(services, stoppingToken))
                        {
                            Rebuilt(index.Name);
                        }
                    },
                    exception => Failed(exception, index.Name),
                    stoppingToken);

                if (!goesOn)
                {
                    return;
                }
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "LUI: the stale typo dictionary of '{Index}' is built anew")]
    private partial void Rebuilt(string index);

    [LoggerMessage(Level = LogLevel.Error, Message = "LUI: the stale typo dictionary of '{Index}' could not be built anew; the next round tries again")]
    private partial void Failed(Exception exception, string index);
}
