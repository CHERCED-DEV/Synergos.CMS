# ADR 0020 — Platform/Settings: tree separado + multi-brand via compBranding

- **Status:** Accepted
- **Date:** 2026-04-22
- **Deciders:** Project owner
- **Source:** promoted from `refactor-docs/adr-drafts/0020-platform-settings-split.md` (Draft 2026-04-21)
- **Authorises:** Ola 31 (Platform/Settings schema — settingsRoot + themeSettings + siteConfigSettings + featureFlagsSettings + compBranding), commit `42b8d6a`, plus Ola 37 (IBrandThemeProvider runtime wiring)
- **Related:** ADR 0008 (uSync hybrid SoT), ADR 0010 (Branding via provider), ADR 0011 (Feature flags typed config), ADR 0013 (No automatic seeders)

## Context

El legado Epic Fail 2 tenía un solo nodo `themeSettings` con props
flat que sufría tres problemas:

1. **Imposible multi-brand**: un único nodo = un único brand.
2. **Config dispersa**: analytics, SEO defaults, feature flags,
   colores — todo en el mismo nodo sin separación por concern.
3. **Acoplado a Content tree**: el nodo existía como hijo de
   `siteRoot`, mezclando settings con páginas editoriales.

Memory `feedback_branding_via_provider` establece: "IBrandingProvider.
Prohibido `if (brand.Key == "X")` en core. Variantes en custom layer
futura." El schema debe soportar N brands sin tocar código.

## Decision

Se establece un **tree separado de Settings** con 3 DocTypes
especializados por concern, todos componiendo una `compBranding` que
identifica a qué brand aplica cada nodo.

### Estructura del tree

```
Content root:
├── siteRoot (Content tree — páginas editoriales)
│   └── pageBase, pageBare, pageBasic, ...
└── settingsRoot (Platform tree — config)
    ├── themeSettings (brandKey="default")
    ├── themeSettings (brandKey="brand-a")  ← multi-brand
    ├── siteConfigSettings (brandKey="default")
    └── featureFlagsSettings (brandKey="default")
```

Ambos roots son AllowAtRoot=true. Son dos trees paralelos en
Umbraco. El arquitecto decide cómo lucen en el backoffice
(typically via sections custom, ola futura).

### compBranding composition

- **brandKey** (Nothing, mandatory, slug regex): identificador
  técnico. Ej. `"default"`, `"brand-a"`, `"brand-b"`. Regla:
  lowercase-hyphen.
- **brandDisplayName** (Culture, mandatory): nombre legible para UX
  editorial (puede variar por idioma).

Cualquier DocType que necesite estar particionado por brand compone
`compBranding`.

### Los 3 Settings DocTypes

1. **themeSettings** — visual branding (colores hex, font-families,
   logos via MediaPicker3, favicon). 10 props. Variations mixto:
   colores/fonts Nothing (técnico), logos Culture (traducibles).

2. **siteConfigSettings** — config site-wide: `analyticsId`
   (GA/Plausible), `googleTagManagerId` (GTM regex), 3 policy URLs
   (cookie/privacy/terms, Culture), SEO defaults
   (title/description/OG image, Culture), analytics keys Nothing.
   8 props.

3. **featureFlagsSettings** — override por brand de feature flags
   (ADR 0011). Single prop `flagsJson` TextArea. `IFeatureGate` debe
   consultar: first `appsettings.json` defaults → second override
   from Umbraco node for current brand. ADR 0011 complementado.

### IBrandingProvider + IBrandThemeProvider (Ola 37 runtime)

El seam `IBrandingProvider` + `IBrandThemeProvider` (ADR 0010 +
Ola 37) ya están implementados como `DefaultBrandingProvider` +
`DefaultBrandThemeProvider` en `Synergos.CMS.Web/Services/`. El
runtime:

1. Resuelve `brandKey` del request actual (Ola 37: del `IOptions<
   BrandingSettings>` vía appsettings, default "default"; Multi-
   brand futuro puede resolver por host/subdomain/etc sobrescribiendo
   `IBrandingProvider`).
2. Busca en el content tree bajo `settingsRoot` los nodos
   `themeSettings` con `compBranding.brandKey` matching.
3. Expone getters tipados via `IBrandThemeProvider.GetThemeForBrand
   (brandKey)` → `BrandTheme` record con 10 nullable fields.
4. Fallback a `brandKey="default"` si no hay match exacto.

`_Layout.cshtml` (Ola 37) emite `<style>:root { --syn-color-primary:
...; ... }</style>` con las CSS custom properties del brand vigente.
El design-system las consume en sus reglas.

**Prohibido** `if (brand.Key == "X") { ... }` en core code. Cualquier
ramificación por brand vive en implementación custom de
`IBrandingProvider` o en CSS del design-system consumiendo variables
de `themeSettings` via CSS custom properties.

## Consequences

**Positive**

- Multi-brand soportado puramente por schema + data. Cero if/else
  en C# core.
- Separación de concerns clara (theme ≠ config ≠ flags).
- Feature flags por brand sin duplicar appsettings.json.
- Analytics IDs por brand (clientes pueden tener su propia
  tracking).
- Settings tree separado del Content tree (no contamina la
  estructura editorial).
- Runtime completo desde Ola 37 — schema no queda como solo
  promesa.

**Negative / limitaciones conocidas**

- Backoffice UX: editores necesitan claridad entre Content tree y
  Settings tree. Typically resuelto vía sections custom en una ola
  siguiente.
- Resolución de `brandKey` por request actualmente sólo via
  `BrandingSettings` en appsettings (un solo brand por deploy).
  Multi-brand real requeriría extender `IBrandingProvider` —
  posible sin romper ADR 0010, sin ITenantContext (memory
  `feedback_product_not_saas_multitenant`).
- `themeSettings.colors` son hex strings; el design-system espera
  recibir tokens CSS `--primary`, `--secondary` etc. El wiring
  actual de `_Layout.cshtml` (Ola 37) emite las custom properties
  inline en `:root`.

## Re-evaluación

- Si multi-brand no se adopta en primeros 12 meses, considerar
  colapsar a un solo DocType `platformSettings` con todas las
  props. Costoso reversar; mejor no prematurear.
- Si se necesita granularidad adicional (ej. per-page branding
  override), considerar `compPageBranding` nueva composition y
  resolver en cascada (page > brand > default).
- Si el producto decide servir múltiples verticales desde un mismo
  deploy (no multi-tenant), introducir `platformRoot` (renombre de
  `tenantRoot` ya ejecutado 2026-04-22) como wrapper opcional del
  árbol de Content y Settings agrupados por vertical.
