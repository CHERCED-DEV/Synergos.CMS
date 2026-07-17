# Migration Changelog — Synergos.CMS

Breaking changes in schema, backoffice, or deployment that require
manual action beyond a redeploy. Each entry describes **exactly what
to do** to migrate an existing environment.

Functional changes (non-breaking features, bug fixes) live in
[`CHANGELOG.md`](CHANGELOG.md). This file is for operational steps only.

---

## Format

Each entry:

```markdown
## [vX.Y.Z] — YYYY-MM-DD — <one-line summary>

**Impact**: <who/what is affected>

**Required steps**:
1. <numbered, imperative, copy-pasteable>
2. <…>

**Rollback**: <how to undo, or "not rollback-safe">

**References**: ADR-NNNN, PR #NN
```

---

## [0.1.0] — 2026-04-17 — Initial scaffolding

**Impact**: N/A — first release. No prior environment to migrate from.

**Required steps**: none.

**Rollback**: delete the folder.

**References**: [CHANGELOG.md](CHANGELOG.md) v0.1.0 entry.

---

## Upcoming (expected)

Entries here are *placeholders* for migrations we know are coming:

- **Database migration to SQL Server** (when production hosting is
  decided) — supersedes ADR 0003.
- **uSync installation** (when first document type is committed) —
  adds `Synergos.CMS/uSync/` to source control.
- **ModelsBuilder mode switch** (if we ever move off `InMemoryAuto`
  to `SourceCodeManual` for production parity).

Each of these will become a concrete entry above when shipped, with the
exact steps.
