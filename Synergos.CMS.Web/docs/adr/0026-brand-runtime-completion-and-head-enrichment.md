# ADR 0026 — Brand runtime completion + `<head>` enrichment (Ola 54)

- **Status:** Accepted
- **Date:** 2026-04-25
- **Deciders:** Arquitecto + agente, durante Ola 54
- **Extiende:** ADR 0010 (Branding via provider) + ADR 0020 (Platform Settings)

## Context

Tras Ola 53 (sync masiva CMS↔UI con 71 elementSyn* scaffolds en Synergos.UI),
quedaban tres frentes runtime del CMS desbloqueados sin tocar:

1. **Multi-brand routing dinámico**: `IBrandingProvider` resolvía
   exclusivamente desde `BrandingSettings` POCO (config estática). Un
   deploy con varios brands en el mismo Umbraco instance no podía
   resolver el brand correcto basado en el host del request.
2. **Brand-aware theme inline emission con scoping page-level**: la
   emisión de CSS custom properties (`--syn-color-primary`, etc.)
   estaba inline en `_Layout.cshtml`. No reusable desde `PageBare.cshtml`
   (Layout=null), sin override por `compPageTheme.pageThemeVariant`
   page-level.
3. **`<head>` con gaps**: `_SeoHead.cshtml` (Ola 44.2) cubría OG +
   meta básico + canonical, pero faltaban Twitter Card, JSON-LD
   structured data y hreflang multi-culture.

Los tres son refinamientos sin bloqueador externo (no dependen del
contrato CDN team) y de bajo riesgo.

## Decision

### Parte A — `HostBasedBrandingProvider`

Reemplazar `DefaultBrandingProvider` (single-brand from config) por
`HostBasedBrandingProvider` (multi-brand from request host) en
`SeamComposer`.

**Pipeline de resolución (primer match gana):**

1. Sin `HttpContext` o sin `IUmbracoContextAccessor` → fallback a
   `DefaultBrandingProvider(BrandingSettings)`.
2. Para cada `siteConfigSettings` publicado:
   - Resolver su `siteRoot` ancestro
   - Comparar `siteRoot.canonicalHostname` (case-insensitive, strip
     protocol + port + path) con `Request.Host.Host`
   - Si match → devolver `BrandIdentity(brandKey, brandDisplayName)`
     leído de `siteConfigSettings.brandKey/brandDisplayName` (vía
     `compBranding`)
3. Sin match → fallback a `DefaultBrandingProvider`.

**Invariante ADR 0010 respetado**: el provider sigue siendo el único
punto de resolución de brand identity. Consumidores no branchean en
`brandKey` — el brand es identidad transportada.

### Parte B — `_BrandThemeStyle` partial + page-level variant

Mover la emisión de `<style>:root { --syn-color-X: ... }</style>` de
inline en `_Layout.cshtml` a un partial reusable
`Views/Shared/_BrandThemeStyle.cshtml`, con modelo
`BrandThemeStyleModel(BrandTheme? Theme, string? PageThemeVariant)`.

El partial emite:

- `:root { --syn-color-primary: ...; ... }` — vars base del brand
- `[data-theme="dark"] { ... }` — override dark (invierte fondos/texto)
- `[data-theme="silverGold"] { ... }` — paleta neutra
- `[data-theme="brand"] { ... }` — refuerza primary
- `[data-surface="muted"] { ... }` — bg mezclado 92/8
- `[data-surface="contrast"] { ... }` — invierte bg/text
- `[data-surface="brand"] { ... }` — bg = primary
- `[data-surface="transparent"] { ... }` — bg = transparent

Los selectores `[data-theme="X"]` y `[data-surface="X"]` ya vienen
emitidos por `_Layout.cshtml` y `PageBare.cshtml` en el `<html>` desde
Ola 49.6 (consumiendo `PageRenderContext.ThemeVariant` y `PageSurface`).

**`PageBare.cshtml` ahora también consume** el partial. Cierra el gap
donde páginas Bare (Layout=null) no recibían el theme del brand activo.

### Parte C — `_SeoHead.cshtml` enriquecido

Agregar al partial existente:

1. **Twitter Card meta tags**: `twitter:card` (`summary_large_image`
   cuando hay `ogImageUrl`, `summary` cuando no), `twitter:title`,
   `twitter:description`, `twitter:image`.
2. **JSON-LD structured data**: `Organization` (name, url, logo) +
   `WebSite` (name, url) cuando hay `siteRoot` + `canonicalHostname`.
   Payloads serializados con `System.Text.Json` para escaping correcto.
3. **Hreflang**: itera por `page.Cultures.Keys`, emite
   `<link rel="alternate" hreflang="X" href="absolute-url" />` por
   cada cultura publicada del nodo actual. Solo cuando hay > 1
   cultura publicada.

## Consequences

**Positivas:**

- **Multi-brand real**: un deploy con 3 siteRoots (cada uno con su
  `canonicalHostname` + su `siteConfigSettings.brandKey`) ahora
  resuelve correctamente el brand activo según el host del request,
  habilitando que el tema, SEO defaults, alertas globales y todo el
  resto del runtime sean brand-aware.
- **DRY en theme emission**: 1 partial, 2 consumidores (`_Layout` +
  `PageBare`). El equipo design system itera el style guide en un
  solo archivo.
- **Page-level theme override** finalmente funciona end-to-end: una
  page con `compPageTheme.pageThemeVariant=dark` renderiza con
  fondos oscuros sin tocar el `themeSettings` brand-wide.
- **SEO/social completos**: Twitter shares, Facebook OG, Google
  Knowledge Graph (JSON-LD), multi-culture sites con hreflang
  correcto. Nada artesanal — todo viene de `compSeo` +
  `siteConfigSettings` + `siteRoot.canonicalHostname` ya existentes.

**Negativas:**

- **`HostBasedBrandingProvider` queries published cache cada
  request**: para sitios con muchos siteConfigSettings (ej. 20+
  brands), la resolución hace un `.SelectMany` + iteración.
  Mitigación: Umbraco's published content cache hace el lookup O(1)
  en memoria; el costo real es despreciable.
- **`_BrandThemeStyle` emite CSS inline en cada request**: redunda
  cuando el brand theme no cambia entre requests del mismo visitante.
  Mitigación válida (futura ola): cachear el `<style>` rendered HTML
  por brand+variante con un sliding TTL. Hoy es trade-off favorable
  porque evita un round-trip extra al CSS file.
- **JSON-LD payloads inline**: el `<script type="application/ld+json">`
  agrega ~200 bytes al `<head>`. Necesario para SEO técnico moderno.

**Neutras:**

- `DefaultBrandingProvider` sigue existiendo como fallback puro de
  configuración (cuando no hay HttpContext). Sin deprecación — su
  rol es válido para escenarios offline (ej. tests, command-line tools).
- Partial `_BrandThemeStyle` consumido vía `BrandThemeStyleModel`
  record (Application/Dto/Responses) — sigue el patrón establecido
  de records-as-view-models.

## Alternatives considered

- **Hostname mapping vía `IOptions<HostBrandMap>`**. Descartado.
  Acoplamiento entre config estática y schema editorial. El editor
  ya configura el host en `siteRoot.canonicalHostname`; reusar esa
  fuente es DRY.
- **Resolver brand vía Umbraco `Domain` (Culture and Hostnames)**.
  Descartado por scope. La feature de Umbraco es para
  culture-routing, no para brand-routing. Reusar `canonicalHostname`
  del schema editorial es más limpio y no acopla a una feature
  Umbraco-specific que podría cambiar entre versiones.
- **Cachear el `<style>` rendered**. Diferido. Optimización
  prematura sin métrica real de costo.
- **Mover JSON-LD a un endpoint API separado**. Descartado. Google
  prefiere JSON-LD inline en `<head>` para discovery confiable.

## Implementation summary (Ola 54, 4 commits)

| Commit | Hash | Foco |
|---|---|---|
| `feat(ola-54.1)` | `a560035` | `HostBasedBrandingProvider` + wire SeamComposer |
| `feat(ola-54.2)` | `e6d1d85` | `_BrandThemeStyle.cshtml` partial + `BrandThemeStyleModel` record + `_Layout` y `PageBare` consumen |
| `feat(ola-54.3)` | `ec7868e` | `_SeoHead` con Twitter Card + JSON-LD + hreflang |

## References

- ADR 0010 — Branding via provider (extendido)
- ADR 0020 — Platform Settings (compBranding fuente de verdad)
- ADR 0022 — Page Composition Standard (compPageTheme.pageThemeVariant)
- ADR 0025 — Global components extension (Ola 52 — patrón resolver
  similar al pattern aquí aplicado)
