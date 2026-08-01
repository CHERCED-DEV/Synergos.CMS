# Las 5 capas de Synergos — qué va dónde

Fuente autoritaria: ADR 0023 (Componentization Layered) + memoria `feedback_componentization_layered`.

Las capas son **estancas**. Una pieza vive en una sola capa; intentar "una composition que también es page" es señal de mal diseño.

## 1. Settings — singletons globales

**Qué:** datos configurados una vez por brand/siteRoot/platform, leídos por todos los templates en runtime.

**Naming:** `cfg*` (ej. `cfgAlert`, `cfgBanner`, `cfgFooterNote`, `cfgModal`) o `*Settings` (ej. `siteConfigSettings`, `themeSettings`, `seoSettings`, `platformSettings`).

**Archivos uSync típicos:**
- `cfgAlert.config`, `cfgBanner.config`, `cfgFooterNote.config`, `cfgModal.config`
- `*Settings.config` bajo platformRoot/siteRoot

**Patrón Global Component:** algunos `cfg*` son consumidos por todos los templates via `IGlobalComponentResolver`. Ej: `cfgAlert` configurado una vez en `siteConfigSettings` → todos los pages lo renderizan si está activo.

**Cuándo recomendar:**
- Alerta que debe aparecer en todo el sitio → `cfgAlert` (NO compAlex per-page).
- Banner promocional global → `cfgBanner`.
- Pie con texto legal compartido → `cfgFooterNote`.
- Tema de marca → `themeSettings`.
- Defaults de SEO globales → `seoSettings`.

**Cuándo NO:** si la decisión es per-page (ej. una sola landing tiene un alert), eso es composition, no setting.

## 2. Compositions — mixins reutilizables

**Qué:** propiedades transversales que se componen en blocks/pages/settings. NO se instancian solas. NO aparecen en el Content tree.

**Naming:** `comp*` (ej. `compSeo`, `compPageTheme`, `compPageOrchestration`, `compNavigation`, `compcontentheading`, `compcontentmedia`, `compdomclass`, `compdomvariant`, `compdomvisibility`, `compdomattributes`, `compdomspacing`, `compdomflex`, `compdomgrid`, `compdomdisplay`, `compdompresetchrome`, `compbranding`, `compmembergating`, `comptagging`).

**Familias dentro de comp*:**
- `compcontent*` — propiedades editoriales (heading, media, cta, badge, metadata, text, embed, date, author, collection)
- `compdom*` — propiedades de presentación HTML (class, variant, visibility, attributes, spacing, flex, grid, display, presetChrome). Estos son universales — los 156 element types los componen para dar al editor control fino del HTML emitido.
- `compcore*` — base/lifecycle (compcorebase, compcorelifecycle)
- `compbehavior*` — behavioral concerns (featureFlag, tracking — bloqueado CDN)
- `compPage*` — page-level orchestration (Theme, Orchestration, Seo, Navigation)
- `comptransversalselectors`, `comptagging`, `compbranding`, `compmembergating` — single-purpose

**Cuándo recomendar una composition:**
- El arquitecto va a crear un block/page nuevo y necesita una propiedad universal (visibility, class, theme override, SEO).
- 2+ consumers reales o uno solo + plan firme. Si no, va inline.

**Cuándo NO:**
- Crear una composition para un solo block con campos que solo ese block necesita. Va inline en el block.
- Crear una composition "para la familia X por si acaso" — anti-patrón.

**Filtro de 3 preguntas antes de proponer composition nueva** (memoria `feedback_composition_design_solid`):
1. ¿Existe ya una composition que cubre esto?
2. ¿2+ consumers reales o uno + plan firme?
3. ¿Captura una capacidad transversal o feature de un solo block?

## 3. Blocks — element types instanciables dentro de Block Grid/List

**Qué:** unidades editoriales que el editor dropea dentro de un Block Grid (sections de página) o Block List (DTBlockList* específicos como FAQ items, gallery items, etc.).

**Naming:**
- **Layout family**: `elementlayout*` (Section, Container, Stack, Grid, Column, 1Col, 2ColEven, MainSidebar, 3Col, 4Col, HolyGrail, SidebarMain, Hero, SnippetRef). 14 layout presets.
- **Action family**: `elementaction*` (actionButton, actionCtaGroup, actionLink).
- **Content family standalone**: `element*` no-prefijo (ej. elementCard, elementGallery, elementFAQ, elementTestimonial, elementBanner, elementTimeline, elementLogoStrip, elementFeature, elementForm, elementAccordion, elementModal, elementTabs, elementNav).
- **CDN-hosted (bundle UI)**: `elementSyn*` — hidratan en cliente como `<synergos-{kebab}>`. Ver `references/naming-and-ui-bridge.md`.

**Cuándo recomendar:**
- El arquitecto está componiendo una sección de página y necesita un bloque concreto (card, hero, testimonio, FAQ, accordion).

**Cuándo NO:**
- Si lo que necesita es global (alert, banner) → Settings, no Block.
- Si lo que necesita es composition de propiedades para varios blocks → Composition, no Block.

**Layout vs contenido:**
- Layout blocks (`elementlayout*`) controlan estructura: definen areas y dropean OTROS blocks dentro.
- Content blocks llenan los slots con la materia editorial (texto, imágenes, formularios).

**Cuándo proponer un layout preset:**
- 1 columna simple → `elementlayout1Col`
- 2 columnas iguales → `elementlayout2ColEven`
- Main + sidebar → `elementlayoutMainSidebar` o `elementlayoutSidebarMain`
- Grid 3-4 col → `elementlayout3Col` / `elementlayout4Col`
- HolyGrail (header / 3-col / footer dentro del section) → `elementlayoutHolyGrail`
- Hero arriba de página → `elementlayoutHero`
- Reusar un set guardado → `elementlayoutSnippetRef` apuntando a un `reusableBlock`

## 4. Pages — DocTypes top-level

**Qué:** los 4 perfiles canónicos del Content tree. Detalle en `references/page-types.md`.

**Naming:** `page*` (`pageBase`, `pageBasic`, `pageBare`, `pageLanding`).

**Otros DocTypes top-level que NO son pages:**
- `siteRoot`, `platformRoot`, `brand` — Wiring (capa 5).
- `authorpage` — entity page, no editorial top-level.

## 5. Wiring — plumbing del árbol

**Qué:** los nodos que estructuran el árbol de Content y resuelven cascadas. El arquitecto editorial casi nunca crea contenido aquí — es montaje del producto.

**Archivos:**
- `siteRoot.config` — raíz por hostname/site, hereda al `platformRoot`. Compone `compPageOrchestration` para actuar como defaults globales.
- `platformRoot.config` — raíz absoluta del deploy (un solo nodo en todo el árbol).
- `brand.config` — nodo brand (alimenta `IBrandingProvider`).
- `comppageorchestration.config` — la composition compartida entre page y siteRoot que hace funcionar la cascada.

**Si el arquitecto pregunta "¿dónde configuro defaults para todo el sitio?":**
- Defaults heredados por pages (chrome, header, footer, theme, surface, visualProfile) → en `siteRoot` properties (compPageOrchestration + compPageTheme).
- Defaults de marca (color primario, fonts, logos, favicon) → en `themeSettings` bajo `platformRoot`.
- Defaults SEO globales → en `seoSettings`.
- Singletons consumidos por templates (alert, banner, footerNote) → en `cfg*` referenciados desde `siteConfigSettings`.

## Anti-patrones — señales de mala asignación de capa

- ❌ Una composition que también aparece en el Content tree → debe ser block, no composition.
- ❌ Un block con `IsElement=false` cuando vive dentro de un Block Grid → flag mal puesto, hay que corregir (ojo: `IsElement` es inmutable post-creación, ver memoria `feedback_iselement_immutable`).
- ❌ Un `cfg*` que se instancia varias veces en el árbol → no es setting, es block.
- ❌ Un `comp*` referenciado desde un solo block sin plan de reuso → muévelo inline.
- ❌ Una `page*` que solo es accesible desde otra page como embed → probablemente es block, no page.
