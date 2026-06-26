namespace Caliber.Webhooks;

/// <summary>
/// Owns the endpoint lifecycle (UC-5): create, update, and disable. Persistence is delegated to the
/// endpoint store; this manager assigns ids and is the seam for future validation.
/// </summary>
internal sealed class SubscriptionManager : IWebhookEndpoints
{
    private readonly IEndpointStore _endpoints;

    public SubscriptionManager(IEndpointStore endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        _endpoints = endpoints;
    }

    public async Task<Endpoint> CreateAsync(Endpoint endpoint, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        var stored = endpoint.Id == Guid.Empty ? WithId(endpoint, Guid.NewGuid()) : endpoint;
        await _endpoints.UpsertAsync(stored, ct).ConfigureAwait(false);
        return stored;
    }

    public Task UpdateAsync(Endpoint endpoint, CancellationToken ct = default)
    {
        // Stryker disable once all : equivalent — the endpoint store guards a null endpoint identically on Upsert.
        ArgumentNullException.ThrowIfNull(endpoint);
        return _endpoints.UpsertAsync(endpoint, ct);
    }

    public Task DisableAsync(Guid endpointId, CancellationToken ct = default)
        => _endpoints.DisableAsync(endpointId, ct);

    private static Endpoint WithId(Endpoint endpoint, Guid id) => new()
    {
        Id = id,
        TenantKey = endpoint.TenantKey,
        Url = endpoint.Url,
        Secret = endpoint.Secret,
        EventTypes = endpoint.EventTypes,
        Enabled = endpoint.Enabled,
        Description = endpoint.Description,
    };
}
