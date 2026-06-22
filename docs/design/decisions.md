---
title: Design Decisions
description: ADR-style log of the Caliber.Webhooks v1 design decisions (#0–#6) and the toolchain/identity choices, with context and consequences. The canonical "why" behind the library.
status: pre-release
audience: [human, ai]
tags: [caliber-webhooks, decisions, adr, design, rationale]
related: [./use-cases.md, ./roadmap.md, ../concepts/architecture.md, ../concepts/transactional-outbox.md]
updated: 2026-06-22
---

# Design Decisions

The decisions that shape Caliber.Webhooks v1, in ADR style (context → decision → consequences). These are **accepted and bedded down**; the portfolio-root `docs/design/*` documents are their frozen provenance.

## Scope: first deliverable = full v1

**Context.** A tighter first cut (SQLite-only, single-node) was weighed against one stronger v1.
**Decision.** Ship the **full design-doc v1**: in-memory + SQLite + Postgres stores, transactional outbox + cross-instance work-claiming all included.
**Consequences.** A bigger first splash; the open enqueue/endpoint questions sat on the critical path and had to be closed before scaffolding (they now are, below). SQL Server, Redis, additional event sources, receiver middleware/admin API, ed25519, and secret rotation are deferred to the [roadmap](./roadmap.md).

## #0 — Architecture method: iDesign

**Context.** The library packs many concerns (outbox, retry, signing, SSRF, claiming); a functional split would leak change across components.
**Decision.** Architect with **iDesign / "The Method"** (Löwy): enumerate core use cases first, then decompose by **volatility** into Clients → Managers → Engines → ResourceAccess → Resources, with Utilities as the cross-cutting bar; validate by tracing each use case as a downward call chain.
**Consequences.** Eight volatilities are each encapsulated behind one component; a store or signing swap can't leak upward. See [Architecture](../concepts/architecture.md) and [Use cases](./use-cases.md).

## #1 — Enqueue API & transactional outbox (relay outbox)

**Context.** How an event gets *into* the store, and how the transactional-outbox UX should feel.
**Decision.** **A1 + B1.** One `PublishAsync`; with an ambient `AppDbContext` it **stages** a thin outbox row that the caller's `SaveChanges` commits atomically (A1). The guarantee tier surfaces at the **store-config line**, never a silent downgrade (B1). A background **relay** drains committed outbox rows into the library's `messages` store (insert + delete in one transaction, idempotent on the source event id) — chosen over claiming directly from the shared table so the host's schema stays thin while the library owns `messages`/`endpoints`.
**Consequences.** EF-idiomatic atomic enqueue with zero ceremony; one extra hop + a relay worker. Standalone stores (own SQLite/Redis/in-mem) persist immediately, no outbox/relay. Rejected: B3 silent-downgrade, B2 capability-typed API. See [Transactional outbox](../concepts/transactional-outbox.md).

## #2 — Endpoints, subscriptions & fan-out

**Context.** The brief left the `Endpoint`/`MatchingEngine` contract open.
**Decision.** Exact event-type match **+ subscribe-all-when-unset**; free-form event types, no catalog; optional indexed `tenant_key` (host-owned, never matched on); relay fan-out **idempotent on a unique `(event_id, endpoint_id)`**; `messages.id` = `webhook-id`, source event id = the relay idempotency key.
**Consequences.** Wildcards and payload filters are deferred but **non-breaking** (same contract and column). See [Endpoints & matching](../concepts/endpoints-and-matching.md).

## #3 — Dead-letter, recovery & attempt history

**Context.** A library that can dead-letter but offers no way to see why or recover is not complete.
**Decision.** Terminal **`DeadLettered`** status with `attempt_count`/`last_error`; **per-attempt history via OpenTelemetry** (no SQL table); a dead-letter read seam; programmatic **`ReplayAsync`** that re-arms a message and **preserves the `webhook-id`**.
**Consequences.** A genuinely recoverable, observable v1 with no history table or admin surface. A persisted `attempts` table, admin HTTP API, and bulk/auto/delivered replay are deferred (additive). See [Delivery semantics](../concepts/delivery-semantics.md#dead-letter-and-recovery).

## #4 — Security scope

**Context.** Signing is the thesis; SSRF is a real liability when customers supply URLs. A bypassable guard would be worse than honest absence.
**Decision.** HMAC-SHA256 Standard Webhooks signing; a **non-bypassable SSRF guard** (HTTPS-only, IP filtering, connect-time anti-rebinding, no auto-redirect, policy seam); outbound payload cap + response-read cap; TLS-on; per-attempt timeout; receiver-side replay protection.
**Consequences.** Table-stakes production-grade security in v1. Secret rotation, ed25519, and mTLS are deferred behind existing seams. See [Security](../concepts/security.md).

## #5 — Options & defaults

**Context.** Which knobs to expose, and with what defaults.
**Decision.** Locked defaults on `AddCaliberWebhooks(options)`: `MaxAttempts=12`, default retry table + jitter (≈27.6h), lease 60s, HTTP timeout 10s, poll 5s, batch 50, concurrency 16, 256 KB cap; **eager fail-fast validation** (notably `HttpTimeout < LeaseDuration`).
**Consequences.** Predictable, production-safe defaults; misconfiguration fails the host build with a clear message. See [Configuration](../concepts/configuration.md).

## #6 — Residual contracts

**Context.** The last enqueue/persistence contracts.
**Decision.** **No-magic `SaveChanges`** — the library stages, the caller commits; no interceptor in v1 (opt-in auto-flush is roadmap). **Single `DbContext`** in v1 (multi-context + Dapper `DbTransaction` overload → roadmap). **Secret-at-rest** auto-encrypted via the host's `IDataProtectionProvider` when present, else stored as-is with a one-time startup warning.
**Consequences.** Explicit, auditable enqueue contract; encryption-by-default where it's free, honest where it isn't. See [Transactional outbox](../concepts/transactional-outbox.md) and [Security → secret-at-rest](../concepts/security.md#secret-at-rest).

## Toolchain & identity

**Decision (locked).**
- **House brand `Caliber`** — one reserved NuGet prefix fronts the portfolio; this project is `Caliber.Webhooks`. The `Caliber.*` namespace was verified uncontested on NuGet. ("Herald", the former working name, was dropped as contested in the messaging/notify lane.)
- **Target frameworks** `net8.0;net10.0` (current + previous LTS; skip net9 STS and netstandard).
- **Toolchain** — MinVer (git-tag versioning), xUnit v3, AwesomeAssertions (OSS; *not* commercial FluentAssertions), WireMock.Net, Testcontainers, Coverlet, Meziantou.Analyzer + nullable + warnings-as-errors, Central Package Management, SourceLink, deterministic builds, `.snupkg`, MIT.
- **Packaging** — keep the low-barrier path dependency-free: in-memory store in `Caliber.Webhooks`; SQLite a tiny add-on; Postgres/SQL Server/Redis/AspNetCore as separate packages.
- **Resilience** — core depends on **no** resilience library (built-in per-attempt timeout via `TimeProvider`+`CancellationToken`); richer resilience is opt-in in `Caliber.Webhooks.Resilience` over `Microsoft.Extensions.Http.Resilience`, behind the standard `HttpClient` seam, so it's swappable if it ever commercializes.

**Consequences.** No commercial-dependency lock-in; a zero-infra on-ramp; a single author/brand signal. The Starlight docs site → GitHub Pages is deferred to M5 (this bundle is Markdown-only for now).

## See also

- [Use cases](./use-cases.md) — the core use cases decision #0 fixed first.
- [Roadmap](./roadmap.md) — what each decision schedules into v1 vs. v1.1+.
- [Architecture](../concepts/architecture.md) — how the decisions map onto iDesign layers.
