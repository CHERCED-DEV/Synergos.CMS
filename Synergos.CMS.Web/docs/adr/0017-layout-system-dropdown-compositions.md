# ADR 0017 — Layout system: per-block compositions con dropdowns

- **Status:** Accepted
- **Date:** 2026-04-22
- **Deciders:** Project owner
- **Source:** redactado directamente como Ratified (no draft previo); el
  slot 0017 estaba vacío en `refactor-docs/adr-drafts/`.
- **Authorises:** Ola 42 del plan de migración (Layout system
  rediseñado).

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
