# ADR 0041 — Mapa Lego canónico + auditoría de acoplamientos (Ola 72)

- **Status:** Accepted
- **Date:** 2026-04-26
- **Deciders:** Arquitecto + agente, durante Ola 72 — disparado por
  petición directa: *"continúa detectando mejoras y orquestación desde
  platform root hasta la pieza más atómica que tengamos. La idea es
  hacer esto un Lego super ensamblable con 0 acoplamiento o
  acoplamiento inteligente no disperso."*
- **Consolida:** ADR 0017, 0020, 0022, 0023, 0040 — visión integral
  del modelo arquitectónico vigente.
- **Producto del audit Ola 72:** 4 commits cleanup + 1 bug fix
  crítico + este ADR.

## Context — qué es "acoplamiento inteligente no disperso"

El sistema crece orgánicamente y acumula:
- **Acoplamientos correctos (inteligentes)**: dos tipos relacionados
  por una composition compartida — la dependencia es deliberada,
  centralizada, una sola fuente de verdad. Ejemplo: los 153 element
  types comparten `compDomClass/Variant/Visibility/Attributes` —
  cuando se necesita un nuevo modificador universal, se agrega en UN
  archivo.
- **Acoplamientos dispersos (malos)**: la misma propiedad declarada
  N veces en N tipos sin composition. Cuando cambia, hay que tocar
  N archivos. Ejemplo histórico (corregido en Ola 71): `brandKey`
  estaba en 3 settings types diferentes — hoy vive en `siteRoot` vía
  `compBranding`.

El objetivo del audit: confirmar que cada acoplamiento del schema cae
en la primera categoría, no la segunda.

## Mapa Lego canónico (post Olas 60-72)

```
Platform Root (multi-site wrapper, opcional)
│
├── Site Root  ← FUENTE ÚNICA DE VERDAD del sitio
│   compone: compCoreBase + compSeo + compPageOrchestration +
│            compPageTheme + compBranding + compSiteChrome +
│            compTransversalSelectors  (7 compositions, post-Ola 71)
│   props propios: siteDisplayName + canonicalHostname
│
│   ├── Configuración (siteConfigFolder)
│   │   ├── Configuración del sitio (siteConfiguration)
│   │   │   compone: themeSettings + siteConfigSettings +
│   │   │            featureFlagsSettings  (DRY via composition,
│   │   │            UN nodo con todos los tabs combinados)
│   │   │
│   │   └── Componentes transversales (transversalsRepository)
│   │       ├── Alertas (transversalAlertsFolder)
│   │       │   └── 0..N transversalAlert  (compone cfgAlert)
│   │       ├── Modales (transversalModalsFolder)
│   │       │   └── 0..N transversalModal  (compone cfgModal)
│   │       ├── Banners (transversalBannersFolder)
│   │       │   └── 0..N transversalBanner  (compone cfgBanner)
│   │       └── Notas footer (transversalFooterNotesFolder)
│   │           └── 0..N transversalFooterNote  (compone cfgFooterNote)
│   │
│   └── Pages  (children del siteRoot)
│       compone: compCoreBase + compSeo + compPageOrchestration +
│                compNavigation  (4 page types) o subset
│       │
│       ├── pageBase (Standard) — heading + sections + sectionsAfterBody
│       ├── pageBasic (Canvas)  — sections (chrome inheritado)
│       ├── pageLanding         — heading + sections (CTA-focused)
│       ├── pageBare            — sections (NO chrome, Layout=null)
│       │
│       ├── postCategoryPage / postPage     (blog runtime)
│       ├── productCategoryPage / productPage (shop runtime)
│       ├── searchPage          (search UX)
│       └── flowDefinition / flowStep (flow engine)
│
│       Cada page tiene 1 propiedad sections (Block Grid) que aloja:
│       │
│       └── 14 elementLayout*  (Layout Composer presets)
│           Hero / Section / Container / Stack / Grid / Column / 1Col /
│           2ColEven / MainSidebar / 3Col / 4Col / HolyGrail /
│           SidebarMain / SnippetRef
│           │
│           └── Areas (root o nested) que aceptan:
│               │
│               └── 153 element types (las piezas atómicas)
│                   compone: compDomClass + compDomVariant +
│                            compDomVisibility + compDomAttributes
│                            (universal — 153 consumers cada una)
│                            + composiciones específicas por familia
│                              (compIntegration en elementSyn*,
│                               compContent* en blocks editoriales,
│                               compDomSpacing/Flex/Grid en layouts)
│
└── 23 seam interfaces  (Synergos.CMS.Interfaces)
    Aplicación-level: IBrandingProvider + IBrandThemeProvider +
        IFeatureGate + IDictionaryCache + IBundleRegistryClient +
        ISynHostEmitter + IBlogQuery + IShopQuery + ISearchQuery +
        ICartService + IFormSubmissionHandler + IMemberAccessGate +
        IMemberAuthService + IEmailService + ICommentRepository +
        IAnalyticsTracker + IGlobalComponentResolver +
        IPageRenderContextResolver + ICompositionReader +
        IContentContextAccessor + IElementViewModelMapper +
        ISchemaHealthProbe + ISynergosServiceBuilder
    Default impls: 16 en Web/Services/, 1 Stub (CDN bloqueado),
        6 en Application/Proxies/Impl o composers.
```

## Acoplamientos auditados — verdict por familia

### Compositions (30 totales) — ✅ todos justificados

| Familia | # consumers | Verdict |
|---|---|---|
| `compDomClass/Variant/Visibility/Attributes` | 153 c/u | ✅ Universal pattern. Acoplamiento correcto centralizado. |
| `compIntegration` | 73 | ✅ Solo en elementSyn* (CDN bridge — ADR 0015). Domain-aligned. |
| `compDomSpacing` | 54 | ✅ Layout-related — todos los blocks que necesitan spacing. |
| `compDomPresetChrome` | 13 | ✅ Solo en elementLayout* (presets). Domain-aligned. |
| `compContentMedia/Text/Heading/Cta` | 5-12 | ✅ Granular content — usadas donde aplica. |
| `compCoreBase` | 18 | ✅ Auditoría/sistema. Universal en pages + nodes editables. |
| `compCoreLifecycle` | 9 | ✅ State lifecycle (publish/draft) en content nodes. |
| `compSeo` | 12 | ✅ Pages + content types públicos navegables. |
| `compPageOrchestration` | 5 | ✅ 4 pages + siteRoot (suppress flags + chrome modes). |
| `compNavigation` | 3 | ✅ pageBase/Basic/Landing — pageBare excluido por diseño. |
| `compTagging` | 2 | ✅ postPage + productPage. Domain-aligned. |
| `compMemberGating` | 1 | ✅ pageBase (gating opt-in). |
| `compBranding` | 1 (siteRoot) | ✅ Single source of truth — Ola 71. |
| `compSiteChrome` | 1 (siteRoot) | ✅ Site-level único. |
| `compTransversalSelectors` | 1 (siteRoot) | ✅ Site-level único. |
| `compPageTheme` | 1 (siteRoot) | ✅ Single source of truth — Ola 71. |
| `compContentBadge/Author/Date` | 1 c/u | 🟡 Granularidad alta — uso específico, OK. |
| `compContentCollection` | 0 | 🟡 [Disponible — sin consumers]. Marked Ola 72.2. |
| `compContentEmbed` | 0 | 🟡 [Disponible — sin consumers]. Marked Ola 72.2. |
| `compContentMetadata` | 0 | 🟡 [Disponible — sin consumers]. Marked Ola 72.2. |
| `compBehaviorFeatureFlag` | 0 | 🟡 [Bloqueado por CDN team]. Marked Ola 72.2. |

**Verdict global**: cero acoplamientos dispersos detectados. Las 4
zero-consumer son legítimamente reservadas (3 disponibles para futuro
uso, 1 bloqueada externamente).

### DataTypes (104 totales — 53 DTSelect*) — ✅ no consolidación

Audit de los sospechosos overlapping (Placement family + Variant
family): **NO son duplicados**. Cada DataType tiene domain semantics
distinta:
- `DTSelectPlacement` (Floating UI) ≠ `DTSelectBannerPlacement`
  (top/bottom binary) ≠ `DTSelectScreenPosition` (toast 9-position grid).
- `DTSelectAlertVariant` (status urgency con `danger`) ≠
  `DTSelectBadgeVariant` (status + emphasis) ≠ `DTSelectVariantKey`
  (button style con `ghost`/`outlined`).

Consolidar perdería domain semantics. Decision: **mantener separados**
— acoplamiento inteligente por dominio, no disperso.

### Element types (153 totales) — ✅ inventario sano

Distribución por prefix muestra organización clara:
- elementSyn* (71) — CDN bridge (ADR 0015)
- elementLayout* (14) — Layout Composer presets (ADR 0017)
- elementCorp* (10), elementShop* (8), elementComp* (7), elementText* (6)
- elementMember* (4), elementForm* (3), elementFlow* (2), elementNav* (2)
- + 26 misc (cfg* + transversal* + elementCommentThread + etc.)

Sin duplicados detectados por nombre. Ola 53 cerró la sync 1:1 con
Synergos.UI Web Components.

### Page types (4 + post/product/search/flow) — ✅ schema diferenciado

`pageBasic` y `pageBare` tienen schema idéntico (solo `sections`)
pero son legítimamente distintos en runtime (chrome vs chrome-less).
Descriptions clarificadas en Ola 72.6 para que el editor decida en
5 segundos cuál crear.

### Seams (23 interfaces) — ✅ separación de concerns clara

Cada seam tiene un dominio específico, sin overlap:
- Identity: `IBrandingProvider`, `IBrandThemeProvider`,
  `IMemberAccessGate`, `IMemberAuthService`
- Query: `IBlogQuery`, `IShopQuery`, `ISearchQuery`
- State: `ICartService`, `IFormSubmissionHandler`, `ICommentRepository`
- Cross-cutting: `IFeatureGate`, `IGlobalComponentResolver`,
  `IPageRenderContextResolver`, `IAnalyticsTracker`, `IEmailService`
- Infra: `IDictionaryCache`, `IBundleRegistryClient`,
  `ISynHostEmitter`, `ICompositionReader`,
  `IContentContextAccessor`, `IElementViewModelMapper`,
  `ISchemaHealthProbe`, `ISynergosServiceBuilder`

Ningún seam podría unificarse con otro sin perder responsabilidad
clara.

## Bug crítico arreglado durante el audit (Ola 71.11b)

`compBranding` mostraba 0 consumers — investigación reveló que mi
commit `d12904b` (Ola 71.11) declaró agregarlo a `siteRoot.config`
pero el `git add` perdió ese archivo. Resultado: `siteRoot` quedó SIN
`compBranding` y el resolver fallaba silenciosamente al fallback
`DefaultBrandingProvider`. **Fixed en `8475295`** — siteRoot ahora
compone los 7 compositions documentados.

**Lección aprendida**: cuando un commit declara modificar N archivos,
verificar en `git show --stat` que efectivamente todos los N están en
el commit, no en working tree pendiente.

## Decisión + Principio canónico

**La regla del Lego ensamblable**:

1. **Una propiedad vive en exactamente UNA composition**. Cualquier tipo
   que la necesite la compone — no la duplica inline.
2. **El siteRoot es la fuente única de verdad de la identidad del sitio**:
   brand + chrome + theme + transversal selectors. Las pages heredan
   vía runtime resolution; nunca duplican.
3. **Compositions con 0 consumers son aceptables si tienen prefijo de
   status claro** (`[Disponible — sin consumers actuales]` o
   `[Bloqueado por X]`). El editor entiende sin necesidad de leer
   changelog.
4. **DataTypes por domain, no por overlap superficial**. Tres
   `DTSelectXVariant` con options parcialmente compartidas son TRES
   datatypes, no uno consolidado — porque la semantic es por dominio.
5. **Seams responden a un dominio único cada una**. No se unifican por
   "parecido superficial".

Esta regla aplica a futuras olas que añadan composiciones, datatypes,
seams o page types: antes de crear, verificar que no exista una
existing que cumpla el rol. Si existe, componer/extender; no duplicar.

## Implementation summary (Ola 72, 5 commits)

| # | Hash | Foco |
|---|---|---|
| 71.11b | `8475295` | **Fix crítico** descubierto en audit: siteRoot.Compositions += compBranding (commit 71.11 olvido) |
| 72.2 | `f4a7877` | Clarify 4 zero-consumer compositions con prefijos `[Disponible]` o `[Bloqueado]` |
| 72.4 | (audit) | Audit DTSelect* — NO duplicados por dominio. **Skip 72.5** (no consolidación). |
| 72.6 | `5e13648` | Clarify diferencia `pageBasic` vs `pageBare` en Descriptions (chrome vs chrome-less) |
| 72.7 | (este) | ADR 0041 + outer current-state |

## Consequences

**Positivas:**

- **Schema autopatible**: 30 compositions auditadas todas tienen
  consumers o status explícito. Cero código zombi sin contexto.
- **Editor backoffice limpio**: descripciones consistentes — el editor
  entiende el rol de cada DocType/Composition de un vistazo.
- **Bug crítico corregido**: `compBranding` ahora wire-correcta en
  siteRoot. Brand resolution funciona end-to-end.
- **Documentación viva del Lego**: este ADR + el outer current-state
  §11 sirven como mapa de referencia para futuras olas.
- **Principio canónico explícito**: la "regla del Lego ensamblable"
  (5 puntos) es checklist obligatoria antes de crear nuevas piezas.

**Negativas:**

- **Compositions Disponible mantenidas**: las 3 `compContent*` con 0
  consumers ocupan espacio en el listado de Compositions del
  backoffice. Trade-off aceptado: preservar trabajo previo + estar
  listas para futuros consumers.
- **`compBehaviorFeatureFlag` queda en limbo**: bloqueada por CDN team.
  Si el bloqueo se prolonga indefinidamente, considerar archivar.

**Neutras:**

- Cero schema rompedor en Ola 72 (solo Description text + 1 fix
  crítico de wire faltante).
- Cero impacto runtime — el resolver recupera el comportamiento
  esperado tras el fix 71.11b (anteriormente fallaba silently al
  DefaultBrandingProvider).

## References

- ADR 0017 — Layout system
- ADR 0020 — Platform/Settings split
- ADR 0022 — Page Composition Standard
- ADR 0023 — Componentization Layered Architecture
- ADR 0040 — Gran Consolidación Arquitectónica (Ola 71)
- Memoria `feedback_composition_design_solid` — filtro 3 preguntas
  antes de crear comp* nueva. Esta ADR refuerza con la "regla del
  Lego ensamblable".
