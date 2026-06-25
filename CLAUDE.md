# CLAUDE.md — Caliber.Webhooks

Project-specific guidance. This file **inherits** the portfolio root `../../CLAUDE.md` (goal, ethos, house tech stack, the production-ready standard, Conventional Commits) and the user's global instructions — it only adds what is specific to *building Caliber.Webhooks*.

## dotnet-claude-kit is wired into this repo

This repo uses the **dotnet-claude-kit** plugin through **two separate channels** — the CI agent does *not* inherit local settings (verified — [#29](https://github.com/williamdewitt/Caliber.Webhooks/issues/29)):

- **Locally** (every contributor / clone): repo-scoped [`.claude/settings.json`](.claude/settings.json) enables the **full** kit — agents, commands, skills, the Roslyn MCP tools, and hooks.
- **In CI** (the `@claude` agent-in-the-loop, [`.github/workflows/claude.yml`](.github/workflows/claude.yml)): the kit's **agents, commands, and skills** are installed via the workflow's explicit `plugins` inputs. The **Roslyn MCP tools are deferred** there (they need a .NET runtime + a per-run whole-solution index) — tracked in [#29](https://github.com/williamdewitt/Caliber.Webhooks/issues/29).

The full capability → flow mapping is **[docs/design/dotnet-claude-kit-adoption.md](docs/design/dotnet-claude-kit-adoption.md)** — consult it; the reach-for shortlist:

- **Before any PR:** `/verify` (7-phase build/analyze/test/security/format gate), then `/code-review` (Roslyn-powered). These are the *local* gate that complements the CI `classify.yml` + ruleset gate.
- **Security surface (HMAC signing, the SSRF guard — decision #4):** the `security-auditor` agent + `/security-scan`. This is `risk:critical` work — never auto-merges.
- **Outbox relay / fan-out / EF (`messages` store — #1, #2, #6):** the `ef-core-specialist` agent + `/migrate` for any schema change.
- **Tests (xUnit v3):** the `test-engineer` agent + `/tdd`.
- **Delivery HTTP + retry/jitter (#5):** the `httpclient-factory` and `resilience` skills.
- **OTel attempt history (#3):** the `opentelemetry` skill.
- **Navigation & quality, always (local):** the Roslyn MCP tools — `get_public_api` (we ship `PublicAPI.*.txt` — a critical surface), `detect_antipatterns`, `detect_circular_dependencies`, `find_references`. (Deferred in CI — see [#29](https://github.com/williamdewitt/Caliber.Webhooks/issues/29).)

## House overrides — these WIN over kit defaults

The kit is opinionated, and several defaults collide with decisions already bedded down in `docs/design/`. Where they collide, **ours win** — do not let a kit rule or skill silently override:

- **Packages →** Central Package Management (`Directory.Packages.props`), pinned + deterministic. NOT the kit's "`dotnet add` without `--version`".
- **Architecture →** iDesign volatility-based decomposition (use the kit's `idesign` skill). NOT its VSA / Clean-Architecture default (`architecture-advisor`, ADR-001).
- **Zero-infra ethos →** in-memory + SQLite providers are a *feature*, not a smell. Ignore the kit's "no InMemory DB" testing rule for consumer-facing guidance.
- **Errors →** case-by-case (exceptions + fail-fast options validation, #5). NOT a blanket Result-pattern rule.
- **Assertions →** AwesomeAssertions (FluentAssertions is commercial).

Full adopt / decline list and rationale: the adoption doc linked above.
