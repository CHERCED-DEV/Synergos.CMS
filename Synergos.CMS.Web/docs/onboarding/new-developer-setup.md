# New Developer Setup

From a clean machine to a running Synergos.CMS in under 15 minutes.

## Prerequisites

- **.NET SDK**: whichever version is pinned in `global.json` at the repo
  root. Today that is `10.0.202`, which can build `.NET 8` targets used
  by every project in this solution. Install from
  <https://dotnet.microsoft.com/download>.
- **Git**.
- **An IDE**: Visual Studio 2022 17.10+, JetBrains Rider 2024.1+, or
  VS Code with the C# Dev Kit extension.

## Clone and build

```bash
git clone <repo-url> synergos
cd synergos
dotnet restore Synergos.CMS.sln
dotnet build Synergos.CMS.sln
```

Expected outcome:
- Restore succeeds.
- Build succeeds with `0 errors`.
- One warning: `NU1902` (moderate-severity advisory on `Umbraco.Cms`
  13.x). This is a known, accepted issue — see
  [`../operations/run-build-test.md`](../operations/run-build-test.md)
  and [ADR 0001](../adr/0001-umbraco-13-lts-pin.md).

## Run

```bash
dotnet run --project Synergos.CMS.Web
```

On first run, Umbraco shows its install wizard at `https://localhost:XXXXX/`
(port assigned by Kestrel — watch the console). Credentials pre-seeded
by the template:

- **Email**: `admin@synergos.local`
- **Password**: `Synergos2026!`

The backoffice is at `/umbraco`. The database is SQLite, stored under
`Synergos.CMS.Web/umbraco/Data/Umbraco.sqlite.db` — safe to delete and start
over during scaffolding.

## What to read next (in order)

1. [`../README.md`](../README.md) — purpose of each folder you just cloned.
2. [`../architecture/overview.md`](../architecture/overview.md) — how the
   four projects relate.
3. [`../conventions/naming.md`](../conventions/naming.md) — naming rules.
4. [`../conventions/commit-style.md`](../conventions/commit-style.md) —
   commit format.
5. [`../adr/README.md`](../adr/README.md) — every architectural decision
   that shaped this codebase.

## Things that commonly go wrong

| Symptom | Cause | Fix |
|---------|-------|-----|
| `error NETSDK1045` at build | Installed SDK doesn't match `global.json` | Install the pinned SDK, or accept a `rollForward` install. |
| Build warning `NU1902` | Known Umbraco 13.x advisory | Ignore — see ADR 0001. |
| `SqliteException: unable to open database file` | `umbraco/Data/` missing or read-only | Ensure the folder exists and is writable; `dotnet run` creates it on first start. |
| Backoffice login loops | Stale cookies from a previous install | Clear browser site data for `localhost`. |
| Port clash on `dotnet run` | Another service on the default Kestrel port | Edit `Synergos.CMS.Web/Properties/launchSettings.json`. |

## When you're ready to write code

Before the first `.cs` file, skim
[`../architecture/folder-layout.md`](../architecture/folder-layout.md)
and find the row for the kind of file you're about to write. That row
tells you the exact folder.

If the folder you want doesn't exist in that table, you're either about
to invent a new convention (write an ADR) or you're solving the wrong
problem (ask a question instead).
