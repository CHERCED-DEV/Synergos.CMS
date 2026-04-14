# Synergos Documentation

Reference manual for the Synergos workspace. Split by topic — pick the doc
that matches what you're about to do.

## The two canonical files at the repo root

- [`CLAUDE.md`](../CLAUDE.md) — **agent rules**. Ten Commandments, dependency
  rule, forbidden patterns. Required reading for any LLM/agent before writing code.
- [`AGENTS.md`](../AGENTS.md) — pointer for non-Claude agents (same content,
  redirects to CLAUDE.md).

## Docs index

### 🧭 Architecture
- [`architecture/overview.md`](architecture/overview.md) — the whole system on one page: ecosystem, layers, request flow, where state lives.
- [`architecture/clean-architecture.md`](architecture/clean-architecture.md) — deep dive on layer boundaries, the dependency rule, and enforceable invariants.
- [`architecture/request-lifecycle.md`](architecture/request-lifecycle.md) — what happens from HTTP request to rendered HTML, end to end.

### 📦 Schema (Umbraco types)
- [`schema/pipeline.md`](schema/pipeline.md) — the 12-phase initializer pipeline, idempotency, SchemaVersion semantics.
- [`schema/content-model.md`](schema/content-model.md) — inventory of document types, element types, compositions, data types.
- [`schema/guid-registry.md`](schema/guid-registry.md) — GUID allocation policy, reserved ranges, collision protocol.

### 🎨 Rendering
- [`rendering/overview.md`](rendering/overview.md) — SSR Razor vs CDN web components, when to pick which.
- [`rendering/cdn-integration.md`](rendering/cdn-integration.md) — CDN registry, element URL resolution, dev overrides.
- [`rendering/macros.md`](rendering/macros.md) — native macros + CDN macros, how the MacroDispatcher works.

### ⚙️ Configuration
- [`configuration/reference.md`](configuration/reference.md) — every `Synergos:*` section in appsettings.json, explained.
- [`configuration/seed.md`](configuration/seed.md) — SeedConfig + SeedTheme + SeedPage — how to rebrand without touching code.

### 🚀 Operations
- [`operations/build-and-run.md`](operations/build-and-run.md) — prerequisites, launch profiles, host setup, certificates.
- [`operations/fresh-boot.md`](operations/fresh-boot.md) — clean-slate procedure (delete DB + uSync, first run).
- [`operations/usync.md`](operations/usync.md) — uSync as a backup format, not a source of truth.

### 🍳 Recipes (step-by-step)
- [`recipes/add-document-type.md`](recipes/add-document-type.md) — new page type end-to-end.
- [`recipes/add-element-type.md`](recipes/add-element-type.md) — new Block Grid element end-to-end.
- [`recipes/add-cdn-macro.md`](recipes/add-cdn-macro.md) — new CDN web-component macro end-to-end.
- [`recipes/add-settings.md`](recipes/add-settings.md) — new typed settings section end-to-end.

## Planning / historical context

The repo root contains planning and audit documents from earlier phases:

- `plan-maestro-arquitectura.md`, `plan-maestro-desacople-total.md`, `plan-maestro-multi-identidad.md`, `plan-maestro-alineacion-synergos.md` — architectural vision docs.
- `plan-migracion-synergos.md`, `plan-ecommerce-synergos.md` — migration + e-commerce plans.
- `auditoria-anti-hardcodeo.md`, `auditoria-integracion-cms-ui-cdn.md` — audits.
- `synergos-flow-engine-*.md`, `synergos-orchestration-backend-strategy.md` — Flow Engine design.
- `synergos-guid-registry.md` — GUID history (supplemented by `docs/schema/guid-registry.md`).
- `API-CHANGELOG.md`, `CONTRIBUTING.md` — API and contribution notes.

These are **historical**. When they conflict with `docs/` or `CLAUDE.md`, the
newer docs win. Keep them as reference for understanding why decisions were made.

## Conventions for writing docs

- **One topic per file.** If a doc crosses 500 lines, split it.
- **Code blocks with language tags.** Use `csharp`, `razor`, `json`, `bash`.
- **Link liberally.** Reference other docs by relative path. Update both sides when you split.
- **Examples from the real codebase.** Never invent example code that doesn't match how the project actually does it.
- **Match CLAUDE.md when it overlaps.** If rules diverge, update both.
