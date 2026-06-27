using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Caliber.Webhooks.EntityFrameworkCore.Outbox;

/// <summary>
/// Maps <see cref="CaliberOutboxMessage"/> to the <c>caliber_outbox</c> table on the caller's model.
/// The columns are snake_case to match Caliber's durable schema contract; the table is intentionally
/// thin so the caller's <c>DbContext</c> carries only a stable outbox, while Caliber owns and evolves
/// the <c>messages</c>/<c>endpoints</c> schema independently.
/// </summary>
internal sealed class CaliberOutboxMessageConfiguration : IEntityTypeConfiguration<CaliberOutboxMessage>
{
    public void Configure(EntityTypeBuilder<CaliberOutboxMessage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("caliber_outbox");

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(o => o.EventType).HasColumnName("event_type");
        builder.Property(o => o.Payload).HasColumnName("payload");
        builder.Property(o => o.TraceContext).HasColumnName("trace_context");

        // Stored as UTC ticks (INTEGER) so the relay's ORDER BY translates on every provider — SQLite
        // cannot ORDER BY a DateTimeOffset column. CreatedAt is always UTC, so the offset carries no data.
        builder.Property(o => o.CreatedAt)
            .HasColumnName("created_at")
            .HasConversion(
                v => v.UtcTicks,
                v => new DateTimeOffset(v, TimeSpan.Zero));

        // The relay drains oldest-first.
        builder.HasIndex(o => o.CreatedAt);
    }
}
