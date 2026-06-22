---
title: Caliber.Webhooks Documentation
description: Index and map for the Caliber.Webhooks documentation bundle — the entry point for human readers and AI context-building alike.
status: pre-release
audience: [human, ai]
tags: [caliber-webhooks, docs, index, webhooks, dotnet]
related: [./overview.md, ./getting-started.md, ./design/roadmap.md]
updated: 2026-06-22
---

# Caliber.Webhooks Documentation

**Reliable, [Standard Webhooks](https://www.standardwebhooks.com/)-compliant webhook _delivery_ for .NET — embedded in your own app, with zero infrastructure by default.**

> **Status: pre-release / in active development.** The design is complete and v1 is being built milestone by milestone (see the [roadmap](./design/roadmap.md)). The library is **not yet published to NuGet**, and the API shown throughout these docs is the **target** surface — faithful to the bedded-down design, but subject to change until v1 ships. Nothing here implies code exists yet.

This bundle is the **canonical, living documentation** for Caliber.Webhooks. It is written for two audiences at once: humans evaluating or using the library, and AI tools building context while the library is implemented. The portfolio-root `docs/design/*` documents remain as **frozen decision provenance** (the "why"); this bundle is where the design is expressed for use.

## Start here

| If you want to… | Read |
|---|---|
| Understand the problem and where this fits | [Overview](./overview.md) |
| See it work with zero infrastructure | [Getting started](./getting-started.md) |
| Understand the reliability guarantees | [Delivery semantics](./concepts/delivery-semantics.md) |
| See the planned v1 scope and milestones | [Roadmap](./design/roadmap.md) |

## Concepts

The "how it works" set — one topic per document.

- [Delivery semantics](./concepts/delivery-semantics.md) — durability, at-least-once, the stable `webhook-id`, retry/backoff, dead-letter, replay.
- [Architecture](./concepts/architecture.md) — the iDesign volatility-based decomposition (Clients → Managers → Engines → ResourceAccess → Resources).
- [Storage & work-claiming](./concepts/storage-and-work-claiming.md) — the store lineup, guarantee tiers, cross-instance claiming, lease/crash-recovery.
- [Transactional outbox](./concepts/transactional-outbox.md) — the enqueue API, outbox → relay → fan-out.
- [Endpoints & matching](./concepts/endpoints-and-matching.md) — the subscription model, exact + subscribe-all matching, idempotent fan-out.
- [Security](./concepts/security.md) — Standard Webhooks signing, SSRF defence, secret-at-rest, hardening.
- [Configuration](./concepts/configuration.md) — the options surface, defaults, and the default retry schedule.
- [Observability](./concepts/observability.md) — OpenTelemetry traces, metrics, and logs.

## Design

The "why" — decision records, the use-case model, and the delivery plan.

- [Decisions](./design/decisions.md) — ADR-style log of the v1 design decisions (#0–#6) and the toolchain choices.
- [Use cases](./design/use-cases.md) — the six core use cases (iDesign-first).
- [Roadmap](./design/roadmap.md) — v1 milestones M0–M5 and the v1.1+ roadmap.

## Reference

- [Public API](./reference/public-api.md) — the target public surface (provisional, pre-release).

## What Caliber.Webhooks is (and is not)

- **It is** a NuGet library you embed in your ASP.NET app to deliver webhooks reliably: durable enqueue, retries with backoff, Standard Webhooks signing, dead-lettering, SSRF defence, and cross-instance work-claiming — backed by your own database.
- **It is not** a separate server you run alongside your app, and it is **not** an internal message bus or event-streaming platform — it is the external HTTP last mile for delivering to third-party endpoints. See [Overview → Where this sits](./overview.md#where-this-sits).

*Caliber.Webhooks is part of the **Caliber** family of .NET developer tools.*
