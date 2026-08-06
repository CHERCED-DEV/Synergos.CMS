# ADR 0022 — Page Composition Standard

- **Status:** Accepted
- **Date:** 2026-04-25
- **Deciders:** Arquitecto + agente, durante ola 49

## Context

Tras la ola 48, las tres páginas existentes (`pageBase`, `pageBasic`,
`pageBare`) habían sido simplificadas para apoyarse 100% en el Layout
Composer (`sections`). Auditando el inventario emergieron seis lagunas:

1. **`pageBase` y `pageBasic` quedaron funcionalmente idénticos** — solo
   se diferenciaban en la descripción. No había razón semántica para
   tener dos page types intercambiables.
2. **No existía un perfil "Landing"** dedicado a páginas de conversión
   (campañas, productos hero, captura).
3. **Ninguna composition manejaba la cintilla superior contextual**
   ("Alex") que la marca Synergos requiere para anuncios, alertas y
   promos por página.
4. **Las decisiones de chrome (header/footer/breadcrumbs/title/intro)
   estaban hardcoded** en los templates Razor. Cualquier nueva regla
   requería tocar cada `.cshtml`.
5. **No había herencia desde `siteRoot`** — cada página debía configurar
   su orquestación aislada, sin defaults globales del sitio.
6. **El theme overrideable a nivel página no existía** — solo el
   `themeSettings` brand-wide (ADR 0020) cubría el tema visual.

El arquitecto pidió reorganizar las páginas en cuatro perfiles
canónicos (Standard / Canvas / Bare / Landing), aplicar herencia
explícita desde `siteRoot`, agregar Alex como composition
independiente (no enchufada en SEO ni Meta) y centralizar las
decisiones de render en un resolver runtime.

## Decision

### 1. Inventario canónico de page types

| Alias actual | Nombre nuevo | Rol | Cuándo usarlo |
|---|---|---|---|
| `pageBase` | Page — Standard | Editorial completa con intro automática, body Layout Composer y bloques after-body | Artículos institucionales, casos, páginas de servicio |
| `pageBasic` | Page — Canvas | Layout Composer puro, sin heading/intro/featured automáticos | Cuando el primer Section trae todo el cromo visual |
| `pageBare` | Page — Bare | Sin chrome compartido, Layout=null | Embeds, modales, capturas, flujos custom |
| `pageLanding` | Page — Landing | Hero + Layout Composer optimizado para CTA | Campañas, productos hero, captura de leads |

Los aliases `pageBare`, `pageBase`, `pageBasic` se preservan (mismas
keys uSync) para no romper los Content existentes ni los Templates
registrados en uSync. Solo cambian Description y Name editor-facing.
`pageLanding` es nuevo.

### 2. Composiciones nuevas (4)

| Alias | Propósito | Field aliases principales |
|---|---|---|
| `compAlex` | Cintilla superior contextual | alexEnabled, alexText, alexCtaLabel, alexCtaLink, alexIcon, alexVariant, alexTone, alexVisibilityMode, alexScheduleStart/End, alexRenderMode, alexDismissible |
| `compPageOrchestration` | Reglas de chrome/header/footer/title/intro/breadcrumbs/container/spacing | chromeMode, headerMode, footerMode, showTitle, showIntro, showBreadcrumbs, pageContainerType, pageSpacingScale |
| `compPageTheme` | Override de tema por página (distinto del brand-wide `themeSettings`) | pageThemeVariant, pageSurface, visualProfile |
| `compNavigation` | Visibilidad y orden en menús | navigationTitle, hideFromMainMenu, hideFromFooter, hideFromBreadcrumbs, hideFromSearch, navigationWeight, navigationIcon |

Decisión clave: **Alex es una composition independiente**, no un sub-tab
de SEO ni de Meta. Es una decisión de presentación contextual por
página, no metadata SEO ni metadato editorial general.

### 3. Composition de cada page type

| Page type | compCoreBase | compCoreLifecycle | compSeo | compAlex | compPageOrchestration | compPageTheme | compNavigation |
|---|---|---|---|---|---|---|---|
| `siteRoot` | ✓ | | ✓ | | ✓ (defaults) | ✓ (defaults) | |
| `pageBase` (Standard) | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| `pageBasic` (Canvas) | ✓ | | ✓ | ✓ | ✓ | ✓ | ✓ |
| `pageBare` | ✓ | | ✓ | | ✓ | ✓ | |
| `pageLanding` | ✓ | | ✓ | ✓ | ✓ | ✓ | ✓ |

Bare excluye Alex (chrome-less) y Navigation (rara vez en menús).
siteRoot recibe compPageOrchestration + compPageTheme para actuar
como **defaults heredables**.

### 4. Cascada de resolución (page → siteRoot → defaults)

`IPageRenderContextResolver` (interface en
`Synergos.CMS.Interfaces`, default impl en
`Synergos.CMS.Web/Services/`) lee la propiedad en la página; si vacía
o "inherit", cae al siteRoot ancestral; si también vacía, aplica
defaults inline (`PageRenderContext.Defaults()`).

El record `PageRenderContext` (también en `Synergos.CMS.Interfaces`,
mismo patrón que `BrandTheme + IBrandThemeProvider`) expone los
valores resueltos a las plantillas Razor. Los booleanos derivados
(`ShowHeader`, `ShowFooter`, `ShowAlex`) ya colapsan reglas como
"chromeMode=none/bare/embedded oculta header" para que las vistas no
repitan string-matching.

### 5. Templates Razor

- `_Layout.cshtml` consume el resolver y decide qué partials
  renderizar (Alex, header, breadcrumbs, footer).
- Tres partials nuevos en `Views/Shared/`:
  - `_PageAlex.cshtml` — Cintilla con icon/text/CTA + scheduled
    visibility.
  - `_PageIntro.cshtml` — heading/subheading/summary/featuredImage
    automáticos.
  - `_Breadcrumbs.cshtml` — Trail desde siteRoot, respeta
    `hideFromBreadcrumbs`.
- Cuatro templates de página delgados (PageBase, PageBasic, PageBare,
  PageLanding) que solo proyectan datos; el chrome viene de _Layout.

### 6. Theme: page-level vs brand-level

`themeSettings` (DocType bajo platformRoot) gobierna el **brand**:
primary/secondary/accent colors, fonts, favicon. Aplicable a todo el
sitio.

`compPageTheme` (NUEVO) hace **override por página**:
themeVariant=dark/light/silverGold/brand, pageSurface=muted/contrast,
visualProfile=premium/editorial. Sin solapamiento — son capas
distintas.

## Consequences

**Positivas:**

- Inventario completo (Standard / Canvas / Bare / Landing) cubre los
  cuatro perfiles editoriales típicos sin duplicación.
- Las decisiones de chrome se centralizan en el resolver. Una nueva
  regla (ej. ocultar footer si la página tiene `requiresAuth=true`)
  se agrega en un solo punto, no en cada `.cshtml`.
- Editores configuran orquestación por página o desde siteRoot como
  default — sin tocar código.
- Alex existe como composition reutilizable; cualquier page type
  futuro puede componerla sin re-implementarla.
- compPageTheme permite landings con tema "dark" sobre un brand
  "light" sin tocar el themeSettings global.
- La cascada `inherit` evita que el editor tenga que decidir cada
  campo en cada página.

**Negativas:**

- Los page types Standard/Canvas/Bare/Landing tienen ahora 6-7
  composiciones cada uno. La pestaña Properties del backoffice se
  vuelve más larga. Mitigación: Tabs nuevos (Alex / Orquestación /
  Apariencia / Navegación) agrupan visualmente las opciones.
- `IPageRenderContextResolver` agrega una llamada por request. Costo
  despreciable (lectura de `IPublishedContent.Value<>` cacheada).
- Alex tiene reglas de visibilidad complejas (always / scheduled /
  authenticatedOnly / anonymousOnly / manual) — esta ola implementa
  always, scheduled y manual. Las reglas de auth se difieren a
  cuando aterricen los miembros.

**Neutras:**

- Aliases `pageBare`, `pageBase`, `pageBasic` se preservan. El
  contenido existente sigue funcionando sin migration.
- Los archivos `PageBase.cshtml`, `PageBasic.cshtml` mantienen sus
  nombres físicos; solo cambia el contenido y la descripción
  semántica.

## Alternatives considered

- **Renombrar aliases (`pageBase` → `pageStandard`):** Descartado.
  uSync hubiera marcado Content existente como huérfano y obligado
  re-publicación de cada nodo.
- **Embebber Alex dentro de compSeo o compMetadata:** Descartado.
  Alex es decisión de presentación contextual, no metadata SEO ni
  meta editorial. Mezclar concerns hubiera forzado cargar Alex en
  todos los DocTypes públicos (Posts, Products, Authors), donde no
  aplica.
- **No crear pageLanding y reusar pageBase con un flag
  "isLanding":** Descartado. Las semánticas son distintas (Landing
  no tiene sectionsAfterBody, su intro es lineal abre-fuerte).
  Separar como page type da claridad editorial y permite reglas de
  permisos / templates diferentes.
- **Mantener decisiones de chrome hardcoded en cada .cshtml:**
  Descartado. Cualquier nueva regla (ej. modo embedded para iframes
  externos) requería tocar 3+ archivos y mantener consistencia
  manual.
- **Hacer `compPageTheme` un sub-tab de `themeSettings`:**
  Descartado. `themeSettings` es brand-wide bajo platformRoot;
  `compPageTheme` es per-page override. Mezclarlos crearía un
  acoplamiento incómodo entre Settings y Content trees.

## References

- ADR 0017 — Layout System (compositions over inheritance)
- ADR 0020 — Platform Settings split (themeSettings brand-wide)
- ADR 0021 — DataType semantics by intent (DTSelect.* canonical)
- `refactor-docs/_notes/ola-49-pages-orchestration-audit.md` —
  auditoría inicial pre-ola-49
