using Caliber.Webhooks.EntityFrameworkCore.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Caliber.Webhooks;

/// <summary>
/// Model-builder extensions that put Caliber's transactional outbox on the caller's <c>DbContext</c>.
/// </summary>
public static class CaliberOutboxModelBuilderExtensions
{
    /// <summary>
    /// Registers Caliber's thin <c>caliber_outbox</c> table on the caller's model. Call this from
    /// <c>OnModelCreating</c> so the outbox migrates with your own <c>dotnet ef</c> workflow:
    /// <code>
    /// protected override void OnModelCreating(ModelBuilder b) => b.AddCaliberOutbox();
    /// </code>
    /// In outbox mode, <c>PublishAsync</c> stages a row into this table within the same change-tracker as
    /// your business writes, so your <c>SaveChangesAsync</c> commits both atomically.
    /// </summary>
    /// <param name="modelBuilder">The model builder being configured.</param>
    /// <returns><paramref name="modelBuilder"/> for chaining.</returns>
    public static ModelBuilder AddCaliberOutbox(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfiguration(new CaliberOutboxMessageConfiguration());
        return modelBuilder;
    }
}
