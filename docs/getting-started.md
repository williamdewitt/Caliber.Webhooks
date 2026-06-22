---
title: Getting Started
description: Install Caliber.Webhooks and deliver your first webhook with zero infrastructure, then scale up to durable storage or a transactional outbox by swapping one line.
status: pre-release
audience: [human, ai]
tags: [caliber-webhooks, getting-started, quickstart, dotnet]
related: [./overview.md, ./concepts/transactional-outbox.md, ./concepts/configuration.md, ./reference/public-api.md]
updated: 2026-06-22
---

# Getting Started

> **Pre-release.** Caliber.Webhooks is not yet published to NuGet, and the API below is the **target** surface — it reflects the bedded-down design and is subject to change until v1 ships. This page will become runnable when the [M1 milestone](./design/roadmap.md) lands; until then it documents the intended developer experience.

## Install

When v1 publishes, the core package (with the in-memory store) is all you need to start:

```bash
dotnet add package Caliber.Webhooks
```

Durable stores are additive packages, so a side-project never pulls a driver it doesn't use:

```bash
dotnet add package Caliber.Webhooks.Sqlite              # durable, single-node, no server
dotnet add package Caliber.Webhooks.EntityFrameworkCore # Postgres (+ SQL Server on the roadmap); transactional outbox
```

## Zero infrastructure by default, scale by swapping one line

The on-ramp is the whole point: `AddCaliberWebhooks()` runs in-memory with no infrastructure. Moving to durable storage — and then to a transactional outbox on your own database — is a one-line change at registration. **The guarantee tier is a property of that one line, never a silent downgrade.**

```csharp
// In-memory (tests / dev) — no infrastructure:
builder.Services.AddCaliberWebhooks();

// Durable, single-node, no server to run:
builder.Services.AddCaliberWebhooks(o => o.UseSqlite("caliber.db"));

// Transactional outbox on your own DB (Postgres / SQLite via EF Core):
builder.Services.AddCaliberWebhooks(o => o.UseEntityFramework<AppDbContext>());
```

See [Storage & work-claiming](./concepts/storage-and-work-claiming.md) for what each tier guarantees, and [Transactional outbox](./concepts/transactional-outbox.md) for the EF Core path.

## 1. Register a subscriber endpoint

Endpoints are typically created per customer, via your own API or dashboard:

```csharp
await endpoints.CreateAsync(new Endpoint
{
    Url        = "https://acme.com/hooks",
    Secret     = WebhookSecret.Generate(),          // whsec_...
    EventTypes = ["order.shipped", "order.cancelled"],
});
```

Leaving `EventTypes` unset subscribes the endpoint to **all** event types — see [Endpoints & matching](./concepts/endpoints-and-matching.md).

## 2. Publish an event

Publishing never delivers inline: the event is persisted first, and a background dispatcher delivers it (durable, signed, retried). In outbox mode, `PublishAsync` stages the event into your `AppDbContext` so your own `SaveChangesAsync` commits it atomically with your business data:

```csharp
// In EF/outbox mode this stages the event in your AppDbContext;
// your SaveChangesAsync commits it atomically with your business data. A relay
// then forwards it to Caliber.Webhooks' delivery queue and the dispatcher delivers it.
await webhooks.PublishAsync("order.shipped", new { orderId, trackingNo });
await db.SaveChangesAsync();   // one transaction: business data + outbox row
```

In standalone mode (the library's own store), `PublishAsync` persists immediately and returns — no `SaveChanges` needed. See [Delivery semantics](./concepts/delivery-semantics.md) for the guarantees, and [Transactional outbox](./concepts/transactional-outbox.md) for how the two modes differ.

## 3. Receive and verify (the other side)

If your .NET app is on the *receiving* end, a symmetric helper verifies the signature and timestamp:

```csharp
var result = WebhookVerifier.Verify(request.Headers, rawBody, secret);
```

Verification recomputes the HMAC-SHA256 signature over `{id}.{timestamp}.{payload}`, compares it in constant time, and rejects stale timestamps (replay protection). See [Security](./concepts/security.md).

## Configure for production

Every knob is configurable; the defaults are safe for production and suit the zero-infra on-ramp:

```csharp
builder.Services.AddCaliberWebhooks(options =>
{
    options.UseEntityFramework<AppDbContext>();
    options.RetrySchedule = RetrySchedule.Default;  // backoff over ~27.6h
    options.MaxAttempts   = 12;
});
```

The full option surface, defaults, and the default retry schedule are in [Configuration](./concepts/configuration.md).

## Next steps

- [Delivery semantics](./concepts/delivery-semantics.md) — what "reliable" actually guarantees.
- [Transactional outbox](./concepts/transactional-outbox.md) — atomic enqueue with your business data.
- [Configuration](./concepts/configuration.md) — options and defaults.
- [Public API](./reference/public-api.md) — the target surface at a glance.
