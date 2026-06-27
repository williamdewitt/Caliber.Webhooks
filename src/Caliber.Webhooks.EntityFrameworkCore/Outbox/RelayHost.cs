using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Caliber.Webhooks.EntityFrameworkCore.Outbox;

/// <summary>
/// The background relay (a Client, in iDesign terms): on a timer it drains the caller's outbox into the
/// <c>messages</c> store via <see cref="RelayProcessor"/>, clearing a backlog without waiting for the next
/// tick and surviving transient failures so the loop keeps running. It resolves the scoped
/// <typeparamref name="TContext"/> in a fresh scope per pass, since a <c>DbContext</c> is scoped and not
/// shareable across the long-lived host.
/// </summary>
/// <typeparam name="TContext">The caller's <c>DbContext</c>, carrying the <c>caliber_outbox</c> table.</typeparam>
internal sealed partial class RelayHost<TContext> : BackgroundService
    where TContext : DbContext
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RelayProcessor _processor;
    private readonly CaliberWebhooksOptions _options;
    private readonly ILogger<RelayHost<TContext>> _logger;

    public RelayHost(
        IServiceScopeFactory scopeFactory,
        RelayProcessor processor,
        CaliberWebhooksOptions options,
        ILogger<RelayHost<TContext>> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(processor);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeFactory = scopeFactory;
        _processor = processor;
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
            // Keep relaying while batches come back full so a backlog clears without waiting for the next tick.
            while (!ct.IsCancellationRequested
                && await RelayOnceAsync(ct).ConfigureAwait(false) == _options.BatchSize)
            {
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down — nothing to do.
        }
#pragma warning disable CA1031 // The relay must survive a transient store/DB failure and try again next tick.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogRelayFailed(ex);
        }
    }

    private async Task<int> RelayOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();
        return await _processor.RelayBatchAsync(context, ct).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Webhook outbox relay failed; retrying on the next tick.")]
    private partial void LogRelayFailed(Exception exception);

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
