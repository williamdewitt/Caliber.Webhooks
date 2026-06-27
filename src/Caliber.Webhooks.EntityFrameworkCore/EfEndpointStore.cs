using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Caliber.Webhooks.EntityFrameworkCore;

/// <summary>
/// A durable <see cref="IEndpointStore"/> backed by EF Core / SQLite. Upsert is a single atomic
/// <c>INSERT … ON CONFLICT (id) DO UPDATE</c>; reads use EF LINQ queries with <c>AsNoTracking</c>.
/// A fresh context is taken per operation — DbContext is not thread-safe and must not be shared.
/// </summary>
internal sealed class EfEndpointStore : IEndpointStore
{
    private readonly IDbContextFactory<CaliberWebhooksDbContext> _contextFactory;

    public EfEndpointStore(IDbContextFactory<CaliberWebhooksDbContext> contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        _contextFactory = contextFactory;
    }

    public async Task UpsertAsync(Endpoint endpoint, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        var context = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await using var contextScope = context.ConfigureAwait(false);

        var connection = context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync(ct).ConfigureAwait(false);
        using var command = connection.CreateCommand();

        // ON CONFLICT (id) upsert: replaces every mutable column when the primary key already exists.
        command.CommandText =
            """
            INSERT INTO endpoints (id, tenant_key, url, secret, subscribed_event_types, enabled, description)
            VALUES ($id, $tenant_key, $url, $secret, $subscribed_event_types, $enabled, $description)
            ON CONFLICT (id) DO UPDATE SET
                tenant_key            = excluded.tenant_key,
                url                   = excluded.url,
                secret                = excluded.secret,
                subscribed_event_types = excluded.subscribed_event_types,
                enabled               = excluded.enabled,
                description           = excluded.description;
            """;

        // null EventTypes = subscribe-all → SQL NULL; a list → JSON array. Matches EndpointConfiguration.
        var eventTypesJson = endpoint.EventTypes is null
            ? null
            : JsonSerializer.Serialize(endpoint.EventTypes, JsonSerializerOptions.Default);

        AddParameter(command, "$id", endpoint.Id);
        AddParameter(command, "$tenant_key", endpoint.TenantKey);
        AddParameter(command, "$url", endpoint.Url);
        AddParameter(command, "$secret", endpoint.Secret);
        AddParameter(command, "$subscribed_event_types", eventTypesJson);
        AddParameter(command, "$enabled", endpoint.Enabled);
        AddParameter(command, "$description", endpoint.Description);

        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DisableAsync(Guid endpointId, CancellationToken ct = default)
    {
        var context = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await using var contextScope = context.ConfigureAwait(false);

        await context.Endpoints
            .Where(e => e.Id == endpointId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Enabled, false), ct)
            .ConfigureAwait(false);
    }

    public async Task<Endpoint?> GetAsync(Guid endpointId, CancellationToken ct = default)
    {
        var context = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await using var contextScope = context.ConfigureAwait(false);

        return await context.Endpoints
            .AsNoTracking()
            .SingleOrDefaultAsync(e => e.Id == endpointId, ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Endpoint>> ListEnabledAsync(CancellationToken ct = default)
    {
        var context = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await using var contextScope = context.ConfigureAwait(false);

        return await context.Endpoints
            .AsNoTracking()
            .Where(e => e.Enabled)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
