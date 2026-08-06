# ADR 0040 — Gran Consolidación Arquitectónica: Theme + Chrome + Configuration + Transversal Selectors + Brand inheritance pure (Ola 71)

- **Status:** Accepted
- **Date:** 2026-04-26
- **Deciders:** Arquitecto + agente, durante Ola 71 — refactor disparado
  por crítica directa del arquitecto: "estoy viendo que las páginas
  tienen las mismas propiedades que los siteRoots y aquí también en las
  configuraciones empiezo a ver el mismo patrón... no siento que estemos
  haciendo bien las cosas. Necesito que hagamos esto lo más usable
  posible."
- **Supersede parcial:** ADR 0022 (Page Composition Standard cascade —
  el primer salto page→siteRoot del theme triplet desaparece), ADR 0023
  (Componentization Layered — compTransversalSelectors es nueva capa),
  ADR 0026 (HostBasedBrandingProvider — iteraba siteConfigSettings,
  ahora itera siteRoot directamente), ADR 0034 + ADR 0039 (chrome
  triadic complete con aside).
- **Habilita futuro:** typed views via ModelsBuilder SourceCodeAuto
  (Ola 72).

## Context — el problema

Tras Olas 60-70 el schema acumuló deuda técnica orgánica:

1. **`compPageTheme` componido por las 4 page types + siteRoot**: editor
   abría cualquier page y veía un tab "Apariencia" con dropdown
   `pageThemeVariant` que confundía: ¿debo configurar aquí o en el
   siteRoot? ¿qué gana?
2. **3 nodos settings separados** (themeSettings + siteConfigSettings +
   featureFlagsSettings) bajo `siteConfigFolder`: el editor preguntaba
   "¿deberían ser un dropdown? no entiendo por qué hay tantos".
3. **`compBranding` componido por los 3 settings nodes**: brandKey
   aparecía literal en 3 lugares editables — duplicación que quebraba
   ADR 0010 (Branding via provider, single source of truth).
4. **Aside missing**: `compSiteChrome` (Ola 69.3) tenía header + footer
   pero NO aside. Crítica directa: "Quiero poder controlar completamente
   la configuración del layout en footer, aside y header. Quiero poder
   pasarle absolutamente todo lo que ellos reciben."
5. **Selección "active" de transversales por flag inline**: editor
   activaba/desactivaba cada nodo del repositorio independientemente
   sin un punto explícito de "qué quiero AHORA". El usuario pidió
   "drop-down" — selector explícito.
6. **Untyped views en todos lados**: `@inherits UmbracoViewPage<dynamic>`
   + `Model.Value<string>("alias")` — sin compile-time check, sin
   autocomplete IDE. Crítica: "deben estar armadas a los templates
   objetivos, con el modelbuilder o lo que estes usando".

## Decision — la consolidación

13 commits coordinados que cierran los 6 problemas + setup ModelsBuilder
para Ola siguiente.

### Parte A — Theme inheritance pura (Olas 71.1 + 71.3)

`compPageTheme` removido de `pageBase`, `pageBasic`, `pageLanding`,
`pageBare`. Permanece SOLO en `siteRoot`. Las pages heredan vía runtime
resolution sin posibilidad de override.

`DefaultPageRenderContextResolver`:
- Helper nuevo `ResolveFromSiteRoot(siteRoot, alias, fallback)` —
  siteRoot ONLY, sin chequear page.
- Theme triplet (`pageThemeVariant`, `pageSurface`, `visualProfile`)
  usa `ResolveFromSiteRoot`.
- `pageContainerType` + `pageSpacingScale` siguen con cascada
  page→siteRoot porque sí pueden override per-page legítimamente
  (landing densa, page de detalle con container narrow).

`_BrandThemeStyle.cshtml`: comentario actualizado documentando
inheritance pure.

### Parte B — ModelsBuilder SourceCodeAuto (Ola 71.4)

`appsettings.json` setea `Umbraco:CMS:ModelsBuilder:ModelsMode =
SourceCodeAuto` + `ModelsNamespace = "Synergos.CMS.Web.PublishedModels"`
+ `ModelsDirectory = "~/umbraco/models"` + `FlagOutOfDateModels = true`.

`.gitignore` (sub-repo) excluye `umbraco/models/*.generated.cs` +
`all.generated.cs` + `models.generated.hash` — son OUTPUT del schema
XML, regenerar local.

Activación: tras siguiente reboot del runtime, Umbraco genera clases
typed en `umbraco/models/`. Las views podrán hacer
`@inherits UmbracoViewPage<SiteRoot>` + `Model.PageThemeVariant`
(autocomplete IDE + compile-time check).

Refactor de views a typed deferido a **Ola 72** — requiere que el
operador haga reboot primero para que los archivos .generated.cs
existan y el IDE los indexe.

### Parte C — Chrome triádico completo (Olas 71.5 + 71.6)

`compSiteChrome` ahora completo:
- `siteHeaderBlocks` (Ola 69.3)
- `siteFooterBlocks` (Ola 69.3)
- `siteAsideBlocks` (NUEVO Ola 71.5) — BlockGrid Sections, Culture,
  opcional. Si vacío → no `<aside>` tag emitido. Si lleno → `<aside>`
  con `Html.GetBlockGridHtmlAsync`.

`compPageOrchestration` += `suppressAside` (Ola 71.6) — flag para que
una page específica oculte el aside aunque el siteRoot tenga bloques.

`_Layout.cshtml` envuelve `<main>` en `<div class="syn-site-body">`
que recibe `--with-aside` modifier cuando hay sidebar — design system
CSS puede usar grid/flex layout main+aside.

### Parte D — Configuration unificada (Olas 71.7 + 71.8)

`siteConfiguration` Document type nuevo. Compone los 3 legacy types
via Compositions — adopta TODOS sus tabs/props sin duplicar XML
(mismo pattern DRY que `transversal*` ⊂ `cfg*` de Ola 70.2):
- themeSettings (Tema tab + paleta + fonts)
- siteConfigSettings (Configuración tab + Componentes globales tab)
- featureFlagsSettings (Feature Flags tab si tiene)

Editor abre el nodo Configuración y ve TODOS los tabs combinados — un
solo lugar para todo.

`siteConfigFolder.Structure` simplifica de 4 children → 2:
- `siteConfiguration` (NUEVO unified)
- `transversalsRepository` (Ola 70.1)

Los 3 legacy DocTypes preservados pero marcados Deprecated en su
Description ("[Legacy desde Ola 71] ... usar siteConfiguration") —
backward compat para sites que ya tenían los nodos.

### Parte E — Selectores explícitos de transversales (Olas 71.9 + 71.10)

`compTransversalSelectors` composition NUEVA con 4 ContentPickers:
- `activeAlertNode` — picker para transversalAlert
- `activeBannerNode` — picker para transversalBanner
- `activeFooterNoteNode` — picker para transversalFooterNote
- `activeModalNode` — picker para transversalModal

Aplicada a `siteRoot` — 7ma composition. Editor ve nuevo tab
"Transversales activos" donde elige EXPLÍCITAMENTE qué pieza está
activa (drop-down pattern que pidió).

`DefaultGlobalComponentResolver` refactor con 3 prioridades:
1. `ResolveExplicitSelector(activeXNodeAlias)` — selector explícito
   del siteRoot (Ola 71.9). Si apunta a nodo + activo + en ventana →
   gana sin consultar otras fuentes.
2. `FindActiveTransversal(transversalXAlias)` — repository scan
   (Ola 70).
3. `FindActiveInBlockList(cfgXAlias)` — BlockList legacy (Olas 50 +
   52).

Para PlatformRoot landing (sin siteRoot ancestor), busca el primer
siteRoot publicado para que también herede selectors.

### Parte F — Brand inheritance pure (Ola 71.11)

`siteRoot.Compositions += compBranding` — la identidad (brandKey +
brandDisplayName) ahora vive en el siteRoot mismo (junto a su
`canonicalHostname` que ya estaba ahí).

`compBranding` removido de `themeSettings` + `siteConfigSettings` +
`featureFlagsSettings` — cero duplicación.

`HostBasedBrandingProvider.GetCurrent()` refactor: itera siteRoots
directamente en lugar de iterar siteConfigSettings + AncestorOrSelf
— un solo nivel de lookup, más rápido + más semántico.

## Consequences

**Positivas:**

- **Modelo mental claro y consistente para el editor**:
  - Tema = siteRoot. Punto.
  - Identidad de brand = siteRoot. Punto.
  - Chrome (header/footer/aside) = siteRoot. Editor arrastra bloques.
  - Configuración (analytics + legales + SEO defaults + flags) = UN
    solo nodo siteConfiguration.
  - Transversales (alertas/modales/banners/footer notes) = repositorio
    + selector explícito en siteRoot.
  - Pages = compone sus orquestación (suppress flags) + body content.
    NO duplica theme, NO duplica brand, NO duplica chrome.
- **DRY estricto**: cero prop duplicada entre tipos. Los aliases
  comunes (alertActive, alertMessage, etc.) viven UNA SOLA VEZ via
  composition.
- **Backward compat respetado**: ningún DocType eliminado. Sites con
  schema legacy siguen funcionando — los resolvers encuentran nodos
  bajo cualquier estructura via `DescendantsOrSelfOfType`.
- **Aside completa el chrome triadic** que el usuario pidió
  explícitamente.
- **ModelsBuilder ready**: setup de SourceCodeAuto preparado para Ola
  72 — typed views inmediatas tras siguiente reboot del runtime.

**Negativas:**

- **Migración manual requerida**: tras uSync Import los nodos legacy
  siguen funcionando pero data en `pageThemeVariant` per-page,
  `brandKey` en themeSettings/siteConfigSettings/featureFlagsSettings
  legacy NO se mueve sola. El operador debe:
  - Verificar que `siteRoot.brandKey` + `siteRoot.brandDisplayName`
    estén poblados (copy-paste de los nodos legacy si tenía).
  - Crear un `siteConfiguration` y consolidar valores ahí (si tenía
    múltiples nodos legacy).
- **Compositions chain depth aumenta**: siteConfiguration compone los
  3 legacy → si Umbraco tiene problemas con DAG resolution profundo,
  podría fallar. No se ha observado en testing local.
- **`compSiteChrome` aplicado solo a siteRoot**: si futuro feature
  pide override del chrome a nivel page (ej. landing con header
  custom), la composition tendrá que aplicarse también ahí. Diferido.
- **`pageThemeVariant` data se pierde** si algún editor configuró
  variant per-page. Decisión deliberada documentada en commit 71.1.
- **Typed views aún pendientes**: la activación de SourceCodeAuto
  (commit 71.4) sienta la base pero los archivos `.generated.cs`
  no existen hasta el siguiente reboot del runtime. Refactor de
  views a `@inherits UmbracoViewPage<TypedModel>` viene en Ola 72.

**Neutras:**

- 7 GUIDs nuevos: 1 ContentType (siteConfiguration) + 1 ContentType
  (compTransversalSelectors) + 1 prop (siteAsideBlocks) + 1 prop
  (suppressAside) + 4 props ContentPicker (activeXNode). Verificación
  cuádruple OK.
- `compBranding` ahora compuesto solo por siteRoot — ContentType
  preservado pero su único consumer es siteRoot.

## Alternatives considered

- **Eliminar themeSettings + siteConfigSettings + featureFlagsSettings
  completamente**: rechazado. Sites en producción con esos nodos
  perderían data. Marcar Deprecated + soft-migrate vía siteConfiguration
  composing-them es más respetuoso.
- **`siteConfiguration` con TODAS las props inline (no via
  composition)**: rechazado. Duplicación XML masiva (~30 props) — el
  pattern composition de Ola 70.2 ya probó que funciona limpio.
- **Quitar `compPageOrchestration` también de las pages y mover suppress
  flags al siteRoot**: rechazado. Suppress flags SON legítimamente
  per-page (una landing decide ocultar el modal global mientras el
  resto del sitio lo muestra). Solo theme se va a inheritance pura.
- **Adoptar `IPublishedContent` extension methods en lugar de typed
  ModelsBuilder**: rechazado. Extension methods siguen siendo string
  alias-driven. ModelsBuilder es la API canónica de Umbraco.
- **`asideMode` enum (left/right/none)**: deferido. KISS para primer
  pase — `<aside>` se renderiza after `<main>`, design system CSS
  decide left/right via grid template. Si justifica feature, agregar
  prop después.
- **Eliminar el legacy BlockList path del resolver**: rechazado.
  Backward compat para sites que aún usan `siteConfigSettings.globalComponents`.
  3-tier priority es razonable.

## Implementation summary (Ola 71, 14 commits)

| # | Hash | Foco |
|---|---|---|
| 71.1 | `efd0dec` | Remover compPageTheme de las 4 page types |
| 71.3 | `586c19e` | _Layout + IPageRenderContextResolver theme del siteRoot ONLY |
| 71.4 | `2159bd8` | Habilitar ModelsBuilder SourceCodeAuto en appsettings |
| 71.4b | `fb812f6` | gitignore para umbraco/models/*.generated.cs |
| 71.5 | `ac89b4b` | compSiteChrome += siteAsideBlocks (chrome triádico) |
| 71.6 | `4e13022` | _Layout renders <aside> + suppressAside flag (Razor) |
| 71.6b | `21033aa` | Fix case-mismatch suppressAside compPageOrchestration commit |
| 71.7 | `e02888b` | siteConfiguration unified + siteConfigFolder simplify |
| 71.8 | `9f84653` | Mark legacy 3 nodes Deprecated en Description |
| 71.9 | `c329a7a` | compTransversalSelectors + apply siteRoot (drop-down pattern) |
| 71.10 | `a9be334` | Resolver: explicit selector > repository > BlockList legacy |
| 71.11 | `d12904b` | Brand inheritance pure (compBranding solo siteRoot + provider refactor) |
| 71.12 | (este) | ADR 0040 + index README |

## References

- ADR 0017 — Layout system (Block Grid Sections — DataType reusado en
  todos los slots de chrome)
- ADR 0020 — Platform/Settings split (legacy preservado pero
  consolidado en siteConfiguration)
- ADR 0022 — Page Composition Standard (cascade page→siteRoot del
  theme triplet desaparece — supersede parcial)
- ADR 0023 — Componentization Layered (compTransversalSelectors es
  nueva L1 composition)
- ADR 0026 — Brand runtime completion (HostBasedBrandingProvider ahora
  itera siteRoot directamente)
- ADR 0030 — Forms internal (referencia del pattern PRG + cookie reuse)
- ADR 0034 — Member self-service (referencia del pattern controller +
  Razor views)
- ADR 0039 — Site Chrome editable + per-site Configuration folder
  (extiende — chrome triadic + folder UX)

## Memorias del agente actualizadas

- `feedback_composition_design_solid` — refrescar con ejemplo de Ola
  71 (siteConfiguration compose-3-legacy es DRY logrado).
- `feedback_no_preassigned_guids_usync` — sigue válido (7 GUIDs Ola
  71 todos verificados cuádruple).
- `feedback_branding_via_provider` — actualizar: brand identity ahora
  vive en siteRoot (canonicalHostname + brandKey + brandDisplayName).
  HostBasedBrandingProvider itera siteRoot, no siteConfigSettings.
