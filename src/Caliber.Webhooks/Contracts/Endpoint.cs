namespace Caliber.Webhooks;

/// <summary>
/// A registered delivery destination: a host- or tenant-owned HTTPS URL, its signing secret, and the
/// set of event types it subscribes to. Endpoints are created and managed by the host application
/// (use case UC-5).
/// </summary>
public sealed class Endpoint
{
    /// <summary>
    /// The unique endpoint identifier.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// An optional, opaque grouping key the host sets and queries by — for example a customer id in a
    /// multi-tenant application. Caliber.Webhooks attaches no semantics to it and never matches on it.
    /// </summary>
    public string? TenantKey { get; init; }

    /// <summary>
    /// The absolute HTTPS URL that deliveries are POSTed to.
    /// </summary>
    public required string Url { get; init; }

    /// <summary>
    /// The endpoint signing secret (a <c>whsec_</c>-prefixed value). Every delivery to this endpoint
    /// is signed with it so the receiver can verify authenticity.
    /// </summary>
    public required string Secret { get; init; }

    /// <summary>
    /// The exact event types this endpoint subscribes to. <see langword="null"/> or empty means
    /// subscribe to <em>all</em> event types. Wildcards and payload filters are out of scope in v1.
    /// </summary>
    public IReadOnlyList<string>? EventTypes { get; init; }

    /// <summary>
    /// Whether the endpoint is active. A disabled endpoint stops matching new events immediately;
    /// messages already queued for it continue to drain. Defaults to <see langword="true"/>.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// An optional, human-readable description for operators.
    /// </summary>
    public string? Description { get; init; }
}
