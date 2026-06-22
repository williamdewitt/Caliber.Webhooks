---
title: Public API Reference (Target Surface)
description: The target public API surface of Caliber.Webhooks v1 — registration, the publisher, endpoint management, options, signing/verification helpers, and recovery. Provisional and pre-release.
status: pre-release
audience: [human, ai]
tags: [caliber-webhooks, reference, api, public-api]
related: [../getting-started.md, ../concepts/configuration.md, ../concepts/transactional-outbox.md]
updated: 2026-06-22
---

# Public API Reference (Target Surface)

> **Pre-release & provisional.** This is the **intended** public surface, drawn faithfully from the bedded-down design. Names and signatures may change until v1 ships; nothing here is implemented yet. Treat it as a design target, not API documentation for shipped code. The authoritative surface will be the `PublicAPI.*.txt` baselines once the code exists.

## Registration

```csharp
// Core entry point — DI registration on IServiceCollection.
IServiceCollection AddCaliberWebhooks(this IServiceCollection services);
IServiceCollection AddCaliberWebhooks(this IServiceCollection services, Action<CaliberWebhooksOptions> configure);
```

Store selection happens inside `configure` (one line determines durability and the guarantee tier — see [Transactional outbox](../concepts/transactional-outbox.md)):

```csharp
options.UseEntityFramework<AppDbContext>();   // transactional outbox (Postgres / SQLite via EF)
options.UseSqlite("caliber.db");              // standalone durable queue, single-node
// (default, no store configured)            // in-memory standalone queue (tests/dev)
```

Outbox-table registration on the host model:

```csharp
ModelBuilder AddCaliberOutbox(this ModelBuilder modelBuilder);
// roadmap: AddCaliberOutbox(autoFlush: true)
```

## Options — `CaliberWebhooksOptions`

The configurable surface; defaults in [Configuration](../concepts/configuration.md).

```csharp
int           MaxAttempts        { get; set; }   // default 12
RetrySchedule RetrySchedule      { get; set; }   // default RetrySchedule.Default
TimeSpan      LeaseDuration      { get; set; }   // default 60s
TimeSpan      HttpTimeout        { get; set; }   // default 10s; must be < LeaseDuration
TimeSpan      PollInterval       { get; set; }   // default 5s
int           BatchSize          { get; set; }   // default 50
int           MaxConcurrency     { get; set; }   // default 16
int           MaxPayloadBytes    { get; set; }   // default 262144 (256 KB)
bool          AllowInsecureHttp  { get; set; }   // default false
TimeSpan      TimestampTolerance { get; set; }   // default 5 min
// TimeProvider is injectable for testable timing (default TimeProvider.System).
```

## Publishing — `IWebhookPublisher`

```csharp
// Outbox mode: stages into the ambient AppDbContext; caller's SaveChanges commits.
// Standalone mode: persists immediately and returns.
Task PublishAsync(string eventType, object payload, CancellationToken ct = default);
```

## Endpoints — management & model

```csharp
Task<Endpoint> CreateAsync(Endpoint endpoint, CancellationToken ct = default);
Task           UpdateAsync(Endpoint endpoint, CancellationToken ct = default);
Task           DisableAsync(Guid endpointId, CancellationToken ct = default);

public sealed class Endpoint
{
    public Guid                 Id          { get; init; }
    public string?              TenantKey   { get; init; }   // opaque host scoping; never matched on
    public required string      Url         { get; init; }
    public required string      Secret      { get; init; }   // whsec_...
    public string[]?            EventTypes  { get; init; }   // null/empty ⇒ subscribe to all
    public bool                 Enabled     { get; init; }
    public string?              Description { get; init; }
}
```

Matching semantics: exact event-type match, or subscribe-all when `EventTypes` is null/empty. See [Endpoints & matching](../concepts/endpoints-and-matching.md).

## Secrets, signing & verification

```csharp
// Generate an endpoint secret (whsec_...).
static string WebhookSecret.Generate();

// Receiver side — verify an incoming webhook (HMAC-SHA256 over {id}.{timestamp}.{payload}).
static VerificationResult WebhookVerifier.Verify(
    IDictionary<string, string> headers, string rawBody, string secret);
// rejects stale webhook-timestamp values (replay protection)
```

Standard Webhooks headers produced/consumed: `webhook-id`, `webhook-timestamp`, `webhook-signature`. See [Security](../concepts/security.md).

## Delivery status & recovery

```csharp
public enum DeliveryStatus { Pending, Delivered, DeadLettered }

// Dead-letter read seam + programmatic replay (preserves the original webhook-id).
Task<IReadOnlyList<WebhookMessage>> ListDeadLetteredAsync(/* paging */ CancellationToken ct = default);
Task ReplayAsync(Guid messageId, CancellationToken ct = default);
```

## Retry schedule

```csharp
static RetrySchedule RetrySchedule.Default;                       // 5s·30s·2m·5m·10m·30m·1h·2h·4h·8h·12h + jitter
static RetrySchedule RetrySchedule.FromDelays(params TimeSpan[] delays);
```

## Observability

No API to call — the library emits via `ActivitySource`/`Meter` named **`"Caliber.Webhooks"`**. Wire it into your OTel pipeline:

```csharp
.AddSource("Caliber.Webhooks").AddMeter("Caliber.Webhooks")
```

See [Observability](../concepts/observability.md) for the full span and metric inventory.

## Roadmap surface (not in v1)

`MapCaliberReceiver("/webhooks")` receiver middleware, an admin HTTP API, a pluggable source-adapter model (`options.AddSource(...)`), ed25519 signing, secret rotation, and a `PublishAsync(evt, DbTransaction tx)` Dapper overload — all in the [roadmap](../design/roadmap.md).

## See also

- [Getting started](../getting-started.md) — these calls in context.
- [Configuration](../concepts/configuration.md) — the options surface in detail.
- [Transactional outbox](../concepts/transactional-outbox.md) — `PublishAsync` modes and `AddCaliberOutbox`.
