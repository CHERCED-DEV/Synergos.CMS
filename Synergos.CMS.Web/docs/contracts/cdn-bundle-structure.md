# CDN bundle structure contract

- **Contract version:** v1
- **Owner:** Joint (CDN team produces, CMS consumes)
- **Status:** Canónico — Cap-280 Batch A (Ola 282b).

## Premisa

El CDN team publica bundles UI hidratables (Web Components
`<synergos-*>`) en una estructura **filesystem-safe + framework-aware
+ versionada**. El CMS consume vía `IBundleRegistryClient` (ADR 0012)
que resuelve por path canónico y emite el `<script>` tag con SRI
integrity.

Este doc define el contract autoritativo. **Cualquier bundle que NO
matchee este shape se considera "fuera del contract"** — el client
puede negarse a servirlo o tratarlo como integration legacy.

## Estructura de directorios

```
{cdn-root}/
└── synergos/
    ├── registry.json                       (opcional — global index)
    └── {tag}/                              ej. synergos-column
        └── {framework}/                    angular | react | svelte | vanilla
            ├── latest/                     pointer mutable a la versión actual
            │   ├── main.js
            │   ├── main.js.map             (opcional — sourcemap)
            │   ├── manifest.json
            │   └── meta.json               (opcional — editorial metadata)
            ├── 1.0.0/                      versión inmutable
            │   ├── main.js
            │   ├── manifest.json
            │   └── meta.json
            ├── 1.0.5/
            │   └── ...
            └── 1.1.0/
                └── ...
```

### Reglas de naming

1. **`{tag}` = exactamente el custom element tag** que el bundle define
   en `customElements.define(...)`. Ej.: si el bundle hace
   `customElements.define('synergos-column', ...)`, la carpeta es
   `synergos-column/`. **Lowercase con guión**, igual que el tag.
2. **`{framework}` ∈ { angular, react, svelte, vanilla }**. Lowercase.
   Solo estos 4 oficiales. Otros frameworks deben proponerse via ADR.
3. **`{version}` semver `MAJOR.MINOR.PATCH`** (sin prefijo `v`).
   `latest` es el único alias permitido — siempre apunta a la versión
   más reciente publicada.
4. **`main.js` es el único entry point obligatorio**. CSS embebido por
   convención (CSS-in-JS o shadow DOM). Si el componente NECESITA un
   `.css` separado (ej. fonts loaded), declararlo en el manifest.

## `manifest.json` — schema canónico

**Obligatorio**. El client lo lee primero antes de servir el `main.js`.

```json
{
  "tag": "synergos-column",
  "framework": "angular",
  "version": "1.0.5",
  "main": "main.js",
  "integrity": "sha384-1RnT5Sru+Yi8BZx9+kn3EDr+xz8gEZ5l74kA0FhxFQ2OIp7fzkcvp+JF",
  "size": 3072,
  "createdAt": "2026-04-29T10:00:00Z",
  "dependencies": [],
  "peerDependencies": {
    "synergos-runtime": "^1.0.0"
  },
  "css": [],
  "minSynergosBridge": "1.0.0"
}
```

### Campos

| Campo | Required | Descripción |
|---|---|---|
| `tag` | sí | Custom element tag. Debe matchear el folder name. |
| `framework` | sí | Uno de `angular`/`react`/`svelte`/`vanilla`. |
| `version` | sí | Semver `MAJOR.MINOR.PATCH` exacto. **NO** uses `latest`. |
| `main` | sí | Filename relativo del entry point. Default `main.js`. |
| `integrity` | sí | SRI hash `sha384-{base64}` calculado sobre el contenido de `main`. El CMS emite `<script integrity="...">` defendiéndose contra tampering. |
| `size` | recomendado | Bytes del `main.js`. Útil para budgets de performance. |
| `createdAt` | recomendado | ISO 8601 UTC. Para drift detection. |
| `dependencies` | sí (puede ser `[]`) | Otros bundles `synergos-X` que este componente necesita cargados antes. Array de tags. |
| `peerDependencies` | recomendado | Versions del runtime / shared libs. |
| `css` | sí (puede ser `[]`) | Filenames `.css` adicionales si los hay. |
| `minSynergosBridge` | recomendado | Versión mínima del `window.synergos` que este bundle necesita (ver `host-bridge.md`). |

### Generar el `integrity` hash

PowerShell:
```powershell
$bytes = [System.IO.File]::ReadAllBytes("main.js")
$sha384 = [System.Security.Cryptography.SHA384]::Create().ComputeHash($bytes)
$base64 = [Convert]::ToBase64String($sha384)
"sha384-$base64"
```

Bash:
```bash
echo "sha384-$(openssl dgst -sha384 -binary main.js | openssl base64 -A)"
```

## `meta.json` — schema (opcional)

Editorial / runtime metadata, separado del manifest para que el client
pueda evitar fetch en hot path. Solo el admin dashboard / catalog
necesita leerlo.

```json
{
  "tag": "synergos-column",
  "displayName": "Column",
  "description": "Layout primitive — distributes children in a vertical column with configurable gap and align.",
  "category": "primitive",
  "tags": ["layout", "container"],
  "allowedRoles": [],
  "perfTags": ["light", "ssr-safe"],
  "lastUpdatedBy": "ui-team@synergos",
  "documentationUrl": "https://docs.synergos.example.com/elements/synergos-column"
}
```

## `registry.json` raíz — opcional pero recomendado

Index global de TODOS los bundles disponibles. Permite al CMS
descubrir el catálogo sin crawling del filesystem.

Path: `{cdn-root}/synergos/registry.json`.

```json
{
  "contractVersion": "1.0.0",
  "generatedAt": "2026-04-29T10:00:00Z",
  "bundles": [
    {
      "tag": "synergos-column",
      "frameworks": {
        "angular": {
          "latest": "1.0.5",
          "versions": ["1.0.0", "1.0.5"]
        }
      }
    },
    {
      "tag": "synergos-accordion",
      "frameworks": {
        "angular": {
          "latest": "1.2.1",
          "versions": ["1.0.0", "1.1.0", "1.2.1"]
        },
        "react": {
          "latest": "1.0.0",
          "versions": ["1.0.0"]
        }
      }
    }
  ]
}
```

Si **no se publica registry.json**, el client cae a "discovery por
path direct": cuando un SynHost partial necesita el bundle X, hace
GET al manifest del path canónico. No requiere index global.

## Cache-Control policies

El CMS (Local CDN endpoint en Cap-280 Batch A) y la CDN remota deben
aplicar:

| Path | Cache-Control | Por qué |
|---|---|---|
| `synergos-X/{fw}/{semver}/*` | `public, max-age=31536000, immutable` | Versión fija — nunca cambia. Cache 1 año seguro. |
| `synergos-X/{fw}/latest/*` | `public, no-cache, must-revalidate` | Pointer mutable — browser revalida cada request, devuelve 304 si no cambió. |
| `registry.json` | `public, max-age=300, must-revalidate` | Index — refresh cada 5 min para descubrir bundles nuevos. |

CORS: `Access-Control-Allow-Origin: *` para todos. Los bundles son
públicos por naturaleza (público = web).

## Deprecation lifecycle

Cuando un bundle pasa a deprecated:

1. **No se elimina** del CDN (otros sites pueden seguir consumiendo).
2. Se agrega `"deprecated": true, "deprecatedReason": "..."` al
   manifest.
3. La carpeta `latest/` deja de actualizarse (apunta a la última
   versión válida pre-deprecation).
4. Después de 6 meses sin uso (CDN access logs), se puede physical
   delete.

## Bundle size budget

Por bundle (`main.js` minified + gzipped):

| Tier | Limit | Aplicable |
|---|---|---|
| Primitive (button, column, badge) | < 5 KB | Layout / form / display básicos |
| Composition (accordion, modal, gallery) | < 15 KB | Multi-element compounds |
| Module (banner-slider, data-table) | < 50 KB | Stateful + complex interactions |
| Experience (insight-explorer) | < 200 KB | Full-page apps |

Bundles que excedan el tier deben justificarse via ADR + setting
explícito `Synergos:Cdn:AllowOversizedBundles=true`.

## Implementation references

- Local CDN endpoint: `Program.cs` static-files middleware con smart
  cache (Cap-280 Batch A, ADR 0089).
- HttpBundleRegistryClient: `Synergos.CMS.Web/Services/HttpBundleRegistryClient.cs`
  (Cap-280 Batch B, futuro).
- Stub fallback: `Synergos.CMS.Web/Services/StubBundleRegistryClient.cs`
  (active mientras la CDN remota o local no esté configurada).

## References

- ADR 0012 — CDN integration consumed (foundation).
- ADR 0015 — Framework-agnostic component naming.
- ADR 0083 — CMS↔UI alignment via contracts.
- ADR 0089 — Cap-280 CDN integration end-to-end (futuro).
- `host-bridge.md` — `window.synergos` shape consumido por bundles.
- `dom-events.md` — CustomEvents que los bundles emiten.
