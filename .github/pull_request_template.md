## What & why

<!-- Brief description. Link the issue this closes, e.g. "Closes #123". -->

## Risk band

<!-- The Classify workflow sets risk:* from the changed paths. Confirm it's right;
     if you think it mis-classified, say why here. -->

- [ ] **trivial** — docs / CI config (auto-merges on green)
- [ ] **low** — tests / samples
- [ ] **core** — `src/Caliber.Webhooks/**` (needs code-owner review)
- [ ] **critical** — SSRF / signing / secret-at-rest / public API / migrations (review **+** linked design update)

## Checklist

- [ ] Conventional Commit title (`type(scope): summary`)
- [ ] CI green — build · test · pack on net8.0 + net10.0
- [ ] Tests added/updated for behaviour changes
- [ ] For **critical** changes: design doc updated and linked
