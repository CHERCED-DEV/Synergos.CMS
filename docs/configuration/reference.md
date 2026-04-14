# Configuration Reference

Every `Synergos:*` section in `appsettings.json`, explained. All values are
bound via `IOptions<T>` to strongly-typed classes in `Synergos.CMS/Configuration/`.

## Root layout

```json
{
  "AllowedHosts": "synergos.local;*.synergos.local;localhost",
  "Serilog":      { /* Serilog.Sinks.Async config */ },
  "Umbraco":      { "CMS": { /* Umbraco 13 native config */ } },
  "Synergos": {
    "Runtime":        { /* web host + CORS */ },
    "StaticAssets":   { /* CDN origin + registry */ },
    "ElementServers": { /* per-element dev overrides */ },
    "FlowEngine":     { /* webhook secret + timeout */ },
    "Security":       { /* API key */ },
    "Seed":           { /* brand + pages + theme for first-boot seeder */ },
    "Cache":          { /* dictionary cache TTLs */ }
  },
  "uSync":           { /* uSync package settings */ },
  "ConnectionStrings": { /* umbracoDbDSN */ }
}
```

## `Synergos:Runtime` → `RuntimeSettings`

Web host + CORS configuration. Kestrel endpoints are declared separately
(see `appsettings.Development.json` → `Kestrel`).

```json
"Runtime": {
  "WebHost":  "synergos.local",
  "HttpPort": 5000,
  "HttpsPort": 5001,
  "AllowedOrigins": [
    "https://static.synergos.local"
  ]
}
```

| Field | Purpose |
|---|---|
| `WebHost` | Host name the app expects to serve. Used for diagnostic logging. |
| `HttpPort` / `HttpsPort` | Documented ports (Kestrel endpoints use these via appsettings.Development.json). |
| `AllowedOrigins` | List of origins allowed by the `SynergosPolicy` CORS policy — primarily the CDN origin. |

## `Synergos:StaticAssets` → `StaticAssetsSettings`

The **single source of truth** for CDN configuration. See
[`../rendering/cdn-integration.md`](../rendering/cdn-integration.md) for deep
dive.

```json
"StaticAssets": {
  "Origin":              "https://static.synergos.local",
  "UiBasePath":          "/synergos",
  "UiFramework":         "angular",
  "UiSlot":              "latest",
  "RegistryLocalPath":   "C:\\LOCAL_CDN",
  "RegistryManifest":    "/synergos/registry.json",
  "ClientHosted":        [ "elementCompHero" ]
}
```

## `Synergos:ElementServers` → `ElementServersSettings`

Per-element static overrides used during dev. Hot-reload equivalent:
`__dev-servers.json`.

```json
"ElementServers": {
  "Overrides": {
    "hero": "http://localhost:4202",
    "card": "http://192.168.1.42:4203"
  }
}
```

| Field | Purpose |
|---|---|
| `Overrides` | `Dictionary<string, string>` — element name → dev server base URL. Empty in production. |

## `Synergos:FlowEngine` → `FlowEngineSettings`

Flow Engine webhook configuration. Webhook payloads are HMAC-SHA256 signed
with `WebhookSecret`; `HttpFlowWebhookDispatcher` POSTs them to each
FlowDefinition's `webhookTargetUrl`.

```json
"FlowEngine": {
  "WebhookSecret":    "synergos-flow-engine-dev-secret",
  "WebhookTimeoutMs": 10000
}
```

| Field | Purpose |
|---|---|
| `WebhookSecret` | HMAC secret shared with Synergos.API. Never commit production secrets — override per environment. |
| `WebhookTimeoutMs` | HTTP timeout. Default 10000 (10s). |

## `Synergos:Security` → `SecuritySettings`

API key for Synergos API controllers protected by `ApiKeyAuthFilter`.

```json
"Security": {
  "ApiKey": ""
}
```

Leave empty in local dev (filter is disabled when empty); set per environment.

## `Synergos:Seed` → `SeedConfig`

First-boot content seeder. See [`seed.md`](seed.md) for the full structure
and overriding patterns.

```json
"Seed": {
  "Enabled": true,
  "PlatformName": "Synergos Platform",
  "SiteName": "Synergos",
  "SiteDisplayName": "Synergos",
  "SiteTagline": "…",
  "SiteTaglineExt": "…",
  "SiteCulture": "es-CO",
  "ContactEmail": "contact@example.com",
  "ContactPhone": "+1 (555) 000-0000",
  "ContactAddress": "City, Country",
  "FacebookUrl": "…", "LinkedInUrl": "…", "TwitterUrl": "…", "YouTubeUrl": "…",
  "SeoTitleSuffix": " | Synergos",
  "SeoDefaultDescription": "…",
  "GlobalSeoDescription": "…",
  "FormRecipientEmail": "contact@example.com",
  "HeaderCtaLabel": "Contáctanos",
  "Theme": { /* SeedTheme — colors, fonts, spacing */ },
  "Pages": [ /* SeedPage[] — page specs */ ]
}
```

Set `Enabled: false` to skip all content seeding (useful in staging when you
want the schema but no brand content).

## `Synergos:Cache` → `CacheSettings`

TTL for the dictionary cache (dictionary items are near-static, so they cache
aggressively).

```json
"Cache": {
  "DictionaryMinutes":      10,
  "DictionaryMissMinutes":  1
}
```

| Field | Purpose |
|---|---|
| `DictionaryMinutes` | How long a found dictionary value is cached. Default 10 minutes. |
| `DictionaryMissMinutes` | How long a "key not found" is cached (avoids hammering `ILocalizationService`). Default 1 minute. |

## Environment overrides

.NET's standard layering applies:

```
appsettings.json
  ← overridden by
appsettings.Development.json (when ASPNETCORE_ENVIRONMENT=Development)
  ← overridden by
appsettings.Local.json (git-ignored — per developer)
  ← overridden by
environment variables (e.g. Synergos__Seed__SiteName=FooCorp)
  ← overridden by
user secrets (dotnet user-secrets, dev only)
```

Use `appsettings.Local.json.example` as a template for developer-specific
overrides. `appsettings.Local.json` is `.gitignore`d.

## Adding a new settings section

1. Create `Configuration/MyFeatureSettings.cs`:
   ```csharp
   namespace Synergos.CMS.Configuration;

   public sealed class MyFeatureSettings
   {
       public const string SectionPath = "Synergos:MyFeature";

       public int SomeTimeoutMs { get; init; } = 5000;
       public string[] AllowedIds { get; init; } = [];
   }
   ```

2. Register in `Program.cs`:
   ```csharp
   builder.Services.Configure<MyFeatureSettings>(
       builder.Configuration.GetSection(MyFeatureSettings.SectionPath));
   ```

3. Add the defaults to `appsettings.json` (so new installs boot with sensible values):
   ```json
   "Synergos": {
     "MyFeature": {
       "SomeTimeoutMs": 5000,
       "AllowedIds": []
     }
   }
   ```

4. Consume via `IOptions<MyFeatureSettings>` or `IOptionsSnapshot<T>` (for
   per-scope rebinding) in any service, controller, or view component.

5. If the feature affects runtime behavior (not just config wiring), update
   `CLAUDE.md` §6 and this doc.

## Non-Synergos sections

### `Umbraco:CMS`

Umbraco 13 native configuration. Key values:

- `Umbraco:CMS:Global:DefaultUILanguage` — "es". Sets `CurrentUICulture` at startup and per-request.
- `Umbraco:CMS:Global:Id` — installation-unique GUID.
- `Umbraco:CMS:Unattended:UpgradeUnattended: true` — auto-runs Umbraco upgrades without manual confirmation.
- `Umbraco:CMS:WebRouting:DisableFindContentByIdPath: true` — prevents `/123` style URLs from matching.

### `uSync:Settings`

- `ImportOnStartup: false` — **Keep this false.** The schema pipeline is our
  source of truth; uSync is a backup. See [`../operations/usync.md`](../operations/usync.md).

### `ConnectionStrings`

SQLite by default. Swap to SQL Server / MySQL by changing the provider and
connection string — Umbraco handles the rest.

## See also

- [`seed.md`](seed.md) — SeedConfig + SeedTheme + SeedPage deep dive.
- [`../operations/build-and-run.md`](../operations/build-and-run.md) — launch profiles, Kestrel vs IIS.
- [`../operations/fresh-boot.md`](../operations/fresh-boot.md) — clean-slate boot procedure.
