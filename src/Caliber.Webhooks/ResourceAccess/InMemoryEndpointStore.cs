namespace Caliber.Webhooks;

/// <summary>
/// A non-durable, single-process <see cref="IEndpointStore"/> for tests and local development.
/// </summary>
internal sealed class InMemoryEndpointStore : IEndpointStore
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Endpoint> _byId = [];

    public Task UpsertAsync(Endpoint endpoint, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        lock (_gate)
        {
            _byId[endpoint.Id] = endpoint;
        }

        return Task.CompletedTask;
    }

    public Task DisableAsync(Guid endpointId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_byId.TryGetValue(endpointId, out var endpoint) && endpoint.Enabled)
            {
                _byId[endpointId] = WithEnabled(endpoint, enabled: false);
            }
        }

        return Task.CompletedTask;
    }

    public Task<Endpoint?> GetAsync(Guid endpointId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_byId.TryGetValue(endpointId, out var endpoint) ? endpoint : null);
        }
    }

    public Task<IReadOnlyList<Endpoint>> ListEnabledAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            IReadOnlyList<Endpoint> enabled = _byId.Values.Where(e => e.Enabled).ToList();
            return Task.FromResult(enabled);
        }
    }

    private static Endpoint WithEnabled(Endpoint endpoint, bool enabled) => new()
    {
        Id = endpoint.Id,
        TenantKey = endpoint.TenantKey,
        Url = endpoint.Url,
        Secret = endpoint.Secret,
        EventTypes = endpoint.EventTypes,
        Enabled = enabled,
        Description = endpoint.Description,
    };
}
