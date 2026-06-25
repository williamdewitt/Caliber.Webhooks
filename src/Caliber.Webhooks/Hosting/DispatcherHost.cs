using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Caliber.Webhooks;

/// <summary>
/// The background dispatcher (a Client in iDesign terms): on a timer it asks the
/// <see cref="DeliveryManager"/> to deliver due messages, draining a backlog without waiting for the
/// next tick and surviving transient failures so the loop keeps running.
/// </summary>
internal sealed partial class DispatcherHost : BackgroundService
{
    private readonly DeliveryManager _delivery;
    private readonly CaliberWebhooksOptions _options;
    private readonly ILogger<DispatcherHost> _logger;

    public DispatcherHost(DeliveryManager delivery, CaliberWebhooksOptions options, ILogger<DispatcherHost> logger)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _delivery = delivery;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.PollInterval, _options.TimeProvider);
        do
        {
            await DrainAsync(stoppingToken).ConfigureAwait(false);
        }
        while (await WaitForNextTickAsync(timer, stoppingToken).ConfigureAwait(false));
    }

    private async Task DrainAsync(CancellationToken ct)
    {
        try
        {
            // Keep delivering while batches come back full so a backlog clears without waiting for the next tick.
            while (!ct.IsCancellationRequested
                && await _delivery.DeliverDueAsync(ct).ConfigureAwait(false) == _options.BatchSize)
            {
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down — nothing to do.
        }
#pragma warning disable CA1031 // The dispatcher must survive a transient delivery/store failure and try again next tick.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogPollFailed(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Webhook delivery poll failed; retrying on the next tick.")]
    private partial void LogPollFailed(Exception exception);

    private static async Task<bool> WaitForNextTickAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
