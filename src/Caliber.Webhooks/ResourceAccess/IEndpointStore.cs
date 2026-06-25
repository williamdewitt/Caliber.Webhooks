namespace Caliber.Webhooks;

/// <summary>
/// Atomic, verb-based access to the <c>endpoints</c> resource. Hides the store-backend volatility (V1).
/// </summary>
internal interface IEndpointStore
{
    /// <summary>Creates or replaces an endpoint.</summary>
    Task UpsertAsync(Endpoint endpoint, CancellationToken ct = default);

    /// <summary>Disables an endpoint so it stops matching new events; a no-op if it does not exist.</summary>
    Task DisableAsync(Guid endpointId, CancellationToken ct = default);

    /// <summary>Returns a single endpoint, or <see langword="null"/> when it does not exist.</summary>
    Task<Endpoint?> GetAsync(Guid endpointId, CancellationToken ct = default);

    /// <summary>Returns the enabled endpoints, the candidate set for matching.</summary>
    Task<IReadOnlyList<Endpoint>> ListEnabledAsync(CancellationToken ct = default);
}
