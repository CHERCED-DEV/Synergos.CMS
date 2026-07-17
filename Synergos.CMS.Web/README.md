# Synergos.CMS

Umbraco 13 LTS backend for the Synergos platform, organized as a
four-project .NET 8 solution.

This is the **third attempt** at Synergos.CMS. The two prior attempts
(`Synergos.CMS.epicfail/`, `Synergos.CMS.epicfail2/`) failed for the
same root cause: a single-project layout let Clean Architecture layers
erode into each other under delivery pressure. This version enforces
the layers physically — via `.csproj` boundaries — and pairs that with
a documentation layer captured from day one.

---

## Project layout

```
Synergos.CMS.sln
├── Synergos.CMS.Web/         ← Web host (Umbraco 13, Controllers, Views, Composers)
├── Synergos.CMS.Application/ ← Pure logic: Services, Proxies, DTOs, Configuration
├── Synergos.CMS.Interfaces/  ← Minimal composition contracts
└── Synergos.CMS.Tests/       ← xUnit tests
```

Dependencies point strictly downward:
`Tests → Web → Application → Interfaces`.

Full layering rules: [`docs/architecture/layers-and-dependencies.md`](docs/architecture/layers-and-dependencies.md).

---

## Getting started

```bash
dotnet restore Synergos.CMS.sln
dotnet build   Synergos.CMS.sln
dotnet run --project Synergos.CMS.Web
```

Installer credentials (pre-seeded by the `dotnet new umbraco` template):
- **Email**: `admin@synergos.local`
- **Password**: `Synergos2026!`

Full setup walkthrough: [`docs/onboarding/new-developer-setup.md`](docs/onboarding/new-developer-setup.md).

---

## Where to look

| If you want to… | Read |
|-----------------|------|
| Understand the overall shape | [`docs/architecture/overview.md`](docs/architecture/overview.md) |
| Know where a new file should live | [`docs/architecture/folder-layout.md`](docs/architecture/folder-layout.md) |
| Know why it is shaped this way | [`docs/adr/README.md`](docs/adr/README.md) |
| Name something consistently | [`docs/conventions/naming.md`](docs/conventions/naming.md) |
| Write a commit or PR | [`docs/conventions/commit-style.md`](docs/conventions/commit-style.md) |
| Set up a fresh machine | [`docs/onboarding/new-developer-setup.md`](docs/onboarding/new-developer-setup.md) |
| Troubleshoot a build/run issue | [`docs/operations/run-build-test.md`](docs/operations/run-build-test.md) |

---

## Governance in one paragraph

Every architectural choice in this codebase is a numbered ADR under
[`docs/adr/`](docs/adr/). ADRs are immutable — to change a decision,
supersede it with a new one. Conventions (naming, folder layout, commit
style) are living documents under [`docs/conventions/`](docs/conventions/).
Releases are tagged with semver in [`CHANGELOG.md`](CHANGELOG.md).
Breaking schema or backoffice migrations get a separate line in
[`MIGRATION-CHANGELOG.md`](MIGRATION-CHANGELOG.md).

If you're about to add a pattern that isn't documented, write the
ADR first. Two prior codebases died from undocumented drift.

---

## Rule: no premature abstraction

- **No `Shared/`, `Common/`, `Utils/`, `Helpers/`** folders.
- **No package references** that aren't already needed by shipping code.
  uSync, AutoMapper, MimeKit, MediatR — none are in the solution until
  a concrete use case demands them.
- **No interface for a single implementation** unless the seam exists
  for testability or composition.
- **No feature folders at project root** — technical kind first,
  feature second. See [`docs/conventions/folder-layout.md`](docs/conventions/folder-layout.md).

---

## Version

See [`CHANGELOG.md`](CHANGELOG.md) for the release history. Current
state is `v0.1.0` — the scaffolding release. No business code yet.

---

## Related

- Solution-wide conventions and commit style live in
  [`docs/conventions/`](docs/conventions/). The old repo-wide
  `CONTRIBUTING.md` (fail-era GUIDs/Schema guide) is archived at
  `../../_archive/docs/CONTRIBUTING.md` for reference only.
- Synergos.UI (Angular 21 + Nx): `../../Synergos.UI/`
- Synergos.API (.NET 8 minimal API): archived at
  `../../_archive/Synergos.API/` (parked — not active).
