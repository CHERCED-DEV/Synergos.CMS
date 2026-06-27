# ADR 0101 — Contrato de identidad pageThemeVariant↔brandKey↔data-theme (1:1) + "recipe de tema" verificable + temas eventsNight / terraLux

- **Status:** Accepted
- **Date:** 2026-06-26
- **Deciders:** Arquitecto + agente, fase SynergosLabs (OLA 4.5 — precondición de los verticales Eventos/Propiedades). Verificado contra código vivo (`DTSelectPageThemeVariant.config`, `DefaultPageRenderContextResolver`, `_Layout.cshtml`, `_BrandThemeStyle.cshtml`, `DefaultBrandThemeProvider`, `wwwroot/css/syn-tokens.css`, `DevContentFiller`).
- **Relacionados:** ADR 0010 (branding vía provider, sin `if (brand.Key == "X")`), ADR 0020 (brand theme provider + themeSettings), ADR 0022 (page composition standard — theme triplet), ADR 0042 (DTSelect mirror en `DropdownOptions`), ADR 0048 (CSS design system espejo de Synergos.UI), ADR 0094 (design tokens como única fuente de verdad + identidad por-siteRoot vía mapeo a tokens canónicos).

---

## Context

La identidad visual de SynergosLabs se aplica por `siteRoot`: cada propiedad
(Entidad, Blogs, Tienda, y los verticales que vienen) pinta su propio tema. El
mecanismo ya existía y estaba vivo, pero **nunca se había ratificado como un
contrato**, así que agregar un tema nuevo era arqueología en vez de un
procedimiento. ADR 0094 dejó la línea tokens-first y arregló el bug de tokens
muertos; falta fijar **la cadena de identidad** y **cómo se extiende**.

El estado real de la cadena (verificado en código):

1. **Schema** — `uSync/v9/DataTypes/DTSelectPageThemeVariant.config`
   (`Umbraco.DropDown.Flexible`) enumera los valores válidos de
   `pageThemeVariant`. Es la fuente de verdad de qué variantes existen.
2. **Resolución** — `DefaultPageRenderContextResolver.Resolve()` lee
   `pageThemeVariant` **sólo del siteRoot** (post-Ola 71: las pages ya no
   componen `compPageTheme`; el tema se gestiona única y exclusivamente desde el
   siteRoot, con `"inherit"`/vacío → default), y lo expone como
   `PageRenderContext.ThemeVariant`.
3. **Emisión** — `Views/_Layout.cshtml` emite
   `<html data-theme="@renderCtx.ThemeVariant">` **verbatim**: el value del
   dropdown viaja sin transformar al atributo `data-theme`.
4. **Pintado** — `wwwroot/css/syn-tokens.css` define un bloque
   `[data-theme="X"]` por variante que sobre-escribe las familias semánticas de
   tokens. Si no hay bloque para el value emitido, el documento se queda con el
   tema indigo frío de `:root` (default).
5. **Identidad de marca** — en paralelo, `DefaultBrandThemeProvider` resuelve un
   `BrandTheme` (primary/accent/fonts/logos) buscando el nodo con
   `HasProperty("primaryColor")` cuyo `brandKey` matchea el del siteRoot, y
   `_BrandThemeStyle.cshtml` lo mapea a los tokens canónicos (ADR 0094).

Esto deja la regla implícita pero no escrita: **un `siteRoot` tiene un
`brandKey` (identidad de negocio) y un `pageThemeVariant` (cuál `data-theme`
pinta), y para que la identidad sea coherente ambos deben corresponder 1:1 con
el bloque CSS del tema.** Sin contrato, dos riesgos concretos:

- **Drift de naming** (la decisión D-GOV-1 de esta ola): en `syn-tokens.css`
  coexistían selectores `silver-gold` (kebab) y `silverGold` (camel) apuntando
  al mismo tema. ¿Cuál es el canónico?
- **Tema incompleto** — un bloque `[data-theme]` al que le falta una familia de
  tokens (le pasó a `silver-gold`, que se parchó en C-2) hace que esa familia
  caiga al `:root` indigo frío, rompiendo la identidad sólo en ciertos
  componentes. Sin checklist de completitud, cada tema nuevo arriesga lo mismo.

OLA 4.5 estrena dos temas (`eventsNight` para el vertical Eventos, `terraLux`
para Propiedades) y es la oportunidad de ratificar el contrato antes de que
OLA 5 / OLA 6 los monten.

## Decision

### 1. Contrato de identidad 1:1

Cada propiedad (siteRoot) declara su identidad con **tres claves que
corresponden 1:1**:

| Clave | Dónde vive | Quién la lee |
|-------|------------|--------------|
| `brandKey` | propiedad del `siteRoot` (Nothing, `^[a-z][a-z0-9-]*$`) | `DefaultBrandThemeProvider` → `BrandTheme` (primary/accent/fonts/logos) |
| `pageThemeVariant` | propiedad del `siteRoot` (`DTSelectPageThemeVariant`) | `DefaultPageRenderContextResolver` → `ThemeVariant` |
| `data-theme` | atributo en `<html>` emitido por `_Layout.cshtml` | el bloque `[data-theme="X"]` de `syn-tokens.css` |

**El value de `pageThemeVariant` ES el value de `data-theme` ES el nombre del
bloque CSS** (string idéntico, sin transformación). El `brandKey` es la
identidad de negocio y, por convención de coherencia, **se nombra igual que su
`pageThemeVariant`** salvo los siteRoots que comparten una variante genérica
(p.ej. la Entidad usa `pageThemeVariant=light`/`brand` con su propio brandKey).
La marca entra al layer visual **sólo por mapeo a tokens canónicos** (ADR 0094),
nunca con `if (brand.Key == "X")` (ADR 0010): `DefaultBrandThemeProvider` ya es
brand-agnóstico (resuelve por propiedad `primaryColor` + `brandKey`), así que un
tema nuevo **no requiere código condicional por marca**.

La cascada de resolución de la variante es **siteRoot-only** (no page-level):
`page` no puede override el tema (decisión Ola 71). `"inherit"`/vacío en el
siteRoot → default (`light`, indigo frío de `:root`).

### 2. D-GOV-1 — `silverGold` (camelCase) es el SoT; `silver-gold` (kebab) es alias deprecado

Evidencia que decide el canónico (qué emite el runtime de verdad):

- `DTSelectPageThemeVariant.config` ofrece **sólo** `"silverGold"` (no existe la
  opción kebab) → es el único value que un editor puede elegir.
- `uSync/v9/Content/blogs.config` (contenido publicado) tiene
  `pageThemeVariant = "silverGold"`.
- `DevContentFiller` siembra el vertical Blogs con `"silverGold"`.
- `DropdownOptions.PageThemeVariant.SilverGold = "silverGold"`.
- `_Layout.cshtml` emite `data-theme` **verbatim** → el DOM real es
  `<html data-theme="silverGold">`.

Por tanto **`silverGold` (camelCase) es la fuente de verdad canónica**. El
selector `[data-theme="silver-gold"]` (kebab) y las clases `.theme-*` **no los
emite nadie**: se conservan como **aliases deprecados** (selectores duplicados
apuntando al mismo bloque) para no romper referencias externas eventuales, y
quedan anotados como tales en `syn-tokens.css`. **Regla de naming para temas
nuevos: el value canónico es camelCase** (`eventsNight`, `terraLux`); no se
crean variantes kebab nuevas.

### 3. RECIPE — cómo agregar un tema nuevo (verificable)

Para estrenar un `data-theme` `<X>` (camelCase) en una propiedad nueva:

1. **CSS (`wwwroot/css/syn-tokens.css`)** — agregar un bloque
   `:root[data-theme="X"], [data-theme="X"], .theme-X { … }` que defina **TODAS
   las familias semánticas** que definen los bloques `dark` + `silverGold`
   (paridad token-a-token). Las primitivas (`--syn-color-neutral-*`,
   `-brand-N`, `-danger-N`, …) viven sólo en `:root` y **no** se redefinen; un
   tema redefine las **familias semánticas**:
   - `color-scheme`
   - **channels** (RGB tripletes, no hex): `--syn-channel-brand`,
     `--syn-channel-accent`, `--syn-channel-ink`
   - **surface**: canvas, primary, secondary, tertiary, elevated, inverse,
     accent, accent-soft, overlay, strong
   - **text**: primary, secondary, muted, disabled, inverse, accent, on-accent
   - **border**: subtle, default, strong, accent, inverse
   - **overlay**: subtle, medium, strong
   - **action-primary**: primary, hover, active, text, emphasis
   - **action-secondary**: surface, surface-hover, text, text-hover, border,
     border-hover
   - **action-ghost**: surface, surface-hover, text, text-hover
   - **action-danger**: danger, hover, text
   - **action-disabled**: surface, border, text
   - **focus-ring** + **focus-ring-danger**
   - **state-brand / state-info / state-success / state-warning / state-danger**:
     surface, border, text (+ `--syn-color-info` y `--syn-color-info-surface`)
   - **gradient-hero**
   - **shadows**: card, card-hover, panel, floating, action, action-hover

   Verificación de completitud (la regla que evita el bug de `silver-gold`):
   el set de familias semánticas del bloque nuevo debe igualar la **unión** de
   las familias de `dark` + `silverGold` (75 familias hoy). Diff vacío = tema
   completo. Un tema incompleto cae al indigo frío de `:root`.
2. **Schema (`DTSelectPageThemeVariant.config`)** — agregar
   `{ "id": N, "value": "X" }` a los items del dropdown (uSync Import requerido).
3. **Mirror (`DropdownOptions.PageThemeVariant`)** — agregar
   `public const string X = "X";` (ADR 0042; recompila C#).
4. **siteRoot** — la propiedad nueva declara `brandKey = X` (o el brandKey de
   negocio) **y** `pageThemeVariant = X`. Para los verticales seedeados,
   `DevContentFiller.SeedVertical(...)` recibe la variante; un `themeSettings`
   con `primaryColor` + `brandKey = X` da identidad de marca (opcional —
   `_BrandThemeStyle` degrada con grace si no existe).

No se toca `_BrandThemeStyle.cshtml` ni el resolver ni el layout por tema: el
contrato es genérico. La única salvedad es la rama `variant == "light"` de
`_BrandThemeStyle.cshtml` (~line 85), que inyecta surface/text base a `:root`
**sólo** para `light`; cualquier tema NO-`light` trae su surface/text de su
bloque `[data-theme]` y no se pisa (P0-5).

### 4. Temas estrenados en esta ola

- **`eventsNight`** (vertical Eventos "Electric Night") — base midnight
  `#0B1020`, surface `#141A2E`, primary electric violet `#7C5CFF`, accent cyan
  `#22D3EE`, text `#F4F6FF`, success/early-bird `#34D399`. `color-scheme: dark`.
  Registro propio (NO clon de `dark`).
- **`terraLux`** (vertical Propiedades "Terra Lux") — base warm ivory `#FBF8F3`,
  surface `#FFFFFF`, ink/text `#1C2B24`, primary warm bronze `#B08D57` (rol
  emphasis) con CTA principal sage `#2F6F5E`, accent sage `#2F6F5E`, alert
  terracotta `#C2603D` (rol danger). `color-scheme: light`.

Ambos definen las 75 familias semánticas (diff vacío contra la unión
dark+silverGold) + los tres channels como RGB tripletes derivados de su
primary/accent/ink.

## Consequences

**Positivas**

- **Agregar un tema es un procedimiento, no arqueología**: la recipe es
  verificable (diff de familias = vacío) y desbloquea OLA 5 / OLA 6 sin riesgo
  de drift de identidad como C-1/C-2.
- **D-GOV-1 zanjado con evidencia**: `silverGold` canónico, kebab como alias
  deprecado documentado — cero ambigüedad de naming, cero ruptura.
- **Completitud garantizada**: `eventsNight` y `terraLux` definen las 75
  familias; no caen al indigo frío en ningún componente.
- **Sin código por marca**: el contrato es genérico (resolver + layout +
  provider son brand-agnósticos); refuerza ADR 0010 + 0094.
- **Channels theme-aware por tema**: glows/gradientes de Eventos/Propiedades
  siguen su identidad (violet/cyan, bronze/sage) automáticamente.

**Negativas / trade-offs**

- **Mantenimiento manual de la paridad de familias**: la completitud se verifica
  con un diff manual (o el check G-1 del audit), no hay gate automático que
  falle un tema incompleto. Mitigación futura: extender `tools/usync-audit` o el
  build de tokens para cross-checkear cada `[data-theme]` contra la unión
  canónica. Diferido.
- **Doble fuente de verdad CMS↔UI** (heredada de ADR 0094): los mismos dos
  temas deben espejarse en el SCSS de `Synergos.UI` (`_brand.scss`) para que el
  render hidratado por web component iguale al SSR. Esta ola sólo toca el CSS del
  CMS; el espejo en UI queda para el build de los verticales (OLA 5/6).
- **Mirror dual `DropdownOptions`** (ADR 0042): agregar una variante toca dos
  archivos (XML + C#); costo conocido y aceptado.

**Neutras**

- 0 GUIDs nuevos, 0 paquetes NuGet, 0 schema nuevo más allá de 2 items de
  dropdown en un DataType existente. Cambios concentrados en
  `wwwroot/css/syn-tokens.css` (2 bloques + nota de alias),
  `uSync/v9/DataTypes/DTSelectPageThemeVariant.config` (2 items),
  `DropdownOptions.cs` (2 consts) + comentarios en `_BrandThemeStyle.cshtml` y
  `BrandThemeStyleModel.cs`.
- **uSync Import requerido** para que el dropdown ofrezca `eventsNight`/
  `terraLux` en backoffice (y para que `DevContentFiller`/`SeedVertical` pueda
  publicar esos values). **Recompila C#** por el mirror en `DropdownOptions`.
  El CSS es estático (sirve en caliente, sin import ni recompila).

## Alternatives considered

- **Adoptar `silver-gold` (kebab) como canónico y migrar el dropdown/contenido**
  — rechazado: invertiría el SoT real (todo el runtime emite camelCase) y
  forzaría una migración de contenido publicado sin beneficio.
- **Transformar el value en `_Layout` (camel→kebab) antes de emitir
  `data-theme`** — rechazado: agrega una capa de mapeo invisible que vuelve a
  separar el value del editor del selector CSS; el contrato 1:1 verbatim es más
  simple y auditable.
- **Clonar el bloque `dark` para `eventsNight`** — rechazado (alineado con el
  roadmap): Eventos es un registro de identidad propio (violet/cyan saturado),
  no una variante de dark; clonar diluiría la identidad.
- **Gate automático de completitud ahora** — diferido (no rechazado): es la
  mitigación correcta del riesgo "tema incompleto", pero se construye con los
  verticales cuando exista el espejo CMS↔UI a validar (CLAUDE.md §6, no
  abstracción prematura).

## References

- ADR 0010 — Branding vía provider (identidad sin conditional branching).
- ADR 0020 — Brand theme provider + `themeSettings` (la marca por siteRoot).
- ADR 0022 — Page composition standard (theme triplet en el render context).
- ADR 0042 — DTSelect mirror en `DropdownOptions` (dual-write).
- ADR 0048 — CSS design system espejo de Synergos.UI.
- ADR 0094 — Design tokens como única fuente de verdad + identidad por-siteRoot.
- `wwwroot/css/syn-tokens.css` — bloques `[data-theme]` (SoT de cada tema).
- `Views/_Layout.cshtml` — emite `<html data-theme="@renderCtx.ThemeVariant">`.
- `Views/Shared/_BrandThemeStyle.cshtml` — mapeo brand → tokens canónicos.
- `Services/DefaultPageRenderContextResolver.cs` — resolución siteRoot-only.
- `Services/DefaultBrandThemeProvider.cs` — `BrandTheme` brand-agnóstico.
- `uSync/v9/DataTypes/DTSelectPageThemeVariant.config` — valores válidos.
- `reference_siteroot_identity_system` (memoria — composición Apariencia).
- `reference_design_line_canonical` (memoria — línea canónica).
