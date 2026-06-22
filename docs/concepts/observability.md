---
title: Observability (OpenTelemetry)
description: How Caliber.Webhooks emits traces, metrics, and logs through BCL System.Diagnostics primitives with no OpenTelemetry SDK dependency — including span links for queued work and W3C traceparent propagation to receivers.
status: pre-release
audience: [human, ai]
tags: [caliber-webhooks, observability, opentelemetry, tracing, metrics, logging]
related: [./delivery-semantics.md, ./architecture.md, ./configuration.md]
updated: 2026-06-22
---

# Observability (OpenTelemetry)

Observability is **first-class in v1**, not an afterthought — it is also how per-attempt delivery history is exposed without a dedicated table (see [Delivery semantics → dead-letter and recovery](./delivery-semantics.md#dead-letter-and-recovery)).

Caliber.Webhooks emits through BCL `System.Diagnostics` primitives (`ActivitySource` + `Meter`) and has **no dependency on the OpenTelemetry SDK**. Consumers wire it into their own pipeline:

```csharp
.AddSource("Caliber.Webhooks").AddMeter("Caliber.Webhooks")
```

## Tracing

- `ActivitySource` named **`"Caliber.Webhooks"`**.
- Two spans: **`webhook.publish`** (a message is created) and **`webhook.deliver`** (per attempt).
- Delivery happens later in a separate context, so `webhook.deliver` carries a **span link** back to the publish context (not a parent-child relationship) — the correct OTel model for queued work. The publish trace context is stored with the message.
- The W3C **`traceparent`** is injected into the outgoing webhook HTTP headers, so a **receiver** can continue the trace — end-to-end distributed tracing across the webhook boundary.
- Attributes align with OTel **messaging** and **http.client** semantic conventions: endpoint id, event type, attempt number, response status, outcome.

## Metrics (`Meter "Caliber.Webhooks"`)

| Instrument | Type | Notes |
|---|---|---|
| `webhooks.published` | counter | messages created |
| `webhooks.delivery.attempts` | counter | tags: outcome, `http.status_code` |
| `webhooks.delivered` | counter | successful deliveries |
| `webhooks.failed` | counter | failed attempts |
| `webhooks.dead_lettered` | counter | messages reaching terminal failure |
| `webhooks.delivery.duration` | histogram (ms) | per-attempt latency |
| `webhooks.queue.pending` | observable gauge | current backlog |
| `webhooks.time_in_queue` | histogram | enqueue → first attempt |
| `webhooks.lease.reclaimed` | counter | signals crashed/slow dispatchers |

The `webhooks.delivery.attempts` counter (tagged with outcome and HTTP status) **is** the per-attempt history — operators get it where they already look, with zero extra storage.

## Logs

Structured `ILogger` output with stable `EventId`s and scopes carrying the message id, endpoint id, and trace id — so logs correlate directly with the spans above.

## See also

- [Delivery semantics](./delivery-semantics.md) — why per-attempt history lives in OTel rather than a table.
- [Architecture](./architecture.md) — Observability as a cross-cutting Utility.
- [Configuration](./configuration.md) — the options that govern delivery behaviour these signals report on.
