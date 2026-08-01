# Empalme CMS ↔ UI — `elementSyn*` / `<synergos-*>` / bundle CDN

Fuente autoritaria: ADR 0012 (CDN contract consumed) + ADR 0015 (SynHost framework-agnostic) + ADR 0083 (CMS↔UI alignment) + memorias `feedback_synhost_naming_convention`, `feedback_framework_agnostic_integration`, `feedback_cdn_integration_is_core`.

## Las 2 maneras de renderizar un block en Synergos

### 1. SSR puro (Razor partial)

El bloque se renderiza 100% server-side. No hay JS de cliente requerido para que funcione el contenido visible inicial.

- Razor partial vive en `Views/Partials/blockgrid/Components/{alias}.cshtml` o en `Views/Shared/SynHost/{Block}.cshtml`.
- Wrapper universal `Views/Shared/SynHost/_Wrapper.cshtml` aplica las propiedades de `compdom*` (class, variant, visibility, attributes, spacing, flex, grid, display).
- Tag HTML emitido es un elemento estándar (`<section>`, `<article>`, `<div>`, etc.) o un elemento `<synergos-*>` placeholder.

**Cuándo usar SSR:**
- Contenido editorial puro, sin interacción JS no-trivial (heading, paragraph, image, banner estático, FAQ accordion simple si se acepta sin animación, breadcrumbs).
- SEO crítico — el bot tiene que ver el contenido en la primera respuesta.

### 2. CDN-hosted bundle (`elementSyn*`)

El bloque emite HTML mínimo en el server (un `<synergos-{kebab}>` con atributos data-*) y un bundle JS publicado por el CDN team hidrata el componente en el cliente.

- Schema alias del block: `elementSyn{PascalCase}` (ej. `elementSynAccordion`, `elementSynCarousel`, `elementSynFormBuilder`).
- DOM tag emitido: `<synergos-{kebab-case}>` (ej. `<synergos-accordion>`, `<synergos-carousel>`, `<synergos-form-builder>`).
- Bundle path canonical: `/elements/{alias}/{version}/main.js`.
- Razor partial vive en `Views/Shared/SynHost/{Block}.cshtml` y emite el `<synergos-*>` con atributos data + slots.

**Cuándo usar elementSyn*:**
- Interacción rica que justifica framework runtime (formularios complejos, carousels con accesibilidad robusta, charts, mapas, dashboards inline).
- Componente que debe ser reusable entre brands/deploys vía CDN.
- Componente que el equipo UI tiene como Angular custom element (Angular es el primer adapter por producto, no por lock-in).

## Convención de naming canónica

| Asset | Convention | Ejemplo |
|---|---|---|
| Custom element tag (DOM) | `synergos-{kebab-case}` | `<synergos-accordion>` |
| Schema alias (CMS, uSync) | `elementSyn{PascalCase}` | `elementSynAccordion` |
| Bundle path (CDN) | `/elements/{alias}/{version}/main.js` | `/elements/elementSynAccordion/1.4.2/main.js` |
| CSS token | `--syn-{category}-{descriptor}` | `--syn-color-brand-500` |
| Dictionary key (i18n) | `{Section}.{SubSection}.{Key}` PascalCase | `Admin.Action.Approve` |
| CustomEvent name | `syn:{component}:{event}` | `syn:accordion:opened` |
| Window namespace | `window.synergos.*` | `window.synergos.i18n.t(...)` |

**Naming legacy `cdn*`:** está deprecado. Reemplazado por `elementSyn*` + `<synergos-*>`. Si encuentras un alias `cdn*` en uSync, marca al arquitecto que es legacy y debe migrarse en una ola futura — no construyas contenido nuevo encima.

## Estado actual del bridge CDN (importante)

**`HttpBundleRegistryClient` está bloqueado** esperando que el equipo CDN publique los 5 puntos del contrato `Synergos.CMS/Synergos.CMS.Web/docs/umbraco/cdn-contract.md`. Hasta entonces:

- `StubBundleRegistryClient` está activo en runtime.
- Cada `<synergos-*>` emite HTML placeholder (comentario indicando que el bundle no está disponible).
- Los blocks `elementSyn*` son schema-completos pero **no se hidratan visualmente** en producción/dev.

**Implicaciones para autoría:**
- Si el arquitecto va a crear contenido con `elementSyn*` ahora, **adviértele** que el bloque va a aparecer como placeholder hasta que CDN team publique. El contenido editorial dentro del block (datos, copy) es válido y se preserva.
- Si la pieza requiere render visible YA y no puede esperar, pregúntale si hay un equivalente SSR puro (`element*` no-Syn) que cubra el caso temporalmente.

## Los 5 contratos CMS↔UI

Si la pieza toca el bridge UI, considera los 5 contratos en `Synergos.CMS/Synergos.CMS.Web/docs/contracts/`:

| # | Doc | Cuándo importa para autoría |
|---|---|---|
| 1 | `cdn-bundle-registry.md` (link a `umbraco/cdn-contract.md`) | Cuando recomiendas un `elementSyn*` — el bundle se resuelve aquí. |
| 2 | `dom-events.md` | Si el block emite eventos que otros blocks o el host escuchan (`syn:accordion:opened`, etc.). |
| 3 | `css-tokens.md` | Cuando el copy del schema o el styling del block referencian tokens `--syn-*`. |
| 4 | `i18n-bridge.md` | Cuando el block UI consume `window.synergos.i18n.t(key, fallback)` para texto traducido. La key viene del Dictionary uSync. |
| 5 | `host-bridge.md` | Big picture runtime — léelo si hay duda del init order entre el HTML server y el bundle. |

## Reglas de no-acoplamiento

- ❌ El UI **no importa código del CMS** (sin shared TS package, sin gRPC stubs).
- ❌ El CMS **no importa código del UI** (sin npm install del UI).
- ❌ Cero shared NuGet/npm package.
- ✅ Single source of truth del schema vive en el CMS uSync XMLs.
- ✅ Cambios de contracts → ADR antes de implementar. Compatibilidad backward-first.

## Para el arquitecto al elegir SSR vs elementSyn*

Pregunta de filtro:

1. ¿El bot SEO necesita ver el contenido renderizado en la primera respuesta HTML? → SSR.
2. ¿Hay interacción JS no-trivial (drag, charts, multi-step forms, virtual scroll)? → elementSyn*.
3. ¿Es contenido que vamos a versionar y deployar independiente del CMS? → elementSyn*.
4. ¿Caso ambiguo? Default a SSR; muévelo a elementSyn* solo cuando la complejidad cliente lo justifique.

## Custom elements actualmente en schema (referencia rápida)

Para listar los `elementSyn*` vivos en el schema, lee `Synergos.CMS/Synergos.CMS.Web/uSync/v9/ContentTypes/elementSyn*.config`. Cada archivo tiene `<Description>` que indica intent y `<GenericProperties>` que indica los campos.

**Ojo:** el archivo en disco usa filename lowercase (convención uSync), pero el alias dentro del XML es PascalCase. Ej: `elementsynaccordion.config` → `<Alias>elementSynAccordion</Alias>` → DOM `<synergos-accordion>`.
