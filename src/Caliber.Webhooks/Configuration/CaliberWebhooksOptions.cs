using Microsoft.Extensions.DependencyInjection;

namespace Caliber.Webhooks;

/// <summary>
/// The configurable surface for Caliber.Webhooks, supplied through
/// <c>AddCaliberWebhooks(options =&gt; ...)</c>. Every default is production-safe; the registration
/// validates the combination and fails fast on a misconfiguration.
/// </summary>
public sealed class CaliberWebhooksOptions
{
    /// <summary>
    /// The maximum number of delivery attempts before a message is dead-lettered. Default <c>12</c>.
    /// </summary>
    public int MaxAttempts { get; set; } = 12;

    /// <summary>
    /// The schedule that spaces out retries. Default <see cref="RetrySchedule.Default"/>.
    /// </summary>
    public RetrySchedule RetrySchedule { get; set; } = RetrySchedule.Default;

    /// <summary>
    /// How long a claimed message is leased to a dispatcher before it becomes reclaimable. Default
    /// <c>60</c> seconds.
    /// </summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// The per-attempt HTTP timeout. Kept strictly below <see cref="LeaseDuration"/> so a delivery
    /// finishes or times out before its lease can lapse. Default <c>10</c> seconds.
    /// </summary>
    public TimeSpan HttpTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How often the dispatcher polls the store for due messages. Default <c>5</c> seconds.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The maximum number of messages claimed per poll. Default <c>50</c>.
    /// </summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>
    /// The maximum number of deliveries attempted in parallel. Default <c>16</c>.
    /// </summary>
    public int MaxConcurrency { get; set; } = 16;

    /// <summary>
    /// The maximum outbound payload size, in bytes. Default <c>262144</c> (256 KB).
    /// </summary>
    public int MaxPayloadBytes { get; set; } = 262144;

    /// <summary>
    /// Whether plain-HTTP endpoint URLs are permitted. Default <see langword="false"/> — HTTPS only.
    /// </summary>
    public bool AllowInsecureHttp { get; set; }

    /// <summary>
    /// How much clock skew the receiver helper tolerates when checking the webhook timestamp. Default
    /// <c>5</c> minutes.
    /// </summary>
    public TimeSpan TimestampTolerance { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The time source used for all scheduling and leasing, injectable so timing is testable. Default
    /// is the system clock (<see cref="System.TimeProvider"/>).
    /// </summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    /// <summary>
    /// When set by a storage-provider package (e.g. <c>UseSqlite</c>), replaces the default in-memory
    /// store registration inside <c>AddCaliberWebhooks</c>. Internal so only Caliber packages can touch it.
    /// </summary>
    internal Action<IServiceCollection>? StoreConfigurator { get; set; }
}
