# ADR 0048 — CSS design system aligned with Synergos.UI (Olas 92-96)

- **Status:** Accepted
- **Date:** 2026-04-26
- **Deciders:** Arquitecto + agente, durante batch tras Ola 91 — *"alineemonos con Synergos.UI", "termina todas esas olas, sin parar"*.
- **Consolida:** 5 olas en un único ADR.

## Context

Tras Ola 91 (composite notifier + webhook channels) el módulo de runtime
estaba completo pero el LAYER VISUAL del CMS era esencialmente
inexistente:

- Único stylesheet: `wwwroot/css/syn-dev-stub.css` (373 líneas, 187
  selectores).
- Cobertura real: chrome del sitio + Layout Composer presets + 12
  primitivos genéricos (button/badge/card/hero/cta-banner/media-text-
  split/stat/feature/testimonial/faq/timeline/alert-bar). El resto
  son utility classes triviales.
- **Faltaba CSS production-ready para 9 áreas de alto valor**:
  account, shop, search, comments, error pages, page templates,
  global components (alert/banner/modal/footer-note), blog, flow,
  member runtime blocks.
- No había pipeline SCSS — todo CSS vanilla en `wwwroot/css/`.
- `Synergos.UI` (sub-repo) tiene 217 SCSS files con un design system
  maduro pero NO se consume desde el CMS hoy (bloqueado por CDN ADR
  0012).

El arquitecto pidió: *"yo creo que en este proyecto Synergos.CMS.Web
nos falta toooodoooo lo que sea scss o css"* → seguido de *"alineemonos
con Synergos.UI"*. Decisión: construir un sistema CSS vanilla en el
CMS que ESPEJEE los tokens y patterns de Synergos.UI, reusable mientras
el CDN bundles no estén publicados, y compatible cuando lleguen.

## Decision

Adoptar un **sistema CSS modular en `wwwroot/css/`** organizado por
área funcional, que mirror los tokens canónicos de Synergos.UI
(`vitals/core-assets/src/scss/tokens/`).

### Estructura

13 archivos CSS divididos en 2 capas:

**Foundations** (cargados unconditionally en `_Layout.cshtml`):
- `syn-tokens.css` — Diseño tokens canónicos (colores Synergos.UI +
  espacio + typography + radius + shadows + motion + z-index +
  3 themes light/dark/silverGold con todas las semantic vars).
- `syn-base.css` — Reset minimalista alineado con `_global.scss` UI:
  heading scale fluida con clamp, body/strong/em/code/pre/blockquote,
  links con focus-visible ring, lists reset, ::selection,
  prefers-reduced-motion guard, .syn-sr-only + .syn-skip-link.
- `syn-utilities.css` — Display/flex/grid/spacing/visibility/text
  utilities + .syn-container con 4 widths.
- `syn-layout.css` — Layout Composer presets actualizados con tokens
  nuevos.
- `syn-chrome.css` — Site header (sticky con backdrop-blur), nav
  (con CTA pill), aside, footer.
- `syn-primitives.css` — 18 familias de componentes con BEM y
  tokens canónicos (buttons/badges/cards/alerts/hero/cta-banner/
  media-text-split/eyebrow/quote/richtext/breadcrumbs/avatar/
  stat/feature/testimonial/faq/timeline/keyvalue/social-share).

**Per-area** (cargados según context):
- `syn-pages.css` — Page templates (PlatformRoot/PageBasic/PageBare/
  PageLanding/PageStandard/PostPage/PostCategoryPage + ArticleList/
  BlogHighlight blocks + EmptyState).
- `syn-shop.css` — Product card/detail/grid + cart item/summary +
  variant picker + qty selector + price display.
- `syn-search.css` — SearchPage form + meta + results list +
  pagination + no-results state.
- `syn-comments.css` — CommentThread con cards + form + login
  prompt + pending state warning.
- `syn-globals.css` — cfgAlert (5 tones + emphasis) + cfgBanner
  (top sticky/bottom sticky) + cfgModal (dialog nativo) +
  cfgFooterNote + BannerSlider (scroll-snap) + MissionBlock +
  NewsletterForm + TabGroup.
- `syn-flow.css` — FlowDefinition landing + FlowStep + FlowProgress.
- `syn-member.css` — MemberGate prompt + MemberLogin block +
  MemberLogout button + MemberProfile inline.
- `syn-blog.css` — TagList + Author byline + ReadingTime + CodeBlock
  + PullQuote.
- `syn-error.css` — Error pages con status code mega-number gradient
  + body BlockGrid o text + search box + home link.
- `syn-account.css` — Login/Register/Profile/ForgotPassword/
  ResetPassword/ResendConfirmation/Registered/ConfirmEmail con
  card central elevated + inputs 44px focus ring + submit primary
  48px shadow action + mensajes inline state surfaces.

### Account views (Layout=null) head bundle

Las 8 Account views NO heredan `_Layout.cshtml` (Layout=null porque son
controllers MVC sin `PublishedRequest`). Para ellas, partial reusable
`Views/Account/_AccountHead.cshtml` inyecta:
```html
<meta charset utf-8>
<meta viewport>
<meta robots noindex>
<link tokens.css>
<link base.css>
<link account.css>
```

`Error.cshtml` (también Layout=null) carga directamente los 3 link tags
en su `<head>` (tokens + base + error).

### Tokens canónicos (de Synergos.UI/vitals/core-assets/src/scss)

**Colores** (HEX exactos):
- Neutrals 11 pasos: `#ffffff` → `#0f172a`.
- Brand indigo 6 pasos: `#eef1ff` → `#3854d8` (500 = `#4f6ef7`).
- Accent violet 6 pasos: `#f3f0ff` → `#7c3aed` (500 = `#8b5cf6`).
- Danger/Success/Warning escalas 6 pasos.
- Semantic surface/text/border/action/state/focus por theme.

**Espacio**: 4px base scale (`--syn-space-1` = 0.25rem) + aliases
xs/sm/md/lg/xl/2xl/3xl.

**Typography**: Manrope sans + JetBrains mono, sizes xs/sm/base/lg/
xl/2xl/3xl/4xl/5xl, weights 300/400/500/600/700, line-heights 1.25/
1.5/1.625, letter-spacing display/tight/normal/wide/eyebrow.

**Radius**: 6/10/14/20/24px + full pill.

**Shadows**: neutral xs→2xl + brand sm/md/lg + accent sm/md (con
RGB indigo y violet por theme).

**Motion**: durations 120/200/280/750/1400ms + easings spring
default/smooth/in/out + composed transitions.

**Themes**: light (default) + dark (selector `[data-theme="dark"]`)
+ silverGold (selector `[data-theme="silverGold"]`) + auto fallback
prefers-color-scheme dark.

### Compatibilidad y migración

**Aliases legacy** preservados en `syn-tokens.css`: `--syn-c-*`,
`--syn-sp-*`, `--syn-fs-*`, `--syn-r-*` mapeados a sus equivalentes
canónicos `--syn-color-*`, `--syn-space-*`, `--syn-font-size-*`,
`--syn-radius-*`. Renderers existentes funcionan sin cambios.

**Dev marker**: `syn-dev-stub.css` shrink de 373 a 28 líneas — solo
contiene la badge de dev marker que aparece cuando `Synergos:DevStub:
Enabled=true`. El contenido real migró a los archivos modulares.

## Consequences

**Positivas:**

- **UX presentable inmediatamente**: 4400+ líneas de CSS production-
  ready que cubren los 9 módulos funcionales del CMS. Account, shop,
  search, comments, error, modales, blogs, flows, members ya tienen
  estilos coherentes.
- **Design language consistente con Synergos.UI**: cuando los CDN
  bundles lleguen, el visitante no notará un cambio brusco — los
  mismos tokens, las mismas sizes, los mismos colors.
- **Tema dinámico operacional**: light/dark/silverGold via
  `data-theme="..."` selector. El `_BrandThemeStyle.cshtml` puede
  override per brand sin tocar componentes.
- **Aliases legacy preservados**: cero ruptura de renderers que ya
  consumen `--syn-c-primary`, `--syn-sp-md`, etc.
- **Modular = mantenible**: 13 archivos por dominio (vs 1 monolítico)
  facilita localizar y actualizar componentes. Cada archivo es
  independiente y referencia tokens — cambiar un valor canónico
  propaga a todo.
- **Cero infraestructura nueva**: vanilla CSS, sin SCSS pipeline, sin
  package.json, sin webpack/vite. Los archivos van directo a
  `wwwroot/css/` y se sirven static.
- **Cero schema rompedor**.
- **Cero NuGet packages nuevos**.

**Negativas:**

- **CSS vanilla sin pipeline**: para sitios con > 50 archivos CSS
  (probable en el futuro), agregar SCSS/PostCSS pipeline será
  inevitable. Diferido hasta que el design system tenga > 30
  componentes propios.
- **Account views requieren manual link**: las 8 views Layout=null
  cargan los 3 link tags via `_AccountHead.cshtml` partial. Si se
  agrega una nueva Account view, el dev debe usar el partial
  explícitamente. Mitigación: el partial es reusable y documentado.
- **Duplicación parcial con Synergos.UI**: tokens y mixins se mirror
  manualmente del SCSS de UI. Si UI cambia un token, el CMS debe
  actualizar `syn-tokens.css` manualmente. Mitigación: los archivos
  críticos a watch son 5 (`tokens/_colors.scss`, `_brand.scss`,
  `_spacing.scss`, `_typography.scss`, `_radius.scss`).
- **No hay autoload de tokens en views Layout=null distintas de
  Account**: si se crea otro controller con view Layout=null que
  use clases `syn-*`, debe agregar manualmente los link tags. Solo
  Error.cshtml lo hace hoy.
- **MemberLogin block name collision**: el bloque inline
  `elementMemberLogin` (Ola 52.C) y el view full-page `Account/
  Login.cshtml` ambos tienen "login". Distinguidos por prefijo BEM:
  `syn-member-login__*` (block) vs `syn-account__*` (view).

**Neutras:**

- 5 commits feat (Olas 92-96) + 1 commit docs ADR consolidado
  (esta Ola 97).
- 0 GUIDs nuevos.
- 0 dependency changes.
- 13 archivos CSS nuevos (~4400 líneas) + 12 view files modificados
  + 1 partial nuevo (`_AccountHead.cshtml`).

## Implementation summary

| # | Hash | Foco |
|---|---|---|
| 92 | `4ed1d32` | Foundations — 6 archivos modulares: tokens (496) + base (292) + utilities (170) + layout (173) + chrome (226) + primitives (1015). _Layout wireado. Dev-stub shrink a 28 líneas. |
| 93 | `4790749` | Account/error/page templates — 3 archivos (account/error/pages). Partial _AccountHead.cshtml + 8 Account views actualizadas + Error.cshtml + _Layout. |
| 94 | `eb5c82c` | Shop module — 1 archivo (789 líneas) cubriendo 8 partials elementShop*: price/product-card/product-grid/product-detail/variant-picker/qty-selector/cart-item/cart-summary. |
| 95 | `4f42d46` | Search + comments — 2 archivos (484 líneas). |
| 96 | `9d25b47` | Globals + flow + member + blog — 4 archivos (1213 líneas). |
| 0048 | (este) | ADR consolidado |

## Próximas direcciones

- **SCSS pipeline opt-in**: cuando hay > 30 archivos componentizados,
  introducir dart-sass standalone build sin webpack (sigue agnostic
  de framework, alineado con Synergos.UI vitals/core-assets).
- **Componentes faltantes**: si el SSR agrega nuevos blocks (ej.
  data-table, gallery, video player, audio player), construir CSS
  específico siguiendo el mismo pattern (mirror del SCSS UI).
- **Synergos.UI consumption**: cuando CDN team publique los bundles
  reales (ADR 0012 desbloqueado), el `IBundleRegistryClient` cargará
  los `<synergos-*>` web components. Los CSS en CMS quedarán como
  fallback/SSR-style; los WC tienen su propio shadow DOM.
- **Storybook visual**: catálogo SSR navegable (`/dev/styleguide`) con
  todos los componentes renderizados con variants — útil para QA
  visual y onboarding.
- **Animation library**: micro-animations on scroll, page transitions,
  skeleton loaders. Reusable mediante CSS custom properties +
  IntersectionObserver helper JS.

## References

- Synergos.UI/vitals/core-assets/src/scss/ — fuente canónica de tokens
  (mirror manual)
- ADR 0010 — Branding via provider (themes consumen tokens via
  `_BrandThemeStyle.cshtml`)
- ADR 0012 — CDN contract is consumed (los CSS aquí son fallback;
  cuando bundles llegan, web components dominan)
- ADR 0017 — Layout Composer (presets consumen `syn-preset--*`
  classes)
- ADR 0023 — Componentization Layered (5 capas — primitives son la
  capa más baja consumida por blocks/composites)
- ADR 0040 — Gran Consolidación (chrome triádico header/footer/aside
  ahora con CSS real en `syn-chrome.css`)
