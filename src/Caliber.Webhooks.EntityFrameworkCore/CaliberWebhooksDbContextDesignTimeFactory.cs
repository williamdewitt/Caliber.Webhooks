using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Caliber.Webhooks.EntityFrameworkCore;

/// <summary>
/// Design-time factory used only by <c>dotnet ef</c> to generate and script Caliber's shipped Postgres
/// migrations. It configures the Npgsql provider so the migrations are emitted as Postgres DDL; the
/// connection string is a placeholder (migration generation is offline and never connects).
/// </summary>
internal sealed class CaliberWebhooksDbContextDesignTimeFactory
    : IDesignTimeDbContextFactory<CaliberWebhooksDbContext>
{
    public CaliberWebhooksDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<CaliberWebhooksDbContext>();
        CaliberNpgsqlConfiguration.Apply(
            builder, "Host=localhost;Database=caliber_design;Username=caliber;Password=caliber");
        return new CaliberWebhooksDbContext(builder.Options);
    }
}
