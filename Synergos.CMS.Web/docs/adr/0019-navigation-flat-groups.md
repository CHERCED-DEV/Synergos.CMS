# ADR 0019 — Navigation: flat groups, no recursion (SSR + a11y first)

- **Status:** Accepted
- **Date:** 2026-04-22
- **Deciders:** Project owner
- **Source:** promoted from `refactor-docs/adr-drafts/0019-navigation-flat-groups.md` (Draft 2026-04-21)
- **Authorises:** Ola 24 (Nav family — NavGroup + NavItem + DT.BlockList.NavItems), commit `0f39934`
- **Related:** ADR 0009 (Extension seams mandatory), ADR 0018 (Forms dual-path — mismo patrón de hidden-child aplica)

## Context

El producto necesita elementos de navegación editorial (main nav,
footer nav, sidebar nav, nav inline en long-form content). El legado
Epic Fail 2 tenía un único `elementNavItem` con soporte recursivo
(children via Block List auto-referenciado) para menús multi-nivel.

Dos problemas con ese diseño:

1. **Complejidad SSR + a11y**: un menú recursivo requiere JS para
   flyout/dropdown, manejo de `aria-expanded`, `aria-haspopup`,
   focus trap, keyboard navigation (Arrow+Esc). Implementarlo desde
   cero en el CMS duplica trabajo que el design-system resuelve.
2. **Editor mental model**: recursión arbitraria lleva a menús de
   4-5 niveles que el UX no soporta. La mayoría de sitios bien
   diseñados tienen 1-2 niveles de nav.

## Decision

Se adopta un modelo **flat por grupos, sin recursión** con dos
Element Types en Ola 24:

### `elementNavGroup`

- Container semántico `<nav>` con `aria-label` (de `navHeading` o
  "Navegación" default).
- Props: `navHeading` (Culture opcional — emite `<h3>` si presente),
  `navItems` BlockList mandatory 1+.
- Backing: `DT.BlockList.NavItems` (mandatory 1+, inline editing on
  para UX rápido).
- Composiciones: `compDomClass` + `compDomVariant`.
- Expuesto en el top-level del Editorial BlockGrid bajo BlockGroup
  "Nav".

### `elementNavItem`

- Atomic link `<li><a href>`. Props: `navLabel` (Culture mandatory),
  `navUrl` (Culture mandatory — localizable), `navOpenInNewTab`
  (Nothing).
- **Hidden top-level**: solo instanciable dentro de
  `NavGroup.navItems`. Mismo patrón que `elementFormField` (ADR
  0018) y `elementCorpTabPanel` (Ola 24).

### Deep menus

Cuando un sitio necesita nav de 2+ niveles, la solución **no** es
recursión en `elementNavItem`, sino:

- **Múltiples `elementNavGroup` hermanos**, uno por sección,
  colocados en un `elementStructStack` o `elementStructGrid`.
  Ejemplo: footer con columnas "Producto", "Recursos", "Legal",
  "Social" = 4 NavGroups dentro de un Stack.
- **Dropdown/flyout**: si el design-system necesita que un link
  revele sub-items, **eso se resuelve en el design-system JS/CSS**,
  no en el schema del CMS. El schema solo provee la lista semántica;
  el comportamiento visual es responsabilidad del host runtime
  (CDN bundle o `elementSyn<NavBlock>` futuro).
- **Mega-menu**: si se requiere nav compleja con tipos mixtos
  (links + highlighted cards + imágenes), redirigir al patrón
  `elementSyn*` (ADR 0015) — crear un bloque CDN-hosted dedicado
  tipo `elementSynMegaMenu`.

### URL localization

`navUrl` es **Culture**, no Nothing. Rationale:

- Paths multi-culture típicos: `/en/about` ≠ `/es/acerca`. Forzarlo
  Nothing obligaría al editor a codificar paths técnicos en vez de
  paths legibles por idioma.
- Para links invariantes (external `https://`, `mailto:`, `tel:`,
  `#anchor`), el editor escribe el mismo valor en ambos culture
  variants — costo marginal.
- Alternativa descartada: `navUrl` como `ContentPicker` → añade
  complejidad de Udi resolution en renderer, y para links externos
  no aplica de todas formas.

### A11y mandatorio

- `<nav aria-label>` mandatorio (de heading o default).
- `<ul><li>` semántico — screen readers anuncian conteo y posición.
- `target="_blank"` + `rel="noopener noreferrer"` auto-emitido
  cuando `navOpenInNewTab=true`.
- Sin JS: la nav es navegable con Tab y Enter. El design-system
  puede mejorar UX (Arrow keys, Esc para cerrar dropdown), pero el
  fallback HTML funciona.

## Consequences

**Positive**

- Schema trivial de editar y razonar.
- SSR completo sin JS.
- A11y obligatoria por diseño.
- Composición flat permite que el design-system decida layout
  (grid, stack, dropdown) sin tocar el CMS.

**Negative / limitaciones conocidas**

- Menús deeply-nested (3+ niveles) no son expresables. Se acepta
  porque es un anti-patrón UX.
- No hay current-page highlighting en SSR (requiere comparar
  `navUrl` con URL actual). Work around: el design-system JS marca
  el active item post-hydration. Si luego se requiere SSR active
  marking, añadir helper Razor que compare URLs en `Item.cshtml`.
- No hay breadcrumb Element Type todavía. Reservar
  `elementNavBreadcrumb` para ola futura si se identifica la
  necesidad.

## Re-evaluación

- Si 3 sitios clientes piden mega-menu con lógica compleja, crear
  `elementSynMegaMenu` (ADR 0015 pattern) — NO reabrir recursión
  en `elementNavItem`.
- Si la internacionalización de `navUrl` genera friction editorial
  (editores pegan siempre el mismo path), evaluar `ContentPicker`
  como DataType alternativo en una ola posterior.
