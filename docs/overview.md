---
title: Overview — The Problem, the .NET Niche, and Positioning
description: Why reliable webhook delivery is a real distributed-systems problem, the specific gap in the .NET ecosystem, and how Caliber.Webhooks is positioned as an embeddable library rather than a standalone service.
status: pre-release
audience: [human, ai]
tags: [caliber-webhooks, overview, positioning, kafka, dotnet]
related: [./getting-started.md, ./concepts/delivery-semantics.md, ./design/decisions.md]
updated: 2026-06-22
---

# Overview

> **Pre-release.** This describes the design and intended positioning of Caliber.Webhooks. The library is in active development and not yet published.

## The problem

Every platform, API, or SaaS product eventually needs to notify *external* systems when something happens — `payment.succeeded`, `order.shipped`. The naïve version is one line:

```csharp
await httpClient.PostAsync(url, json);
```

…and it's a trap. The receiver is an untrusted server you don't control: it can be down, slow, or malicious. Doing it *reliably* — durable delivery, retries with backoff, signing, idempotency, dead-lettering, SSRF protection — is a genuine distributed-systems problem, not a one-liner.

## The .NET niche

In .NET specifically there is a gap:

- The serious webhook infrastructure is delivered as standalone **Rust/Go servers** you run as separate services.
- The .NET NuGet options are simplistic "just POST" senders, chat-platform-specific (Slack/Teams), or abandoned (Microsoft's is in maintenance mode).

There is no widely-adopted, **Standard-Webhooks-compliant, production-grade library you embed in your own ASP.NET app**. That gap is the product.

## The goal

A NuGet-publishable .NET library that delivers webhooks reliably **from inside your application** — durable retries, [Standard Webhooks](https://www.standardwebhooks.com/) signing, dead-lettering — with **zero infrastructure by default** (in-memory or SQLite) and a one-line swap up to Postgres (and, on the roadmap, SQL Server or Redis) as you grow. On relational stores it can ride your business transaction (the [transactional outbox](./concepts/transactional-outbox.md)) for atomic enqueue.

The artifact is the deliverable: a tool others install, not a demo.

## Positioning (honest framing)

The honest one-liner is **"production-grade reliability as a library — no separate service to run."** The standalone services are excellent; Caliber.Webhooks is the *embeddable .NET option*, not a claim that "nothing exists."

| Existing option | Form | How Caliber.Webhooks differs |
|---|---|---|
| Standalone webhook services | Self-hosted Rust/Go servers (often + SaaS) | In-process .NET library, no separate service; backed by *your* DB (transactional outbox) |
| Existing .NET libraries | Simplistic "just POST" senders, or unmaintained | Standard-Webhooks-first, with transactional outbox, a pluggable source model, SSRF hardening, and a receiver helper |

## Where this sits

Caliber.Webhooks handles the **external edge** — pushing to *third parties'* HTTP endpoints on the public internet. There is no offset the receiver manages; the sender owns retries, backoff, dead-lettering, signing, and SSRF defence, per untrusted destination.

It is deliberately **source-agnostic**: deliveries can be fed by a direct `PublishAsync` or drained from a [transactional outbox](./concepts/transactional-outbox.md), with further source adapters on the [roadmap](./design/roadmap.md). It is **not** an internal message bus or event-streaming platform — it sits at the HTTP edge and complements whatever you already run internally, rather than replacing your internal eventing.

## Why it's worth building

A dense concentration of senior-backend concerns in one coherent library — transactional outbox, background processing, retry/backoff scheduling, HMAC/crypto signing, idempotency / at-least-once semantics, SSRF hardening, and pluggable persistence — fronted by a zero-infra on-ramp. It fills a real .NET gap, complements infrastructure you already run, and is interoperable by standard (the same signing scheme major API providers use).

## See also

- [Getting started](./getting-started.md) — the zero-infra example.
- [Delivery semantics](./concepts/delivery-semantics.md) — the reliability guarantees in detail.
- [Decisions](./design/decisions.md) — the design decisions behind this positioning.
