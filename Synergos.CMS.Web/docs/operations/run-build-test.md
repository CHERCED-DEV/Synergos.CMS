# Operations — Run, Build, Test

The full lifecycle commands for Synergos.CMS, executed from the repo root.

## Restore

```bash
dotnet restore Synergos.CMS.sln
```

Central Package Management resolves every `<PackageReference>` against
`Directory.Packages.props` — no floating versions, no surprise upgrades.

## Build

```bash
dotnet build Synergos.CMS.sln
```

**Expected output:**
- 34 projects built successfully (the integrating solution — see `CLAUDE.md` §7
  for the other three).
- `0 errors` **and `0 warnings`**.

This section said «exactly 1 warning: `NU1902`» until #149, and that stopped
being true at #134: `NU1902` went into `NoWarn` (with its reason) and
`TreatWarningsAsErrors=true` went on, so **any** warning is now an error and a
clean build prints zero of both. A doc that tells you to expect one warning
teaches you to skim past the line that matters.

## Run (dev)

```bash
dotnet run --project Synergos.CMS.Web
```

First run triggers the Umbraco install flow at `https://localhost:XXXXX/`.
Credentials seeded by the template:
- **Email**: `admin@synergos.local`
- **Password**: `Synergos2026!`

The SQLite database is at `Synergos.CMS.Web/umbraco/Data/Umbraco.sqlite.db`.
Delete it to reset to pristine state.

## Test

```bash
dotnet test Synergos.CMS.sln
```

Today this runs the (empty) `Synergos.CMS.Tests` project. As tests get
added, they run here.

For a specific test project:
```bash
dotnet test Synergos.CMS.Tests/Synergos.CMS.Tests.csproj
```

For coverage (via coverlet, already referenced centrally):
```bash
dotnet test --collect:"XPlat Code Coverage"
```

## Clean

```bash
dotnet clean Synergos.CMS.sln
# Nuclear option, if bin/obj drift causes ghost errors:
find . -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
```

## Known warnings and how to triage them

| Warning | Source | Action |
|---------|--------|--------|
| `NU1902` — moderate advisory on `Umbraco.Cms` | `GHSA-54mj-vcvj-q3v5`; vulnerable range `(, 16.3.3]`, so **no** 13.x release closes it | Accept — suppressed centrally (`NoWarn=NU1902`). See ADR 0001. |
| `NU1903` — **high** advisory on `Umbraco.Cms` | `GHSA-wr57-hqmp-fgvh`; vulnerable range `[12.0.0, 13.15.1)` | **Fixed, not suppressed** — pin moved to 13.16.2 (#149). A NuGet advisory with a patch inside the pinned branch never belongs in `NoWarn`. |
| `CS1591` — missing XML doc on public type | Analyzer | Suppressed centrally (`NoWarn=1591`). |
| `IDE0005` — unused using | Analyzer | Fix immediately — this is on as an error-level by project policy. |

## Troubleshooting

### `NETSDK1045: The current .NET SDK does not support targeting net8.0`

Install the SDK pinned in `global.json`. Currently `10.0.202`.

### `HTTP Error 500.30 — ASP.NET Core app failed to start`

Most often a misconfigured connection string in `appsettings.json`.
Inspect `Synergos.CMS.Web/Logs/` for the real exception.

### `SqliteException: database is locked`

Another process holds the SQLite file. Stop the previous `dotnet run`,
or (for single-dev convenience) delete
`Synergos.CMS.Web/umbraco/Data/Umbraco.sqlite.db` and reinstall.

### Backoffice returns 404 for a saved document

- Verify the page is *published*, not just saved.
- Verify the document type has a corresponding Razor view in
  `Synergos.CMS.Web/Views/` with the exact alias.

### `PackageReference ... has no version` (build error)

CPM is strict. Either add the missing `<PackageVersion>` entry in
`Directory.Packages.props`, or delete the `<PackageReference>`.
