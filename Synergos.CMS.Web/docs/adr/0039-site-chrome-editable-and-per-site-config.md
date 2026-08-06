# ADR 0039 — Site Chrome editable + PlatformRoot landing + per-site Configuration folder (Ola 69)

- **Status:** Accepted
- **Date:** 2026-04-26
- **Deciders:** Arquitecto + agente, durante Ola 69
- **Inspirado en (NO copy-paste):** legacy `_archive/fails/Synergos.CMS.epicfail2`
  (`headerConfig`, `footerConfig`, `layoutFolder`, `layoutProfile`)
- **Extiende:** ADR 0017 (Layout system), ADR 0020 (Platform/Settings),
  ADR 0023 (Componentization Layered)

## Context

Tras Olas 60-68 (Forms internal + Search + SEO + Members + Email +
Output cache + Analytics + Comments) el CMS tenía 9 ADRs nuevos y
38 ratificados — pero el editor seguía con tres puntos de fricción
significativos en el flujo de configuración inicial:

1. **PlatformRoot blank**: el wrapper multi-site arrancaba con un
   warning runtime (`No physical template file was found for
   document type with alias platformRoot`) y, peor, no daba al
   editor ninguna pista visual sobre qué hacer con ese nodo.
2. **Chrome del sitio hard-coded**: el `_Layout.cshtml` derivaba
   header del `siteRoot.Children` (nav) + footer estático con
   copyright. Para cambiar el header agregando logo/búsqueda/CTA o
   reorganizar el footer en columnas, había que tocar Razor — no
   era editable.
3. **"De dónde salen estas cosas?"**: el editor abría un siteRoot,
   iba al tab "Orquestación" y veía toggles tipo *"Suprimir alerta
   global"*, *"Suprimir banner global"*, etc. — pero no tenía un
   path claro al nodo que configura esos componentes globales. El
   `settingsRoot` vivía al nivel platform, separado del siteRoot,
   sin link visual.

Auditoría del legacy `Synergos.CMS.epicfail2` reveló:
- **Lecciones a mantener**: folder DocTypes para clarity visual,
  separación page-level vs site-chrome, preset/profile pattern.
- **Anti-patterns a evitar**: duplicar props entre tipos
  (`headerConfig.logoAltText` + `siteSettings.siteLogoAltText`),
  marcar campos "Deprecated — use X" en UI, `PlatformRoot` blank
  sin propósito editorial.

## Decision

Tres entregables coordinados que cierran el flujo editorial inicial
sin replicar los anti-patterns del legacy.

### Parte A — `compSiteChrome` (Olas 69.3 + 69.4)

Composición nueva con **2 BlockGrid slots** sobre el DataType
`DTBlockGridSections` ya existente (reuso — los 148 blocks + 14
layout presets están disponibles para arrastrar):

- `siteHeaderBlocks` (Culture, opcional)
- `siteFooterBlocks` (Culture, opcional)

Aplicada a `siteRoot` (única vez — no se duplica en otros tipos para
evitar el anti-pattern del legacy).

**`_Layout.cshtml`** detecta `siteRoot.Value<BlockGridModel>(...)`:
- Si `siteHeaderBlocks` tiene blocks → emite `<header
  class="syn-site-header--custom">` con
  `Html.GetBlockGridHtmlAsync`.
- Si vacío → mantiene el header default (brand link + nav siteRoot
  children).
- Mismo patrón para footer; `_GlobalFooterNote` se preserva en ambas
  ramas (sigue siendo global).
- `renderCtx.ShowHeader` / `ShowFooter` siguen gobernando si el tab
  Orquestación suprime — cero conflicto con `compPageOrchestration`.

**Backward compat 100%**: sites existentes con slots vacíos siguen
renderizando el chrome default.

### Parte B — `PlatformRoot` template (Ola 69.2)

Cierra el warning runtime + da agency editorial real al wrapper
multi-site (anti-pattern del legacy: nunca dejar un DocType sin
propósito).

Schema (`platformroot.config`):
- `DefaultTemplate=PlatformRoot` + `AllowedTemplates`.
- 2 props nuevos:
  - `welcomeMessage` (TextArea, Culture, opcional) — fallback
    `Platform.WelcomeFallback` dictionary.
  - `introBody` (BlockGrid, Culture, opcional) — si llenado,
    reemplaza la lista por defecto de sitios con diseño
    editor-driven.

Razor (`Views/PlatformRoot.cshtml`):
- `Layout=null` por diseño — platform root NO es un sitio en sí
  mismo, no debe heredar chrome del siteRoot.
- Pipeline:
  1. Si `introBody` tiene blocks → renderiza ese cuerpo (override
     editorial total).
  2. Si vacío Y no hay `siteRoot` children publicados → mensaje
     editor-facing.
  3. Si hay `siteRoot` children → cards con `siteDisplayName` +
     `canonicalHostname` + URL.
- `meta robots`: `noindex` si no hay sitios, `index,follow` si hay.
- 3 dictionary keys (Platform.*) con fallback inline en es-CO.

### Parte C — `siteConfigFolder` per-site Configuration UX (Ola 69.5)

Folder DocType nuevo que vive como child de cada siteRoot. Da al
editor un path **explícito** desde el nodo del sitio hasta sus
configuraciones.

Schema (`siteconfigfolder.config` — GUID `6b2b64e3`):
- `Folder=Platform`, `Variations=Nothing` (contenedor),
  `AllowAtRoot=False`, `IsListView=True`.
- Cero compositions, cero properties — estructura pura.
- Allowed children: `siteConfigSettings` + `themeSettings` +
  `featureFlagsSettings` (los 3 tipos que ya existían bajo
  `settingsRoot` platform-level).

Wire: `siteRoot.Structure` agrega `siteConfigFolder` primero
(SortOrder 0). Page types pasan a SortOrder 10-16.

**Cero cambio en resolvers**: `HostBasedBrandingProvider`,
`DefaultBrandThemeProvider` y `DefaultGlobalComponentResolver` ya
usaban `DescendantsOrSelfOfType` desde `GetAtRoot` — encuentran las
settings sin importar dónde estén en el árbol. Ambos paths conviven:

- **Per-site (recomendado, nuevo)**: `Platform Root → oe (siteRoot)
  → Configuración (siteConfigFolder) → Site Settings + Theme +
  Feature Flags`.
- **Platform-shared (legacy, sigue válido)**: `Platform Root →
  settingsRoot → Site Settings + Theme + Feature Flags`. Útil para
  configs cross-site que aplican a varios siteRoots por matching de
  brandKey/canonicalHostname.

### Parte D — Build hygiene (Ola 69.1)

Cleanup paralelo:
- `AccountController.SafeReturnUrl` → `static` (CA1822).
- `FileSystemCommentRepository.AddAsync` param `newComment` →
  `comment` para match con interface (CA1725); variable local
  renombrada a `persisted`.
- `IGlobalComponentResolver.cs`: agregados `<param>` XML tags
  faltantes en `CfgBanner` (5 params) y `CfgModal` (6 params)
  cerrando los 11 warnings CS1573 que aparecían cada build.

## Consequences

**Positivas:**

- **Flujo editorial autocontenido**: editor crea siteRoot → crea
  Configuración (folder) → crea Site Settings/Theme/Feature Flags
  dentro. Todo el contexto del sitio queda visualmente debajo del
  siteRoot. El tab "Suprimir X" del Orquestación tab tiene sentido
  porque el editor ve el "X" configurado en
  `oe → Configuración → Site Settings → Components`.
- **Chrome custom sin código**: editor arrastra cualquier de los 148
  blocks existentes a `siteHeaderBlocks` / `siteFooterBlocks`.
  Diferentes siteRoots pueden tener diferentes chromes.
- **PlatformRoot funcional**: visitante que llega a `/` en un deploy
  multi-site ve un selector de sitios (defecto) o un cuerpo
  editor-driven (override). Cierra el warning runtime que existía
  desde Ola 56+.
- **Backward compat estricta**: sites existentes que NO usen los
  nuevos slots/folders siguen funcionando idéntico. Los resolvers
  encuentran settings tanto bajo `settingsRoot` (legacy) como bajo
  `siteConfigFolder` (nuevo).
- **Anti-patterns del legacy evitados**:
  - Cero duplicación de props (chrome NO se replica en otros tipos
    además de siteRoot).
  - Cero campos "Deprecated — use X" visibles al editor.
  - PlatformRoot ya no es blank.
- **Inspiración aplicada del legacy** (lecciones positivas):
  - Folder DocType para clarity visual (`siteConfigFolder` =
    análogo del legacy `layoutFolder` pero a nivel siteRoot).
  - Separación estructural (`siteRoot` no acumula campos de chrome
    — los recibe via composición).

**Negativas:**

- **Duplicación opcional**: si un editor crea `siteConfigSettings`
  bajo `settingsRoot` (legacy) Y también bajo
  `siteConfigFolder` (nuevo) para el mismo siteRoot, los resolvers
  encontrarán ambos y el matching depende del orden de iteración
  del published cache. Documentar la convención: "una sola
  configuración por siteRoot, idealmente bajo Configuración del
  propio siteRoot".
- **Sin migración automática**: sites existentes con configs en
  `settingsRoot` no se mueven solos. El editor las puede arrastrar
  manualmente desde el backoffice si quiere unificar bajo el
  patrón nuevo. KISS — no hay migrator code.
- **`compSiteChrome` aplicado solo a siteRoot**: si un futuro
  feature requiere overridir el chrome a nivel page (ej.
  `pageLanding` con header custom), habrá que aplicar la
  composition también ahí. Diferido — primer pase cubre el 90% de
  casos.
- **No layout profiles** (preset reusable): el legacy tenía
  `layoutProfile` referenciado desde `siteSettings.defaultProfile`.
  Hoy cada siteRoot configura su chrome independiente. Si surge
  necesidad de "header default" reusable, agregar
  `siteChromeProfile` DocType + property picker en compSiteChrome
  en futura ola.

**Neutras:**

- 5 GUIDs nuevos (1 ContentType + 1 Template + 3 Properties).
  Verificación cuádruple OK.
- 1 nueva ContentType (`siteConfigFolder`), 1 nueva Composition
  (`compSiteChrome`), 1 nuevo Template (`PlatformRoot`).
- 0 nuevos paquetes NuGet.
- 0 cambios en seams o resolvers C#.

## Alternatives considered

- **Header/Footer como props discretas en compSiteChrome** (logo
  picker, ctaLabel, copyrightText, navigationGroup picker —
  modelo del legacy): descartado. Anti-pattern claro del legacy:
  cada nuevo feature requiere prop nueva, duplicación entre tipos
  cuando aparece la siguiente abstracción. BlockGrid es más
  flexible y reusa toda la infraestructura Layout Composer.
- **`compSiteChrome` aplicado a múltiples tipos** (siteConfigSettings,
  themeSettings, etc.): descartado. Anti-pattern del legacy.
  Una sola fuente de verdad — el siteRoot.
- **`siteConfigFolder` con un template propio que liste las
  settings**: descartado por scope. La vista IsListView del
  backoffice ya da al editor un listado funcional sin código.
- **Migrar automáticamente `settingsRoot` → `siteConfigFolder`**:
  descartado. ADR 0013 (no automatic seeders) + ADR 0008 (uSync
  hybrid SSoT). El editor decide.
- **Renombrar `settingsRoot` a `platformConfigFolder` para
  paralelo**: descartado por backward compat. `settingsRoot` ya
  está en producción.

## Implementation summary (Ola 69, 8 commits)

| Commit | Hash | Foco |
|---|---|---|
| `chore(ola-69.1)` | `897e355` | Build warnings cleanup (CA1822 + CA1725 + 11 CS1573) |
| `chore(ola-69.1b)` | `718297f` | UTF-8 BOM normalization en 8 vistas Razor |
| `feat(ola-69.2)` | `9f9ad24` | PlatformRoot template + schema (welcomeMessage + introBody) + DefaultTemplate |
| `feat(ola-69.3)` | `cc71718` | `compSiteChrome` composition (2 BlockGrid slots) + apply a siteRoot |
| `feat(ola-69.4)` | `73cd420` | `_Layout.cshtml` consume compSiteChrome con fallback al chrome default |
| `feat(ola-69.5)` | `7fe566d` | `siteConfigFolder` DocType — carpeta "Configuración" bajo siteRoot |
| `fix(ola-69.5)` | `fb3c5c3` | siteRoot Structure incluye siteConfigFolder (wire del commit anterior) |
| `docs(ola-69.6)` | (este) | ADR 0039 + index README |

## References

- ADR 0017 — Layout system (BlockGrid Sections — DataType reusado)
- ADR 0020 — Platform/Settings split (settingsRoot legacy preservado)
- ADR 0023 — Componentization Layered (compSiteChrome es L1
  composition, _Layout es L5 wiring)
- ADR 0026 — Brand runtime completion (HostBasedBrandingProvider
  encuentra siteConfigSettings sin importar dónde esté)
- Memoria `feedback_composition_design_solid` — filtro 3 preguntas
  pasado para crear compSiteChrome (1 reuso BlockGrid existente,
  2 cero duplicación en otros tipos, 3 si se descarta no rompe
  anything).
- Memoria `feedback_no_preassigned_guids_usync` — todos los GUIDs
  generados con `[guid]::NewGuid()` y verificados cuádruple.
- Legacy referenciado para inspiración (NO copia):
  `_archive/fails/Synergos.CMS.epicfail2/uSync/v9/ContentTypes/`
  (`headerconfig.config`, `footerconfig.config`, `layoutfolder.config`,
  `layoutprofile.config`).
