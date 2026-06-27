---
title: Transactional Outbox
description: How events get into Caliber.Webhooks — the single PublishAsync enqueue API, the ambient-DbContext outbox, the idempotent relay that fans out into the messages store, and the standalone-queue alternative.
status: pre-release
audience: [human, ai]
tags: [caliber-webhooks, outbox, transactional, enqueue, relay, fan-out, ef-core]
related: [./delivery-semantics.md, ./endpoints-and-matching.md, ./storage-and-work-claiming.md, ../design/decisions.md]
updated: 2026-06-22
---

# Transactional Outbox

This is how an event gets *into* the store, upstream of [work-claiming](./storage-and-work-claiming.md). There is **one** `PublishAsync`; its guarantee is determined by the store you configure, and that guarantee is always visible at the registration line — never a silent downgrade.

## Outbox mode — your `DbContext` *is* the outbox

Configure Caliber.Webhooks with the caller's `DbContext` and register its thin outbox table on your model:

```csharp
builder.Services.AddCaliberWebhooks(o => o.UseEntityFramework<AppDbContext>());

protected override void OnModelCreating(ModelBuilder b) => b.AddCaliberOutbox();
// ...
await _webhooks.PublishAsync("order.shipped", new { orderId, trackingNo });
await db.SaveChangesAsync();   // ONE transaction: business data + outbox row
```

`PublishAsync` **stages** a thin outbox row into the **same scoped `AppDbContext`** the caller already uses — it stages but does **not** commit. The caller's own `SaveChangesAsync()` commits that row **atomically with the business data**. This is a true transactional outbox with zero ceremony: no dual-write, no "did the webhook enqueue but the order roll back?" race. It is the EF-idiomatic path and covers the v1 store lineup (SQLite default + Postgres via EF Core).

> **No magic `SaveChanges`.** The library never auto-commits and ships **no** `SaveChanges` interceptor in v1 — the contract is explicit and auditable: *you staged it, your `SaveChanges` commits it.* An opt-in auto-flush interceptor (`AddCaliberOutbox(autoFlush: true)`) is on the [roadmap](../design/roadmap.md), off by default.

## The relay — outbox → `messages`, idempotently

A background **relay** drains committed outbox rows into Caliber.Webhooks' own `messages` store, **fanning out** each event to its matching endpoints (the endpoint set as of relay time); the dispatcher then claims from `messages` exactly as for any other store.

- The relay **inserts the fan-out idempotently** — one `messages` row per matching endpoint, unique on `(event_id, endpoint_id)` — and then **deletes the drained outbox rows**.
- Crash-safety rests on that idempotency, **keyed on the stable source event id** (the outbox row id): if the relay inserts the deliveries but crashes before deleting the outbox row, the retry re-fans-out, the inserts no-op against the unique index, and the rows clear on the next pass — never double-enqueued, never lost.

The relay was chosen over claiming directly from the shared table so your `AppDbContext` carries only a **thin, stable outbox table**, while Caliber.Webhooks **owns and evolves** the `messages`/`endpoints` schema independently. The cost is one extra hop plus the relay worker — bought back by clean schema separation. Fan-out and the two ids involved are detailed in [Endpoints & matching](./endpoints-and-matching.md).

## Standalone mode — durable queue, no shared transaction

Configure Caliber.Webhooks with its **own** store and `PublishAsync` **persists immediately and returns**:

```csharp
builder.Services.AddCaliberWebhooks(o => o.UseSqlite("caliber.db"));   // or Redis / default in-memory
await _webhooks.PublishAsync("order.shipped", new { orderId, trackingNo });
```

This is durable (on SQLite/Redis) but **not** atomic-with-your-business-DB — a standalone queue. It is the only mode Redis/in-memory offer, because no shared business transaction exists. There is **no outbox and no relay** here: `PublishAsync` matches and fans out, writing `messages` rows directly.

## The guarantee tier surfaces at the swap line

There is **one** `PublishAsync`; its semantics come from the store you configure. So *"scale by swapping one line"* stays true **and** the change is never silent:

- Register a shared `AppDbContext` ⇒ outbox/stage (atomic with your data).
- Register the library's own store ⇒ standalone/immediate (durable, not atomic).

You cannot accidentally keep atomic code while moving to Redis, because Redis was never holding your `DbContext` — the config line you swap is exactly where the guarantee changes.

> **Rejected alternatives.** A single uniform call with the weaker guarantee merely *documented* (a silent downgrade on a SQLite→Redis swap) — rejected, it violates honest positioning. A capability-typed `IOutboxPublisher` only relational stores expose — maximally honest but two API shapes and more friction than "visible at the swap line" buys back.

## Scope notes

- **Single `DbContext` in v1.** Exactly one registered outbox host via `UseEntityFramework<TContext>()`. Multiple contexts and a raw-ADO/Dapper `PublishAsync(evt, DbTransaction tx)` overload are on the [roadmap](../design/roadmap.md) (the abstraction already anticipates them).
- **Wakeup is poll-based in v1.** Both the relay (draining the outbox) and the dispatcher (claiming `messages`) poll. Low-latency wakeup (Postgres `LISTEN/NOTIFY`, or a post-`SaveChanges` signal) is a named roadmap seam.

## See also

- [Endpoints & matching](./endpoints-and-matching.md) — the fan-out the relay performs.
- [Storage & work-claiming](./storage-and-work-claiming.md) — what happens once a `messages` row exists.
- [Delivery semantics](./delivery-semantics.md) — durability and the source-event vs. `webhook-id` distinction.
