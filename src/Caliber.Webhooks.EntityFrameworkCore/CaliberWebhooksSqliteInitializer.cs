using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace Caliber.Webhooks.EntityFrameworkCore;

/// <summary>
/// Ensures the Caliber.Webhooks schema exists before the dispatcher begins polling. Runs as a plain
/// <see cref="IHostedService"/> (not a <see cref="BackgroundService"/>) so <see cref="StartAsync"/>
/// is awaited to completion and the schema is ready before the next hosted service starts.
/// </summary>
internal sealed class CaliberWebhooksSqliteInitializer : IHostedService
{
    private readonly IDbContextFactory<CaliberWebhooksDbContext> _factory;

    public CaliberWebhooksSqliteInitializer(IDbContextFactory<CaliberWebhooksDbContext> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var context = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var contextScope = context.ConfigureAwait(false);
        await context.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
