using Microsoft.Extensions.Hosting;
using Nordmann.LUI.Sample.Indexes;

namespace Nordmann.LUI.Sample;

/// <summary>
/// Applies the outbox of the linked index every two seconds (ADR-0015): the rows the triggers of the application noted
/// come into the index, without the page waiting for a click.
/// <para>
/// The application does this itself: the container of LUI holds the catalog and nothing else (ADR-0033), so the index —
/// and the work around it — belongs to the application that owns it.
/// </para>
/// </summary>
internal sealed class OutboxService(SampleIndexes indexes, ILogger<OutboxService> logger) : BackgroundService
{
    /// <summary>How often the outbox is applied.</summary>
    private static readonly TimeSpan Every = TimeSpan.FromSeconds(2);

    /// <summary>The name of the linked index whose outbox is applied.</summary>
    private const string LinkedIndex = "tags";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Every);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            Apply(stoppingToken);
        }
    }

    /// <summary>Applies the outbox now; a failure is logged and the loop goes on.</summary>
    private void Apply(CancellationToken cancellationToken)
    {
        if (!indexes.TryGet(LinkedIndex, out var index) || index is not ILinkedSampleIndex linked)
        {
            return;
        }

        try
        {
            linked.ApplyPending(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // the host stops
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "The outbox of the linked index could not be applied.");
        }
    }
}
