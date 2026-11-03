using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Nordmann.LUI.Hosting;

/// <summary>
/// Applies the outbox of every linked index that asked for it, on its own interval (ADR-0015: "a hosted service that
/// applies them on a timer comes with the dependency injection"). An error is logged and the loop goes on — a locked
/// file or a table that changed must not end the application; <c>Pending()</c> shows that the index falls behind.
/// </summary>
internal sealed partial class LuiPendingApplier(
    IServiceProvider services,
    LuiRegistrations registrations,
    TimeProvider timeProvider,
    ILogger<LuiPendingApplier> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(registrations.Indexes
            .Where(index => index is { ApplyPending: not null, ApplyPendingEvery: not null })
            .Select(index => Loop(index, stoppingToken)));

    private async Task Loop(IndexRegistration index, CancellationToken stopping)
    {
        var every = index.ApplyPendingEvery!.Value;
        while (!stopping.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(every, timeProvider, stopping).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                var applied = index.ApplyPending!(services, stopping);
                if (applied > 0)
                {
                    Applied(applied, index.Name);
                }
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // the loop outlives one failure; the failure is logged and the next round tries again
            catch (Exception exception)
#pragma warning restore CA1031
            {
                Failed(exception, index.Name);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "LUI: {Applied} noted rows of '{Index}' applied")]
    private partial void Applied(int applied, string index);

    [LoggerMessage(Level = LogLevel.Error, Message = "LUI: the noted rows of '{Index}' could not be applied; the next round tries again")]
    private partial void Failed(Exception exception, string index);
}
