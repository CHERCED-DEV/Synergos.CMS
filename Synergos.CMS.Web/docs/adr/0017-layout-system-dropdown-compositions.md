# ADR 0017 — Layout system: per-block compositions con dropdowns

- **Status:** Accepted
- **Date:** 2026-04-22
- **Deciders:** Project owner
- **Source:** redactado directamente como Ratified (no draft previo); el
  slot 0017 estaba vacío en `refactor-docs/adr-drafts/`.
- **Authorises:** Ola 42 (Layout system rediseñado) + Ola 42.5
  (Layout Composer con presets + areas + preview en backoffice) +
  Ola 42.6 (UX al drop: thumbnails + client-side defaults +
  BlockGroups reorder) + Ola 42.7 (taxonomía universal + 3 presets
  adicionales + mobile collapse + starter scaffold opt-in + siteRoot/
  pageBare opt-in + semantic HTML) — ver Addenda al final.

## Context

El inventario del Epic Fail 2 (`05-legacy-refinement-inventory.md`)
proponía migrar el sistema de layout del legado como un conjunto de
**Document Types de "layout preset"** (`layoutpreset1col`,
`layoutpreset2col`, `layoutpresetmainsidebar`, `layoutprofile`,
`layoutprofileFolder`, `layoutFolder`) + dos composiciones tipo
"preset/profile" (`compDomLayoutPreset`, `compDomLayoutProfile`), con
el veredicto **REDISEÑAR**.

Esa arquitectura del legado acoplaba layout a *tipos de página
enteros*: el editor elegía "1 columna" o "main + sidebar" para toda
la página, y los bloques internos heredaban. En la práctica ese
modelo fracasa en cuanto una página quiere **mezclar** topologías
(hero grid + cuerpo 2col + footer flex). La consecuencia: páginas
que se fuerzan dentro del preset equivocado, con CSS override
editorial para "romperlo localmente".

El diseño actual de Synergos ya había introducido `compDomLayout` en
Ola 12 — una composición per-block con dos props **TextBox libres**:
`layoutAlign` y `layoutDirection`. Era correcto en dirección (layout
es decisión del bloque, no del tipo de página) pero insuficiente en
granularidad: dos ejes planos no cubren flex-wrap, grid-template,
gap asimétrico, alineación multi-línea, etc. Y TextBox libre
permite al editor escribir cualquier valor ("rowz", "centre"),
rompiendo el contrato CSS del design system.

Ola 42 lleva la idea de Ola 12 a su forma completa.

## Decision

Se **rediseña** el sistema de layout sobre tres principios:

1. **Per-block granularity** — cada contenedor editorial decide su
   propia topología, no hereda de un preset de página.
2. **Dropdown-only** — todos los props son `Umbraco.DropDown.Flexible`
   con ValueList cerrado. Nada de TextBox libre para valores que van
   a generar CSS classes. Cero typos posibles; cero CSS orphan.
3. **Composition-per-concept** — un display model (display), un
   modelo flex (flex), un modelo grid (grid). SRP aplicado al schema
   de compositions (feedback `feedback_composition_design_solid`).

### Artefactos schema

**10 DataTypes nuevos** en `Synergos.CMS.Web/uSync/v9/DataTypes/`:

| DataType | Values |
|---|---|
| `DTSelectDisplayMode` | block · flex · grid |
| `DTSelectFlexDirection` | row · row-reverse · column · column-reverse |
| `DTSelectFlexWrap` | nowrap · wrap · wrap-reverse |
| `DTSelectJustifyContent` | flex-start · center · flex-end · space-between · space-around · space-evenly |
| `DTSelectAlignItems` | stretch · flex-start · center · flex-end · baseline |
| `DTSelectAlignContent` | stretch · flex-start · center · flex-end · space-between · space-around |
| `DTSelectGridTemplate` | 1col · 2col-even · 2col-8-4 · 2col-4-8 · 3col · 4col · main-sidebar · sidebar-main · holy-grail |
| `DTSelectGridRows` | auto · 2-rows · 3-rows |
| `DTSelectJustifyItems` | stretch · start · center · end |
| `DTSelectSpacingScale` | none · xs · sm · md · lg · xl · 2xl |

**3 Compositions nuevas** en `Synergos.CMS.Web/uSync/v9/ContentTypes/`:

- `compDomDisplay` — 1 prop: `displayMode` (DTSelectDisplayMode).
  Decide el modelo raíz del bloque. Variations=Nothing.

- `compDomFlex` — 6 props: `flexDirection`, `flexWrap`,
  `justifyContent`, `flexAlignItems`, `alignContent`, `flexGap`.
  Aliases prefijados para no colisionar con compDomGrid en
  elementos que opten a ambas. Variations=Nothing.

- `compDomGrid` — 7 props: `gridTemplate`, `gridRows`,
  `justifyItems`, `gridAlignItems`, `gridGap`, `gridColumnGap`,
  `gridRowGap`. Variations=Nothing.

**Opt-in retrofit en 7 elements** (matriz de qué composición aplica a
qué container):

| Element | +Display | +Flex | +Grid | Razón |
|---|---|---|---|---|
| `elementStructSection` | ✓ | ✓ | ✓ | container estructural polivalente |
| `elementStructContainer` | ✓ | ✓ | ✓ | wrapper neutro polivalente |
| `elementStructStack` | ✗ | ✓ | ✗ | 1D flex por definición |
| `elementStructGrid` | ✗ | ✗ | ✓ | 2D grid por definición |
| `elementMediaGallery` | ✓ | ✓ | ✓ | imágenes en flex-wrap o grid preciso |
| `elementMediaLogoCloud` | ✓ | ✓ | ✗ | flex-wrap horizontal típico |
| `elementActionCtaGroup` | ✗ | ✓ | ✗ | botones en línea, flex |

**Retrofit no relacionado pero beneficiado de `DTSelectSpacingScale`**:
`compDomSpacing` pasa sus 3 props (`spacingTop`, `spacingBottom`,
`spacingInline`) de TextBox libre a dropdown cerrado contra el mismo
DataType. Mismos valores válidos (none/xs/sm/md/lg/xl/2xl), editor
ya no puede escribir "mediu".

**`compDomLayout` queda deprecada** (description marcada `LEGACY
(Ola 42 — deprecada)`). No se elimina el XML; los 5 elements que la
tenían (Stack/Grid/Gallery/LogoCloud/CtaGroup) la siguen componiendo
para backward-compat del Content editorial. El renderer lee primero
las composiciones nuevas y cae a `layoutDirection`/`layoutAlign`
sólo si las nuevas están vacías.

### Runtime

**`Synergos.CMS.Web/Services/LayoutCssBuilder.cs`** — helper estático
`public static IEnumerable<string> Build(IPublishedElement model)`
que lee todos los props de las 4 composiciones (Display/Flex/Grid/
Spacing + legacy Layout fallback) y emite BEM modifiers:

- `syn-display--{value}` del compDomDisplay.
- `syn-flex--dir-*`, `--wrap-*`, `--justify-*`, `--align-items-*`,
  `--align-content-*`, `--gap-*` del compDomFlex.
- `syn-grid--tpl-*`, `--rows-*`, `--justify-items-*`,
  `--align-items-*`, `--gap-*`, `--col-gap-*`, `--row-gap-*` del
  compDomGrid.
- `syn-space--top-*`, `--bottom-*`, `--inline-*` del compDomSpacing.
- Fallback: cuando `flexDirection`/`flexAlignItems` están vacíos
  pero el bloque tiene `layoutDirection`/`layoutAlign`, emite el
  equivalente flex (`syn-flex--dir-*`, `syn-flex--align-items-*`).

Los 7 renderers Razor (`Stack.cshtml`, `Grid.cshtml`,
`Section.cshtml`, `Container.cshtml`, `Gallery.cshtml`,
`LogoCloud.cshtml`, `CtaGroup.cshtml`) concatenan el output del
helper con su base class (`syn-stack`, `syn-section`, etc.) en el
atributo `class`.

### Regla que fija la granularidad

Cuando aparezca un nuevo container element que quiera opt-in a
layout:

1. Si es **flex por naturaleza** (stack, bar, inline-group) → sólo
   compDomFlex.
2. Si es **grid por naturaleza** (cuadrícula fija, masonry) → sólo
   compDomGrid.
3. Si el editor debe poder **elegir** topología → compDomDisplay +
   compDomFlex + compDomGrid. No hay forma de elegir "flex" sin
   tener compDomFlex compuesta; y viceversa.

## Scope de código autorizado por este ADR

1. Los 10 XMLs de DataTypes en `uSync/v9/DataTypes/DTSelect*.config`.
2. Los 3 XMLs de Compositions en
   `uSync/v9/ContentTypes/compdom{display,flex,grid}.config`.
3. El upgrade inline de `compdomspacing.config` (TextBox → DropDown
   referenciando `DTSelectSpacingScale`).
4. La actualización de description de `compdomlayout.config`
   marcándola `LEGACY`.
5. El opt-in Compositions + description-refresh en los 7 elementos
   retrofitteados.
6. `Synergos.CMS.Web/Services/LayoutCssBuilder.cs` — helper estático
   `IEnumerable<string> Build(IPublishedElement)`.
7. Actualización de los 7 renderers Razor a consumir el helper.

## What this ADR does NOT authorise

- Layout preset DocTypes del legado (`layoutpreset1col`, etc.). El
  inventario ya los marcaba REDISEÑAR; esta ADR los cierra como
  **descartados** — la composición per-block + gridTemplate
  dropdown subsume su propósito.
- `compDomLayoutPreset` y `compDomLayoutProfile` del legado.
  Subsumidos por `compDomDisplay` + `compDomFlex` + `compDomGrid`.
- Eliminar `compDomLayout`. Se deprecia, no se borra; el Content
  editorial que aún consuma `layoutDirection`/`layoutAlign` sigue
  renderizando correctamente via el fallback del helper.
- Mover `LayoutCssBuilder` a `Synergos.CMS.Application`. El helper
  consume `IPublishedElement` de Umbraco — violaría ADR 0002.
  Vive en Web por la misma razón que `DefaultBrandThemeProvider`
  y `FlowResolver`.
- Escribir el CSS correspondiente a los modifiers emitidos. El
  design-system externo es responsable del CSS real
  (`syn-flex--dir-row` → `flex-direction: row`).
- Tests unitarios. Ola 42 entrega schema + runtime; las pruebas
  caen en la fase de tests post-migración completa
  (feedback `feedback_tests_after_full_migration`).

## Consequences

**Positive**

- Editor arma su layout con dropdowns cerrados; cero typos, cero
  valores orphan que el CSS no conozca.
- Granularidad per-block: un hero grid + cuerpo flex-column + footer
  flex-row conviven en la misma página sin hack editorial.
- `compDomGrid.gridTemplate` da al editor 9 layouts predefinidos
  semánticos (`main-sidebar`, `holy-grail`, etc.) sin que tenga que
  pensar en `grid-template-columns: 2fr 1fr`.
- Schema y runtime desacoplados del design system: el helper emite
  classes; el CSS las implementa donde quiera (producto consumidor,
  brand custom layer).
- Una sola composición por concept — SRP. Una nueva familia de
  layout (ej. `compDomSubgrid`) se añade sin tocar ninguna de las
  tres existentes.

**Negative**

- Un container polivalente como `elementStructSection` gana **3
  compositions + 11 props de layout visibles** en su tab DOM. Es
  ruido para un editor que quiera "solo poner una section"; el
  default-empty de todos los props mitiga — si el editor no toca
  nada, el bloque se renderea `syn-section` pleno, sin modifiers.
- Los aliases prefijados (`flexAlignItems`, `gridGap`) leen raro vs
  los CSS originales (`align-items`, `gap`). Decisión necesaria
  para evitar alias collision entre las dos compositions; el
  helper los traduce al CSS class correcto
  (`syn-flex--align-items-*`).
- Content editorial existente con valores libres en `spacingTop`
  (e.g. "mediu") queda con string que no matchea el dropdown — el
  backoffice mostrará "(no value)" al editor. Esto es aceptable
  porque el user confirmó "tenemos el backup para mirar" y porque
  los valores libres no eran muchos en el Content actual.
- `gridColumns` (prop propia de `elementStructGrid` y
  `elementMediaGallery`) sobrevive como fallback cuando
  `compDomGrid.gridTemplate` está vacío. Redundante técnicamente
  pero mantiene continuidad editorial del legado (editor que ya
  aprendió "2-6 columnas" sigue sirviendo). Si la redundancia molesta
  en una ola futura, se quita gridColumns y se obliga a gridTemplate.

## Alternatives considered

- **Mantener layout preset DocTypes del legado (REFINAR)**. Rechazado:
  acopla layout a tipo de página, forzando CSS overrides editoriales
  cuando la página quiere mezclar topologías.
- **Un solo `compDomLayoutFull` con los 14 props + displayMode
  switch**. Rechazado: viola SRP; un cambio en modelo grid
  modificaría la misma composición que lee el modelo flex; ruido
  visible al editor incluso cuando sólo usa uno de los dos.
- **Permitir TextBox libres con Regex validator**. Rechazado: regex
  valida forma pero no semántica (un `space-arount` con typo
  pasaría un regex permisivo); además un editor que lee "value
  libre" asume que puede meter cualquier cosa.
- **Exponer CSS Custom Properties como props** (`--align: center`).
  Rechazado: acopla schema al CSS ecosystem del producto consumidor.
  El modelo BEM actual mantiene schema semántico y deja al design
  system traducir a CSS.
- **Heredar una abstracción desde el Layout composer del Epic Fail
  2**. Rechazado según memoria
  `history_layout_config_vision_epicfail2`: LayoutComposer del
  legado se **rediseña, no se copia**.

---

## Addendum Ola 42.5 — Layout Composer con presets + areas + preview

### Por qué un addendum en vez de un ADR nuevo

Ola 42 resolvió el layout **per-block** con dropdowns granulares. Eso
está bien — y sigue vigente — pero le faltaba la capa estructural
con la que el editor arma una página mixta (hero + main-sidebar +
3col) sin escribir markup. Ola 42.5 añade esa capa **encima** de las
composiciones existentes sin desplazarlas: sigue siendo el mismo
modelo BEM, mismo `LayoutCssBuilder`, mismos dropdowns en el tab DOM.

### Decisión

Se introduce el **Layout Composer** como Block Grid dedicado con
preview visual de presets en el backoffice. Componentes:

1. **10 `elementLayout*` ElementTypes** (carpeta `Elements/Layout`) —
   shell sin propiedades propias, opta por:
   - `compDomClass` (cssClass)
   - `compDomVariant` (variantKey)
   - `compDomSpacing` (top/bottom/inline — Ola 42 dropdowns)
   - `compDomPresetChrome` (NEW — containerType, theme, 4 flags noPad/noMargin)

   Matriz:

   | Alias | Areas | Uso |
   |---|---|---|
   | `elementLayoutSection` | 1 × 12 (sectionContent) | wrapper raíz, `<section>` |
   | `elementLayoutContainer` | 1 × 12 (containerContent) | wrapper neutro |
   | `elementLayoutStack` | 1 × 12 (stackContent) | flex 1D |
   | `elementLayoutGrid` | 1 × 12 (gridColumns, restricted a Column) | grid custom |
   | `elementLayoutColumn` | 1 × 12 (columnContent) | hijo de Grid, columnSpan 1-12 |
   | `elementLayout1Col` | 1 × 12 (main) | full width |
   | `elementLayout2ColEven` | 2 × 6+6 (left/right) | equilibrado |
   | `elementLayout2ColMainSidebar` | 2 × 8+4 (main/sidebar) | lectura + rail |
   | `elementLayout3Col` | 3 × 4+4+4 (col1..col3) | tríada |
   | `elementLayout4Col` | 4 × 3+3+3+3 (col1..col4) | densidad |

2. **`DT.BlockGrid.Sections`** — Block Grid DataType dedicado. Un
   solo BlockGroup "Layout" con los 10 presets. Cada block declara:
   - `contentElementTypeKey` → elementLayout* correspondiente
   - `areas` array con 1-4 areas (alias + columnSpan + key fresco + specifiedAllowance)
   - `columnSpanOptions` 12 en todos excepto Column (1-12)
   - `stylesheet` → `/App_Plugins/LayoutComposer/styles/layout-composer.css`
   - `view` → `~/App_Plugins/LayoutComposer/views/block-*.html`

3. **Plugin `App_Plugins/LayoutComposer/`** — recuperado del Epic
   Fail 2 archive, sin rediseñar (el plugin era un activo sólido;
   el problema de Epic Fail 2 era el acoplamiento code-first, no
   las views AngularJS):
   - `package.manifest` registra JS + CSS
   - `styles/layout-composer.css` — chrome backoffice (lc-block,
     lc-col-vis, lc-chip, lc-area-labels, etc.)
   - `scripts/layout-composer.preview.js` — filtros AngularJS
     sgPreviewText/Count/Host + autoload del CSS
   - `views/` — 10 HTML con column visualization + chips de config
     + `<umb-block-grid-render-area-slots>` para los drop zones

4. **`compDomPresetChrome`** (composición nueva, exclusiva de
   presets) — 6 props:
   - `containerType` (DT.Select.ContainerType): full-bleed | normal | narrow | ultra-narrow
   - `theme` (DT.Select.Theme): light | dark | brand | accent
   - `noPaddingTop`, `noPaddingBottom`, `noMarginTop`, `noMarginBottom` (TrueFalse)

5. **`DTSelectContainerType` + `DTSelectTheme`** — DataTypes dropdown.

6. **10 Razor SSR renderers** en `Views/Partials/blockgrid/Components/
   elementLayout*.cshtml` — iteran `Model.Areas` y llaman
   `Html.GetBlockGridItemsHtmlAsync(area)` por cada drop zone.
   Clases base: `syn-layout syn-layout--{alias}` + modifiers de
   `LayoutCssBuilder.Build(element)` (que ahora también emite los
   `syn-preset--*` del chrome).

7. **`sections` property** en `pageBase` (SortOrder=20, Culture,
   DT.BlockGrid.Sections). Tres campos editoriales escalables:
   - `body` (TinyMCE corto, para intros)
   - `bodyBlocks` (DT.BlockGrid.Editorial flat, legacy/simple)
   - `sections` (DT.BlockGrid.Sections con presets, **recomendado**)

### Regla editorial — cuándo usar cada campo

- **`sections`** es el default para páginas nuevas con layout
  estructurado (about, services, landings con hero + 2col + 3col).
- **`bodyBlocks`** para páginas simples flat (FAQ, términos, contact
  minimal) — el editor no necesita decidir topología.
- **`body`** sólo para intros cortos o páginas legacy.

Los tres conviven en pageBase; `PageBase.cshtml` los renderiza en
orden: SeoTitle → BodyHtml → sections → bodyBlocks.

### Scope de código autorizado por el addendum

1. `uSync/v9/DataTypes/DTSelectContainerType.config`,
   `DTSelectTheme.config`, `DTBlockGridSections.config`.
2. `uSync/v9/ContentTypes/compdompresetchrome.config` +
   `elementlayout*.config` (10 archivos en Elements/Layout).
3. `App_Plugins/LayoutComposer/package.manifest` + styles + scripts
   + 10 views HTML.
4. Property `sections` añadida a `page-base.config`.
5. Extensión de `Synergos.CMS.Web/Services/LayoutCssBuilder.cs`
   para emitir los `syn-preset--*` modifiers.
6. 10 Razor partials `Views/Partials/blockgrid/Components/
   elementLayout*.cshtml`.
7. Actualización de `Views/PageBase.cshtml` para renderizar
   `sections`.

### Lo que el addendum NO autoriza

- Añadir `sections` property a siteRoot/pageBasic/pageBare — queda
  pendiente; esos DocTypes tienen semánticas distintas (siteRoot es
  home con identity, pageBasic es smoke mínimo, pageBare es landing
  sin chrome). Si alguno los necesita, se decide caso a caso.
- Eliminar `bodyBlocks` de pageBase. Los tres campos coexisten;
  `bodyBlocks` es la ruta simple para páginas sin topología.
- Añadir presets adicionales (holy-grail, 2col asimétricos 8-4/4-8,
  separator, universal fallback) — el set de 10 cubre los casos
  reales reportados; se añaden más cuando surja el caso.
- CSS del design system que implementa `syn-layout--*`,
  `syn-preset--*`, `syn-layout__area--*` — es responsabilidad del
  producto consumidor.

### Consequences del addendum

**Positive**

- El editor ve el layout mientras lo arma: cada preset es preview
  visual con divisiones de columnas + chips de configuración
  activa + drop zones explícitas.
- Topología mixta por página natural: hero 1col → main-sidebar →
  3col → 1col cierre, todo en el mismo `sections` Block Grid.
- Preview del backoffice + SSR emiten la misma taxonomía BEM
  (`syn-layout--main-sidebar`, etc.): el design system implementa
  una vez, ambos entornos responden igual.
- Plugin es AngularJS (Umbraco 13 backoffice nativo) — sin build
  step adicional, sin dependencias externas.
- Presets son ElementTypes (no DocTypes) — el inventario Epic Fail
  2 sugería DocTypes, rechazado en §Decision original; presets como
  elements es más liviano en el árbol de contenido y encaja con el
  modelo Block Grid.

**Negative**

- Duplicación conceptual entre `elementStructSection/Container/
  Stack/Grid/Column` (Ola 20) y `elementLayoutSection/Container/
  Stack/Grid/Column` (Ola 42.5). Los primeros sirven dentro de
  `bodyBlocks` (editorial flat), los segundos dentro de `sections`
  (con areas). Coexisten; si converge en el tiempo, se deprecia
  una familia. Nota para olas futuras.
- Block Grid JSON con areas es ruidoso — `DTBlockGridSections.config`
  pesa ~190 líneas para 10 blocks. Trade-off aceptado: el JSON es
  declarativo y cada pieza es self-contained.
- Plugin `App_Plugins/LayoutComposer/` es AngularJS — si Umbraco
  migra el backoffice a un framework distinto en una versión
  futura, el plugin hay que reescribir. Mitigación: memoria
  `feedback_umbraco_version_pin.md` fija Umbraco 13.x.
- El editor con 10 presets puede elegir mal topológicamente. El
  design system debe proveer CSS `syn-layout--*` que sea visualmente
  razonable incluso con configuración vacía (defaults sensatos).

### Alternatives considered para Ola 42.5

- **No hacer presets, sólo dropdowns Ola 42**. Rechazado porque
  obliga al editor a entender flex/grid CSS para armar una página
  mixta — no escala a editores no técnicos.
- **Custom property editor (plugin) en vez de Block Grid con
  areas**. Rechazado porque Block Grid nativo de Umbraco 13 ya
  resuelve areas + drag-drop + preview; reescribir sería NIH.
- **Layout Preset DocTypes (como el inventario sugería)**.
  Rechazado: los DocTypes son nodos en el árbol de contenido —
  añaden ruido. ElementTypes dentro de un Block Grid son
  invisibles en el tree.
- **Reescribir el plugin en Lit o TypeScript moderno**. Rechazado
  porque Umbraco 13 backoffice sigue siendo AngularJS nativamente;
  un plugin moderno requeriría bridge + build step sin ganancia
  real vs copiar el archive.

---

## Addendum Ola 42.6 — UX al drop

Tres refinamientos visibles en el backoffice al dropear un preset.

### Decisión

**1. SVG thumbnails per-preset.** En vez del icon-* stock
compartido, cada preset trae su thumbnail propio en
`App_Plugins/LayoutComposer/thumbnails/layout-*.svg` (200×120,
paleta gris-azulada `#d4dbe6` / `#6b7c9b`). Cada block en
`DTBlockGridSections` declara su `"thumbnail": "~/App_Plugins/
LayoutComposer/thumbnails/layout-*.svg"`. Editor distingue 2col de
main-sidebar a simple vista.

**2. Defaults client-side antes del save.** Nuevo directive
AngularJS `lcInitDefaults` añadido a
`App_Plugins/LayoutComposer/scripts/layout-composer.preview.js`.
Corre en el pre-link phase del block preview view y rellena
`scope.block.data` con `containerType=normal`, `theme=light`,
`spacingTop=lg`, `spacingBottom=lg`, `spacingInline=md` cuando las
props están vacías. El atributo `lc-init-defaults` se añade al
root `<div>` de los 10 (luego 13) views HTML.

Complementa al handler server-side `LayoutPresetDefaults.cs`
(Ola 42.5) — dos capas defensivas: cliente rellena antes de que
el editor vea la overlay; servidor lo garantiza en persistencia.

**3. BlockGroups reordenados.** El array en
`DTBlockGridSections.config` se reordena para priorizar editorial
progression: Layout primero (siempre), Syn (CDN) segundo (memoria
`feedback_cdn_integration_is_core`), Comp tercero (bloques
compuestos), después Text/Action/Media/Info/Corp, después
Structural legacy, al final Form/Nav/Shop/Member/Flow. Coma suelto
del concat manual arreglado.

### Scope adicional Ola 42.6

- 10 SVG en `App_Plugins/LayoutComposer/thumbnails/`.
- Actualización de 10 `"thumbnail"` en `DTBlockGridSections.config`.
- Directive `lcInitDefaults` en `layout-composer.preview.js`.
- Atributo `lc-init-defaults` en los 10 views HTML existentes.
- Reorder del array `BlockGroups` en `DTBlockGridSections.config`.

---

## Addendum Ola 42.7 — taxonomía universal + 3 presets + mobile +
scaffold + 2 pages + semantic landmarks

Cinco refinamientos estructurales que completan el Layout Composer.

### Decisión

**1. Taxonomía universal de tabs.** Los 6 compositions DOM que
seguían en tab `"dom"` se mueven a tabs semánticas en el backoffice:

  | Composition | Tab |
  |---|---|
  | `compDomDisplay` | "Layout" (`layout`) |
  | `compDomFlex` | "Layout" |
  | `compDomGrid` | "Layout" |
  | `compDomLayout` (legacy) | "Layout" |
  | `compDomAttributes` | "Atributos" (`attributes`) |
  | `compDomVisibility` | "Visibilidad" (`visibility`) |

  Combinado con Ola 42.5 ("Estilo"/"Espaciado"), cualquier element
  que compone estos seams ve ahora un taxonomy consistente:
  Content → Estilo → Layout → Espaciado → Atributos → Visibilidad
  → SEO.

**2. 3 presets adicionales (13 totales).** El catálogo se completa:

  | Alias | Areas | Caso de uso |
  |---|---|---|
  | `elementLayoutHolyGrail` | nav (2) + main (8) + aside (2) | docs, portales admin, dashboards |
  | `elementLayoutSidebarMain` | sidebar (4) + main (8) | inverso de main-sidebar — ToC izquierda |
  | `elementLayoutHero` | content (12) full-bleed | franja hero de landing con `<section aria-label="Hero">` |

  Cada uno con: ElementType XML + entry en `DTBlockGridSections` +
  custom view HTML + SVG thumbnail + Razor renderer SSR + GUID en
  `LayoutPresetDefaults.PresetContentTypeKeys`.

**3. Mobile collapse override.** Nueva prop
`mobileCollapseMode` en `compDomPresetChrome` (tab "Layout", nueva
dentro de la composición), dropdown `DT.Select.MobileCollapse`
con 5 opciones: `auto` (default, sin class emitida), `stack`,
`reverse-stack`, `hide-sidebar`, `keep-multi`. `LayoutCssBuilder`
extiende con `syn-preset--mobile-{value}` cuando ≠ auto. Editor
override comportamiento responsive sin escribir CSS.

**4. Starter scaffold (opt-in).** Nueva clase
`Synergos.CMS.Application.Configuration.LayoutComposerSettings`
con flag `EnableStarterScaffold` (default `false`). Nuevo handler
`LayoutComposerStarterScaffold` hooked a
`ContentSavingNotification` que, cuando el flag está `true` Y la
content es `pageBase` Y sections está vacía, pre-puebla con Hero
+ 2ColEven. Registrado junto a `LayoutPresetDefaults` en
`SeamComposer`.

**5. siteRoot + pageBare opt-in a sections.** Cierra el set de
4 page types con Layout Composer — pageBase y pageBasic ya lo
tenían. siteRoot gana tab "Content" nueva; pageBare gana prop
sections a su tab existente. Ambos templates Razor emiten
sections si hay contenido, fallback a su render previo si no.

**6. Semantic HTML landmarks** en 3 renderers:
`elementLayoutHolyGrail` (`nav`/`main`/`aside` por alias de area),
`elementLayout2ColMainSidebar` y `elementLayoutSidebarMain`
(`main`/`aside`). Mejora a11y (screen readers) + SEO (Googlebot
parsing semántico de regiones).

### Scope adicional Olas 42.6 + 42.7

- Ola 42.6: 10 SVGs + directive `lcInitDefaults` + `lc-init-defaults`
  attr en 10 views + BlockGroups reorder.
- Ola 42.7:
  - 6 comp*.config retaggeadas (compDomDisplay/Flex/Grid/Layout/Attributes/Visibility).
  - 3 nuevos ElementType XMLs (HolyGrail/SidebarMain/Hero).
  - 3 nuevos custom views HTML.
  - 3 nuevos SVG thumbnails.
  - 3 nuevos Razor renderers SSR.
  - 3 nuevos block entries en `DTBlockGridSections.config`.
  - 3 GUIDs añadidos a `LayoutPresetDefaults.PresetContentTypeKeys`.
  - 1 nueva DataType `DTSelectMobileCollapse` + 1 nueva prop en
    `compDomPresetChrome` + extensión de `LayoutCssBuilder`.
  - 1 nueva settings class `LayoutComposerSettings` + binding en
    `OptionsComposer` + nuevo handler `LayoutComposerStarterScaffold`.
  - 2 page DocTypes opt-in (siteroot + page-bare) + 2 templates
    Razor actualizados.
  - 3 renderers con semantic HTML landmarks.

### Updated status

El Layout Composer pasa de "schema completo" (Ola 42.5) a
"producto refinado" con catálogo completo de 13 presets, UX sin
estado vacío en ningún momento, control responsive explícito,
semantic HTML correcto, y consistencia de taxonomy tabs en el
100% del sistema de compositions DOM.

