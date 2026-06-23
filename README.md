# Caliber.Webhooks

**Reliable, [Standard Webhooks](https://www.standardwebhooks.com/)-compliant webhook *delivery* for .NET — embedded in your own app, with zero infrastructure by default.**

> **Status: pre-release / in active development.** The design is complete and v1 is being built milestone by milestone (see [Roadmap](#roadmap)). It is **not yet published to NuGet** — watch/star to follow along.

[![CI](https://github.com/williamdewitt/Caliber.Webhooks/actions/workflows/ci.yml/badge.svg)](https://github.com/williamdewitt/Caliber.Webhooks/actions/workflows/ci.yml)
![Status: pre-release](https://img.shields.io/badge/status-pre--release-orange)
![Target: net8.0 | net10.0](https://img.shields.io/badge/target-net8.0%20%7C%20net10.0-512BD4)
![License: MIT](https://img.shields.io/badge/license-MIT-blue)

---

## The problem

Every platform eventually needs to notify *external* systems when something happens — `payment.succeeded`, `order.shipped`. The naïve version is one line — `httpClient.PostAsync(url, json)` — and it's a trap: the receiver is an untrusted server you don't control that can be down, slow, or malicious. Doing it *reliably* — durable delivery, retries with backoff, signing, idempotency, dead-lettering, SSRF protection — is a genuine distributed-systems problem.

In .NET specifically there's a gap. The serious webhook infrastructure is delivered as standalone **Rust/Go servers** you run as separate services. The .NET NuGet options are simplistic "just POST" senders or abandoned. There is no widely-adopted, Standard-Webhooks-compliant, production-grade library you embed in your own ASP.NET app.

**Caliber.Webhooks fills that gap — production-grade reliability as a library, no separate service to run.** *(Honest framing: the standalone services are excellent; this is the embeddable .NET option, not a claim that "nothing exists.")*

## What v1 delivers

- **Durable, never-inline delivery.** `PublishAsync` persists the event and returns; a background dispatcher delivers it. A crash mid-send never loses an event.
- **Transactional outbox.** Configure it with your `DbContext` and the event is staged in the *same* transaction as your business data — atomic enqueue, no dual-write.
- **At-least-once with a stable `webhook-id`.** Receivers can dedupe; exactly-once over HTTP is impossible and never promised.
- **Standard Webhooks signing** (HMAC-SHA256) — the same scheme major API providers use.
- **Retry with backoff + jitter** on a durable schedule (~24h), then **dead-letter** with programmatic replay.
- **Non-bypassable SSRF protection** — connect-time IP validation (defeats DNS-rebinding), HTTPS-only, no auto-redirect.
- **Cross-instance work-claiming** — run multiple dispatchers with no double-send; crash-safe leases.
- **OpenTelemetry first-class** — traces (with span links across the queue boundary) and metrics via BCL `ActivitySource`/`Meter`, no SDK dependency.
- **Zero infrastructure by default** — runs in-memory or on SQLite; scale to Postgres with a one-line swap.
- **Receiver helper** — verify incoming signatures symmetrically.

## Zero-infra by default, scale by swapping one line

```csharp
// In-memory (tests / dev) — no infrastructure:
builder.Services.AddCaliberWebhooks();

// Durable, single-node, no server to run:
builder.Services.AddCaliberWebhooks(o => o.UseSqlite("caliber.db"));

// Transactional outbox on your own DB (Postgres / SQLite via EF Core):
builder.Services.AddCaliberWebhooks(o => o.UseEntityFramework<AppDbContext>());
```

## Quick look (target API — subject to change)

```csharp
// Register a subscriber endpoint (per customer, via your API / dashboard)
await endpoints.CreateAsync(new Endpoint
{
    Url        = "https://acme.com/hooks",
    Secret     = WebhookSecret.Generate(),     // whsec_...
    EventTypes = ["order.shipped", "order.cancelled"],
});

// Publish — in outbox mode this stages the event in your AppDbContext;
// your SaveChangesAsync commits it atomically with your business data.
await webhooks.PublishAsync("order.shipped", new { orderId, trackingNo });
await db.SaveChangesAsync();   // one transaction: business data + outbox row
```

```csharp
// Receiver side — verify an incoming webhook
var result = WebhookVerifier.Verify(request.Headers, rawBody, secret);
```

## Where this sits

Caliber.Webhooks handles the **external edge** — pushing to third-party HTTP endpoints on the public internet, owning retries, backoff, signing, and SSRF defence per untrusted destination. It is deliberately **source-agnostic**: deliveries can be fed by a direct `PublishAsync` or drained from a transactional outbox (with further source adapters on the roadmap). It is **not** an internal message bus or event-streaming platform — it complements whatever you already run internally.

## Positioning

| Existing option | Form | How Caliber.Webhooks differs |
|---|---|---|
| Standalone webhook services | Self-hosted Rust/Go servers (often + SaaS) | In-process .NET library, no separate service; backed by *your* DB (transactional outbox) |
| Existing .NET libraries | Simplistic "just POST" senders, or unmaintained | Standard-Webhooks-first, with transactional outbox, SSRF hardening, and a receiver helper |

## Roadmap

**v1 (in progress)** — built in milestones:

| Milestone | Scope |
|---|---|
| **M0** | Production-ready shell — solution, multi-target, analyzers, CI, packaging |
| **M1** | Core delivery loop (in-memory) — dispatcher, signing, retry, dead-letter |
| **M2** | Durable + transactional outbox (SQLite + Postgres); cross-instance claiming |
| **M3** | Security hardening (SSRF, caps) + receiver verify helper |
| **M4** | Recovery (replay) + OpenTelemetry |
| **M5** | Docs, samples, ship v1 |

**v1.1+** — SQL Server & Redis stores · additional event sources · admin API + receiver middleware · ed25519 + secret rotation · per-endpoint rate-limiting / circuit-breaking.

## Tech stack

C# / **.NET 8 + .NET 10** (multi-target) · `IHttpClientFactory` · `BackgroundService` · `System.Text.Json` · OpenTelemetry via BCL `ActivitySource`/`Meter` · stores: in-memory + SQLite + Postgres (v1). Built with MinVer, SourceLink, deterministic builds, and Central Package Management; tested with xUnit v3, WireMock.Net, and Testcontainers.

## License

Licensed under the **MIT License** (LICENSE file added with the M0 shell).

---

*Caliber.Webhooks is part of the **Caliber** family of .NET developer tools.*
