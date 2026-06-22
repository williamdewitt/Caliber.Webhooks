---
title: Delivery Semantics & Reliability
description: What "reliable delivery" guarantees in Caliber.Webhooks — durable never-inline enqueue, at-least-once with a stable webhook-id, retry with backoff and jitter, dead-lettering, and programmatic replay.
status: pre-release
audience: [human, ai]
tags: [caliber-webhooks, delivery, reliability, idempotency, retry, dead-letter, replay]
related: [./storage-and-work-claiming.md, ./transactional-outbox.md, ./configuration.md, ../design/use-cases.md]
updated: 2026-06-22
---

# Delivery Semantics & Reliability

This is the heart of the library's promise. Caliber.Webhooks delivers webhooks **reliably to untrusted endpoints, surviving failure** — and it states its guarantees precisely rather than over-claiming.

## Never deliver inline

`PublishAsync` **persists the event and returns** — it never makes the outbound HTTP call on the caller's thread. A background dispatcher delivers it later. Crashing mid-send must never lose an event, so the event is durable *before* any delivery is attempted.

## At-least-once, with a stable `webhook-id`

Because we retry, a receiver may see the same delivery more than once. We do not pretend otherwise:

- **At-least-once delivery** is the contract. **Exactly-once over HTTP to arbitrary receivers is impossible**, and Caliber.Webhooks never promises it.
- Every delivery carries a **stable `webhook-id`** (the Standard Webhooks header) so the receiver can **dedupe**. The `webhook-id` is the per-endpoint message id and is preserved across retries *and* across replay.

This contract is documented explicitly so integrators build idempotent receivers from day one.

## One logical event → N deliveries

A producer publishes **one logical event**; Caliber.Webhooks owns the fan-out to matching endpoints (the producer never enumerates them). Two deliberately distinct ids keep this correct:

- **source event id** — the relay's idempotency key (a relay retry never re-fans-out).
- **per-endpoint `webhook-id`** — exactly one per `(event, endpoint)` pair; this is what the receiver dedupes on.

The full fan-out and idempotency model lives in [Endpoints & matching](./endpoints-and-matching.md) and [Transactional outbox](./transactional-outbox.md).

## Retry with backoff and jitter

On a failed attempt the dispatcher computes the next attempt time from a **durable, predictable schedule** with full jitter, and reschedules the message — it does not block or hold a connection open.

The default schedule is an explicit table (operators value predictability for webhooks): `5s · 30s · 2m · 5m · 10m · 30m · 1h · 2h · 4h · 8h · 12h` — 11 inter-attempt delays giving **12 attempts over ≈27.6h**, each delay jittered ±20%. It is fully configurable; see [Configuration → Retry schedule](./configuration.md#default-retry-schedule).

## Dead-letter and recovery

After `MaxAttempts` a message moves to the terminal **`DeadLettered`** status. A delivery library that can dead-letter but offers no way to *see why* or *recover* is not complete — so v1 ships a genuine recovery story:

- **Queryable failure state** — `attempt_count`, `last_error`, and `next_attempt_at` live on each message row, so current state and the *last* failure are directly queryable.
- **Per-attempt history via OpenTelemetry** — each attempt emits a `webhook.deliver` span and a `webhooks.delivery.attempts` metric (tagged with outcome and HTTP status), so operators get full per-attempt history *where they already look*, with no extra storage. See [Observability](./observability.md).
- **Dead-letter read seam** — a host can list dead-lettered messages and surface or recover them programmatically.
- **Programmatic replay** — `ReplayAsync(messageId)` re-arms a `DeadLettered` message to `Pending` with `next_attempt_at = now`. It is idempotent and safe under the existing claim model, and it **preserves the original `webhook-id`**, so a receiver that already got the first attempt can still dedupe.

A persisted per-attempt `attempts` table, an admin HTTP API, bulk/auto replay, and replay of already-*Delivered* messages are deferred to the [roadmap](../design/roadmap.md) — all additive.

## Crash safety across instances

Multiple dispatcher instances can run with **no double-send**, and a dispatcher that crashes mid-delivery does not strand its messages: claims are leased, and an **expired lease is automatically reclaimable**. The lease/crash-recovery invariant (per-attempt HTTP timeout kept strictly below the claim lease) is detailed in [Storage & work-claiming](./storage-and-work-claiming.md#lease--crash-recovery-invariant).

## The guarantees, summarised

| Guarantee | What it means |
|---|---|
| **Durable enqueue** | The event is persisted before any delivery; a crash never loses it. |
| **At-least-once** | Receivers may see duplicates; a stable `webhook-id` lets them dedupe. Exactly-once is not promised. |
| **Ordered retries** | Predictable backoff schedule + jitter; per-attempt timeout below the lease. |
| **Dead-letter + replay** | Terminal status after max attempts; programmatic replay preserves the `webhook-id`. |
| **No double-send** | Cross-instance work-claiming with crash-safe leases. |

## See also

- [Storage & work-claiming](./storage-and-work-claiming.md) — how claiming, leases, and crash recovery work per store.
- [Transactional outbox](./transactional-outbox.md) — durable, atomic enqueue with your business data.
- [Configuration](./configuration.md) — retry schedule, attempt counts, timeouts.
- [Use cases](../design/use-cases.md) — UC-1 (publish) through UC-4 (reclaim).
