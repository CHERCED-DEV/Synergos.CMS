# Rendering Overview — SSR Razor vs CDN Web Components

Synergos renders pages in two coordinated modes. Understanding which mode to
pick, and how they interoperate, is key to not painting yourself into a corner.

## The two modes

### SSR Razor (server-side)

The Razor view engine produces HTML on the server. The browser receives
complete HTML with inline text content.

```
HTTP request
  ↓
Controller → view model → Razor view → HTML response
```

Used for:
- Every full page (`PageBase`, `BlogHome`, `BlogPost`, …) — the outer shell always SSR.
- Block Grid elements whose view is a Razor partial (`Views/Partials/blockgrid/Components/elementStruct*.cshtml`, `Views/Partials/Blog/PostCard.cshtml`, …).
- Native macros inside rich text (`Views/MacroPartials/Native/*.cshtml`).

Styling comes from `wwwroot/css/*.css` (compiled from `scss/`).

**Pros:** fast first paint, SEO-friendly, works without JavaScript, easy to
author with plain Razor.
**Cons:** no interactivity without extra JS layer, harder to build rich
experiences (video players, carousels, filters, …).

### CDN Web Components (client hydration)

The server emits a `<script type="module">` tag + a custom element tag with a
JSON config attribute. The browser fetches the Angular-built bundle, registers
the Custom Element, reads the config, and renders its shadow DOM.

```
HTTP request
  ↓
Controller → view model → Razor view → HTML with <script> + <synergos-x config='…'>
  ↓
Browser fetches bundle from CDN
  ↓
Custom Element class registers and hydrates
```

Used for:
- All 49 CDN macros (`Views/MacroPartials/Modules|Compositions|Primitives|Experiences|Shop/Cdn*.cshtml`).
- Block Grid elements marked in `StaticAssetsSettings.ClientHosted`.

**Pros:** rich interactivity (carousels, quizzes, filters), same component
works across multiple sites consuming the CDN, UI team iterates without
redeploying the CMS.
**Cons:** requires JS enabled, hydration flash (invisible with CSS containment),
bundle size matters.

## How they coexist on one page

```
<html>
<head>
  ← LayoutHead view component (SSR, inlines import-map.json + tracking scripts)
</head>
<body>
  ← LayoutHeader (SSR, inlines nav HTML)
  ← LayoutAlertBar (SSR, emits <synergos-alert-bar> if ClientHosted alias)
  ← LayoutBanner (SSR, emits <synergos-banner> if ClientHosted alias)

  <main>
    <article>
      ← page sections from Block Grid:
        section 1: <div class="sg-block">…SSR Razor partial for this element…</div>
        section 2: <script src="…/card/angular/latest/main.js" type="module"></script>
                    <synergos-card config='{…json…}'></synergos-card>
        section 3: <div class="sg-block">…another SSR partial…</div>
    </article>
  </main>

  ← LayoutFooter (SSR)
  ← tracking body-end scripts (inlined from SiteSettings.siteBodyEndScripts +
    GlobalSettings.bodyEndScripts + generated GTM noscript fallback)
</body>
</html>
```

The outer shell is always SSR. Individual blocks can be SSR or CDN,
independently per block. Mixing within one page is the default and works fine.

## Deciding: SSR or CDN for a new element?

| Indicator | Pick SSR | Pick CDN |
|---|---|---|
| Pure presentational (static text, image) | ✅ | ❌ |
| Needs client-side state (filter, quiz, counter) | ❌ | ✅ |
| SEO-critical content | ✅ | ⚠ (works but hydration timing matters) |
| Shared across multiple sites on the CDN | ❌ | ✅ |
| Uses third-party JS widget | ❌ | ✅ |
| Needs shadow DOM style isolation | ❌ | ✅ |
| Editorial layout (grid, column, stack, divider) | ✅ | ❌ |

## Where to put the view

| Element location in block tree | View location |
|---|---|
| Structural (Section, Container, Grid, Column, Stack, Divider, Spacer) | `Views/Partials/blockgrid/Components/elementStruct*.cshtml` |
| Layout preset (1col, 2col, 3col, 4col, main-sidebar) | `Views/Partials/blockgrid/Components/layoutPreset*.cshtml` |
| Blog cards (FeaturedPostCard, PostCard, AuthorBio) | `Views/Partials/Blog/*.cshtml` |
| CDN block (any Cdn* element) | `Views/MacroPartials/<Family>/Cdn<Name>.cshtml` |
| Native macro (inline in rich text) | `Views/MacroPartials/Native/<Name>.cshtml` |

**Never** invent a new folder for views. The MacroDispatcher and BlockGrid
component resolvers rely on these locations.

## Shared plumbing

### `IDictionaryCache`
All user-facing strings (labels, aria, placeholders) go through
`@inject Synergos.CMS.Domain.Services.IDictionaryCache Dict`. See
[`../../CLAUDE.md`](../../CLAUDE.md) §9.

### `IElementUrlResolver`
All CDN bundle URLs go through
`@inject Synergos.CMS.Application.Rendering.IElementUrlResolver ElementUrl`.
See [`cdn-integration.md`](cdn-integration.md).

### `StaticUrlBuilder`
All static asset URLs (fonts, images, generic CSS/JS) go through
`@inject Synergos.CMS.Application.Rendering.StaticUrlBuilder Urls`.

### `ISectionMapper`
Every Block Grid element has exactly one `ISectionMapper` implementation in
`Application/Mapping/Elements/<Family>Mappers.cs`. The mapper:
1. Reads the element's properties from `IPublishedContent`.
2. Applies compositions via the registered readers.
3. Emits a `SectionView` with a `ViewName` pointing to the Razor partial (SSR)
   or the CDN macro partial.

`SectionMapperDispatcher` picks the mapper by element type alias.

## Styles

Razor views pull from two sources:

- **`wwwroot/css/synergos.css`** — compiled from `scss/`. Site-wide tokens +
  base + components + elements + corporate. Applied to SSR partials.
- **Shadow DOM** inside each CDN element — owned by the UI team per element.
  The CDN element reads theme tokens passed in its `config` JSON (see
  [`cdn-integration.md`](cdn-integration.md)) and applies them to its shadow
  root via CSS custom properties.

Cross-cutting tokens (brand colors, fonts, spacing) are exposed as CSS custom
properties at `:root` by `ThemeSettings` rendering in `_Layout.cshtml`:

```razor
<style>
  :root {
    --sg-color-primary: @Model.Theme.ColorPrimary;
    --sg-color-secondary: @Model.Theme.ColorSecondary;
    /* … */
  }
</style>
```

CDN elements read these via `var(--sg-color-primary)` in their shadow CSS. The
bridge from `ThemeSettings` content → `:root` custom properties → shadow DOM is
the whole reason both worlds share one brand.

## See also

- [`cdn-integration.md`](cdn-integration.md) — CDN registry, URL resolution, dev overrides.
- [`macros.md`](macros.md) — Native vs CDN macro specifics.
- [`../recipes/add-cdn-macro.md`](../recipes/add-cdn-macro.md) — add a CDN macro end-to-end.
