# Build and Run

Getting the Synergos CMS running locally from zero to a serving instance.

## Prerequisites

- **.NET 8 SDK** — project targets `net8.0`. Later SDKs (10.x) work too; the
  runtime must be 8.
- **Windows / Mac / Linux** — tested primarily on Windows 11. Mac/Linux work
  with path adjustments (see Kestrel certificate below).
- **SQLite** — bundled via `Microsoft.Data.Sqlite`. No external DB setup.
- **`synergos.local` hosts entry** (Kestrel launch profile only):
  ```
  127.0.0.1  synergos.local
  127.0.0.1  static.synergos.local
  ```
  Edit `C:\Windows\System32\drivers\etc\hosts` on Windows, `/etc/hosts` elsewhere.
- **HTTPS dev certificate** (Kestrel launch profile only) at
  `C:\LOCAL_CDN\synergos-dev.crt` + `.key`. Generate via your tool of choice
  (mkcert, OpenSSL, `dotnet dev-certs`).
- **CDN local mirror** (optional but realistic): `C:\LOCAL_CDN\synergos\registry.json`
  + bundle files. Synergos.UI's build produces these.

## Launch profiles

`Synergos.CMS/Properties/launchSettings.json` defines two profiles:

### IIS Express

```
URL:             http://localhost:4046 + https://localhost:44382
Environment:     Development
Pros:            quick to start from VS
Cons:            locks DLLs on rebuild; doesn't match synergos.local architecture
When to use:     one-off smoke tests, quick boot to verify schema pipeline
```

### Umbraco.Web.UI (Kestrel)

```
URL:             http://synergos.local:5000 + https://synergos.local:5001
                 (from appsettings.Development.json → Kestrel)
Environment:     Development
Pros:            matches CDN/CORS architecture, no DLL locks
Cons:            requires hosts file + cert setup
When to use:     normal dev work, integration with Synergos.UI
```

From VS Code / VS: pick the profile in the run dropdown.
From CLI: `cd Synergos.CMS && dotnet run --launch-profile "Umbraco.Web.UI"`.

## Quick build (no run)

```bash
cd Synergos.CMS
dotnet build Synergos.CMS.csproj
```

Expected output:
```
1 Advertencia(s)   ← NU1902 (Umbraco.Cms 13.13.1 advisory — non-blocking)
0 Errores
```

If the DLL is locked (IIS Express running), add:
```bash
dotnet build Synergos.CMS.csproj -p:CopyBuildOutputToOutputDirectory=false
```
This skips the copy step but still verifies compilation.

## Build + run

```bash
cd Synergos.CMS
dotnet run --launch-profile "Umbraco.Web.UI"
```

Watch the logs for:

```
[INF] Synergos schema mismatch (stored=none, expected=10.0.1). Running pipeline.
[INF] Synergos schema complete (10.0.1).
[INF] ContentSeeder: verifying content tree...
[INF] ContentSeeder: seed complete.
[INF] Content is ready and running!
[INF] Now listening on: https://synergos.local:5001
```

## First-time Umbraco user creation

On first boot with an empty DB, Umbraco prompts for an admin user via an
unattended install (`Umbraco:CMS:Unattended:UpgradeUnattended: true`).
Navigate to `/umbraco` and follow the prompts.

## Day-to-day workflow

```bash
# 1. Start the CMS (leave running)
cd Synergos.CMS
dotnet watch run --launch-profile "Umbraco.Web.UI"

# 2. In a second terminal, make code changes
#    dotnet watch picks them up automatically for Razor/HTML edits
#    For C# changes, `dotnet watch` rebuilds and restarts Kestrel

# 3. Browse to https://synergos.local:5001/umbraco for backoffice
#    or https://synergos.local:5001/ for the public site
```

## VS / VS Code solution

Open `Synergos.CMS/Synergos.CMS.sln` to load the project (and
`Synergos.CMS.Tests` when applicable) in Visual Studio. Also works in VS Code +
C# Dev Kit.

## Troubleshooting

### "Address already in use"

Something already bound `synergos.local:5001`. Find and kill:

```bash
# Windows
netstat -ano | findstr :5001
taskkill /PID <pid> /F

# Mac/Linux
lsof -i :5001
kill <pid>
```

### "File is being used by another process" on build

IIS Express holds DLLs open. Either:
- Stop IIS Express (look for its tray icon on Windows).
- Use the `-p:CopyBuildOutputToOutputDirectory=false` flag to compile without overwriting the locked DLL.
- Switch to the Kestrel launch profile — it releases files on stop.

### Kestrel cert error

`Authentication failed because the remote party sent a TLS alert: 'UnknownCa'`
or similar. Your `synergos-dev.crt` isn't trusted.

- **Windows:** import into `Trusted Root Certification Authorities`.
- **Mac:** `security add-trusted-cert -d -r trustRoot -k ~/Library/Keychains/login.keychain synergos-dev.crt`.
- **Linux:** varies by distro — usually `update-ca-certificates`.

Or switch to the IIS Express profile which uses the Visual Studio dev cert.

### `Synergos.sqlite.db: database is locked`

Another process has the DB open. Typically a leftover `dotnet` or IIS Express.
Kill it.

### "DataType X not found" during schema pipeline

You bumped `SchemaVersion` and the pipeline is running, but an initializer
references a DataType not yet created. Either the phase order is wrong or you
need to add the DataType to `DataTypeInitializer` first.

### Schema pipeline doesn't run on boot

Log shows `Synergos schema up to date (10.0.1).`. The stored version matches.
To force a re-run:
1. Bump `SchemaVersion.Value` in `Schema/Constants/SchemaVersion.cs`, rebuild, restart.
2. Or delete the row from `umbracoKeyValue` where key = `Synergos.Schema.Version`.

## See also

- [`fresh-boot.md`](fresh-boot.md) — clean-slate procedure.
- [`usync.md`](usync.md) — uSync export/import lifecycle.
- [`../configuration/reference.md`](../configuration/reference.md) — all settings.
