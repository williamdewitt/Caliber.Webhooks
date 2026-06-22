---
title: Endpoints, Subscriptions & Matching
description: The Caliber.Webhooks subscription model — endpoint fields, exact event-type matching with subscribe-all-when-unset, optional tenant scoping, fan-out from one logical event to N messages, and idempotent fan-out on a unique index.
status: pre-release
audience: [human, ai]
tags: [caliber-webhooks, endpoints, subscriptions, matching, fan-out, multi-tenant]
related: [./transactional-outbox.md, ./delivery-semantics.md, ./security.md, ../design/decisions.md]
updated: 2026-06-22
---

# Endpoints, Subscriptions & Matching

A lean, Standard-Webhooks-aligned subscription model. The producer publishes **one logical event**; Caliber.Webhooks owns fan-out — the producer never enumerates endpoints.

## Endpoint model

`endpoints`: `id, tenant_key (null), url, secret, subscribed_event_types (null/empty ⇒ all), enabled, description (null), created_at, updated_at`.

- **`subscribed_event_types`** — a set of exact event-type strings. **Null or empty ⇒ subscribe to all** (a common default). Event types are free-form strings; there is **no pre-registered catalog** in v1 (that ceremony is deferred).
- **`tenant_key`** *(optional, nullable, indexed)* — an opaque grouping key the **host** sets and queries by, so a multi-tenant SaaS can scope endpoints per customer without bolting on its own join table. Caliber.Webhooks attaches **no semantics** to it beyond filter/index — it **never participates in matching**.
- **`secret`** — a `whsec_…` value; at-rest handling is covered in [Security → secret-at-rest](./security.md#secret-at-rest).
- **`enabled`** — a disabled endpoint stops matching **new** events immediately; messages already queued continue to drain per their own status.

## Matching contract

```
match(eventType, endpoints) =
  endpoints.where(e => e.enabled
                    && (e.subscribedTypes is null/empty        // subscribe-all
                        || e.subscribedTypes.contains(eventType)))  // exact
```

**v1 = exact event-type match + subscribe-all-when-unset.** Wildcards (`order.*`) and payload-content filtering are **deferred** — and importantly, the `MatchingEngine` contract and the `subscribed_event_types` column are **unchanged** when they arrive (a wildcard is just another string the engine interprets; a payload filter is an added predicate). So this is a **non-breaking roadmap extension, not a schema migration**.

> Rejected for v1: wildcard precedence rules and a filter-expression language — feature-parity creep beyond the narrow "reliable delivery" promise.

## Fan-out — one logical event → N messages

- **Outbox mode:** fan-out runs at **relay time** — the relay matches the event against the **endpoint set as of relay time** and inserts one `messages` row per match.
- **Standalone mode:** the same match + fan-out runs **inline at publish**.
- **No backfill.** Subscriptions are evaluated at fan-out time; an endpoint created *after* an event fans out does **not** receive that past event (intentional re-delivery is [Replay](./delivery-semantics.md#dead-letter-and-recovery)). With sub-second relay lag this matches the intuitive "endpoints receive events that occur after they're created."

## Idempotent fan-out

Two deliberately distinct ids keep fan-out correct under retries:

- **source event id** = the outbox row id — the relay's idempotency key (a relay retry never re-fans-out).
- **per-endpoint message id** = `messages.id` — this **is** the Standard-Webhooks **`webhook-id`** the receiver dedupes on; exactly one per `(event, endpoint)`.

Fan-out is made idempotent by a **unique index on `messages(event_id, endpoint_id)`** with insert-or-ignore semantics. A relay crash between "insert messages" and "delete the outbox row" is therefore safe: the retry's inserts conflict and no-op, so no endpoint is double-queued. (`messages.id` stays a fresh per-row GUID; uniqueness is enforced on the `(event_id, endpoint_id)` pair.)

## Managing endpoints

Endpoints are created and managed by the host's own API or dashboard (typically per customer):

```csharp
await endpoints.CreateAsync(new Endpoint
{
    Url        = "https://acme.com/hooks",
    Secret     = WebhookSecret.Generate(),     // whsec_...
    EventTypes = ["order.shipped", "order.cancelled"],  // omit ⇒ subscribe to all
});
```

Creating, updating, and disabling endpoints is use case **UC-5**; secret generation and at-rest protection are covered in [Security](./security.md). A future admin HTTP API (list / replay / disable) is on the [roadmap](../design/roadmap.md).

## See also

- [Transactional outbox](./transactional-outbox.md) — where relay-time fan-out happens.
- [Delivery semantics](./delivery-semantics.md) — the `webhook-id` and dedupe contract.
- [Security](./security.md) — endpoint secrets and signing.
