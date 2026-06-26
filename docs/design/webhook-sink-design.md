---
title: WebhookSink + Caliber.Webhooks.Testing — A Webhook Sink & Delivery Test Harness
description: Design for the second portfolio artifact — a local, signature-aware webhook sink + dashboard, and a Caliber.Webhooks.Testing package that lets any project assert webhook deliveries in xUnit. Incubated in-repo against the M1 surface; extracted to its own repo once concrete.
status: pre-release
audience: [human, ai]
tags: [caliber-webhooks, webhook-sink, testing, test-harness, sample, second-artifact]
related: [./roadmap.md, ./development-loop.md, ../reference/public-api.md, ../concepts/security.md]
updated: 2026-06-26
---

# WebhookSink + Caliber.Webhooks.Testing

> **Status: pre-release / being built.** This is the design for a **second portfolio artifact** that ships alongside Caliber.Webhooks. It is **incubated in-repo** (under `samples/` and, later, `src/`) against the shipped **M1** surface, and is extracted to its own repository only once both its own shape *and* the shared shell (post-M2) are concrete. This document is the contract the agent-loop issues build against.

## What it is

A local, **.NET-native webhook sink** — think *Mailpit / smtp4dev, but for webhooks* — in two layers:

- **`samples/WebhookSink`** — a runnable minimal-API app: a catch-all receiver that captures every incoming webhook, a live dashboard to inspect them, per-bucket **fault injection** to exercise a sender's reliability, and a **signature panel** that validates Standard-Webhooks signing.
- **`src/Caliber.Webhooks.Testing`** — a shipped NuGet package: an **in-process** sink you spin up inside an xUnit test, point your system-under-test at, and assert against (`WaitForAsync(predicate)` + AwesomeAssertions extensions). This is the drop-in test harness; the dashboard app is its interactive sibling.

## Why this is a coherent artifact (not a tangent)

A sink is the **receiver** side; Caliber.Webhooks is a **delivery (sender)** library. ~80% of a generic "catch any webhook" sink is plain ASP.NET and needs no library at all. The artifact earns its place at exactly two seams, and is honest about the rest:

1. **Signature verification** — the sink validates that incoming hooks are correctly Standard-Webhooks-signed (HMAC-SHA256 over `{id}.{timestamp}.{body}`, timestamp freshness). That is precisely the **M3 `WebhookVerifier`** helper. It is **not built yet**, so the sink hand-rolls the HMAC today and **swaps to `WebhookVerifier` when M3 lands**. Building the sink is therefore the design pressure that defines the receiver-side API — it ships against a real consumer instead of a guess.
2. **Delivery fault injection** — a sink that returns 500 / 429 / timeout / drop on demand is the canonical way to test a *sender's* retry/backoff/dead-lettering. It makes the sink the **dogfood receiver for the M1 delivery loop that is already built**: publish through Caliber.Webhooks → into the sink → watch the retry storm and `webhook-id` dedup on one screen. That screen is the README GIF.

This is on-brand with the repo's "dogfood + CI-native + observable" ethos: *the library, and the test harness that proves it.*

## Honest niche

`webhook.site` / RequestBin (hosted, manual inspection), Smocker / Hookdeck CLI (Go) all exist. The differentiated wedge is **local + .NET-native + Standard-Webhooks-signature-aware + fault-injecting + embeddable in xUnit**. No .NET tool fills that. The README leads with that niche — never "nothing exists."

## In-repo now, own repo later

Incubated in-repo because:

- The dashboard app is a **tool, not a published package**, so it has zero packaging coupling forcing a split.
- It reuses the **settled shell** (CPM, analyzers, MTP test stack, CI) with **zero duplication** — avoiding the "maintain two copies" cost while the shell still accretes M2 packages.
- It earns its keep immediately as the dogfood receiver, and it lets us discover its real shape against the live API before committing to a repo boundary.

**Extraction trigger:** when the sink's own surface is concrete *and* the shared shell has settled post-M2, lift `samples/WebhookSink` (+ `src/Caliber.Webhooks.Testing`) into a standalone repo, templating the shell at that point. Provisional standalone name: `Caliber.Sink` (TBD).

## Architecture

```
   any webhook sender (Stripe, GitHub, your app, Caliber.Webhooks)
                    │  HTTP POST /in/{bucket}
                    ▼
        ┌──────────────────────────┐
        │      Capture endpoint     │  records method, headers, raw body, received-at
        │      (catch-all, any verb)│  into a bounded in-memory ring buffer
        └───────────┬──────────────┘
                    │
     ┌──────────────┼────────────────────────────┐
     ▼              ▼                             ▼
 Dashboard (SSE)  Signature panel            Fault injection
 live list +      HMAC verify over           per-bucket response policy:
 detail view      {id}.{ts}.{body} +         status / latency / drop
                  timestamp freshness        → exercises sender retries
                  (→ WebhookVerifier @ M3)
                    │
                    ▼
        GET /api/hooks  (JSON feed → assertions)
                    │
        ┌──────────────────────────┐
        │ Caliber.Webhooks.Testing  │  in-proc host + WaitForAsync(predicate)
        │ (xUnit drop-in)           │  + AwesomeAssertions: HaveValidSignature(secret)
        └──────────────────────────┘
```

## Testing-package API (target feel)

```csharp
await using var sink = WebhookSink.Start();           // ephemeral, in-process, random port
mySystem.WebhookUrl = sink.Url("/in/orders");         // point the system-under-test at it

var hook = await sink.WaitForAsync(
    h => h.EventType == "order.shipped", timeout: TimeSpan.FromSeconds(5));

hook.Should().HaveValidSignature(secret);             // AwesomeAssertions extension
hook.Payload.Should().Contain("trackingNo");
sink.Received.Should().ContainSingle(h => h.EventType == "order.shipped");
```

## Build sequence (sink milestones S0–S6)

Shell-first, then features — mirroring the library's discipline. Each is one small agent-loop issue (Acceptance criteria + DoD), risk-banded per the [development loop](./development-loop.md) routing table.

| # | Slice | Path | Risk band |
|---|---|---|---|
| **S0** | Scaffold `samples/WebhookSink` minimal-API (net10.0), wired into `.slnx`, builds green, `/healthz` + placeholder home | `samples/**` | low |
| **S1** | Catch-all capture → bounded in-memory store; `GET /api/hooks` JSON | `samples/**` | low |
| **S2** | Live dashboard (SSE list + detail: headers + pretty JSON) | `samples/**` | low |
| **S3** | Per-bucket fault injection (status / latency / drop) | `samples/**` | low |
| **S4** | Signature panel (hand-rolled HMAC + timestamp freshness; `WebhookVerifier` swap noted) | `samples/**` | low |
| **S5** | `Caliber.Webhooks.Testing` package — in-proc host + `WaitForAsync` + assertion extensions | `src/**` | core |
| **S6** | Dogfood: publish through Caliber.Webhooks into the sink; docs page + README + retry-storm GIF | `samples/**`, docs | low / trivial |

S0 is the only `ready` slice at filing time; S1–S6 are enumerated on the epic and filed as their predecessor lands (no scheduler is wired — see the development-loop doc).

## How it rides the self-building loop

- The work is filed as an **epic** + small `ready`+`agent` issues; the agent implements each on a `claude/issue-<n>-*` branch and opens a PR.
- Per the routing table, `samples/**` PRs are **`risk:low`** (CI-green + a light human approval) and the `src/Caliber.Webhooks.Testing` package is **`risk:core`** (CODEOWNERS approval). Docs land **`risk:trivial`** (auto-merge). So building is autonomous; **merging code is the deliberate human touchpoint.**
- The signature seam (S4) and the Testing package (S5) intentionally generate the requirements for the **M3 receiver verifier**, so that milestone ships against a real consumer.

## See also

- [Roadmap](./roadmap.md) — the library's v1 milestones this artifact dogfoods.
- [The self-building development loop](./development-loop.md) — how these issues build, gate, and merge.
- [Public API](../reference/public-api.md) — the M1 surface S0–S6 build against.
- [Security](../concepts/security.md) — the signing scheme the signature panel verifies.
</content>
</invoke>
