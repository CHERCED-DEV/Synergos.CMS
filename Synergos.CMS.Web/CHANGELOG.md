# Changelog — Synergos.CMS

All notable changes to the Synergos.CMS solution will be documented in
this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
and this project adheres to [Semantic Versioning](https://semver.org/).

Breaking schema / backoffice migrations are *also* tracked in
[`MIGRATION-CHANGELOG.md`](MIGRATION-CHANGELOG.md) with the data-migration
steps required.

---

## [Unreleased]

### Changed

- **Repo root cleanup**. The repo root now contains only three entries:
  `Synergos.CMS/` (the full CMS solution), `Synergos.UI/`, and
  `_archive/`. Everything else has been moved under `_archive/`.
  - The CMS solution (4 `.csproj` projects, `Synergos.CMS.sln`,
    `Directory.Build.props`, `Directory.Packages.props`, `global.json`,
    `.editorconfig`) was consolidated under a single `Synergos.CMS/`
    parent folder — symmetric with `Synergos.UI/` at root. No project
    or assembly name was renamed; `.sln` relative paths still resolve.
  - `_archive/` was reorganised: `fails/` groups the four prior fail
    folders, `docs/` collects all legacy `*.md` files (plans, audits,
    flow-engine recovery docs, old repo-wide `CONTRIBUTING.md`),
    `Synergos.API/` and `multimedia/` were moved in as parked material.
  - `azure-pipelines.yml` moved to `_archive/` — the fail-era pipeline
    is inoperative against the new structure; a new CI config will get
    its own ADR when written.
  - `_archive/README.md` rewritten to reflect the new layout and rules.
- **Web project renamed** from `Synergos.CMS` to `Synergos.CMS.Web`.
  - Folder `Synergos.CMS/Synergos.CMS/` → `Synergos.CMS/Synergos.CMS.Web/`.
  - Project file `Synergos.CMS.csproj` → `Synergos.CMS.Web.csproj`.
  - `.sln` updated to reference the new project name and path.
  - `Synergos.CMS.Tests.csproj` `ProjectReference` updated.
  - Docs swept: overview diagram, dependency table, folder-layout,
    composers/usync/models-builder docs, operations, onboarding, ADR
    0002 and ADR 0005 now reflect the new project name.
  - Rationale: the old double-nested path `Synergos.CMS/Synergos.CMS/`
    was ambiguous, and the bare name "Synergos.CMS" invited confusion
    with the solution. `Synergos.CMS.Web` describes the project
    honestly — it is an HTTP/backoffice host. Clean Architecture's
    `Core` would have meant the opposite (domain), so it was rejected.
  - No behaviour change. Build stays `0 errors`.

---

## [0.1.0] — 2026-04-17 — Scaffolding

First commit of the third Synergos.CMS attempt. Contains no business
code — only structure, build governance, and documentation.

### Added

- **Multi-project solution** `Synergos.CMS.sln` with four projects:
  `Synergos.CMS.Web` (Umbraco 13.13.1 host), `Synergos.CMS.Application`,
  `Synergos.CMS.Interfaces`, `Synergos.CMS.Tests` (xUnit). Dependencies
  wired strictly downward.
- **Build governance** at repo root:
  - `global.json` pins the .NET SDK.
  - `Directory.Build.props` sets shared MSBuild properties
    (`Nullable=enable`, `LangVersion=latest`, analyzer level).
  - `Directory.Packages.props` enables Central Package Management with
    transitive pinning — no floating versions.
  - `.editorconfig` enforces a minimal style baseline.
- **Folder scaffolding** per `docs/architecture/folder-layout.md`:
  `Composers/`, `Controllers/`, `Models/`, `Services/`, `Resolvers/`,
  `ValueConverters/`, `Notifications/`, `Config/`, `App_Plugins/`,
  `Views/Partials/`, `Views/MacroPartials/` in Web; `Configuration/`,
  `Dto/`, `Services/`, `Proxies/`, `Extensions/` in Application;
  mirror folders in Tests. Empty folders carry `.gitkeep`.
- **Documentation layer** at `Synergos.CMS.Web/docs/`:
  - `architecture/` — overview, layers and dependencies, folder layout.
  - `adr/` — ADRs 0001 through 0007.
  - `conventions/` — naming, folder layout, commit style.
  - `onboarding/` — new-developer setup.
  - `umbraco/` — composers, uSync (deferred), ModelsBuilder.
  - `operations/` — run / build / test / troubleshooting.
- **Project-root docs**: `README.md`, `CHANGELOG.md`,
  `MIGRATION-CHANGELOG.md`.

### Architectural decisions captured

- [ADR 0001](docs/adr/0001-umbraco-13-lts-pin.md) — Umbraco 13 LTS pin.
- [ADR 0002](docs/adr/0002-multi-project-solution.md) — Multi-project solution.
- [ADR 0003](docs/adr/0003-sqlite-dev-database.md) — SQLite for dev DB.
- [ADR 0004](docs/adr/0004-central-package-management.md) — Central Package Management.
- [ADR 0005](docs/adr/0005-composers-centralized.md) — Composers in one folder.
- [ADR 0006](docs/adr/0006-documentation-first-governance.md) — Documentation-first governance.
- [ADR 0007](docs/adr/0007-xunit-test-framework.md) — xUnit as the test framework.

### Explicitly out of scope

- No business code. No document types. No content seeds. No uSync.
- No CI/CD (`azure-pipelines.yml` for this solution pending).
- No additional NuGet packages beyond `Umbraco.Cms`, ICU runtime,
  and xUnit. More are added only when a concrete use case demands them.

---

[Unreleased]: #
[0.1.0]: #010--2026-04-17--scaffolding
