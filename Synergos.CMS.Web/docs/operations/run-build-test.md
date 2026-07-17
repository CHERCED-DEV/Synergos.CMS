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
- 4 projects built successfully.
- `0 errors`.
- Exactly **1 warning**: `NU1902` on `Umbraco.Cms` 13.x (known advisory,
  see ADR 0001). Any other warning is a real finding — treat it as such.

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
| `NU1902` — moderate advisory on `Umbraco.Cms` | Umbraco 13.x, no patch yet | Accept. See ADR 0001. |
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
