using Caliber.Webhooks.EntityFrameworkCore.Configuration;
using Microsoft.EntityFrameworkCore;

namespace Caliber.Webhooks.EntityFrameworkCore;

/// <summary>
/// The EF Core <see cref="DbContext"/> that owns the Caliber.Webhooks <c>messages</c> and
/// <c>endpoints</c> schema. Caliber.Webhooks owns these tables; the host owns its own outbox table
/// (a later milestone). This foundation maps the M1 domain and creates the schema via ensure-created;
/// the <see cref="IMessageStore"/>/<see cref="IEndpointStore"/> implementations land in following slices.
/// </summary>
internal sealed class CaliberWebhooksDbContext : DbContext
{
    public CaliberWebhooksDbContext(DbContextOptions<CaliberWebhooksDbContext> options)
        : base(options)
    {
    }

    /// <summary>The delivery jobs: one event addressed to one endpoint.</summary>
    public DbSet<WebhookMessage> Messages => Set<WebhookMessage>();

    /// <summary>The registered delivery destinations.</summary>
    public DbSet<Endpoint> Endpoints => Set<Endpoint>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfiguration(new WebhookMessageConfiguration());
        modelBuilder.ApplyConfiguration(new EndpointConfiguration());
    }
}
