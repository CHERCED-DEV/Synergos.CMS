# Synergos.CMS — Documentation Index

> **Rule of thumb**: every architectural decision, convention, or operational
> procedure in this solution has a document below. If you're about to add
> something that feels "implicit" or "team knowledge", write it here first.

This is the third attempt at Synergos.CMS. The two previous attempts
(`Synergos.CMS.epicfail`, `Synergos.CMS.epicfail2`) failed for the same root
cause: **lack of enforced structure and of a living trail of decisions**.
This documentation exists to prevent the third failure.

---

## How this folder is organized

| Folder | Purpose | When to edit |
|--------|---------|--------------|
| [`architecture/`](architecture/) | System overview, layering, dependency rules | Adding/removing a layer or cross-layer rule |
| [`adr/`](adr/) | Architecture Decision Records (numbered, immutable) | Making an architectural choice |
| [`conventions/`](conventions/) | Naming, folders, commits, code style | Changing a team convention |
| [`onboarding/`](onboarding/) | Setup guide for a new developer | Dev environment changes |
| [`umbraco/`](umbraco/) | Umbraco-specific patterns (composers, uSync, models) | Umbraco integration changes |
| [`operations/`](operations/) | Running, building, testing, troubleshooting | Tooling/process changes |

---

## Reading order for a new contributor

1. [`../README.md`](../README.md) — what Synergos.CMS is
2. [`onboarding/new-developer-setup.md`](onboarding/new-developer-setup.md) — get it running locally
3. [`architecture/overview.md`](architecture/overview.md) — the 4 projects and how they relate
4. [`architecture/folder-layout.md`](architecture/folder-layout.md) — where does X go
5. [`conventions/naming.md`](conventions/naming.md) — naming rules
6. [`adr/README.md`](adr/README.md) — the decisions behind the structure

---

## Rules for this folder

- Docs are **plain Markdown**, no HTML, no generated sites.
- ADRs are **immutable once accepted** — to change a decision, write a new ADR that supersedes the previous one.
- Every ADR has: Context / Decision / Consequences / Status.
- Non-ADR docs are **living** — keep them current. If a doc is wrong, fix it in the same PR as the code change that made it wrong.
- No screenshots unless absolutely necessary — text is searchable, images are not.
