using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Caliber.Webhooks.EntityFrameworkCore.Configuration;

/// <summary>
/// Maps <see cref="Endpoint"/> to the <c>endpoints</c> table. The subscribed event types are stored as
/// a JSON array in a single column; <see langword="null"/> means "subscribe to all" and round-trips as
/// SQL NULL (distinct from an empty array). The <c>enabled</c> index backs the matching candidate query.
/// </summary>
internal sealed class EndpointConfiguration : IEntityTypeConfiguration<Endpoint>
{
    private static readonly ValueComparer<IReadOnlyList<string>?> EventTypesComparer = new(
        (a, b) => ReferenceEquals(a, b) || (a != null && b != null && a.SequenceEqual(b, StringComparer.Ordinal)),
        v => v == null
            ? 0
            : v.Aggregate(0, (hash, item) => HashCode.Combine(hash, StringComparer.Ordinal.GetHashCode(item))),
        v => v == null ? null : v.ToList());

    public void Configure(EntityTypeBuilder<Endpoint> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("endpoints");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(e => e.TenantKey).HasColumnName("tenant_key");
        builder.Property(e => e.Url).HasColumnName("url");
        builder.Property(e => e.Secret).HasColumnName("secret");
        builder.Property(e => e.Enabled).HasColumnName("enabled");
        builder.Property(e => e.Description).HasColumnName("description");

        builder.Property(e => e.EventTypes)
            .HasColumnName("subscribed_event_types")
            .HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
                v => v == null ? null : JsonSerializer.Deserialize<List<string>>(v, JsonSerializerOptions.Default),
                EventTypesComparer);

        // The matching candidate set is the enabled endpoints.
        builder.HasIndex(e => e.Enabled);
    }
}
