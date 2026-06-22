---
title: Architecture (iDesign Decomposition)
description: Caliber.Webhooks decomposed by volatility (not function) into iDesign layers — Clients, Managers, Engines, ResourceAccess, Resources — with Utilities as the cross-cutting bar, validated by tracing each core use case as a downward call chain.
status: pre-release
audience: [human, ai]
tags: [caliber-webhooks, architecture, idesign, volatility, layers]
related: [../design/use-cases.md, ./storage-and-work-claiming.md, ./transactional-outbox.md, ../design/decisions.md]
updated: 2026-06-22
---

# Architecture (iDesign Decomposition)

Caliber.Webhooks is architected with **iDesign / "The Method"** (Juval Löwy): **volatility-based decomposition, not functional**. Components encapsulate *axes of change*; layers enforce a strict downward call direction. The structure is validated by tracing each [core use case](../design/use-cases.md) as a call chain.

## Why volatility, not function

A functional decomposition ("a Signing service, a Retry service, a Storage service") leaks change across components and produces the very coupling iDesign exists to prevent. We instead ask **"what is most likely to change?"** and encapsulate each answer behind a component, so a change lands in one place.

### The volatilities Caliber.Webhooks absorbs

| # | Volatility | Today (v1) | Foreseeable change | Encapsulated by |
|---|---|---|---|---|
| V1 | **Store backend** | in-mem, SQLite, Postgres | SQL Server, Redis, others | ResourceAccess (`MessageAccess`, `EndpointAccess`, `OutboxAccess`) |
| V2 | **Signing algorithm** | HMAC-SHA256 | ed25519, multi-secret rotation | `SigningEngine` |
| V3 | **Event source** | direct `PublishAsync` | outbox drain, further sources | `IngestionManager` + source adapters |
| V4 | **Retry / backoff policy** | exp + jitter over ~27.6h | per-endpoint, custom schedules | `RetryEngine` |
| V5 | **Matching / fan-out rules** | exact event-type + subscribe-all | wildcards, payload filters | `MatchingEngine` |
| V6 | **Transport** | HTTPS via `IHttpClientFactory` | mTLS, alternate transports | `DeliveryChannel` |
| V7 | **URL safety / SSRF policy** | block private/loopback/metadata | org allow/deny lists | `SsrfGuardEngine` |
| V8 | **Serialization** | `System.Text.Json` | alternate payload encodings | Serialization utility |

Layers are **closed to volatility leaking upward**: a store swap (V1) never reaches a Manager; a signing swap (V2) never reaches storage.

## The layer map

```
┌──────────────────────────────────────────────────────────────────────────┐
│ CLIENTS   Producer code · Subscription-admin code · DispatcherHost ·       │
│           RelayHost · Source adapters (Direct / Outbox / other) · Receiver │
├──────────────────────────────────────────────────────────────────────────┤   U
│ MANAGERS  IngestionManager · DeliveryManager · SubscriptionManager         │   T
│           (workflow/sequence volatility; no synchronous peer calls)        │   I
├──────────────────────────────────────────────────────────────────────────┤   L
│ ENGINES   MatchingEngine · RetryEngine · SigningEngine · SsrfGuardEngine · │   I
│           VerificationEngine   (volatile business computation, stateless)  │   T
├──────────────────────────────────────────────────────────────────────────┤   I
│ RESOURCE  MessageAccess · EndpointAccess · OutboxAccess · DeliveryChannel  │   E
│ ACCESS    (atomic, verb-based I/O; hides store/transport volatility)       │   S
├──────────────────────────────────────────────────────────────────────────┤   ▲
│ RESOURCES Database (messages/endpoints/outbox) · External HTTP endpoint    │   │
└──────────────────────────────────────────────────────────────────────────┘
   Utilities bar (cross-cutting, callable by any layer): Observability (OTel
   ActivitySource/Meter) · Diagnostics/Logging · Clock (TimeProvider) ·
   Security primitives (secret gen, crypto, constant-time compare) ·
   Serialization · Options/Config · In-process Signal (relay→dispatcher wakeup seam)
```

## Components

### Clients — trigger use cases; hold no business rules
- **Producer code** — the host app calling `PublishAsync` (the entry seam; not the library's code).
- **Subscription-admin code** — the host app's API/dashboard calling endpoint CRUD (not the library's code).
- **DispatcherHost** — the library's `BackgroundService`; on a timer triggers `DeliveryManager.DeliverDueAsync`.
- **RelayHost** — the library's `BackgroundService`; on a timer triggers `IngestionManager.RelayAsync` (outbox mode).
- **Source adapters** — Direct (v1), Outbox-drain (v1), with further sources (roadmap); each adapts an origin into a call on `IngestionManager` (V3).
- **Receiver** — the host app verifying inbound webhooks via the verify helper.

### Managers — own the *sequence* of a use case and its transaction boundary
- **IngestionManager** — UC-1 (publish: stage-to-outbox vs. immediate write) and UC-2 (relay/fan-out).
- **DeliveryManager** — UC-3/UC-4 (claim → sign → guard → POST → outcome → reschedule/dead-letter; reclaim is intrinsic to claim).
- **SubscriptionManager** — UC-5 (endpoint lifecycle).

> **No Manager calls another Manager synchronously.** Ingestion → Delivery is decoupled through the **store** (queue) and the in-process Signal utility — never a direct method call. UC-6 (verify) is stateless with no resource, so it is exposed as a thin façade over `VerificationEngine`, no Manager required.

### Engines — volatile business computation; stateless, no I/O
- **MatchingEngine** (V5) — event → set of matching endpoints. Exact vs. wildcard vs. payload-filter lives *here only*.
- **RetryEngine** (V4) — given attempt count + outcome, compute the next-attempt time (backoff + jitter) or signal terminal/dead-letter.
- **SigningEngine** (V2) — produce Standard Webhooks headers (HMAC-SHA256 in v1; ed25519/rotation behind the same contract).
- **SsrfGuardEngine** (V7) — resolve the host, block private/loopback/link-local + cloud-metadata ranges; the policy seam for allow/deny lists.
- **VerificationEngine** (V2, receiver side) — recompute and constant-time-compare inbound signatures; timestamp-staleness check.

### ResourceAccess — atomic, verb-based I/O; one per resource; hides V1/V6
- **MessageAccess** — `ClaimDueAsync`, `MarkDeliveredAsync`, `RescheduleAsync`, `DeadLetterAsync`. The per-provider claim lives behind this contract — see [Storage & work-claiming](./storage-and-work-claiming.md).
- **EndpointAccess** — upsert/list/disable endpoints; `ListEnabledForMatching`.
- **OutboxAccess** — stage a row into the ambient `AppDbContext`; drain committed rows (outbox mode).
- **DeliveryChannel** (V6) — the actual HTTPS POST over the named `HttpClient` (the SSRF guard and optional resilience handler sit in this pipeline).

### Resources
- **Database** — `messages` / `endpoints` (library-owned schema) + `outbox` (host-owned, in `AppDbContext`).
- **External HTTP endpoint** — untrusted, public internet.

### Utilities — cross-cutting bar; any layer may call
Observability (`ActivitySource`/`Meter`) · Diagnostics/Logging · Clock (`TimeProvider`) · Security primitives (secret gen, HMAC, constant-time compare) · Serialization · Options/Config · In-process Signal (the relay→dispatcher / publish→relay wakeup seam; poll-backed in v1).

## Call rules (enforced)

1. Calls go **downward only**: Clients → Managers → Engines/ResourceAccess → Resources.
2. **Managers never call peer Managers** synchronously — they decouple through the store/queue or the Signal utility.
3. **Engines and ResourceAccess never call up** and never call a Manager; Engines do no I/O, ResourceAccess holds no business rules.
4. **Utilities** are callable by any layer and call nothing above themselves.
5. A Manager **owns the transaction boundary** for its use case; the ResourceAccess components it calls enlist in that unit of work (this is how the relay's "insert messages + delete outbox in one tx" is expressed without ResourceAccess components knowing about each other).

## Use-case validation

Each [core use case](../design/use-cases.md) traces as a legal, downward-only call chain — no upward or peer-Manager call is ever required, which is the proof the decomposition holds. For example:

- **UC-3 Deliver:** `DispatcherHost → DeliveryManager.DeliverDueAsync → MessageAccess.ClaimDueAsync → {SigningEngine.Sign, SsrfGuardEngine.Validate, DeliveryChannel.Post} → MessageAccess.MarkDelivered | RetryEngine.NextAttempt → MessageAccess.Reschedule/DeadLetter`.
- **UC-2 Relay/fan-out:** `RelayHost → IngestionManager.RelayAsync → OutboxAccess.DrainCommitted → MatchingEngine.Match(evt, EndpointAccess.ListEnabled) → [MessageAccess.Enqueue + OutboxAccess.Delete] in one tx`.

The full set of traces is in [Use cases](../design/use-cases.md).

## See also

- [Use cases](../design/use-cases.md) — the six core use cases this structure is validated against.
- [Storage & work-claiming](./storage-and-work-claiming.md) — what ResourceAccess hides.
- [Decisions](../design/decisions.md) — why iDesign, and the decisions behind each volatility.
