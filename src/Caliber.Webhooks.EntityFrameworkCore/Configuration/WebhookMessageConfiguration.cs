using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Caliber.Webhooks.EntityFrameworkCore.Configuration;

/// <summary>
/// Maps <see cref="WebhookMessage"/> to the <c>messages</c> table. The schema mirrors the M1 domain
/// and carries the two indexes the delivery loop relies on: a unique <c>(event_id, endpoint_id)</c>
/// for idempotent fan-out, and <c>(status, next_attempt_at)</c> for the due-message claim query.
/// </summary>
internal sealed class WebhookMessageConfiguration : IEntityTypeConfiguration<WebhookMessage>
{
    public void Configure(EntityTypeBuilder<WebhookMessage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("messages");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(m => m.EventId).HasColumnName("event_id");
        builder.Property(m => m.EndpointId).HasColumnName("endpoint_id");
        builder.Property(m => m.EventType).HasColumnName("event_type");
        builder.Property(m => m.Payload).HasColumnName("payload");
        builder.Property(m => m.CreatedAt).HasColumnName("created_at");
        builder.Property(m => m.Status).HasColumnName("status");
        builder.Property(m => m.AttemptCount).HasColumnName("attempt_count");
        builder.Property(m => m.NextAttemptAt).HasColumnName("next_attempt_at");
        builder.Property(m => m.Owner).HasColumnName("owner");
        builder.Property(m => m.LeaseUntil).HasColumnName("lease_until");
        builder.Property(m => m.LastError).HasColumnName("last_error");

        // Fan-out is idempotent on (event_id, endpoint_id): a relay or publish retry never double-enqueues.
        builder.HasIndex(m => new { m.EventId, m.EndpointId }).IsUnique();

        // The claim query selects due, pending messages ordered by next_attempt_at.
        builder.HasIndex(m => new { m.Status, m.NextAttemptAt });
    }
}
