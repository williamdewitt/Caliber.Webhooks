---
title: Storage & Work-Claiming
description: The Caliber.Webhooks store lineup and guarantee tiers, the cross-instance work-claiming model (claim, lease, owner), per-provider claim mechanisms, and the lease/crash-recovery invariant.
status: pre-release
audience: [human, ai]
tags: [caliber-webhooks, storage, work-claiming, lease, crash-recovery, postgres, sqlite]
related: [./delivery-semantics.md, ./transactional-outbox.md, ./configuration.md, ../design/decisions.md]
updated: 2026-06-28
---

# Storage & Work-Claiming

**Zero-infra by default, scale up by swapping one line.** `AddCaliberWebhooks()` runs in-memory; `.UseSqlite("caliber.db")` adds durability with no server; moving to Postgres is a one-line store swap with the same API and guarantees. The low-barrier on-ramp *is* the adoption strategy.

## Store lineup

| Store | Audience | Claim mechanism | Multi-instance | Transactional outbox | Ships |
|---|---|---|---|---|---|
| In-memory | tests / demos | in-process queue | ✗ (1 proc, non-durable) | ✗ | v1 |
| **SQLite** | **side-project default** | serialized conditional `UPDATE` (WAL) | single-node | ✓ | v1 |
| Postgres | production | `FOR UPDATE SKIP LOCKED` | ✓ | ✓ | v1 |
| SQL Server | .NET enterprise | `READPAST` / `UPDLOCK` | ✓ | ✓ | roadmap (v1.1) |
| Redis | throughput / decoupled | Streams + consumer groups (+ ZSET) | ✓ | ✗ | roadmap (v1.1) |

**v1 ships** in-memory + SQLite + Postgres — a complete zero-to-production story. SQL Server and Redis are **additive** (the abstraction already supports them); see the [roadmap](../design/roadmap.md).

## Guarantee tiers

- **Relational stores** (SQLite / Postgres / SQL Server) support the [transactional outbox](./transactional-outbox.md): `PublishAsync` stages a thin outbox row in the caller's DB transaction, which a relay forwards to the delivery queue — atomic with your business data.
- **Redis / in-memory** are standalone queues: durable (Redis) or not (in-memory), but with **no atomic-with-your-business-DB guarantee**.

The active tier is determined by the store you configure and is surfaced at that one registration line — never a silent property of an identical-looking call. See [Transactional outbox → the guarantee surfaces at the swap line](./transactional-outbox.md#the-guarantee-tier-surfaces-at-the-swap-line).

## Work-claiming

The goal: multiple dispatcher instances pull due messages with **no double-send**, surviving crashes.

### The `messages` data model

`messages`: `id (= Standard-Webhooks webhook-id), event_id (source event), endpoint_id, event_type, payload, status (Pending | Delivered | DeadLettered), attempt_count, next_attempt_at, owner (claim token, null), lease_until (null), last_error, created_at`.

- Index on `(status, next_attempt_at)`.
- **Unique index on `(event_id, endpoint_id)`** — this enforces idempotent fan-out (see [Endpoints & matching](./endpoints-and-matching.md#idempotent-fan-out)).

There is **no explicit "in-flight" status** — `owner` + `lease_until` express it, and an **expired lease is auto-reclaimable**, so crash recovery falls out for free:

```
claimable = status = Pending
        AND next_attempt_at <= now
        AND (owner IS NULL OR lease_until < now)
```

### The store-agnostic contract

```csharp
Task<IReadOnlyList<ClaimedMessage>> ClaimDueAsync(int batch, TimeSpan lease, CancellationToken ct);
Task MarkDeliveredAsync(Guid id, CancellationToken ct);
Task RescheduleAsync(Guid id, DateTimeOffset nextAttempt, string? error, CancellationToken ct);
Task DeadLetterAsync(Guid id, string error, CancellationToken ct);
```

The dispatcher is a `BackgroundService`: **poll → claim batch → deliver with bounded parallelism → mark / reschedule / dead-letter**. `TimeProvider` makes the timing testable; backoff is exponential + jitter.

### Per-provider claim — provider-optimal, not portable-lowest-common-denominator

The claim is implemented with each store's best concurrency primitive rather than a portable optimistic `UPDATE`, so it stays production-grade and scales without contention.

**Postgres** — `FOR UPDATE SKIP LOCKED`:
```sql
WITH due AS (
  SELECT id FROM messages
  WHERE status='Pending' AND next_attempt_at <= now()
    AND (owner IS NULL OR lease_until < now())
  ORDER BY next_attempt_at FOR UPDATE SKIP LOCKED LIMIT @batch)
UPDATE messages m SET owner=@me, lease_until=now()+@lease
FROM due WHERE m.id=due.id RETURNING m.*;
```

**SQL Server** (roadmap) — `READPAST` + `UPDLOCK`:
```sql
;WITH due AS (
  SELECT TOP (@batch) * FROM messages WITH (READPAST, UPDLOCK, ROWLOCK)
  WHERE status='Pending' AND next_attempt_at <= @now
    AND (owner IS NULL OR lease_until < @now)
  ORDER BY next_attempt_at)
UPDATE due SET owner=@me, lease_until=@lease OUTPUT inserted.*;
```

**SQLite** — serialized single-writer: one atomic `UPDATE … WHERE id IN (SELECT id … ORDER BY next_attempt_at LIMIT @batch) RETURNING *` claims and returns the batch in a single statement. SQLite serializes writers so a competing dispatcher's identical statement runs against the already-claimed state — no double-claim, no `SKIP LOCKED` needed. Fine for single-node / low concurrency.

**In-memory** — in-process concurrent structure; single node, non-durable; tests/dev only.

**Redis** (roadmap) — `XREADGROUP` = claim, `XACK` = delivered, `XAUTOCLAIM` (min-idle-time) = lease/crash-recovery. Streams deliver immediately, so backoff uses a companion sorted set scored by `next_attempt_at`; a sweeper promotes due items into the stream.

### Proven across instances

The Postgres no-double-send guarantee is not a claim on paper — it is asserted against a **real Postgres** (spun via Testcontainers) by `tests/Caliber.Webhooks.IntegrationTests`: N concurrent dispatchers draining one shared backlog claim every message exactly once (`FOR UPDATE SKIP LOCKED`), a crashed dispatcher's expired lease is reclaimed and its work redelivered, concurrent fan-out of the same `(event_id, endpoint_id)` inserts exactly one row, and the outbox relay is idempotent across a mid-relay crash. The suite is Docker-required, so it runs in a dedicated CI job (`.github/workflows/integration.yml`) kept off the fast PR path — opt in locally with `CALIBER_INTEGRATION=1` and Docker running.

### Schema provisioning

The library provisions its own `messages`/`endpoints` schema — **ensure-created on startup** for the SQLite/standalone path, **shipped migrations** (opt-in startup auto-apply) for Postgres. The host-owned `outbox` table is migrated by the host via `AddCaliberOutbox()` and the normal `dotnet ef` workflow. **Caliber.Webhooks never issues silent DDL against your business tables.**

## Lease / crash-recovery invariant

A short fixed lease (default **~60s**), with **no renewal in v1**. The per-attempt HTTP timeout (default **10s**) is kept strictly **< the lease**, so a delivery finishes or times out *before* its lease lapses → no concurrent double-work in practice. This invariant is **validated at startup** (`HttpTimeout < LeaseDuration`; see [Configuration → fail-fast validation](./configuration.md#fail-fast-validation)).

The rare pathological overlap is safe because delivery is [at-least-once with a stable `webhook-id`](./delivery-semantics.md#at-least-once-with-a-stable-webhook-id) — receivers dedupe. Exactly-once over HTTP to arbitrary receivers is impossible; we don't pretend.

Reclaiming a crashed lease is **intrinsic** to the claim predicate (`owner IS NULL OR lease_until < now`), not a separate sweeper — a message whose owner crashed becomes claimable again once its lease lapses. The library emits a `webhooks.lease.reclaimed` metric so slow/crashed dispatchers are visible.

## See also

- [Transactional outbox](./transactional-outbox.md) — how events get *into* the store atomically.
- [Delivery semantics](./delivery-semantics.md) — the at-least-once contract that makes lease overlap safe.
- [Configuration](./configuration.md) — lease duration, batch size, concurrency, poll interval.
