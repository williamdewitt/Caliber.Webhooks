namespace Caliber.Webhooks;

/// <summary>
/// Creates and manages delivery endpoints (use case UC-5). Resolve this from dependency injection in
/// your admin or onboarding code.
/// </summary>
public interface IWebhookEndpoints
{
    /// <summary>
    /// Registers a new endpoint, assigning an id when one is not supplied.
    /// </summary>
    /// <returns>The stored endpoint, including its id.</returns>
    Task<Endpoint> CreateAsync(Endpoint endpoint, CancellationToken ct = default);

    /// <summary>
    /// Replaces an existing endpoint's configuration.
    /// </summary>
    Task UpdateAsync(Endpoint endpoint, CancellationToken ct = default);

    /// <summary>
    /// Disables an endpoint so it stops matching new events.
    /// </summary>
    Task DisableAsync(Guid endpointId, CancellationToken ct = default);
}
