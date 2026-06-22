---
title: Core Use Cases
description: Caliber.Webhooks' six core use cases (iDesign-first) — publish, relay/fan-out, deliver-with-retry, reclaim a crashed lease, manage an endpoint, and verify an incoming webhook — plus the secondary variants that reduce to them.
status: pre-release
audience: [human, ai]
tags: [caliber-webhooks, use-cases, idesign, design]
related: [../concepts/architecture.md, ../concepts/delivery-semantics.md, ./decisions.md]
updated: 2026-06-22
---

# Core Use Cases

Authored **use-cases-first** per iDesign: the core use cases are fixed *before* components, and the [volatility-based decomposition](../concepts/architecture.md) is then validated by tracing each one as a call chain.

A **core use case** is one of the smallest representative set that captures the system's essence. Caliber.Webhooks' essence is: *get an event in durably → turn it into per-subscriber work → deliver that work reliably to untrusted endpoints, surviving failure → and let the receiver trust what arrives.* Six use cases cover that; everything else is a variation on one of them.

## Actors

| Actor | Role |
|---|---|
| **Producer** | The host app's domain code calling `PublishAsync` (in-process). |
| **Subscription admin** | The host app's API/dashboard registering & managing endpoints (per customer/tenant). |
| **Dispatcher** | The library's own `BackgroundService` — a *system actor* that triggers delivery on a timer. |
| **Relay** | The library's own `BackgroundService` — drains the outbox into the delivery queue (outbox mode only). |
| **Receiver** | A downstream/customer system (or a .NET app using the verify helper) consuming the webhook. |
| **External endpoint** | The customer's untrusted HTTP endpoint — a *resource*, not a trusted actor. |

## UC-1 — Publish an event

**Trigger:** Producer calls `PublishAsync(type, payload)`.
**Guarantee:** the event is **durably captured before any delivery** and is never lost on a mid-send crash. Two registration-determined modes:
- **Outbox mode** (ambient `AppDbContext`): stages a thin outbox row; the caller's `SaveChanges` commits it *atomically with business data*. Stage-only — no fan-out or signing yet.
- **Standalone mode** (the library's own store): persists immediately and returns; fans out to `messages` directly (no outbox/relay).

**Volatilities touched:** store backend, source, serialization. **Not** touched: signing, retry, matching (downstream).

## UC-2 — Relay & fan-out

**Trigger:** Relay timer fires (outbox mode only).
**Main flow:** read committed outbox rows → for each, **match** the logical event against enabled endpoints → write **one `messages` row per matching endpoint** → delete the drained outbox row, **all in one transaction, idempotent on the stable event id**. A relay crash mid-batch never double-enqueues and never loses a row.
**Why it's core:** it is the single point where one *logical* event becomes N *physical* delivery jobs — the fan-out contract. (Standalone mode performs the same match+fan-out inline at publish; same essence, different trigger.)
**Volatilities touched:** matching rules, store backend.

## UC-3 — Deliver a due message (with retry/backoff)

**Trigger:** Dispatcher timer fires.
**Main flow:** **claim** a batch of due, unleased messages → for each (bounded parallelism): **sign** (Standard Webhooks) → **SSRF-guard** the URL → **POST** over HTTPS with a per-attempt timeout → on 2xx **mark delivered**; on failure **compute next attempt** (backoff+jitter) and **reschedule**, or **dead-letter** if attempts are exhausted.
**Guarantee:** **at-least-once** with a stable `webhook-id`; the trace context captured at publish is propagated to the receiver.
**Volatilities touched:** signing algo, retry policy, transport, store backend, SSRF/security policy.

## UC-4 — Reclaim a crashed/expired lease

**Trigger:** Dispatcher timer fires (this is *intrinsic* to UC-3's claim, not a separate workflow).
**Main flow:** the claim predicate treats `owner IS NULL OR lease_until < now` as claimable, so a message whose owner crashed mid-delivery is automatically re-claimable once its short lease lapses. No heartbeat, no renewal in v1.
**Why it's core despite being intrinsic:** it is the resilience invariant that makes "never lose an event" true across process crashes — it must be designed in, not bolted on. Emits `webhooks.lease.reclaimed`.
**Volatilities touched:** store backend (per-provider claim semantics), lease policy.

## UC-5 — Register & manage an endpoint

**Trigger:** Subscription admin calls create / update / disable.
**Main flow:** generate or accept a `whsec_…` secret → persist the endpoint (URL, secret, subscribed event types, enabled) → upserts feed UC-2's matching.
**Guarantee:** secret-at-rest handling is explicit (decision #6); a disabled endpoint stops matching new events immediately.
**Volatilities touched:** matching/subscription model, secret storage/rotation (rotation → roadmap).

## UC-6 — Verify an incoming webhook (receiver side)

**Trigger:** Receiver gets an HTTP request and calls the verify helper.
**Main flow:** read `webhook-id` / `webhook-timestamp` / `webhook-signature` → recompute HMAC-SHA256 over `{id}.{timestamp}.{payload}` with the endpoint secret → constant-time compare → reject stale timestamps (replay protection).
**Why it's core:** it closes the loop and is the *interop* promise — symmetric with what the library sends, and the same scheme major API providers use. The stateless verifier function is v1; the ASP.NET middleware (`MapCaliberReceiver`) is roadmap.
**Volatilities touched:** signing algo (HMAC v1 / ed25519 roadmap).

## Secondary / variant use cases (compositions — not core)

| Variant | Reduces to | Status |
|---|---|---|
| Publish from an **alternate source** | UC-1 with a different *source* feeding the same ingest | roadmap (v1.1) |
| **Replay** a dead-lettered/delivered message | re-arm → UC-3 | partly v1 (dead-letter replay); rest roadmap |
| **Admin: list / disable a flapping endpoint** | query over UC-3 outcomes + UC-5 | roadmap (admin API) |
| **Secret rotation** (multi-secret window) | UC-5 + UC-3 signing with N active secrets | roadmap |
| **Dead-letter** as a standalone step | terminal branch of UC-3 | folded into UC-3 |

## Validation

Each core use case is traced as a **call chain through the iDesign layers** in [Architecture → use-case validation](../concepts/architecture.md#use-case-validation). If a use case can't be expressed as `Client → Manager → Engine/ResourceAccess → Resource` without an upward or peer-Manager call, the decomposition is wrong — not the use case.

## See also

- [Architecture](../concepts/architecture.md) — the decomposition these use cases validate.
- [Delivery semantics](../concepts/delivery-semantics.md) — the guarantees UC-1…UC-4 deliver.
- [Decisions](./decisions.md) — the decisions each use case exercises.
