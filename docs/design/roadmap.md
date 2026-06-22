---
title: Roadmap — v1 Milestones & Beyond
description: The Caliber.Webhooks delivery plan — v1 built shell-first across milestones M0–M5, and the additive v1.1+ roadmap. Reflects the full-v1 scope (outbox + relay + cross-instance claiming all in v1).
status: pre-release
audience: [human, ai]
tags: [caliber-webhooks, roadmap, milestones, planning, delivery-plan]
related: [./decisions.md, ../concepts/architecture.md, ../reference/public-api.md]
updated: 2026-06-22
---

# Roadmap — v1 Milestones & Beyond

v1 is built **shell-first, then features** (per the portfolio's production-ready standard): a production-ready shell that *reads* shippable comes before any feature code. The milestones below reflect the full-v1 scope — transactional outbox, relay, and cross-instance work-claiming are all **in v1**, not deferred.

> **Status: pre-release.** No milestone is complete yet; this is the plan, not a changelog. The next action is to scaffold **M0**.

## v1 milestones

### M0 — Production-ready shell *(scaffold first, before any feature)*
- Solution + `src` projects per the repository structure; multi-target `net8.0;net10.0`.
- Central Package Management, `Directory.Build.props`, `.editorconfig`, analyzers (nullable, warnings-as-errors, Meziantou), MinVer, SourceLink, deterministic builds, `.snupkg`.
- CI (build / test / pack) + NuGet packaging wired day one; honest-niche README stub; dogfood test project skeleton.

### M1 — Core delivery loop (in-memory)
- Domain contracts (`WebhookEvent` / `Endpoint` / `WebhookMessage` / `DeliveryStatus`).
- iDesign components: `IngestionManager`, `DeliveryManager`, `MatchingEngine` (exact + subscribe-all), `RetryEngine` (default schedule + jitter), `SigningEngine` (HMAC-SHA256), `MessageAccess`/`EndpointAccess` over the **in-memory store**.
- `DispatcherHost` `BackgroundService` (poll → claim → sign → POST → outcome); at-least-once + stable `webhook-id`.
- `AddCaliberWebhooks(options)` surface + fail-fast validation. Dogfooded flaky-receiver xUnit suite.

### M2 — Durable + transactional outbox (SQLite + Postgres)
- `OutboxAccess` + `AddCaliberOutbox()`; `RelayHost` (drain → fan-out → `messages`, idempotent on `(event_id, endpoint_id)`).
- SQLite store (ensure-created) + Postgres store (shipped migrations, `FOR UPDATE SKIP LOCKED`).
- Cross-instance work-claiming + lease/crash-recovery — **Testcontainers proves no-double-send**. Standalone vs. outbox modes.

### M3 — Security hardening + receiver verify
- Non-bypassable SSRF guard (`SsrfGuardEngine`: HTTPS-only, IP filtering, connect-time anti-rebinding, no-redirect, policy seam); outbound payload cap + response-read cap; TLS-on.
- Secret-at-rest via the host's `IDataProtectionProvider`.
- Receiver **verifier helper** (`WebhookVerifier`, UC-6) + timestamp replay protection.

### M4 — Recovery + observability
- Terminal `DeadLettered`; dead-letter read seam + `ReplayAsync` (preserves `webhook-id`).
- OpenTelemetry first-class (`ActivitySource`/`Meter`, span links, `traceparent` to receiver) — per-attempt history lives in OTel.

### M5 — Docs, samples, ship v1
- Starlight docs site (this bundle → GitHub Pages); samples (BasicSender, ReceiverApp).
- NuGet release: `Caliber.Webhooks` + `.Sqlite` + `.EntityFrameworkCore`.

## How the milestones map to concepts

| Milestone | Primary concepts |
|---|---|
| M1 | [Delivery semantics](../concepts/delivery-semantics.md), [Endpoints & matching](../concepts/endpoints-and-matching.md), [Configuration](../concepts/configuration.md) |
| M2 | [Storage & work-claiming](../concepts/storage-and-work-claiming.md), [Transactional outbox](../concepts/transactional-outbox.md) |
| M3 | [Security](../concepts/security.md) |
| M4 | [Delivery semantics → recovery](../concepts/delivery-semantics.md#dead-letter-and-recovery), [Observability](../concepts/observability.md) |

## v1.1+ roadmap (additive)

All of the below sit behind seams the v1 design already exposes, so none is a breaking change:

- **Stores:** SQL Server + Redis.
- **Sources:** additional event-source adapters.
- **ASP.NET:** admin API (list / replay / disable) + receiver middleware (`MapCaliberReceiver`).
- **Signing:** ed25519 + secret rotation (multi-secret windows).
- **Resilience:** per-endpoint rate-limiting / circuit-breaking (`Caliber.Webhooks.Resilience`).
- **Matching:** wildcard (`order.*`) + payload-content filtering.
- **Enqueue:** opt-in `SaveChanges` auto-flush interceptor; multi-`DbContext` + Dapper `DbTransaction` overload.
- **Latency:** low-latency wakeup (`LISTEN/NOTIFY`) instead of polling.
- **History:** persisted `attempts` table (durable audit beyond OTel retention).
- **Ordering:** per-subscription ordering.

## See also

- [Decisions](./decisions.md) — what each milestone implements and why.
- [Public API](../reference/public-api.md) — the surface M1–M4 build toward.
- [Architecture](../concepts/architecture.md) — the components named in the milestones.
