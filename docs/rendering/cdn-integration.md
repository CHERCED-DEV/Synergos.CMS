# CDN Integration

How Synergos CMS resolves and emits references to CDN-hosted web components.

## Moving parts

```
 appsettings.json
 ├── Synergos:StaticAssets         ← origin, UI base path, framework, slot, ClientHosted aliases
 └── Synergos:ElementServers        ← per-element static overrides for dev

 C:\LOCAL_CDN\synergos\
 ├── registry.json                  ← CDN manifest: alias → tag → implementations map
 ├── __dev-servers.json             ← runtime dynamic overrides (hot-reloaded)
 └── runtime/angular/latest/
     └── import-map.json            ← shared chunks (ng-core, rxjs, etc.) import map

 Code
 ├── Application/Rendering/
 │   ├── StaticUrlBuilder.cs            ← pure URL builder
 │   ├── IArtifactResolver.cs           ← interface
 │   └── IElementUrlResolver.cs         ← interface
 └── Infrastructure/Cdn/
     ├── FileSystemArtifactResolver.cs      ← reads registry.json, hot-reloads
     └── FileSystemElementUrlResolver.cs    ← reads __dev-servers.json, hot-reloads

 Views
 └── Views/MacroPartials/*/Cdn*.cshtml  ← 49 CDN macros, each injects IElementUrlResolver
```

## `StaticAssetsSettings` — the single source of truth

```json
"Synergos": {
  "StaticAssets": {
    "Origin":              "https://static.synergos.local",
    "UiBasePath":          "/synergos",
    "UiFramework":         "angular",
    "UiSlot":              "latest",
    "RegistryLocalPath":   "C:\\LOCAL_CDN",
    "RegistryManifest":    "/synergos/registry.json",
    "ClientHosted":        [ "elementCompHero" ]
  }
}
```

| Field | Purpose | Editable at runtime? |
|---|---|---|
| `Origin` | Browser-visible URL of the CDN (scheme + host, no trailing slash) | No — startup only |
| `UiBasePath` | Base path under Origin for bundles (`/synergos/hero/angular/latest/main.js`) | No |
| `UiFramework` | Framework name path segment (`angular` now; could be `react`, `svelte`) | No |
| `UiSlot` | Version slot: `latest` (dev/staging) or `v{N}` (production pinned) | No |
| `RegistryLocalPath` | Server-side filesystem path for `registry.json` + `__dev-servers.json` | No — watchers set at startup |
| `RegistryManifest` | Relative path to the manifest from `RegistryLocalPath` | No |
| `ClientHosted` | Element aliases that render as Custom Elements instead of SSR partials | No |

**Do NOT store these in Umbraco `GlobalSettings`.** They're infrastructure,
not editorial. The old `cdnBaseUrl`/`cdnRegistryPath`/`clientHostedElements`
fields in GlobalSettings were dead and have been removed. Don't add them back.

## URL construction

`StaticUrlBuilder` is the only place that concatenates CDN URLs. Three methods:

```csharp
// Canonical element bundle URL (used by FileSystemArtifactResolver + fallback in ElementUrlResolver)
string ElementBundle(string elementName)
    => $"{_uiBase}/{elementName}/{_framework}/{_slot}/main.js";

// Arbitrary UI bundle path (e.g. for a shared chunk)
string UiBundle(string artifactPath)
    => _uiBase + "/" + artifactPath.TrimStart('/');

// Raw static asset (outside the UI tree — fonts, images, etc.)
string Asset(string path)
    => _origin + "/" + path.TrimStart('/');

// Runtime directory base (for shared chunks)
string RuntimeBase()
    => $"{_uiBase}/runtime/{_framework}";
```

**Never** build a CDN URL by string concatenation in a view, controller, or
service. Inject `StaticUrlBuilder` or `IElementUrlResolver`.

## `IElementUrlResolver` — resolution priority

Consumed by all CDN macros. Three-level fallback:

```csharp
ResolveBundle("hero")
  │
  1. Dynamic override from __dev-servers.json
  │    if "hero" is listed → returns "<url>/main.js"
  ├─ otherwise
  2. Static override from ElementServersSettings.Overrides
  │    if appsettings has "hero": "..." → returns "<url>/main.js"
  └─ otherwise
  3. Canonical CDN URL via StaticUrlBuilder.ElementBundle("hero")
     → https://static.synergos.local/synergos/hero/angular/latest/main.js
```

### `__dev-servers.json` — hot-reloadable dev overrides

Written by Synergos.UI dev tools when an element runs locally. Format:

```json
{
  "servers": {
    "hero":  { "url": "http://localhost:4202", "framework": "angular", "pid": 12345, "startedAt": "2026-04-13T10:30:00Z" },
    "card":  { "url": "http://localhost:4203", "framework": "angular", "pid": 12346, "startedAt": "2026-04-13T10:31:00Z" }
  }
}
```

`FileSystemElementUrlResolver` watches this file with a `FileSystemWatcher` and
a 500 ms debounce (constant `DevServersReloadDebounceMs`). Changes propagate
without CMS restart. Deleting the file clears all dynamic overrides.

### `ElementServersSettings.Overrides` — static dev overrides

For overrides that shouldn't hot-reload (e.g. a URL pointing to a coworker's
machine). Lives in `appsettings.Development.json`:

```json
"Synergos": {
  "ElementServers": {
    "Overrides": {
      "hero": "http://192.168.1.42:4202"
    }
  }
}
```

## `IArtifactResolver` — CDN registry

`registry.json` is the CDN manifest produced by Synergos.UI's build pipeline.
Example structure (simplified):

```json
{
  "generated": "2026-04-13T10:00:00Z",
  "version": "1.0.0",
  "baseUrl": "https://static.synergos.local",
  "elements": [
    {
      "alias": "elementCompHero",
      "tag": "synergos-hero",
      "name": "hero",
      "tier": "module",
      "implementations": {
        "angular": {
          "latest": "1.4.2",
          "v1":     "1.0.0"
        }
      }
    }
  ]
}
```

`FileSystemArtifactResolver`:
- Loads on startup and swaps atomically (volatile dictionary).
- Hot-reloads on file changes (same 500 ms debounce pattern).
- If missing/corrupt → logs a warning and keeps the previous state (or empty on startup → all elements fall back to SSR).

Public API:

```csharp
bool IsClientHosted(string alias);         // uses ClientHosted config
ArtifactInfo? ResolveByAlias(string alias); // metadata or null
IEnumerable<string> GetClientStyleUrls();   // CSS URLs from client-hosted artifacts
```

## Emitting the script + element tag (the macro pattern)

Every CDN macro (in `Views/MacroPartials/<Family>/Cdn<Name>.cshtml`) follows
this template:

```razor
@inherits Umbraco.Cms.Web.Common.Macros.PartialViewMacroPage
@using System.Text.Json
@using System.Text.Json.Serialization
@using Synergos.CMS.Application.Cdn.Configs
@inject Synergos.CMS.Application.Rendering.IElementUrlResolver ElementUrl
@inject Synergos.CMS.Domain.Services.IDictionaryCache Dict
@{
    var p = Model.MacroParameters;
    string? Get(string key) => p.ContainsKey(key) ? p[key]?.ToString() : null;

    var opt = new JsonSerializerOptions {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase
    };
    var translations = Dict.GetAll();

    var cfg = JsonSerializer.Serialize(new MyCdnConfig(
        Title:        Get("title"),
        Subtitle:     Get("subtitle"),
        // …
        Translations: translations
    ), opt);
}
<script src="@ElementUrl.ResolveBundle("myElement")" type="module" defer></script>
<synergos-my-element config='@cfg' class="sg-cdn sg-cdn--my-element"></synergos-my-element>
```

Each macro:
- Injects `IElementUrlResolver` + `IDictionaryCache`.
- Builds a strongly-typed config DTO from `Application/Cdn/Configs/`.
- Includes the full dictionary under `translations` so the element has i18n.
- Uses `class="sg-cdn sg-cdn--<name>"` so site-wide CSS can target CDN mount points.

## `import-map.json` — shared chunks

The head of the page injects an import map so multiple CDN elements can share
common chunks (rxjs, ng-core, etc.) without duplicating them. `LayoutHeadViewComponent`:

1. Reads `RegistryLocalPath + UiBasePath + /runtime/{framework}/latest/import-map.json`.
2. Falls back to the newest versioned subfolder if `latest/` is missing.
3. Substitutes the `__BASE_URL__` placeholder with `StaticAssetsSettings.Origin`.
4. Normalizes `integrity` keys from bare specifiers to full URLs.
5. Emits `<script type="importmap">…</script>` in `<head>`.

If the import map is missing, elements still load — they bundle their deps.

## Troubleshooting

### Element bundle 404s in browser

1. Check `IElementUrlResolver.ResolveBundle("yourElement")` in a debugger or log.
2. If resolving to `http://localhost:4xxx`, check `__dev-servers.json` contents.
3. If resolving to `https://static.synergos.local/…`, check that the CDN server
   is running and the URL path matches the file layout.

### Registry never loads

- Confirm `Synergos:StaticAssets:RegistryLocalPath` points to a directory that
  exists at app startup.
- Check logs for `"CDN registry not found at {Path}"` — that means the
  `registryLocalPath + registryManifest` combination points to a missing file.
- Check for a "CDN registry at X is empty or malformed" warning — malformed JSON.

### Dev override doesn't hot-reload

- Confirm `__dev-servers.json` lives alongside `registry.json` (same directory).
- Confirm the `FileSystemWatcher` logged "FileSystemWatcher active on X" at
  startup. If not, the directory may not exist yet.
- Some editors write in ways that skip `Changed` events — save with a trailing
  newline or use `echo > __dev-servers.json` style writes.

### Import map errors like "Integrity key is not a valid URL"

`LayoutHeadViewComponent.NormalizeImportMap` resolves these, but only if the
keys in the `integrity` block match filenames in the `imports` block. Ensure
the build tool emits matching keys.

## See also

- [`overview.md`](overview.md) — the two rendering modes.
- [`macros.md`](macros.md) — macro patterns.
- [`../configuration/reference.md`](../configuration/reference.md) — full appsettings reference.
