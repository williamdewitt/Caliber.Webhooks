---
title: The Self-Building Development Loop
description: How Caliber.Webhooks builds and supports itself on GitHub — a risk-tiered, progressively-autonomous loop where the same classification drives the merge gate, the model, and the reasoning effort. Maintainer/process design (distinct from the product design).
status: pre-release
audience: [human, ai]
tags: [caliber-webhooks, github, ci, automation, governance, autonomy, process]
related: [./roadmap.md, ./decisions.md]
updated: 2026-06-22
---

# The Self-Building Development Loop

> **Status: pre-release / being built.** This is the design for how the *repository* develops and supports itself — its CI/CD, governance, and (opt-in) AI automation. It is **maintainer/process** documentation, distinct from the product docs (which describe the library for its consumers). It is a **living** spec: each layer is implemented incrementally and this doc is updated as the `.github/` automation lands.

## What this is

The goal is a repository that can **iterate itself toward a beautiful state**, driven two ways:

- **Drive from the issues board** — a human (or a triage pass) files/grooms issues; the loop picks up *ready* work, does it at the right model and effort, and gates the merge by risk.
- **Set a north star** — point the project at a goal (a milestone, an epic) and let it decompose and work toward it, surfacing the risky decisions for a human while clearing the safe ones autonomously.

The spine that makes this safe is a **single classification** applied to every unit of work. That one classification has three outputs: **how it merges** (the gate), **which model does it**, and **how hard that model thinks** (effort). Gate policy and cost policy are the same policy.

## The four layers

Each layer feeds the one above it; lower layers are useful on their own.

| Layer | What it is | GitHub primitives |
|---|---|---|
| **1 · Backlog as data** | The roadmap (M0–M5, use-cases, decisions) as machine-readable, *ready*-marked work | Milestones, Issues, Labels, Project (v2) board + fields, issue *forms* |
| **2 · CI/CD** | Every change is built, tested, packed, released with no human running `dotnet` | Actions: `ci.yml` (build/test matrix), `release.yml` (tag → pack → NuGet) |
| **3 · Automation glue** | Items move themselves; deps/security self-report; releases self-draft | Project workflows, Dependabot, CodeQL, dependency-review, Release Drafter, labeler |
| **4 · Agent-in-the-loop** | The agent picks up an issue, implements, opens a PR; CI gates it; risk decides merge | `claude-code-action` on `@claude` / dispatch |

## The routing table (the spine)

One classification → gate + model + effort:

| Risk band | Triggered by | Model | Effort | Merge gate |
|---|---|---|---|---|
| **Trivial** | `docs/**`, `**/*.md`, formatting, Dependabot **patch** | none → **Haiku 4.5** (`claude-haiku-4-5`) | minimal | auto-merge on green, no human |
| **Low** | `tests/**`, `samples/**` (not core) | **Sonnet 4.6** (`claude-sonnet-4-6`) | medium | CI green + light approval |
| **Core** | `src/Caliber.Webhooks/**` | **Opus 4.8** (`claude-opus-4-8`) | high | CODEOWNERS human approval |
| **Critical** | SSRF/signing/secret-at-rest, `PublicAPI.*.txt`, `Migrations/**` | **Opus 4.8** | max | human approval **+** linked design update |

Two principles fall out of this table:

- **Progressive autonomy.** Trust is earned by blast radius. A typo merges itself; the SSRF guard never does. As confidence grows, bands can be relaxed *downward* deliberately — never by accident.
- **Cost control is gate control.** Trivial work spends **nothing** (pure GitHub automation, no model). Everything else runs on the **cheapest model that can do the job well**, reserving Opus for core/critical. Opus is never burned fixing typos.

## Classification — how a unit of work lands in a band

- **Pull requests** are classified **deterministically** by changed paths (a path-classifier workflow applies a `risk:*` label). Paths are unambiguous, so this is free and reliable.
- **Issues** declare their `area` + `risk` via issue **forms** (dropdowns) — deterministic and free. A cheap **Haiku** triage pass only runs on issues that arrive unlabeled, to assign a band.
- The `risk:*` label is the **single source of truth**: it drives required checks/approvals (the gate) and, for agent work, the `model` + effort the invoking workflow requests.

## Enforcement — four primitives, no custom service

1. **CODEOWNERS** maps protected paths (`src/**`, security files, `PublicAPI.*.txt`) to a human → those paths cannot merge without human review.
2. **Branch protection / rulesets** on `main`: require PR, require CI status checks green, require approvals (+ CODEOWNERS review on protected paths).
3. **Path-classifier workflow** labels each PR `risk:trivial|low|core|critical`.
4. **Native auto-merge** is enabled by a workflow *only* when a PR is `risk:trivial` and CI is green.

First dogfood: **Dependabot patch auto-merge** exercises the whole *classify → CI green → auto-merge* path before the agent is ever pointed at it.

## Work selection — which issue, next

"Ready" is an explicit state, not a guess. A dispatch step (scheduled or manually triggered) picks the top issue that is: in the current milestone, labeled `ready` (has acceptance criteria + definition-of-done), unblocked, highest priority. It then invokes the agent with the model + effort mapped from the issue's `risk:*` band. North-star mode is the same machinery with an epic decomposed into ready issues first.

## The agent & its economics

The in-CI agent (`claude-code-action`, headless on Actions runners) is **not** the same resource as interactive Claude Code sessions:

- **API key (`ANTHROPIC_API_KEY`)** → Anthropic API, **pay-per-token, billed separately** from any Claude subscription. Scales; metered money.
- **Subscription token (`claude setup-token` → `CLAUDE_CODE_OAUTH_TOKEN`)** → draws from the **Claude subscription pool**, shared with (and competing against) interactive sessions.

> **To verify before relying on the subscription path:** current **Pro-vs-Max eligibility and limits** for the Action's subscription auth, and the exact **effort/thinking** knob the Action exposes. Use the `claude-code-guide` agent / `/claude-api` skill. Until confirmed, design assumes API-key auth as the dependable path and treats subscription auth as a cost optimization.

## Build sequence

1. **M0 shell + `ci.yml`** — foundation; CI must be green before anything can gate on it. *(in progress)*
2. **`release.yml` + Release Drafter** — tag → MinVer → pack → NuGet (manual-approval environment).
3. **Backlog as data** — Milestones M0–M5, label taxonomy, roadmap seeded as issues, Project board. *(needs `project` gh scope)*
4. **Rule system + support automation** — branch protection/rulesets, CODEOWNERS, path-classifier, native auto-merge, Dependabot (first dogfood), CodeQL, dependency-review.
5. **Agent-in-the-loop** — pointed at Tier 1–2 issues, gated by everything above.

## Status

Foundation only. Layers 2–4 are planned per the sequence above; nothing in this doc implies the automation exists yet. This file is the contract those `.github/` workflows are built against, and it is updated as each lands.

## See also

- [Roadmap](./roadmap.md) — the product milestones M0–M5 the loop delivers.
- [Decisions](./decisions.md) — the product design decisions (#0–#6) and toolchain.
