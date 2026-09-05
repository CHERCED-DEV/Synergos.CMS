# UI Elements Catalog — 122 bundles publicados al CDN

> **AUTO-GENERATED** by `tools/refresh-skill-catalog.mjs`. Re-run via `npm run skill:refresh`
> o automáticamente al final de `npm run release:angular`. Edits manuales se pierden.
>
> Snapshot del CDN registry (`C:\LOCAL_CDN\synergos\registry.json`) + UI contracts
> (`vitals/contracts/src/{element-config,elements-syn,element-inputs}`).
>
> Generated: 2026-09-05T23:25:46.040Z

## Cómo leer este catálogo

Cada elemento listado tiene:
- **`tag`**: el custom element DOM name que el SSR Razor del CMS emite
  (`<synergos-{kebab}>`).
- **`alias`**: el alias CMS uSync (`elementSyn{Pascal}`) que aparece en los
  ContentTypes XMLs de `Synergos.CMS.Web/uSync/v9/ContentTypes/`.
- **`framework`(s)**: el(los) framework(s) en los que el bundle está publicado
  Hoy la única plataforma es angular (purga 2026-08-04).
- **`shape rich`** (cuando existe): el contract canónico editorial 3-way mirror
  C# `CdnConfig` ↔ TypeScript `{Name}ElementConfig` ↔ Web Component `config`
  prop. Vive en `vitals/contracts/src/element-config.contract.ts` (manual).
- **`shape schema`**: mirror 1:1 del schema CMS uSync (props con sus aliases
  literales). Vive en `vitals/contracts/src/elements-syn.contract.ts`
  (auto-generado por `cms-sync.mjs`).
- **`inputs`**: declaraciones públicas exposadas como atributos del Custom
  Element (`element-inputs.json` — kebab-case en HTML, camelCase aquí).

Cuando recomendes un elemento, **siempre** mencioná: tier, tag DOM, y la
shape que el bundle espera (rich si existe, schema si no).


## Primitives (31)

**Primitives** — atómicos, sin lógica de negocio. Building blocks reutilizables (avatar, badge, divider, etc.). Pueden vivir solos o composarse.

### `<synergos-avatar>` — elementSynAvatar

- **tag**: `<synergos-avatar>`
- **alias CMS**: `elementSynAvatar`
- **tier**: primitive
- **frameworks**: angular
- **shape rich** (`AvatarElementConfig` — manual canónico):
  - `src`: string
  - `alt`: string
  - `name`: string
  - `size`: string
  - `variant`: string
  - `tone`: string
  - `translations`: ComponentTranslations
- **shape schema** (`SynAvatarSchema` — auto del CMS):
  - `avatarImage`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `src` (string) — Avatar image URL
  - `alt` (string) — Avatar image alt text
  - `name` (string) — Display name used for initials fallback
  - `size` (string) — Avatar size token
  - `shape` (string) — Avatar shape (circle | rounded | square)
  - `status` (string) — Presence status (online | offline | busy | away)
  - `theme` (string) — Color theme (light | dark)

### `<synergos-badge>` — elementSynBadge

- **tag**: `<synergos-badge>`
- **alias CMS**: `elementSynBadge`
- **tier**: primitive
- **frameworks**: angular
- **shape rich** (`BadgeElementConfig` — manual canónico):
  - `text`: string
  - `tone`: string
  - `ariaLabel`: string
  - `translations`: ComponentTranslations
- **shape schema** (`SynBadgeSchema` — auto del CMS):
  - `label`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `text` (string) — Visible badge text
  - `ariaLabel` (string) — Accessible label override
  - `tone` (string) — Visual tone (neutral | brand | inverse)

### `<synergos-breadcrumb>` — elementSynBreadcrumb

- **tag**: `<synergos-breadcrumb>`
- **alias CMS**: `elementSynBreadcrumb`
- **tier**: primitive
- **frameworks**: angular
- **shape schema** (`SynBreadcrumbSchema` — auto del CMS):
  - `itemsJson`: string
  - `includeStructuredData`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `itemsJson` (string) — Array de items serializado como JSON string
  - `includeStructuredData` (string) — Campo "Include Structured Data" del componente synergos-breadcrumb. Editor: editar manualmente para enriquecer documentación.
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-button-container>` — elementActionButton

- **tag**: `<synergos-button-container>`
- **alias CMS**: `elementActionButton`
- **tier**: primitive
- **frameworks**: angular
- **shape rich** (`ButtonContainerElementConfig` — manual canónico):
  - `label`: string
  - `href`: string
  - `target`: string
  - `variant`: string
  - `tone`: string
  - `size`: string
  - `disabled`: boolean
  - `loading`: boolean
  - `loadingLabel`: string
  - `translations`: ComponentTranslations
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `label` (string) — Button visible text
  - `variant` (string) — Variante visual. Acepta el vocabulario del CMS (primary | secondary | outlined | ghost | neutral | emphasis) y el propio (solid | outline | ghost | danger | gradient); lo desconocido cae a solid.
  - `size` (string) — sm | md | lg
  - `href` (string) — If set, renders as <a> instead of <button>
  - `target` (string) — Link target (_self | _blank)
  - `loading` (boolean) — Loading state; disables interaction and shows the loading label
  - `loadingLabel` (string) — Text shown while loading (falls back to the button label)
  - `disabled` (boolean) — Disabled state

### `<synergos-copy-button>` — elementSynCopyButton

- **tag**: `<synergos-copy-button>`
- **alias CMS**: `elementSynCopyButton`
- **tier**: primitive
- **frameworks**: angular
- **shape schema** (`SynCopyButtonSchema` — auto del CMS):
  - `copyText`: string
  - `buttonLabel`: string
  - `feedbackLabel`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `copyText` (string) — Campo "Copy Text" del componente synergos-copy-button. Editor: editar manualmente para enriquecer documentación.
  - `buttonLabel` (string) — Texto visible del elemento (botón, input, badge, etc.) (campo "Button Label" del componente synergos-copy-button)
  - `feedbackLabel` (string) — Texto visible del elemento (botón, input, badge, etc.) (campo "Feedback Label" del componente synergos-copy-button)
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-divider>` — elementSynDivider

- **tag**: `<synergos-divider>`
- **alias CMS**: `elementSynDivider`
- **tier**: primitive
- **frameworks**: angular
- **shape rich** (`DividerElementConfig` — manual canónico):
  - `orientation`: string
  - `inset`: string
  - `variant`: string
  - `tone`: string
  - `theme`: string
  - `translations`: ComponentTranslations
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `orientation` (string) — Separator orientation (horizontal | vertical)
  - `inset` (string) — Outer spacing token applied around the divider
  - `theme` (string) — Color theme (light | dark)

### `<synergos-eyebrow>` — elementTextEyebrow

- **tag**: `<synergos-eyebrow>`
- **alias CMS**: `elementTextEyebrow`
- **tier**: primitive
- **frameworks**: angular
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `headingText` (string) — Heading text content
  - `headingLevel` (string) — HTML heading tag: h1-h6
  - `body` (string) — Supporting body copy
  - `alignment` (string) — Text alignment (left | center)
  - `theme` (string) — Color theme (light | dark)

### `<synergos-fab>` — elementSynFab

- **tag**: `<synergos-fab>`
- **alias CMS**: `elementSynFab`
- **tier**: primitive
- **frameworks**: angular
- **shape schema** (`SynFabSchema` — auto del CMS):
  - `iconKey`: string
  - `actionLink`: string
  - `position`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `iconKey` (string) — Nombre del icono stock o URL del SVG custom (campo "Icon Key" del componente synergos-fab)
  - `actionLink` (string) — URL destino del form submit o action handler (campo "Action Link" del componente synergos-fab)
  - `position` (string) — Campo "Position" del componente synergos-fab. Editor: editar manualmente para enriquecer documentación.
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-heading>` — elementTextHeading

- **tag**: `<synergos-heading>`
- **alias CMS**: `elementTextHeading`
- **tier**: primitive
- **frameworks**: angular
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `headingText` (string) — Heading text content
  - `headingLevel` (string) — HTML heading tag: h1-h6
  - `body` (string) — Supporting body copy
  - `alignment` (string) — Text alignment (left | center)
  - `theme` (string) — Color theme (light | dark)

### `<synergos-icon-block>` — elementMediaIcon

- **tag**: `<synergos-icon-block>`
- **alias CMS**: `elementMediaIcon`
- **tier**: primitive
- **frameworks**: angular
- **shape rich** (`IconBlockElementConfig` — manual canónico):
  - `icon`: string
  - `size`: string
  - `color`: string
  - `ariaLabel`: string
  - `ariaHidden`: boolean
  - `translations`: ComponentTranslations
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `icon` (string) — Icon or symbol name
  - `size` (string) — Icon size token
  - `color` (string) — Optional icon color token or CSS value
  - `ariaLabel` (string) — Accessible label for assistive technology
  - `ariaHidden` (boolean) — Decorative by default: the icon is hidden from assistive tech. Set false only when the icon carries meaning no nearby text conveys, and pair it with ariaLabel.

### `<synergos-icon-label>` — elementSynIconLabel

- **tag**: `<synergos-icon-label>`
- **alias CMS**: `elementSynIconLabel`
- **tier**: primitive
- **frameworks**: angular
- **shape schema** (`SynIconLabelSchema` — auto del CMS):
  - `iconKey`: string
  - `labelText`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `iconKey` (string) — Nombre del icono stock o URL del SVG custom (campo "Icon Key" del componente synergos-icon-label)
  - `labelText` (string) — Texto visible del elemento (botón, input, badge, etc.) (campo "Label Text" del componente synergos-icon-label)
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-image-block>` — elementMediaImage

- **tag**: `<synergos-image-block>`
- **alias CMS**: `elementMediaImage`
- **tier**: primitive
- **frameworks**: angular
- **shape rich** (`ImageBlockElementConfig` — manual canónico):
  - `src`: string
  - `alt`: string
  - `caption`: string
  - `aspectRatio`: string
  - `loading`: string
  - `translations`: ComponentTranslations
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `src` (string) — Image source URL
  - `alt` (string) — Image alt text
  - `caption` (string) — Optional caption text
  - `aspectRatio` (string) — Aspect ratio token or CSS ratio
  - `loading` (string) — Native image loading mode

### `<synergos-label>` — elementTextLabel

- **tag**: `<synergos-label>`
- **alias CMS**: `elementTextLabel`
- **tier**: primitive
- **frameworks**: angular
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `headingText` (string) — Heading text content
  - `headingLevel` (string) — HTML heading tag: h1-h6
  - `body` (string) — Supporting body copy
  - `alignment` (string) — Text alignment (left | center)
  - `theme` (string) — Color theme (light | dark)

### `<synergos-link-block>` — elementActionLink

- **tag**: `<synergos-link-block>`
- **alias CMS**: `elementActionLink`
- **tier**: primitive
- **frameworks**: angular
- **shape rich** (`LinkBlockElementConfig` — manual canónico):
  - `href`: string
  - `label`: string
  - `target`: string
  - `ariaLabel`: string
  - `variant`: string
  - `tone`: string
  - `translations`: ComponentTranslations
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `href` (string) — Link destination URL
  - `label` (string) — Visible link text
  - `target` (string) — Link target attribute
  - `ariaLabel` (string) — Accessible label override
  - `variant` (string) — Presentation variant key

### `<synergos-paragraph>` — elementTextParagraph

- **tag**: `<synergos-paragraph>`
- **alias CMS**: `elementTextParagraph`
- **tier**: primitive
- **frameworks**: angular
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `headingText` (string) — Heading text content
  - `headingLevel` (string) — HTML heading tag: h1-h6
  - `body` (string) — Supporting body copy
  - `alignment` (string) — Text alignment (left | center)
  - `theme` (string) — Color theme (light | dark)

### `<synergos-popover>` — elementSynPopover

- **tag**: `<synergos-popover>`
- **alias CMS**: `elementSynPopover`
- **tier**: primitive
- **frameworks**: angular
- **shape schema** (`SynPopoverSchema` — auto del CMS):
  - `triggerLabel`: string
  - `popoverContent`: string
  - `placement`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `triggerLabel` (string) — Texto visible del elemento (botón, input, badge, etc.) (campo "Trigger Label" del componente synergos-popover)
  - `popoverContent` (string) — Campo "Popover Content" del componente synergos-popover. Editor: editar manualmente para enriquecer documentación.
  - `placement` (string) — Campo "Placement" del componente synergos-popover. Editor: editar manualmente para enriquecer documentación.
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-price-display>` — elementShopPriceDisplay

- **tag**: `<synergos-price-display>`
- **alias CMS**: `elementShopPriceDisplay`
- **tier**: primitive
- **frameworks**: angular
- **shape rich** (`PriceDisplayElementConfig` — manual canónico):
  - `showOriginalPrice`: boolean
  - `showDiscount`: boolean
  - `priceSize`: 'sm' | 'md' | 'lg'
  - `currency`: string
  - `theme`: string
  - `variant`: string
  - `translations`: ComponentTranslations
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Price display configuration from CMS contract bridge.
  - `showOriginalPrice` (boolean) — Show original/base price.
  - `showDiscount` (boolean) — Show discount badge/value.
  - `priceSize` (string) — Price typography size token.
  - `currency` (string) — ISO currency code.
  - `theme` (string) — Color theme key.
  - `variant` (string) — Visual variant key.
  - `price` (number) — Current product price.
  - `originalPrice` (number) — Original product price.
  - `discount` (number) — Discount percent value.

### `<synergos-progress-bar>` — elementSynProgressBar

- **tag**: `<synergos-progress-bar>`
- **alias CMS**: `elementSynProgressBar`
- **tier**: primitive
- **frameworks**: angular
- **shape schema** (`SynProgressBarSchema` — auto del CMS):
  - `valueNow`: string
  - `valueMax`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `valueNow` (string) — Campo "Value Now" del componente synergos-progress-bar. Editor: editar manualmente para enriquecer documentación.
  - `valueMax` (string) — Campo "Value Max" del componente synergos-progress-bar. Editor: editar manualmente para enriquecer documentación.
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-qr-code>` — elementSynQrCode

- **tag**: `<synergos-qr-code>`
- **alias CMS**: `elementSynQrCode`
- **tier**: primitive
- **frameworks**: angular
- **shape schema** (`SynQrCodeSchema` — auto del CMS):
  - `data`: string
  - `size`: string
  - `ecLevel`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `data` (string) — Campo "Data" del componente synergos-qr-code. Editor: editar manualmente para enriquecer documentación.
  - `size` (string) — Tamaño: sm | md | lg | xl según escala del componente
  - `ecLevel` (string) — Campo "Ec Level" del componente synergos-qr-code. Editor: editar manualmente para enriquecer documentación.
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-quantity-selector>` — elementShopQuantitySelector

- **tag**: `<synergos-quantity-selector>`
- **alias CMS**: `elementShopQuantitySelector`
- **tier**: primitive
- **frameworks**: angular
- **shape rich** (`QuantitySelectorElementConfig` — manual canónico):
  - `label`: string
  - `min`: number
  - `minQty`: number
  - `max`: number
  - `maxQty`: number
  - `step`: number
  - `value`: number
  - `initialQty`: number
  - `theme`: string
  - `variant`: string
  - `variantKey`: string
  - `translations`: ComponentTranslations
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Quantity selector configuration from CMS contract bridge.
  - `label` (string) — Accessible field label or visible caption for the quantity selector.
  - `min` (number) — Lower allowed quantity bound.
  - `minQty` (number) — CMS compatibility alias for the lower allowed quantity bound.
  - `max` (number) — Upper allowed quantity bound.
  - `maxQty` (number) — CMS compatibility alias for the upper allowed quantity bound.
  - `step` (number) — Increment/decrement step.
  - `value` (number) — Initial quantity value.
  - `initialQty` (number) — CMS compatibility alias for the initial quantity value.
  - `theme` (string) — Color theme key.
  - `variant` (string) — Visual variant key.
  - `variantKey` (string) — CMS compatibility alias for the visual variant key.

### `<synergos-quote>` — elementTextQuote

- **tag**: `<synergos-quote>`
- **alias CMS**: `elementTextQuote`
- **tier**: primitive
- **frameworks**: angular
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `headingText` (string) — Heading text content
  - `headingLevel` (string) — HTML heading tag: h1-h6
  - `body` (string) — Supporting body copy
  - `alignment` (string) — Text alignment (left | center)
  - `theme` (string) — Color theme (light | dark)

### `<synergos-rich-text>` — elementTextRichtext

- **tag**: `<synergos-rich-text>`
- **alias CMS**: `elementTextRichtext`
- **tier**: primitive
- **frameworks**: angular
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `headingText` (string) — Heading text content
  - `headingLevel` (string) — HTML heading tag: h1-h6
  - `body` (string) — Supporting body copy
  - `alignment` (string) — Text alignment (left | center)
  - `theme` (string) — Color theme (light | dark)

### `<synergos-scroll-top>` — elementSynScrollTop

- **tag**: `<synergos-scroll-top>`
- **alias CMS**: `elementSynScrollTop`
- **tier**: primitive
- **frameworks**: angular
- **shape schema** (`SynScrollTopSchema` — auto del CMS):
  - `scrollThreshold`: string
  - `position`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `scrollThreshold` (string) — Campo "Scroll Threshold" del componente synergos-scroll-top. Editor: editar manualmente para enriquecer documentación.
  - `position` (string) — Campo "Position" del componente synergos-scroll-top. Editor: editar manualmente para enriquecer documentación.
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-separator>` — elementSynSeparator

- **tag**: `<synergos-separator>`
- **alias CMS**: `elementSynSeparator`
- **tier**: primitive
- **frameworks**: angular
- **shape schema** (`SynSeparatorSchema` — auto del CMS):
  - `style`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `style` (string) — Campo "Style" del componente synergos-separator. Editor: editar manualmente para enriquecer documentación.
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-skeleton>` — elementSynSkeleton

- **tag**: `<synergos-skeleton>`
- **alias CMS**: `elementSynSkeleton`
- **tier**: primitive
- **frameworks**: angular
- **shape schema** (`SynSkeletonSchema` — auto del CMS):
  - `shape`: string
  - `count`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `shape` (string) — Campo "Shape" del componente synergos-skeleton. Editor: editar manualmente para enriquecer documentación.
  - `count` (string) — Campo "Count" del componente synergos-skeleton. Editor: editar manualmente para enriquecer documentación.
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-spacer>` — elementSynSpacer

- **tag**: `<synergos-spacer>`
- **alias CMS**: `elementSynSpacer`
- **tier**: primitive
- **frameworks**: angular
- **shape rich** (`SpacerElementConfig` — manual canónico):
  - `size`: string
  - `axis`: string
  - `translations`: ComponentTranslations
- **shape schema** (`SynSpacerSchema` — auto del CMS):
  - `size`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `size` (string) — Spacing token that controls the spacer size
  - `axis` (string) — Spacer axis (vertical | horizontal)

### `<synergos-stat-ticker>` — elementSynStatTicker

- **tag**: `<synergos-stat-ticker>`
- **alias CMS**: `elementSynStatTicker`
- **tier**: primitive
- **frameworks**: angular
- **shape schema** (`SynStatTickerSchema` — auto del CMS):
  - `statValue`: string
  - `statLabel`: string
  - `statPrefix`: string
  - `statSuffix`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `statValue` (string) — Campo "Stat Value" del componente synergos-stat-ticker. Editor: editar manualmente para enriquecer documentación.
  - `statLabel` (string) — Texto visible del elemento (botón, input, badge, etc.) (campo "Stat Label" del componente synergos-stat-ticker)
  - `statPrefix` (string) — Campo "Stat Prefix" del componente synergos-stat-ticker. Editor: editar manualmente para enriquecer documentación.
  - `statSuffix` (string) — Campo "Stat Suffix" del componente synergos-stat-ticker. Editor: editar manualmente para enriquecer documentación.
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-tag>` — elementSynTag

- **tag**: `<synergos-tag>`
- **alias CMS**: `elementSynTag`
- **tier**: primitive
- **frameworks**: angular
- **shape schema** (`SynTagSchema` — auto del CMS):
  - `tagLabel`: string
  - `tagColor`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `tagLabel` (string) — Texto visible del elemento (botón, input, badge, etc.) (campo "Tag Label" del componente synergos-tag)
  - `tagColor` (string) — Campo "Tag Color" del componente synergos-tag. Editor: editar manualmente para enriquecer documentación.
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-text-block>` — elementTextBlock

- **tag**: `<synergos-text-block>`
- **alias CMS**: `elementTextBlock`
- **tier**: primitive
- **frameworks**: angular
- **shape rich** (`TextBlockElementConfig` — manual canónico):
  - `headingText`: string
  - `headingLevel`: string
  - `body`: string
  - `alignment`: string
  - `variant`: string
  - `tone`: string
  - `theme`: string
  - `translations`: ComponentTranslations
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `headingText` (string) — Heading text content
  - `headingLevel` (string) — HTML heading tag: h1-h6
  - `body` (string) — Supporting body copy
  - `alignment` (string) — Text alignment (left | center)
  - `theme` (string) — Color theme (light | dark)

### `<synergos-tooltip>` — elementSynTooltip

- **tag**: `<synergos-tooltip>`
- **alias CMS**: `elementSynTooltip`
- **tier**: primitive
- **frameworks**: angular
- **shape schema** (`SynTooltipSchema` — auto del CMS):
  - `triggerText`: string
  - `tooltipText`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `triggerText` (string) — Campo "Trigger Text" del componente synergos-tooltip. Editor: editar manualmente para enriquecer documentación.
  - `tooltipText` (string) — Campo "Tooltip Text" del componente synergos-tooltip. Editor: editar manualmente para enriquecer documentación.
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-video-block>` — elementMediaVideo

- **tag**: `<synergos-video-block>`
- **alias CMS**: `elementMediaVideo`
- **tier**: primitive
- **frameworks**: angular
- **shape rich** (`VideoBlockElementConfig` — manual canónico):
  - `src`: string
  - `title`: string
  - `poster`: string
  - `controls`: boolean
  - `autoplay`: boolean
  - `muted`: boolean
  - `loop`: boolean
  - `translations`: ComponentTranslations
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `src` (string) — Video source URL
  - `title` (string) — Video title or caption
  - `poster` (string) — Poster image URL
  - `controls` (boolean) — Shows the native video controls
  - `autoplay` (boolean) — Starts playback automatically when allowed
  - `muted` (boolean) — Mutes the media by default
  - `loop` (boolean) — Loops playback continuously


## Compositions (46)

**Compositions** — combinan 2+ primitives + lógica simple. Self-contained editorial pieces (accordion, dropdown, search-box, etc.). Hidratan en cliente.

### `<synergos-accordion>` — elementSynAccordion

- **tag**: `<synergos-accordion>`
- **alias CMS**: `elementSynAccordion`
- **tier**: composition
- **frameworks**: angular
- **shape schema** (`SynAccordionSchema` — auto del CMS):
  - `itemsJson`: string
  - `allowMultiple`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `heading` (string) — Accordion trigger heading
  - `body` (string) — Accordion body content
  - `icon` (string) — Disclosure icon label
  - `variant` (string) — Presentation variant key
  - `theme` (string) — Color theme (light | dark)

### `<synergos-alert-bar>` — elementCorpAlertBar

- **tag**: `<synergos-alert-bar>`
- **alias CMS**: `elementCorpAlertBar`
- **tier**: composition
- **frameworks**: angular
- **shape rich** (`AlertBarElementConfig` — manual canónico):
  - `title`: string
  - `description`: string
  - `ctaLabel`: string
  - `ctaUrl`: string
  - `variant`: string
  - `tone`: string
  - `theme`: string
  - `dismissible`: boolean
  - `translations`: ComponentTranslations
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object for the alert bar.
  - `title` (string) — Alert heading text.
  - `description` (string) — Alert supporting copy.
  - `ctaLabel` (string) — Action label.
  - `ctaUrl` (string) — Action destination URL.
  - `tone` (string) — Alert tone (neutral | brand | critical).
  - `dismissible` (boolean) — Whether the alert can be dismissed.

### `<synergos-autocomplete>` — elementSynAutocomplete

- **tag**: `<synergos-autocomplete>`
- **alias CMS**: `elementSynAutocomplete`
- **tier**: composition
- **frameworks**: angular
- **shape schema** (`SynAutocompleteSchema` — auto del CMS):
  - `label`: string
  - `placeholder`: string
  - `suggestionsEndpoint`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `label` (string) — Texto visible del elemento (botón, input, badge, etc.)
  - `placeholder` (string) — Texto guía mostrado cuando el campo está vacío
  - `suggestionsEndpoint` (string) — Campo "Suggestions Endpoint" del componente synergos-autocomplete. Editor: editar manualmente para enriquecer documentación.
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-avatar-group>` — elementSynAvatarGroup

- **tag**: `<synergos-avatar-group>`
- **alias CMS**: `elementSynAvatarGroup`
- **tier**: composition
- **frameworks**: angular
- **shape schema** (`SynAvatarGroupSchema` — auto del CMS):
  - `avatarsJson`: string
  - `maxVisible`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `avatarsJson` (string) — Campo "Avatars Json" del componente synergos-avatar-group. Editor: editar manualmente para enriquecer documentación.
  - `maxVisible` (string) — Campo "Max Visible" del componente synergos-avatar-group. Editor: editar manualmente para enriquecer documentación.
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-avatar-upload>` — elementSynAvatarUpload

- **tag**: `<synergos-avatar-upload>`
- **alias CMS**: `elementSynAvatarUpload`
- **tier**: composition
- **frameworks**: angular
- **shape schema** (`SynAvatarUploadSchema` — auto del CMS):
  - `label`: string
  - `uploadEndpoint`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `label` (string) — Texto visible del elemento (botón, input, badge, etc.)
  - `uploadEndpoint` (string) — Campo "Upload Endpoint" del componente synergos-avatar-upload. Editor: editar manualmente para enriquecer documentación.
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-badge-group>` — elementSynBadgeGroup

- **tag**: `<synergos-badge-group>`
- **alias CMS**: `elementSynBadgeGroup`
- **tier**: composition
- **frameworks**: angular
- **shape schema** (`SynBadgeGroupSchema` — auto del CMS):
  - `badgesJson`: string
  - `layout`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `badgesJson` (string) — Campo "Badges Json" del componente synergos-badge-group. Editor: editar manualmente para enriquecer documentación.
  - `layout` (string) — Campo "Layout" del componente synergos-badge-group. Editor: editar manualmente para enriquecer documentación.
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-button-group>` — elementSynButtonGroup

- **tag**: `<synergos-button-group>`
- **alias CMS**: `elementSynButtonGroup`
- **tier**: composition
- **frameworks**: angular
- **shape rich** (`ButtonGroupElementConfig` — manual canónico):
  - `alignment`: 'left' | 'center' | 'right'
  - `direction`: 'row' | 'column'
  - `gap`: 'xs' | 'sm' | 'md' | 'lg'
  - `items`: ReadonlyArray<ButtonGroupItemConfig>
  - `translations`: ComponentTranslations
- **shape schema** (`SynButtonGroupSchema` — auto del CMS):
  - `buttonsJson`: string
  - `alignment`: string
  - `direction`: string
  - `gap`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `buttons` (json) — JSON array of button items. Overrides config.buttons when provided directly.
  - `alignment` (string) — Horizontal alignment (left | center | right)
  - `gap` (string) — Space between actions (xs | sm | md | lg)
  - `direction` (string) — Layout direction (row | column)

### `<synergos-card>` — elementCompCard

- **tag**: `<synergos-card>`
- **alias CMS**: `elementCompCard`
- **tier**: composition
- **frameworks**: angular
- **shape rich** (`CardElementConfig` — manual canónico):
  - `title`: string
  - `subtitle`: string
  - `body`: string
  - `imageSrc`: string
  - `imageAlt`: string
  - `ctaLabel`: string
  - `ctaUrl`: string
  - `badgeText`: string
  - `badgeType`: string
  - `variant`: string
  - `tone`: string
  - `theme`: string
  - `translations`: ComponentTranslations
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `title` (string) — Card heading
  - `subtitle` (string) — Card subheading
  - `body` (string) — Card body copy
  - `imageSrc` (string) — Card image URL
  - `imageAlt` (string) — Card image alt text
  - `ctaLabel` (string) — CTA button label
  - `ctaUrl` (string) — CTA destination URL
  - `badgeText` (string) — Badge label text
  - `badgeType` (string) — Badge semantic type (info | warning | success)
  - `variant` (string) — Card layout variant
  - `theme` (string) — Color theme (light | dark)

### `<synergos-cart-item>` — elementShopCartItem

- **tag**: `<synergos-cart-item>`
- **alias CMS**: `elementShopCartItem`
- **tier**: composition
- **frameworks**: angular
- **shape rich** (`CartItemElementConfig` — manual canónico):
  - `item`: CartItem
  - `productSku`: string
  - `quantity`: number
  - `unitPrice`: string
  - `updateEndpoint`: string
  - `theme`: string
  - `variant`: string
  - `variantKey`: string
  - `translations`: ComponentTranslations
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Cart item configuration from CMS contract bridge.
  - `item` (json) — Serialized CartItem object for direct rendering.
  - `productSku` (string) — CMS compatibility field for flat cart item payloads.
  - `quantity` (number) — CMS compatibility quantity field for flat cart item payloads.
  - `unitPrice` (string) — CMS compatibility unit price field for flat cart item payloads.
  - `updateEndpoint` (string) — CMS compatibility endpoint field for server-driven cart updates.
  - `theme` (string) — Color theme key.
  - `variant` (string) — Visual variant key.
  - `variantKey` (string) — CMS compatibility alias for the visual variant key.

### `<synergos-code-block>` — elementSynCodeBlock

- **tag**: `<synergos-code-block>`
- **alias CMS**: `elementSynCodeBlock`
- **tier**: composition
- **frameworks**: angular
- **shape schema** (`SynCodeBlockSchema` — auto del CMS):
  - `code`: string
  - `language`: string
  - `showLineNumbers`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `code` (string) — Campo "Code" del componente synergos-code-block. Editor: editar manualmente para enriquecer documentación.
  - `language` (string) — Campo "Language" del componente synergos-code-block. Editor: editar manualmente para enriquecer documentación.
  - `showLineNumbers` (string) — Campo "Show Line Numbers" del componente synergos-code-block. Editor: editar manualmente para enriquecer documentación.
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-color-picker>` — elementSynColorPicker

- **tag**: `<synergos-color-picker>`
- **alias CMS**: `elementSynColorPicker`
- **tier**: composition
- **frameworks**: angular
- **shape schema** (`SynColorPickerSchema` — auto del CMS):
  - `label`: string
  - `initialColor`: string
  - `paletteJson`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `label` (string) — Texto visible del elemento (botón, input, badge, etc.)
  - `initialColor` (string) — Campo "Initial Color" del componente synergos-color-picker. Editor: editar manualmente para enriquecer documentación.
  - `paletteJson` (string) — Campo "Palette Json" del componente synergos-color-picker. Editor: editar manualmente para enriquecer documentación.
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-color-swatches>` — elementSynColorSwatches

- **tag**: `<synergos-color-swatches>`
- **alias CMS**: `elementSynColorSwatches`
- **tier**: composition
- **frameworks**: angular
- **shape schema** (`SynColorSwatchesSchema` — auto del CMS):
  - `swatchesJson`: string
  - `shape`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `swatchesJson` (string) — Campo "Swatches Json" del componente synergos-color-swatches. Editor: editar manualmente para enriquecer documentación.
  - `shape` (string) — Campo "Shape" del componente synergos-color-swatches. Editor: editar manualmente para enriquecer documentación.
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-cta-group>` — elementActionCtaGroup

- **tag**: `<synergos-cta-group>`
- **alias CMS**: `elementActionCtaGroup`
- **tier**: composition
- **frameworks**: angular
- **shape rich** (`CtaGroupElementConfig` — manual canónico):
  - `primaryLabel`: string
  - `primaryUrl`: string
  - `primaryTarget`: string
  - `primaryVariant`: string
  - `secondaryLabel`: string
  - `secondaryUrl`: string
  - `secondaryTarget`: string
  - `secondaryVariant`: string
  - `alignment`: string
  - `translations`: ComponentTranslations
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `primaryLabel` (string) — Primary action label
  - `primaryUrl` (string) — Primary action destination URL
  - `secondaryLabel` (string) — Secondary action label
  - `secondaryUrl` (string) — Secondary action destination URL
  - `alignment` (string) — Action alignment (left | center | right)
  - `primaryTarget` (string) — Legacy primary CTA target override.
  - `primaryVariant` (string) — Legacy primary CTA variant override.
  - `secondaryTarget` (string) — Legacy secondary CTA target override.
  - `secondaryVariant` (string) — Legacy secondary CTA variant override.

### `<synergos-date-picker>` — elementSynDatePicker

- **tag**: `<synergos-date-picker>`
- **alias CMS**: `elementSynDatePicker`
- **tier**: composition
- **frameworks**: angular
- **shape schema** (`SynDatePickerSchema` — auto del CMS):
  - `label`: string
  - `initialDate`: string
  - `minDate`: string
  - `maxDate`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `label` (string) — Texto visible del elemento (botón, input, badge, etc.)
  - `initialDate` (string) — Campo "Initial Date" del componente synergos-date-picker. Editor: editar manualmente para enriquecer documentación.
  - `minDate` (string) — Campo "Min Date" del componente synergos-date-picker. Editor: editar manualmente para enriquecer documentación.
  - `maxDate` (string) — Campo "Max Date" del componente synergos-date-picker. Editor: editar manualmente para enriquecer documentación.
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-dropdown>` — elementSynDropdown

- **tag**: `<synergos-dropdown>`
- **alias CMS**: `elementSynDropdown`
- **tier**: composition
- **frameworks**: angular
- **shape schema** (`SynDropdownSchema` — auto del CMS):
  - `triggerLabel`: string
  - `optionsJson`: string
  - `selectedValue`: string
  - `searchable`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `triggerLabel` (string) — Texto visible del elemento (botón, input, badge, etc.) (campo "Trigger Label" del componente synergos-dropdown)
  - `optionsJson` (string) — Campo "Options Json" del componente synergos-dropdown. Editor: editar manualmente para enriquecer documentación.
  - `selectedValue` (string) — Campo "Selected Value" del componente synergos-dropdown. Editor: editar manualmente para enriquecer documentación.
  - `searchable` (string) — Campo "Searchable" del componente synergos-dropdown. Editor: editar manualmente para enriquecer documentación.
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-faq-item>` — elementInfoFaqItem

- **tag**: `<synergos-faq-item>`
- **alias CMS**: `elementInfoFaqItem`
- **tier**: composition
- **frameworks**: angular
- **shape rich** (`FaqItemElementConfig` — manual canónico):
  - `question`: string
  - `answer`: string
  - `initiallyExpanded`: boolean
  - `theme`: string
  - `translations`: ComponentTranslations
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object for the FAQ item.
  - `question` (string) — Question or prompt text.
  - `answer` (string) — Answer or explanation text.
  - `initiallyExpanded` (boolean) — Controls the initial expanded state.
  - `theme` (string) — Color theme key.

### `<synergos-feature-item>` — elementInfoFeature

- **tag**: `<synergos-feature-item>`
- **alias CMS**: `elementInfoFeature`
- **tier**: composition
- **frameworks**: angular
- **shape rich** (`FeatureItemElementConfig` — manual canónico):
  - `headingText`: string
  - `body`: string
  - `icon`: string
  - `variant`: string
  - `tone`: string
  - `theme`: string
  - `translations`: ComponentTranslations
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `icon` (string) — Icon or symbol name
  - `headingText` (string) — Feature heading text
  - `body` (string) — Feature body copy
  - `variant` (string) — Presentation variant key
  - `theme` (string) — Color theme (light | dark)

### `<synergos-form-stepper>` — elementSynFormStepper

- **tag**: `<synergos-form-stepper>`
- **alias CMS**: `elementSynFormStepper`
- **tier**: composition
- **frameworks**: angular
- **shape schema** (`SynFormStepperSchema` — auto del CMS):
  - `stepsJson`: string
  - `submitEndpoint`: string
  - `allowSkip`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `stepsJson` (string) — Campo "Steps Json" del componente synergos-form-stepper. Editor: editar manualmente para enriquecer documentación.
  - `submitEndpoint` (string) — Campo "Submit Endpoint" del componente synergos-form-stepper. Editor: editar manualmente para enriquecer documentación.
  - `allowSkip` (string) — Campo "Allow Skip" del componente synergos-form-stepper. Editor: editar manualmente para enriquecer documentación.
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-gallery-item>` — elementMediaGalleryItem

- **tag**: `<synergos-gallery-item>`
- **alias CMS**: `elementMediaGalleryItem`
- **tier**: composition
- **frameworks**: angular
- **shape rich** (`GalleryItemElementConfig` — manual canónico):
  - `src`: string
  - `alt`: string
  - `caption`: string
  - `aspectRatio`: string
  - `translations`: ComponentTranslations
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object for the gallery item.
  - `src` (string) — Media source URL.
  - `alt` (string) — Media alt text.
  - `caption` (string) — Optional caption text.
  - `aspectRatio` (string) — Aspect ratio token or CSS ratio.

### `<synergos-info-block>` — elementSynInfoBlock

- **tag**: `<synergos-info-block>`
- **alias CMS**: `elementSynInfoBlock`
- **tier**: composition
- **frameworks**: angular
- **shape rich** (`InfoBlockElementConfig` — manual canónico):
  - `title`: string
  - `body`: string
  - `ctaLabel`: string
  - `ctaUrl`: string
  - `variant`: string
  - `tone`: string
  - `theme`: string
  - `translations`: ComponentTranslations
- **shape schema** (`SynInfoBlockSchema` — auto del CMS):
  - `title`: string
  - `body`: string
  - `ctaLabel`: string
  - `ctaUrl`: string
  - `variant`: string
  - `theme`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `title` (string) — Heading text for the block
  - `body` (string) — Supporting body copy
  - `ctaLabel` (string) — Call-to-action label
  - `ctaUrl` (string) — Call-to-action destination URL
  - `variant` (string) — Presentation variant key
  - `theme` (string) — Color theme key

### `<synergos-key-value>` — elementInfoKeyValue

- **tag**: `<synergos-key-value>`
- **alias CMS**: `elementInfoKeyValue`
- **tier**: composition
- **frameworks**: angular
- **shape rich** (`KeyValueElementConfig` — manual canónico):
  - `label`: string
  - `value`: string
  - `helpText`: string
  - `theme`: string
  - `translations`: ComponentTranslations
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object for the key-value item.
  - `label` (string) — Label or key text.
  - `value` (string) — Value text.
  - `helpText` (string) — Optional supporting copy.
  - `theme` (string) — Color theme key.

### `<synergos-logo-item>` — elementMediaLogoItem

- **tag**: `<synergos-logo-item>`
- **alias CMS**: `elementMediaLogoItem`
- **tier**: composition
- **frameworks**: angular
- **shape rich** (`LogoItemElementConfig` — manual canónico):
  - `src`: string
  - `alt`: string
  - `href`: string
  - `label`: string
  - `target`: string
  - `translations`: ComponentTranslations
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object for the logo item.
  - `src` (string) — Logo image URL.
  - `alt` (string) — Logo image alt text.
  - `href` (string) — Optional logo destination URL.
  - `label` (string) — Optional label for the logo.
  - `target` (string) — Link target attribute.

### `<synergos-media-text>` — elementSynMediaText

- **tag**: `<synergos-media-text>`
- **alias CMS**: `elementSynMediaText`
- **tier**: composition
- **frameworks**: angular
- **shape rich** (`MediaTextElementConfig` — manual canónico):
  - `imageSrc`: string
  - `imageAlt`: string
  - `headingText`: string
  - `body`: string
  - `ctaLabel`: string
  - `ctaUrl`: string
  - `ctaTarget`: string
  - `mediaPosition`: 'left' | 'right'
  - `variant`: string
  - `tone`: string
  - `theme`: string
  - `translations`: ComponentTranslations
- **shape schema** (`SynMediaTextSchema` — auto del CMS):
  - `headingText`: string
  - `body`: string
  - `mediaReference`: string
  - `mediaAlt`: string
  - `mediaPosition`: string
  - `theme`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `imageSrc` (string) — Media image URL
  - `imageAlt` (string) — Media image alt text
  - `headingText` (string) — Main heading text
  - `body` (string) — Supporting body copy
  - `ctaLabel` (string) — Call-to-action label
  - `ctaUrl` (string) — Call-to-action destination URL
  - `mediaPosition` (string) — Media placement (left | right)
  - `theme` (string) — Color theme (light | dark)
  - `ctaTarget` (string) — CTA target from CMS config.
  - `variant` (string) — Visual variant key from CMS config.

### `<synergos-modal-trigger>` — elementSynModalTrigger

- **tag**: `<synergos-modal-trigger>`
- **alias CMS**: `elementSynModalTrigger`
- **tier**: composition
- **frameworks**: angular
- **shape schema** (`SynModalTriggerSchema` — auto del CMS):
  - `triggerLabel`: string
  - `modalTitle`: string
  - `modalContent`: string
  - `modalSize`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `triggerLabel` (string) — Texto visible del elemento (botón, input, badge, etc.) (campo "Trigger Label" del componente synergos-modal-trigger)
  - `modalTitle` (string) — Título mostrado destacado (campo "Modal Title" del componente synergos-modal-trigger)
  - `modalContent` (string) — Campo "Modal Content" del componente synergos-modal-trigger. Editor: editar manualmente para enriquecer documentación.
  - `modalSize` (string) — Tamaño: sm | md | lg | xl según escala del componente (campo "Modal Size" del componente synergos-modal-trigger)
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-newsletter-form>` — elementCorpNewsletterForm

- **tag**: `<synergos-newsletter-form>`
- **alias CMS**: `elementCorpNewsletterForm`
- **tier**: composition
- **frameworks**: angular
- **shape rich** (`NewsletterFormElementConfig` — manual canónico):
  - `title`: string
  - `intro`: string
  - `placeholder`: string
  - `submitLabel`: string
  - `consentText`: string
  - `successMessage`: string
  - `errorMessage`: string
  - `actionUrl`: string
  - `method`: string
  - `theme`: string
  - `translations`: ComponentTranslations
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object for the newsletter form.
  - `title` (string) — Form heading text.
  - `intro` (string) — Supporting introductory copy.
  - `placeholder` (string) — Email input placeholder.
  - `submitLabel` (string) — Submit button label.
  - `consentText` (string) — Optional consent or note text.
  - `successMessage` (string) — Success feedback message.
  - `errorMessage` (string) — Error feedback message.
  - `actionUrl` (string) — Optional form action URL.
  - `method` (string) — Form submission method.
  - `theme` (string) — Color theme key.

### `<synergos-otp-input>` — elementSynOtpInput

- **tag**: `<synergos-otp-input>`
- **alias CMS**: `elementSynOtpInput`
- **tier**: composition
- **frameworks**: angular
- **shape schema** (`SynOtpInputSchema` — auto del CMS):
  - `label`: string
  - `length`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `label` (string) — Texto visible del elemento (botón, input, badge, etc.)
  - `length` (string) — Campo "Length" del componente synergos-otp-input. Editor: editar manualmente para enriquecer documentación.
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-pagination>` — elementSynPagination

- **tag**: `<synergos-pagination>`
- **alias CMS**: `elementSynPagination`
- **tier**: composition
- **frameworks**: angular
- **shape schema** (`SynPaginationSchema` — auto del CMS):
  - `totalItems`: string
  - `itemsPerPage`: string
  - `currentPage`: string
  - `urlTemplate`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `totalItems` (string) — Campo "Total Items" del componente synergos-pagination. Editor: editar manualmente para enriquecer documentación.
  - `itemsPerPage` (string) — Campo "Items Per Page" del componente synergos-pagination. Editor: editar manualmente para enriquecer documentación.
  - `currentPage` (string) — Campo "Current Page" del componente synergos-pagination. Editor: editar manualmente para enriquecer documentación.
  - `urlTemplate` (string) — Campo "Url Template" del componente synergos-pagination. Editor: editar manualmente para enriquecer documentación.
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-pax-selector>` — elementSynPaxSelector

- **tag**: `<synergos-pax-selector>`
- **alias CMS**: `elementSynPaxSelector`
- **tier**: composition
- **frameworks**: angular
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Runtime configuration object; merged over the individual attributes for pax-selector.
  - `maxRooms` (number) — Maximum number of rooms selectable.
  - `maxPerRoom` (number) — Maximum number of guests per room.
  - `maxChildAge` (number) — Highest age still counted as a child.
  - `initial` (json) — JSON with the initial occupancy selection.

### `<synergos-product-card>` — elementShopProductCard

- **tag**: `<synergos-product-card>`
- **alias CMS**: `elementShopProductCard`
- **tier**: composition
- **frameworks**: angular
- **shape rich** (`ProductCardElementConfig` — manual canónico):
  - `productSku`: string
  - `productUrlTemplate`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Product card configuration from CMS contract bridge.
  - `productSku` (string) — Product SKU resolved from CMS.
  - `productUrlTemplate` (string) — Product detail URL template with placeholders ({id}, {sku}, {slug}).
  - `name` (string) — Editorial product name override.
  - `imageSrc` (string) — Editorial product image override.
  - `imageAlt` (string) — Editorial image alt override.
  - `showPrice` (boolean) — Whether the price section is rendered.
  - `showBadge` (boolean) — Whether the product badge is rendered.
  - `layout` (string) — Card layout mode (vertical | horizontal).
  - `cardLayout` (string) — CMS compatibility alias for card layout (standard | vertical | horizontal).
  - `theme` (string) — Color theme key.
  - `variant` (string) — Visual variant key.
  - `variantKey` (string) — CMS compatibility alias for the visual variant key.

### `<synergos-range-slider>` — elementSynRangeSlider

- **tag**: `<synergos-range-slider>`
- **alias CMS**: `elementSynRangeSlider`
- **tier**: composition
- **frameworks**: angular
- **shape schema** (`SynRangeSliderSchema` — auto del CMS):
  - `label`: string
  - `minValue`: string
  - `maxValue`: string
  - `step`: string
  - `initialValue`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `label` (string) — Texto visible del elemento (botón, input, badge, etc.)
  - `minValue` (string) — Campo "Min Value" del componente synergos-range-slider. Editor: editar manualmente para enriquecer documentación.
  - `maxValue` (string) — Campo "Max Value" del componente synergos-range-slider. Editor: editar manualmente para enriquecer documentación.
  - `step` (string) — Campo "Step" del componente synergos-range-slider. Editor: editar manualmente para enriquecer documentación.
  - `initialValue` (string) — Campo "Initial Value" del componente synergos-range-slider. Editor: editar manualmente para enriquecer documentación.
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-rating-stars>` — elementSynRatingStars

- **tag**: `<synergos-rating-stars>`
- **alias CMS**: `elementSynRatingStars`
- **tier**: composition
- **frameworks**: angular
- **shape schema** (`SynRatingStarsSchema` — auto del CMS):
  - `valueNow`: string
  - `maxStars`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `valueNow` (string) — Campo "Value Now" del componente synergos-rating-stars. Editor: editar manualmente para enriquecer documentación.
  - `maxStars` (string) — Campo "Max Stars" del componente synergos-rating-stars. Editor: editar manualmente para enriquecer documentación.
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-rich-tooltip>` — elementSynRichTooltip

- **tag**: `<synergos-rich-tooltip>`
- **alias CMS**: `elementSynRichTooltip`
- **tier**: composition
- **frameworks**: angular
- **shape schema** (`SynRichTooltipSchema` — auto del CMS):
  - `triggerText`: string
  - `tooltipContent`: string
  - `placement`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `triggerText` (string) — Campo "Trigger Text" del componente synergos-rich-tooltip. Editor: editar manualmente para enriquecer documentación.
  - `tooltipContent` (string) — Campo "Tooltip Content" del componente synergos-rich-tooltip. Editor: editar manualmente para enriquecer documentación.
  - `placement` (string) — Campo "Placement" del componente synergos-rich-tooltip. Editor: editar manualmente para enriquecer documentación.
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-search-box>` — elementSynSearchBox

- **tag**: `<synergos-search-box>`
- **alias CMS**: `elementSynSearchBox`
- **tier**: composition
- **frameworks**: angular
- **shape schema** (`SynSearchBoxSchema` — auto del CMS):
  - `searchPlaceholder`: string
  - `searchEndpoint`: string
  - `searchParamName`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `searchPlaceholder` (string) — Texto guía mostrado cuando el campo está vacío (campo "Search Placeholder" del componente synergos-search-box)
  - `searchEndpoint` (string) — Campo "Search Endpoint" del componente synergos-search-box. Editor: editar manualmente para enriquecer documentación.
  - `searchParamName` (string) — Identificador o nombre mostrado (campo "Search Param Name" del componente synergos-search-box)
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-seat-map>` — elementSynSeatMap

- **tag**: `<synergos-seat-map>`
- **alias CMS**: `elementSynSeatMap`
- **tier**: composition
- **frameworks**: angular
- **shape schema** (`SynSeatMapSchema` — auto del CMS):
  - `mapRef`: string
  - `maxSelectable`: string
  - `currency`: string
  - `density`: string
  - `hidePrices`: string
  - `hideLegend`: string
  - `style`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Runtime configuration object; merged over the individual attributes for seat-map.
  - `seatmap` (json) — JSON describing the seat layout: rows, seats and their state. Optional per row: serviceClass (cabin section — first/business/premium/economy, or any provider vocabulary). Optional per seat: features[] (extra-legroom, exit-row, bulkhead, recline-limited, or any provider vocabulary). `type` carries the POSITION (window/aisle/middle); the legacy value extra-legroom is still accepted and folded into features. aisleAfterColumns marks where the aisles go, by 1-based column index: an array ([3, 6] for a 3-3-3 widebody) or a single number for a one-aisle cabin. Omitted, the widest row is split in half. A row may override it with its own rows[].aisleAfterColumns when its section has a different layout (business 1-2-1 over economy 3-3-3); an empty array there means that row has no aisle at all, which is not the same as omitting the key.
  - `currency` (string) — ISO 4217 currency code used to format amounts.
  - `maxSelectable` (number) — Maximum number of seats selectable at once.
  - `density` (string) — How much room the map takes: comfortable | compact. compact shrinks the seat, the gaps and the aisle without removing anything; a 44-row cabin does not fit on a phone. Anything else falls back to comfortable.
  - `showPrices` (boolean) — Whether the per-seat surcharge label is drawn. Turn it off where the price does not distinguish seats — the total stays in the summary and the price stays in each seat's aria-label.
  - `showLegend` (boolean) — Whether the legend is drawn. The legend already derives from the payload (a map with no features explains none), so this is for maps embedded where the visitor has already seen the conventions.

### `<synergos-select-multi>` — elementSynSelectMulti

- **tag**: `<synergos-select-multi>`
- **alias CMS**: `elementSynSelectMulti`
- **tier**: composition
- **frameworks**: angular
- **shape schema** (`SynSelectMultiSchema` — auto del CMS):
  - `label`: string
  - `optionsJson`: string
  - `maxSelections`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `label` (string) — Texto visible del elemento (botón, input, badge, etc.)
  - `optionsJson` (string) — Campo "Options Json" del componente synergos-select-multi. Editor: editar manualmente para enriquecer documentación.
  - `maxSelections` (string) — Campo "Max Selections" del componente synergos-select-multi. Editor: editar manualmente para enriquecer documentación.
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-share-bar>` — elementSynShareBar

- **tag**: `<synergos-share-bar>`
- **alias CMS**: `elementSynShareBar`
- **tier**: composition
- **frameworks**: angular
- **shape schema** (`SynShareBarSchema` — auto del CMS):
  - `platforms`: string
  - `shareLink`: string
  - `shareTitle`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `platforms` (string) — Campo "Platforms" del componente synergos-share-bar. Editor: editar manualmente para enriquecer documentación.
  - `shareLink` (string) — Campo "Share Link" del componente synergos-share-bar. Editor: editar manualmente para enriquecer documentación.
  - `shareTitle` (string) — Título mostrado destacado (campo "Share Title" del componente synergos-share-bar)
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-signature-pad>` — elementSynSignaturePad

- **tag**: `<synergos-signature-pad>`
- **alias CMS**: `elementSynSignaturePad`
- **tier**: composition
- **frameworks**: angular
- **shape schema** (`SynSignaturePadSchema` — auto del CMS):
  - `label`: string
  - `width`: string
  - `height`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `label` (string) — Texto visible del elemento (botón, input, badge, etc.)
  - `width` (string) — Ancho explícito (CSS value: px / % / fr / auto)
  - `height` (string) — Alto explícito (CSS value)
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-social-proof>` — elementSynSocialProof

- **tag**: `<synergos-social-proof>`
- **alias CMS**: `elementSynSocialProof`
- **tier**: composition
- **frameworks**: angular
- **shape schema** (`SynSocialProofSchema` — auto del CMS):
  - `template`: string
  - `dataSource`: string
  - `rotationInterval`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `template` (string) — Campo "Template" del componente synergos-social-proof. Editor: editar manualmente para enriquecer documentación.
  - `dataSource` (string) — Campo "Data Source" del componente synergos-social-proof. Editor: editar manualmente para enriquecer documentación.
  - `rotationInterval` (string) — Campo "Rotation Interval" del componente synergos-social-proof. Editor: editar manualmente para enriquecer documentación.
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-social-share>` — elementCorpSocialShare

- **tag**: `<synergos-social-share>`
- **alias CMS**: `elementCorpSocialShare`
- **tier**: composition
- **frameworks**: angular
- **shape rich** (`SocialShareElementConfig` — manual canónico):
  - `title`: string
  - `pageUrl`: string
  - `layout`: 'row' | 'stack'
  - `links`: ReadonlyArray<SocialShareLinkConfig>
  - `translations`: ComponentTranslations
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object for social share links.
  - `title` (string) — Navigation title.
  - `pageUrl` (string) — Page URL used to generate share links.
  - `links` (json) — JSON array of social link objects.
  - `layout` (string) — Visual layout (row | stack).

### `<synergos-splitter>` — elementSynSplitter

- **tag**: `<synergos-splitter>`
- **alias CMS**: `elementSynSplitter`
- **tier**: composition
- **frameworks**: angular
- **shape schema** (`SynSplitterSchema` — auto del CMS):
  - `leftContent`: string
  - `rightContent`: string
  - `orientation`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `leftContent` (string) — Campo "Left Content" del componente synergos-splitter. Editor: editar manualmente para enriquecer documentación.
  - `rightContent` (string) — Campo "Right Content" del componente synergos-splitter. Editor: editar manualmente para enriquecer documentación.
  - `orientation` (string) — Campo "Orientation" del componente synergos-splitter. Editor: editar manualmente para enriquecer documentación.
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-stepper>` — elementSynStepper

- **tag**: `<synergos-stepper>`
- **alias CMS**: `elementSynStepper`
- **tier**: composition
- **frameworks**: angular
- **shape schema** (`SynStepperSchema` — auto del CMS):
  - `stepsJson`: string
  - `currentStep`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `stepsJson` (string) — Campo "Steps Json" del componente synergos-stepper. Editor: editar manualmente para enriquecer documentación.
  - `currentStep` (string) — Campo "Current Step" del componente synergos-stepper. Editor: editar manualmente para enriquecer documentación.
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-tabs>` — elementSynTabs

- **tag**: `<synergos-tabs>`
- **alias CMS**: `elementSynTabs`
- **tier**: composition
- **frameworks**: angular
- **shape schema** (`SynTabsSchema` — auto del CMS):
  - `tabsJson`: string
  - `initialTab`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `tabsJson` (string) — Campo "Tabs Json" del componente synergos-tabs. Editor: editar manualmente para enriquecer documentación.
  - `initialTab` (string) — Campo "Initial Tab" del componente synergos-tabs. Editor: editar manualmente para enriquecer documentación.
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-testimonial-item>` — elementInfoTestimonialItem

- **tag**: `<synergos-testimonial-item>`
- **alias CMS**: `elementInfoTestimonialItem`
- **tier**: composition
- **frameworks**: angular
- **shape rich** (`TestimonialItemElementConfig` — manual canónico):
  - `quote`: string
  - `name`: string
  - `role`: string
  - `avatarSrc`: string
  - `avatarAlt`: string
  - `theme`: string
  - `translations`: ComponentTranslations
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object for the testimonial item.
  - `quote` (string) — Quoted testimonial content.
  - `name` (string) — Author name.
  - `role` (string) — Author role or subtitle.
  - `avatarSrc` (string) — Author avatar URL.
  - `avatarAlt` (string) — Author avatar alt text.
  - `theme` (string) — Color theme key.

### `<synergos-timeline-horizontal>` — elementSynTimelineHorizontal

- **tag**: `<synergos-timeline-horizontal>`
- **alias CMS**: `elementSynTimelineHorizontal`
- **tier**: composition
- **frameworks**: angular
- **shape schema** (`SynTimelineHorizontalSchema` — auto del CMS):
  - `eventsJson`: string
  - `snapEnabled`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `eventsJson` (string) — Campo "Events Json" del componente synergos-timeline-horizontal. Editor: editar manualmente para enriquecer documentación.
  - `snapEnabled` (string) — Campo "Snap Enabled" del componente synergos-timeline-horizontal. Editor: editar manualmente para enriquecer documentación.
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-timeline-item>` — elementInfoTimelineItem

- **tag**: `<synergos-timeline-item>`
- **alias CMS**: `elementInfoTimelineItem`
- **tier**: composition
- **frameworks**: angular
- **shape rich** (`TimelineItemElementConfig` — manual canónico):
  - `headingText`: string
  - `body`: string
  - `date`: string
  - `variant`: string
  - `tone`: string
  - `theme`: string
  - `translations`: ComponentTranslations
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object for the timeline item.
  - `headingText` (string) — Timeline item heading.
  - `body` (string) — Timeline item body copy.
  - `date` (string) — Timeline date label.
  - `variant` (string) — Presentation variant key.
  - `theme` (string) — Color theme key.

### `<synergos-variant-picker>` — elementShopVariantPicker

- **tag**: `<synergos-variant-picker>`
- **alias CMS**: `elementShopVariantPicker`
- **tier**: composition
- **frameworks**: angular
- **shape rich** (`VariantPickerElementConfig` — manual canónico):
  - `label`: string
  - `selectedValue`: string
  - `variantType`: 'color' | 'size' | 'storage' | 'custom'
  - `displayAs`: 'buttons' | 'swatches' | 'dropdown'
  - `variantsJson`: string
  - `theme`: string
  - `variant`: string
  - `variantKey`: string
  - `translations`: ComponentTranslations
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Variant picker configuration from CMS contract bridge.
  - `label` (string) — Visible group label or legend for the variant picker.
  - `selectedValue` (string) — Initial selected variant value or id.
  - `variants` (json) — Serialized product variant list.
  - `variantsJson` (string) — CMS compatibility JSON string emitted by current Web partials.
  - `variantType` (string) — Variant family to display (size | color | storage | custom).
  - `displayAs` (string) — UI mode (buttons | swatches | dropdown).
  - `theme` (string) — Color theme key.
  - `variant` (string) — Visual variant key.
  - `variantKey` (string) — CMS compatibility alias for the visual variant key.


## Modules (53)

**Modules** — features ricas con state propio + posiblemente fetch (carousel, hero, comments-widget, etc.). Self-contained but heavier.

### `<synergos-academy>` — elementSynAcademy

- **tag**: `<synergos-academy>`
- **alias CMS**: `elementSynAcademy`
- **tier**: module
- **frameworks**: angular
- **shape schema** (`SynAcademySchema` — auto del CMS):
  - `heading`: string
  - `subheading`: string
  - `apiBase`: string
  - `config`: string
  - `content`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Runtime configuration object; merged over the individual attributes for academy.
  - `apiBase` (string) — Base URL of the backing API. Defaults to the module route when unset.
  - `currency` (string) — ISO 4217 currency code used to format amounts.
  - `scope` (string) — Content scope (siteRoot) the module reads from.
  - `role` (string) — Viewer role; drives which actions and sections are offered.

### `<synergos-app-launcher>` — elementSynAppLauncher

- **tag**: `<synergos-app-launcher>`
- **alias CMS**: `elementSynAppLauncher`
- **tier**: module
- **frameworks**: angular
- **shape schema** (`SynAppLauncherSchema` — auto del CMS):
  - `heading`: string
  - `subheading`: string
  - `apps`: string
  - `content`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Runtime configuration object; merged over the individual attributes for app-launcher.
  - `title` (string) — Main heading rendered above the module.
  - `searchLabel` (string) — Accessible label of the search field.
  - `searchPlaceholder` (string) — Placeholder text of the search field.
  - `ctaLabel` (string) — Label of the primary call to action.
  - `emptyLabel` (string) — Message shown when there is nothing to list.
  - `allFiltersLabel` (string) — Label of the option that clears every filter.
  - `apps` (json) — JSON array of apps to list: {id, name, tagline, status, url, icon}.

### `<synergos-audio-player>` — elementSynAudioPlayer

- **tag**: `<synergos-audio-player>`
- **alias CMS**: `elementSynAudioPlayer`
- **tier**: module
- **frameworks**: angular
- **shape schema** (`SynAudioPlayerSchema` — auto del CMS):
  - `audioFile`: string
  - `trackTitle`: string
  - `artistName`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `audioFile` (string) — Campo "Audio File" del componente synergos-audio-player. Editor: editar manualmente para enriquecer documentación.
  - `trackTitle` (string) — Título mostrado destacado (campo "Track Title" del componente synergos-audio-player)
  - `artistName` (string) — Identificador o nombre mostrado (campo "Artist Name" del componente synergos-audio-player)
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-banner>` — elementCompCtaBanner

- **tag**: `<synergos-banner>`
- **alias CMS**: `elementCompCtaBanner`
- **tier**: module
- **frameworks**: angular
- **shape rich** (`BannerElementConfig` — manual canónico):
  - `eyebrow`: string
  - `title`: string
  - `body`: string
  - `imageSrc`: string
  - `imageAlt`: string
  - `ctaLabel`: string
  - `ctaUrl`: string
  - `ctaTarget`: string
  - `secondaryCtaLabel`: string
  - `secondaryCtaUrl`: string
  - `secondaryCtaTarget`: string
  - `variant`: string
  - `tone`: string
  - `theme`: string
  - `translations`: ComponentTranslations
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `title` (string) — Banner heading text
  - `body` (string) — Banner body copy
  - `ctaLabel` (string) — CTA button label
  - `ctaUrl` (string) — CTA destination URL
  - `ctaTarget` (string) — Link target attribute
  - `variant` (string) — Layout variant key
  - `theme` (string) — Color theme (light | dark)
  - `eyebrow` (string) — Optional eyebrow copy used as a legacy override.
  - `imageSrc` (string) — Optional supporting image URL used outside CMS config.
  - `imageAlt` (string) — Optional supporting image alt text.
  - `secondaryCtaLabel` (string) — Legacy secondary CTA label override.
  - `secondaryCtaUrl` (string) — Legacy secondary CTA URL override.
  - `secondaryCtaTarget` (string) — Legacy secondary CTA target override.

### `<synergos-banner-slider>` — elementCorpBannerSlider

- **tag**: `<synergos-banner-slider>`
- **alias CMS**: `elementCorpBannerSlider`
- **tier**: module
- **frameworks**: angular
- **shape rich** (`BannerSliderElementConfig` — manual canónico):
  - `headingText`: string
  - `body`: string
  - `autoplay`: boolean
  - `loop`: boolean
  - `variant`: string
  - `tone`: string
  - `theme`: string
  - `slides`: ReadonlyArray<BannerSliderSlideConfig>
  - `translations`: ComponentTranslations
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object for the banner slider.
  - `headingText` (string) — Slider heading text.
  - `body` (string) — Slider supporting copy.
  - `items` (json) — JSON array of slide objects.
  - `autoplay` (boolean) — Whether the slider should autoplay.
  - `loop` (boolean) — Whether the slider should loop.
  - `variant` (string) — Presentation variant key.
  - `theme` (string) — Color theme key.

### `<synergos-blogs>` — elementSynBlogs

- **tag**: `<synergos-blogs>`
- **alias CMS**: `elementSynBlogs`
- **tier**: module
- **frameworks**: angular
- **shape schema** (`SynBlogsSchema` — auto del CMS):
  - `heading`: string
  - `subheading`: string
  - `apiBase`: string
  - `config`: string
  - `content`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Runtime configuration object; merged over the individual attributes for blogs.
  - `apiBase` (string) — Base URL of the backing API. Defaults to the module route when unset.
  - `scope` (string) — Content scope (siteRoot) the module reads from.
  - `user` (string) — Author identifier whose posts are listed.
  - `viewerHandle` (string) — Handle of the signed-in reader, used for their own actions.
  - `viewerName` (string) — Display name of the signed-in reader.
  - `view` (string) — Initial view to render.

### `<synergos-booking-wizard>` — elementSynBookingWizard

- **tag**: `<synergos-booking-wizard>`
- **alias CMS**: `elementSynBookingWizard`
- **tier**: module
- **frameworks**: angular
- **shape schema** (`SynBookingWizardSchema` — auto del CMS):
  - `apiBase`: string
  - `destinationLabel`: string
  - `currency`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Runtime configuration object; merged over the individual attributes for booking-wizard.
  - `apiBase` (string) — Base URL of the backing API. Defaults to the module route when unset.
  - `currency` (string) — ISO 4217 currency code used to format amounts.
  - `destinationLabel` (string) — Label of the destination field.
  - `sessionKey` (string) — Key used to persist wizard progress across reloads.

### `<synergos-calendar>` — elementSynCalendar

- **tag**: `<synergos-calendar>`
- **alias CMS**: `elementSynCalendar`
- **tier**: module
- **frameworks**: angular
- **shape schema** (`SynCalendarSchema` — auto del CMS):
  - `eventsEndpoint`: string
  - `initialMonth`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `eventsEndpoint` (string) — Campo "Events Endpoint" del componente synergos-calendar. Editor: editar manualmente para enriquecer documentación.
  - `initialMonth` (string) — Campo "Initial Month" del componente synergos-calendar. Editor: editar manualmente para enriquecer documentación.
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-carousel>` — elementSynCarousel

- **tag**: `<synergos-carousel>`
- **alias CMS**: `elementSynCarousel`
- **tier**: module
- **frameworks**: angular
- **shape schema** (`SynCarouselSchema` — auto del CMS):
  - `slidesJson`: string
  - `autoplayInterval`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `slidesJson` (string) — Campo "Slides Json" del componente synergos-carousel. Editor: editar manualmente para enriquecer documentación.
  - `autoplayInterval` (string) — Campo "Autoplay Interval" del componente synergos-carousel. Editor: editar manualmente para enriquecer documentación.
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-cart-summary>` — elementShopCartSummary

- **tag**: `<synergos-cart-summary>`
- **alias CMS**: `elementShopCartSummary`
- **tier**: module
- **frameworks**: angular
- **shape rich** (`CartSummaryElementConfig` — manual canónico):
  - `title`: string
  - `summaryTitle`: string
  - `showCoupon`: boolean
  - `checkoutUrl`: string
  - `checkoutEndpoint`: string
  - `continueShoppingUrl`: string
  - `showShipping`: boolean
  - `showTax`: boolean
  - `theme`: string
  - `variant`: string
  - `variantKey`: string
  - `translations`: ComponentTranslations
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Cart summary configuration from CMS contract bridge.
  - `title` (string) — Optional drawer title override.
  - `summaryTitle` (string) — CMS compatibility alias for the cart summary title.
  - `showCoupon` (boolean) — Enables coupon input controls.
  - `checkoutUrl` (string) — Checkout destination URL.
  - `checkoutEndpoint` (string) — CMS compatibility alias for checkout destination.
  - `continueShoppingUrl` (string) — Continue shopping URL.
  - `showShipping` (boolean) — CMS compatibility flag for shipping line rendering.
  - `showTax` (boolean) — CMS compatibility flag for tax line rendering.
  - `open` (boolean) — External open-state override for drawer mode.
  - `theme` (string) — Color theme key.
  - `variant` (string) — Visual variant key.
  - `variantKey` (string) — CMS compatibility alias for the visual variant key.

### `<synergos-chart-bar>` — elementSynChartBar

- **tag**: `<synergos-chart-bar>`
- **alias CMS**: `elementSynChartBar`
- **tier**: module
- **frameworks**: angular
- **shape schema** (`SynChartBarSchema` — auto del CMS):
  - `chartTitle`: string
  - `dataJson`: string
  - `orientation`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `chartTitle` (string) — Título mostrado destacado (campo "Chart Title" del componente synergos-chart-bar)
  - `dataJson` (string) — Campo "Data Json" del componente synergos-chart-bar. Editor: editar manualmente para enriquecer documentación.
  - `orientation` (string) — Campo "Orientation" del componente synergos-chart-bar. Editor: editar manualmente para enriquecer documentación.
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-comments-widget>` — elementSynCommentsWidget

- **tag**: `<synergos-comments-widget>`
- **alias CMS**: `elementSynCommentsWidget`
- **tier**: module
- **frameworks**: angular
- **shape schema** (`SynCommentsWidgetSchema` — auto del CMS):
  - `provider`: string
  - `threadId`: string
  - `configNote`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `provider` (string) — Campo "Provider" del componente synergos-comments-widget. Editor: editar manualmente para enriquecer documentación.
  - `threadId` (string) — Campo "Thread Id" del componente synergos-comments-widget. Editor: editar manualmente para enriquecer documentación.
  - `configNote` (string) — Campo "Config Note" del componente synergos-comments-widget. Editor: editar manualmente para enriquecer documentación.
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-cookie-consent>` — elementSynCookieConsent

- **tag**: `<synergos-cookie-consent>`
- **alias CMS**: `elementSynCookieConsent`
- **tier**: module
- **frameworks**: angular
- **shape schema** (`SynCookieConsentSchema` — auto del CMS):
  - `bannerText`: string
  - `acceptLabel`: string
  - `rejectLabel`: string
  - `settingsLabel`: string
  - `policyLink`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `bannerText` (string) — Campo "Banner Text" del componente synergos-cookie-consent. Editor: editar manualmente para enriquecer documentación.
  - `acceptLabel` (string) — Texto visible del elemento (botón, input, badge, etc.) (campo "Accept Label" del componente synergos-cookie-consent)
  - `rejectLabel` (string) — Texto visible del elemento (botón, input, badge, etc.) (campo "Reject Label" del componente synergos-cookie-consent)
  - `settingsLabel` (string) — Texto visible del elemento (botón, input, badge, etc.) (campo "Settings Label" del componente synergos-cookie-consent)
  - `policyLink` (string) — Campo "Policy Link" del componente synergos-cookie-consent. Editor: editar manualmente para enriquecer documentación.
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-countdown-clock>` — elementSynCountdownClock

- **tag**: `<synergos-countdown-clock>`
- **alias CMS**: `elementSynCountdownClock`
- **tier**: module
- **frameworks**: angular
- **shape rich** (`CountdownClockElementConfig` — manual canónico):
  - `title`: string
  - `theme`: string
  - `variant`: string
  - `tone`: string
  - `elementId`: string
  - `targetDate`: string
  - `expiredText`: string
  - `translations`: ComponentTranslations
- **shape schema** (`SynCountdownClockSchema` — auto del CMS):
  - `endDateTime`: string
  - `labelFormat`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Countdown clock configuration: targetDate, label, theme.

### `<synergos-countdown-digital>` — elementSynCountdownDigital

- **tag**: `<synergos-countdown-digital>`
- **alias CMS**: `elementSynCountdownDigital`
- **tier**: module
- **frameworks**: angular
- **shape schema** (`SynCountdownDigitalSchema` — auto del CMS):
  - `endDateTime`: string
  - `showLabels`: string
  - `style`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `endDateTime` (string) — Campo "End Date Time" del componente synergos-countdown-digital. Editor: editar manualmente para enriquecer documentación.
  - `showLabels` (string) — Texto visible del elemento (botón, input, badge, etc.) (campo "Show Labels" del componente synergos-countdown-digital)
  - `style` (string) — Campo "Style" del componente synergos-countdown-digital. Editor: editar manualmente para enriquecer documentación.
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-data-grid>` — elementSynDataGrid

- **tag**: `<synergos-data-grid>`
- **alias CMS**: `elementSynDataGrid`
- **tier**: module
- **frameworks**: angular
- **shape schema** (`SynDataGridSchema` — auto del CMS):
  - `dataSource`: string
  - `columnsJson`: string
  - `pageSize`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `dataSource` (string) — Campo "Data Source" del componente synergos-data-grid. Editor: editar manualmente para enriquecer documentación.
  - `columnsJson` (string) — Campo "Columns Json" del componente synergos-data-grid. Editor: editar manualmente para enriquecer documentación.
  - `pageSize` (string) — Tamaño: sm | md | lg | xl según escala del componente (campo "Page Size" del componente synergos-data-grid)
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-data-table>` — elementCorpDataTable

- **tag**: `<synergos-data-table>`
- **alias CMS**: `elementCorpDataTable`
- **tier**: module
- **frameworks**: angular
- **shape rich** (`DataTableElementConfig` — manual canónico):
  - `caption`: string
  - `emptyLabel`: string
  - `striped`: boolean
  - `bordered`: boolean
  - `hoverable`: boolean
  - `compact`: boolean
  - `columns`: ReadonlyArray<{
    readonly key?: string
  - `label`: string
  - `align`: 'left' | 'center' | 'right'
  - `sortable`: boolean
  - `width`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object for the data table.
  - `caption` (string) — Accessible table caption.
  - `columns` (json) — JSON array of column definitions.
  - `rows` (json) — JSON array of row records.
  - `emptyLabel` (string) — Fallback label when the table is empty.
  - `striped` (boolean) — Enables striped row styling.
  - `bordered` (boolean) — Enables cell borders.
  - `hoverable` (boolean) — Enables hover styling.
  - `compact` (boolean) — Uses compact row spacing.

### `<synergos-drawer>` — elementSynDrawer

- **tag**: `<synergos-drawer>`
- **alias CMS**: `elementSynDrawer`
- **tier**: module
- **frameworks**: angular
- **shape schema** (`SynDrawerSchema` — auto del CMS):
  - `triggerLabel`: string
  - `drawerContent`: string
  - `side`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `triggerLabel` (string) — Texto visible del elemento (botón, input, badge, etc.) (campo "Trigger Label" del componente synergos-drawer)
  - `drawerContent` (string) — Campo "Drawer Content" del componente synergos-drawer. Editor: editar manualmente para enriquecer documentación.
  - `side` (string) — Campo "Side" del componente synergos-drawer. Editor: editar manualmente para enriquecer documentación.
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-dropzone>` — elementSynDropzone

- **tag**: `<synergos-dropzone>`
- **alias CMS**: `elementSynDropzone`
- **tier**: module
- **frameworks**: angular
- **shape schema** (`SynDropzoneSchema` — auto del CMS):
  - `label`: string
  - `acceptedTypes`: string
  - `uploadEndpoint`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `label` (string) — Texto visible del elemento (botón, input, badge, etc.)
  - `acceptedTypes` (string) — Campo "Accepted Types" del componente synergos-dropzone. Editor: editar manualmente para enriquecer documentación.
  - `uploadEndpoint` (string) — Campo "Upload Endpoint" del componente synergos-dropzone. Editor: editar manualmente para enriquecer documentación.
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-ehr>` — elementSynEhr

- **tag**: `<synergos-ehr>`
- **alias CMS**: `elementSynEhr`
- **tier**: module
- **frameworks**: angular
- **shape schema** (`SynEhrSchema` — auto del CMS):
  - `heading`: string
  - `subheading`: string
  - `apiBase`: string
  - `config`: string
  - `content`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Runtime configuration object; merged over the individual attributes for ehr.
  - `apiBase` (string) — Base URL of the backing API. Defaults to the module route when unset.
  - `clinic` (string) — Clinic identifier the records belong to.
  - `scope` (string) — Content scope (siteRoot) the module reads from.
  - `role` (string) — Viewer role; drives which actions and sections are offered.
  - `patient` (string) — Patient identifier to open on load.
  - `copayMinor` (number) — Copay amount in minor units (cents).

### `<synergos-eventos>` — elementSynEventos

- **tag**: `<synergos-eventos>`
- **alias CMS**: `elementSynEventos`
- **tier**: module
- **frameworks**: angular
- **shape schema** (`SynEventosSchema` — auto del CMS):
  - `heading`: string
  - `subheading`: string
  - `apiBase`: string
  - `role`: string
  - `config`: string
  - `content`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Runtime configuration object; merged over the individual attributes for eventos.
  - `apiBase` (string) — Base URL of the backing API. Defaults to the module route when unset.
  - `currency` (string) — ISO 4217 currency code used to format amounts.
  - `scope` (string) — Content scope (siteRoot) the module reads from.
  - `role` (json) — Viewer role; drives which actions and sections are offered.
  - `eventId` (string) — Event identifier to open on load.
  - `feePercent` (number) — Service fee applied to the ticket price, as a percentage.

### `<synergos-faq-section>` — elementSynFaqSection

- **tag**: `<synergos-faq-section>`
- **alias CMS**: `elementSynFaqSection`
- **tier**: module
- **frameworks**: angular
- **shape rich** (`FaqSectionElementConfig` — manual canónico):
  - `headingText`: string
  - `theme`: string
  - `items`: ReadonlyArray<FaqSectionItemConfig>
  - `translations`: ComponentTranslations
- **shape schema** (`SynFaqSectionSchema` — auto del CMS):
  - `headingText`: string
  - `itemsJson`: string
  - `theme`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `headingText` (string) — Section heading text
  - `items` (json) — JSON array of FAQ items with question, answer and optional initiallyExpanded
  - `theme` (string) — Color theme (light | dark)

### `<synergos-feature-grid>` — elementSynFeatureGrid

- **tag**: `<synergos-feature-grid>`
- **alias CMS**: `elementSynFeatureGrid`
- **tier**: module
- **frameworks**: angular
- **shape rich** (`FeatureGridElementConfig` — manual canónico):
  - `headingText`: string
  - `columns`: number
  - `variant`: string
  - `tone`: string
  - `theme`: string
  - `items`: ReadonlyArray<FeatureGridItemConfig>
  - `translations`: ComponentTranslations
- **shape schema** (`SynFeatureGridSchema` — auto del CMS):
  - `headingText`: string
  - `itemsJson`: string
  - `columns`: string
  - `theme`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `headingText` (string) — Section heading text
  - `columns` (number) — Preferred number of columns
  - `items` (json) — JSON array of feature items with heading, body and optional icon
  - `theme` (string) — Color theme (light | dark)
  - `variant` (string) — Visual variant key from CMS config.

### `<synergos-feature-journey>` — experienceFeatureJourney

- **tag**: `<synergos-feature-journey>`
- **alias CMS**: `experienceFeatureJourney`
- **tier**: module
- **frameworks**: angular
- **shape rich** (`FeatureJourneyElementConfig` — manual canónico):
  - `title`: string
  - `theme`: string
  - `variant`: string
  - `tone`: string
  - `elementId`: string
  - `translations`: ComponentTranslations
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base experience configuration object from CMS.
  - `title` (string) — Experience title.
  - `theme` (string) — Theme key.
  - `variant` (string) — Variant key.
  - `elementId` (string) — DOM element id.

### `<synergos-file-uploader>` — elementSynFileUploader

- **tag**: `<synergos-file-uploader>`
- **alias CMS**: `elementSynFileUploader`
- **tier**: module
- **frameworks**: angular
- **shape schema** (`SynFileUploaderSchema` — auto del CMS):
  - `uploadEndpoint`: string
  - `acceptedTypes`: string
  - `maxFileSizeMb`: string
  - `maxFiles`: string
  - `label`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `uploadEndpoint` (string) — Campo "Upload Endpoint" del componente synergos-file-uploader. Editor: editar manualmente para enriquecer documentación.
  - `acceptedTypes` (string) — Campo "Accepted Types" del componente synergos-file-uploader. Editor: editar manualmente para enriquecer documentación.
  - `maxFileSizeMb` (string) — Tamaño: sm | md | lg | xl según escala del componente (campo "Max File Size Mb" del componente synergos-file-uploader)
  - `maxFiles` (string) — Campo "Max Files" del componente synergos-file-uploader. Editor: editar manualmente para enriquecer documentación.
  - `label` (string) — Texto visible del elemento (botón, input, badge, etc.)
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-gov>` — elementSynGov

- **tag**: `<synergos-gov>`
- **alias CMS**: `elementSynGov`
- **tier**: module
- **frameworks**: angular
- **shape schema** (`SynGovSchema` — auto del CMS):
  - `heading`: string
  - `subheading`: string
  - `apiBase`: string
  - `config`: string
  - `content`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Runtime configuration object; merged over the individual attributes for gov.
  - `apiBase` (string) — Base URL of the backing API. Defaults to the module route when unset.
  - `role` (string) — Viewer role; drives which actions and sections are offered.
  - `agency` (string) — Agency identifier the procedures belong to.
  - `scope` (string) — Content scope (siteRoot) the module reads from.

### `<synergos-hero>` — elementCompHero

- **tag**: `<synergos-hero>`
- **alias CMS**: `elementCompHero`
- **tier**: module
- **frameworks**: angular
- **shape rich** (`HeroElementConfig` — manual canónico):
  - `headingText`: string
  - `headingLevel`: string
  - `body`: string
  - `imageSrc`: string
  - `imageAlt`: string
  - `ctaLabel`: string
  - `ctaUrl`: string
  - `ctaTarget`: string
  - `variant`: string
  - `tone`: string
  - `theme`: string
  - `translations`: ComponentTranslations
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `headingText` (string) — Main heading text
  - `headingLevel` (string) — HTML heading tag: h1–h6
  - `body` (string) — Body copy / paragraph text
  - `imageSrc` (string) — Hero image URL
  - `imageAlt` (string) — Hero image alt text
  - `ctaLabel` (string) — Call-to-action button label
  - `ctaUrl` (string) — Call-to-action destination URL
  - `ctaTarget` (string) — Link target attribute (_self | _blank)
  - `variant` (string) — Layout variant key
  - `theme` (string) — Color theme (light | dark)

### `<synergos-hero-banner>` — elementSynHeroBanner

- **tag**: `<synergos-hero-banner>`
- **alias CMS**: `elementSynHeroBanner`
- **tier**: module
- **frameworks**: angular
- **shape schema** (`SynHeroBannerSchema` — auto del CMS):
  - `title`: string
  - `subtitle`: string
  - `media`: string
  - `ctaLabel`: string
  - `ctaLink`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `title` (string) — Título mostrado destacado
  - `subtitle` (string) — Texto secundario debajo del título
  - `media` (string) — Campo "Media" del componente synergos-hero-banner. Editor: editar manualmente para enriquecer documentación.
  - `ctaLabel` (string) — Texto del call-to-action (botón)
  - `ctaLink` (string) — Campo "Cta Link" del componente synergos-hero-banner. Editor: editar manualmente para enriquecer documentación.
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-insight-explorer>` — experienceInsightExplorer

- **tag**: `<synergos-insight-explorer>`
- **alias CMS**: `experienceInsightExplorer`
- **tier**: module
- **frameworks**: angular
- **shape rich** (`InsightExplorerElementConfig` — manual canónico):
  - `title`: string
  - `theme`: string
  - `variant`: string
  - `tone`: string
  - `elementId`: string
  - `translations`: ComponentTranslations
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base experience configuration object from CMS.
  - `title` (string) — Experience title.
  - `theme` (string) — Theme key.
  - `variant` (string) — Variant key.
  - `elementId` (string) — DOM element id.
  - `items` (json) — JSON array of insight items.

### `<synergos-kpi-card>` — elementSynKpiCard

- **tag**: `<synergos-kpi-card>`
- **alias CMS**: `elementSynKpiCard`
- **tier**: module
- **frameworks**: angular
- **shape schema** (`SynKpiCardSchema` — auto del CMS):
  - `kpiLabel`: string
  - `kpiValue`: string
  - `kpiTrend`: string
  - `kpiDelta`: string
  - `kpiPeriod`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `kpiLabel` (string) — Texto visible del elemento (botón, input, badge, etc.) (campo "Kpi Label" del componente synergos-kpi-card)
  - `kpiValue` (string) — Campo "Kpi Value" del componente synergos-kpi-card. Editor: editar manualmente para enriquecer documentación.
  - `kpiTrend` (string) — Campo "Kpi Trend" del componente synergos-kpi-card. Editor: editar manualmente para enriquecer documentación.
  - `kpiDelta` (string) — Campo "Kpi Delta" del componente synergos-kpi-card. Editor: editar manualmente para enriquecer documentación.
  - `kpiPeriod` (string) — Campo "Kpi Period" del componente synergos-kpi-card. Editor: editar manualmente para enriquecer documentación.
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-lightbox-gallery>` — elementSynLightboxGallery

- **tag**: `<synergos-lightbox-gallery>`
- **alias CMS**: `elementSynLightboxGallery`
- **tier**: module
- **frameworks**: angular
- **shape schema** (`SynLightboxGallerySchema` — auto del CMS):
  - `imagesJson`: string
  - `columns`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `imagesJson` (string) — Campo "Images Json" del componente synergos-lightbox-gallery. Editor: editar manualmente para enriquecer documentación.
  - `columns` (string) — Campo "Columns" del componente synergos-lightbox-gallery. Editor: editar manualmente para enriquecer documentación.
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-livestream>` — elementSynLivestream

- **tag**: `<synergos-livestream>`
- **alias CMS**: `elementSynLivestream`
- **tier**: module
- **frameworks**: angular
- **shape schema** (`SynLivestreamSchema` — auto del CMS):
  - `streamUrl`: string
  - `streamType`: string
  - `viewerCountEndpoint`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `streamUrl` (string) — Campo "Stream Url" del componente synergos-livestream. Editor: editar manualmente para enriquecer documentación.
  - `streamType` (string) — Campo "Stream Type" del componente synergos-livestream. Editor: editar manualmente para enriquecer documentación.
  - `viewerCountEndpoint` (string) — Campo "Viewer Count Endpoint" del componente synergos-livestream. Editor: editar manualmente para enriquecer documentación.
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-map-pin>` — elementSynMapPin

- **tag**: `<synergos-map-pin>`
- **alias CMS**: `elementSynMapPin`
- **tier**: module
- **frameworks**: angular
- **shape schema** (`SynMapPinSchema` — auto del CMS):
  - `centerLat`: string
  - `centerLng`: string
  - `zoomLevel`: string
  - `pinsJson`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `centerLat` (string) — Campo "Center Lat" del componente synergos-map-pin. Editor: editar manualmente para enriquecer documentación.
  - `centerLng` (string) — Campo "Center Lng" del componente synergos-map-pin. Editor: editar manualmente para enriquecer documentación.
  - `zoomLevel` (string) — Campo "Zoom Level" del componente synergos-map-pin. Editor: editar manualmente para enriquecer documentación.
  - `pinsJson` (string) — Campo "Pins Json" del componente synergos-map-pin. Editor: editar manualmente para enriquecer documentación.
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-media-explorer>` — experienceMediaExplorer

- **tag**: `<synergos-media-explorer>`
- **alias CMS**: `experienceMediaExplorer`
- **tier**: module
- **frameworks**: angular
- **shape rich** (`MediaExplorerElementConfig` — manual canónico):
  - `title`: string
  - `theme`: string
  - `variant`: string
  - `tone`: string
  - `elementId`: string
  - `defaultCategory`: string
  - `translations`: ComponentTranslations
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base experience configuration object from CMS.
  - `title` (string) — Experience title.
  - `theme` (string) — Theme key.
  - `variant` (string) — Variant key.
  - `elementId` (string) — DOM element id.
  - `defaultCategory` (string) — Initial category filter.
  - `items` (json) — JSON array of media items.

### `<synergos-notification-center>` — elementSynNotificationCenter

- **tag**: `<synergos-notification-center>`
- **alias CMS**: `elementSynNotificationCenter`
- **tier**: module
- **frameworks**: angular
- **shape schema** (`SynNotificationCenterSchema` — auto del CMS):
  - `fetchEndpoint`: string
  - `pollingInterval`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `fetchEndpoint` (string) — Campo "Fetch Endpoint" del componente synergos-notification-center. Editor: editar manualmente para enriquecer documentación.
  - `pollingInterval` (string) — Campo "Polling Interval" del componente synergos-notification-center. Editor: editar manualmente para enriquecer documentación.
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-notification-toast>` — elementSynNotificationToast

- **tag**: `<synergos-notification-toast>`
- **alias CMS**: `elementSynNotificationToast`
- **tier**: module
- **frameworks**: angular
- **shape schema** (`SynNotificationToastSchema` — auto del CMS):
  - `message`: string
  - `type`: string
  - `durationMs`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `message` (string) — Campo "Message" del componente synergos-notification-toast. Editor: editar manualmente para enriquecer documentación.
  - `type` (string) — Campo "Type" del componente synergos-notification-toast. Editor: editar manualmente para enriquecer documentación.
  - `durationMs` (string) — Campo "Duration Ms" del componente synergos-notification-toast. Editor: editar manualmente para enriquecer documentación.
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-oembed>` — elementSynOEmbed

- **tag**: `<synergos-oembed>`
- **alias CMS**: `elementSynOEmbed`
- **tier**: module
- **frameworks**: angular
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `embedUrl` (string) — Campo "Embed Url" del componente synergos-oembed. Editor: editar manualmente para enriquecer documentación.
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-poll>` — elementSynPoll

- **tag**: `<synergos-poll>`
- **alias CMS**: `elementSynPoll`
- **tier**: module
- **frameworks**: angular
- **shape schema** (`SynPollSchema` — auto del CMS):
  - `question`: string
  - `optionsJson`: string
  - `voteEndpoint`: string
  - `resultsEndpoint`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `question` (string) — Campo "Question" del componente synergos-poll. Editor: editar manualmente para enriquecer documentación.
  - `optionsJson` (string) — Campo "Options Json" del componente synergos-poll. Editor: editar manualmente para enriquecer documentación.
  - `voteEndpoint` (string) — Campo "Vote Endpoint" del componente synergos-poll. Editor: editar manualmente para enriquecer documentación.
  - `resultsEndpoint` (string) — Campo "Results Endpoint" del componente synergos-poll. Editor: editar manualmente para enriquecer documentación.
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-product-detail>` — elementShopProductDetail

- **tag**: `<synergos-product-detail>`
- **alias CMS**: `elementShopProductDetail`
- **tier**: module
- **frameworks**: angular
- **shape rich** (`ProductDetailElementConfig` — manual canónico):
  - `productSku`: string
  - `showVariantPicker`: boolean
  - `showQuantitySelector`: boolean
  - `showRating`: boolean
  - `showReviews`: boolean
  - `showRelated`: boolean
  - `layout`: 'imageLeft' | 'imageRight' | 'imageTop'
  - `theme`: string
  - `variant`: string
  - `variantKey`: string
  - `translations`: ComponentTranslations
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Product detail configuration from CMS contract bridge.
  - `productSku` (string) — Product SKU to fetch.
  - `showVariantPicker` (boolean) — Shows variant picker block.
  - `showQuantitySelector` (boolean) — Shows quantity selector block.
  - `showRating` (boolean) — Shows rating summary block.
  - `showReviews` (boolean) — CMS compatibility flag for reviews placeholder rendering.
  - `showRelated` (boolean) — CMS compatibility flag for related-products placeholder rendering.
  - `layout` (string) — Layout mode (imageLeft | imageRight | imageTop).
  - `theme` (string) — Color theme key.
  - `variant` (string) — Visual variant key.
  - `variantKey` (string) — CMS compatibility alias for the visual variant key.

### `<synergos-product-grid>` — elementShopProductGrid

- **tag**: `<synergos-product-grid>`
- **alias CMS**: `elementShopProductGrid`
- **tier**: module
- **frameworks**: angular
- **shape rich** (`ProductGridElementConfig` — manual canónico):
  - `headingText`: string
  - `categoryAlias`: string
  - `categoryFilter`: string
  - `productUrlTemplate`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Product grid configuration from CMS contract bridge.
  - `headingText` (string) — Optional grid heading.
  - `categoryAlias` (string) — Category alias used for product API filtering.
  - `categoryFilter` (string) — CMS compatibility alias for category-based filtering.
  - `productUrlTemplate` (string) — Product detail URL template with placeholders ({id}, {sku}, {slug}).
  - `maxItems` (number) — Maximum items per page.
  - `columns` (number) — Preferred grid columns.
  - `showFilters` (boolean) — Enables search/sort controls.
  - `sortOrder` (string) — Initial sort key.
  - `sortBy` (string) — CMS compatibility alias for initial sort mode.
  - `layout` (string) — CMS compatibility layout hint emitted by current Web partials.
  - `theme` (string) — Color theme key.
  - `variant` (string) — Visual variant key.
  - `variantKey` (string) — CMS compatibility alias for the visual variant key.

### `<synergos-quote-animated>` — elementSynQuoteAnimated

- **tag**: `<synergos-quote-animated>`
- **alias CMS**: `elementSynQuoteAnimated`
- **tier**: module
- **frameworks**: angular
- **shape schema** (`SynQuoteAnimatedSchema` — auto del CMS):
  - `quote`: string
  - `attribution`: string
  - `animationMode`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `quote` (string) — Campo "Quote" del componente synergos-quote-animated. Editor: editar manualmente para enriquecer documentación.
  - `attribution` (string) — Campo "Attribution" del componente synergos-quote-animated. Editor: editar manualmente para enriquecer documentación.
  - `animationMode` (string) — Campo "Animation Mode" del componente synergos-quote-animated. Editor: editar manualmente para enriquecer documentación.
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-realty>` — elementSynRealty

- **tag**: `<synergos-realty>`
- **alias CMS**: `elementSynRealty`
- **tier**: module
- **frameworks**: angular
- **shape schema** (`SynRealtySchema` — auto del CMS):
  - `heading`: string
  - `subheading`: string
  - `apiBase`: string
  - `config`: string
  - `content`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Runtime configuration object; merged over the individual attributes for realty.
  - `apiBase` (string) — Base URL of the backing API. Defaults to the module route when unset.
  - `currency` (string) — ISO 4217 currency code used to format amounts.
  - `scope` (string) — Content scope (siteRoot) the module reads from.
  - `role` (string) — Viewer role; drives which actions and sections are offered.
  - `operation` (string) — Listing operation to show: sale or rent.
  - `layout` (string) — Layout variant used to present the listings.
  - `defaultRate` (number) — Interest rate prefilled in the mortgage estimator.

### `<synergos-seller>` — elementSynSeller

- **tag**: `<synergos-seller>`
- **alias CMS**: `elementSynSeller`
- **tier**: module
- **frameworks**: angular
- **shape schema** (`SynSellerSchema` — auto del CMS):
  - `heading`: string
  - `subheading`: string
  - `apiBase`: string
  - `config`: string
  - `content`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Runtime configuration object; merged over the individual attributes for seller.
  - `apiBase` (string) — Base URL of the backing API. Defaults to the module route when unset.
  - `sellerName` (string) — Seller display name shown in the console header.
  - `heading` (string) — Main heading rendered above the module.
  - `currency` (string) — ISO 4217 currency code used to format amounts.

### `<synergos-storefront>` — elementSynStorefront

- **tag**: `<synergos-storefront>`
- **alias CMS**: `elementSynStorefront`
- **tier**: module
- **frameworks**: angular
- **shape schema** (`SynStorefrontSchema` — auto del CMS):
  - `heading`: string
  - `subheading`: string
  - `apiBase`: string
  - `config`: string
  - `content`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Runtime configuration object; merged over the individual attributes for storefront.
  - `apiBase` (string) — Base URL of the backing API. Defaults to the module route when unset.
  - `currency` (string) — ISO 4217 currency code used to format amounts.
  - `scope` (string) — Content scope (siteRoot) the module reads from.

### `<synergos-tab-group>` — elementCorpTabGroup

- **tag**: `<synergos-tab-group>`
- **alias CMS**: `elementCorpTabGroup`
- **tier**: module
- **frameworks**: angular
- **shape rich** (`TabGroupElementConfig` — manual canónico):
  - `title`: string
  - `activeId`: string
  - `ariaLabel`: string
  - `tabs`: ReadonlyArray<{
    readonly id?: string
  - `label`: string
  - `content`: string
  - `disabled`: boolean
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object for the tab group.
  - `title` (string) — Optional group heading.
  - `tabs` (json) — JSON array of tabs with id, label, content and disabled.
  - `activeId` (string) — Initially selected tab id.
  - `ariaLabel` (string) — Accessible label for the tablist.
  - `variant` (string) — Presentation variant key.
  - `theme` (string) — Color theme key.

### `<synergos-testimonial-carousel>` — elementSynTestimonialCarousel

- **tag**: `<synergos-testimonial-carousel>`
- **alias CMS**: `elementSynTestimonialCarousel`
- **tier**: module
- **frameworks**: angular
- **shape schema** (`SynTestimonialCarouselSchema` — auto del CMS):
  - `testimonialsJson`: string
  - `autoplayInterval`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `testimonialsJson` (string) — Campo "Testimonials Json" del componente synergos-testimonial-carousel. Editor: editar manualmente para enriquecer documentación.
  - `autoplayInterval` (string) — Campo "Autoplay Interval" del componente synergos-testimonial-carousel. Editor: editar manualmente para enriquecer documentación.
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-testimonial-section>` — elementSynTestimonialSection

- **tag**: `<synergos-testimonial-section>`
- **alias CMS**: `elementSynTestimonialSection`
- **tier**: module
- **frameworks**: angular
- **shape rich** (`TestimonialSectionElementConfig` — manual canónico):
  - `headingText`: string
  - `theme`: string
  - `items`: ReadonlyArray<TestimonialSectionItemConfig>
  - `translations`: ComponentTranslations
- **shape schema** (`SynTestimonialSectionSchema` — auto del CMS):
  - `headingText`: string
  - `itemsJson`: string
  - `theme`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `headingText` (string) — Section heading text
  - `items` (json) — JSON array of testimonial items with name, quote, role and avatarSrc
  - `theme` (string) — Color theme (light | dark)

### `<synergos-timeline>` — elementSynTimeline

- **tag**: `<synergos-timeline>`
- **alias CMS**: `elementSynTimeline`
- **tier**: module
- **frameworks**: angular
- **shape schema** (`SynTimelineSchema` — auto del CMS):
  - `eventsJson`: string
  - `orientation`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `eventsJson` (string) — Campo "Events Json" del componente synergos-timeline. Editor: editar manualmente para enriquecer documentación.
  - `orientation` (string) — Campo "Orientation" del componente synergos-timeline. Editor: editar manualmente para enriquecer documentación.
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-toast-center>` — elementSynToastCenter

- **tag**: `<synergos-toast-center>`
- **alias CMS**: `elementSynToastCenter`
- **tier**: module
- **frameworks**: angular
- **shape schema** (`SynToastCenterSchema` — auto del CMS):
  - `position`: string
  - `maxVisible`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `position` (string) — Campo "Position" del componente synergos-toast-center. Editor: editar manualmente para enriquecer documentación.
  - `maxVisible` (string) — Campo "Max Visible" del componente synergos-toast-center. Editor: editar manualmente para enriquecer documentación.
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-tour-guide>` — elementSynTourGuide

- **tag**: `<synergos-tour-guide>`
- **alias CMS**: `elementSynTourGuide`
- **tier**: module
- **frameworks**: angular
- **shape schema** (`SynTourGuideSchema` — auto del CMS):
  - `stepsJson`: string
  - `autoStart`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `stepsJson` (string) — Campo "Steps Json" del componente synergos-tour-guide. Editor: editar manualmente para enriquecer documentación.
  - `autoStart` (string) — Campo "Auto Start" del componente synergos-tour-guide. Editor: editar manualmente para enriquecer documentación.
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-travel-shell>` — elementSynTravelShell

- **tag**: `<synergos-travel-shell>`
- **alias CMS**: `elementSynTravelShell`
- **tier**: module
- **frameworks**: angular
- **shape schema** (`SynTravelShellSchema` — auto del CMS):
  - `heading`: string
  - `subheading`: string
  - `apiBase`: string
  - `config`: string
  - `content`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Runtime configuration object; merged over the individual attributes for travel-shell.
  - `apiBase` (string) — Base URL of the backing API. Defaults to the module route when unset.
  - `currency` (string) — ISO 4217 currency code used to format amounts.
  - `scope` (string) — Content scope (siteRoot) the module reads from.
  - `traveler` (string) — Traveller identifier whose bookings are listed.

### `<synergos-tree-view>` — elementSynTreeView

- **tag**: `<synergos-tree-view>`
- **alias CMS**: `elementSynTreeView`
- **tier**: module
- **frameworks**: angular
- **shape schema** (`SynTreeViewSchema` — auto del CMS):
  - `treeJson`: string
  - `expandAll`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `treeJson` (string) — Campo "Tree Json" del componente synergos-tree-view. Editor: editar manualmente para enriquecer documentación.
  - `expandAll` (string) — Campo "Expand All" del componente synergos-tree-view. Editor: editar manualmente para enriquecer documentación.
  - `integration` (string) — Hook opcional para integración custom — vacío por default

### `<synergos-video-player>` — elementSynVideoPlayer

- **tag**: `<synergos-video-player>`
- **alias CMS**: `elementSynVideoPlayer`
- **tier**: module
- **frameworks**: angular
- **shape schema** (`SynVideoPlayerSchema` — auto del CMS):
  - `videoFile`: string
  - `posterImage`: string
  - `chaptersJson`: string
  - `enableAnalytics`: string
  - `integration`: string
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `videoFile` (string) — Campo "Video File" del componente synergos-video-player. Editor: editar manualmente para enriquecer documentación.
  - `posterImage` (string) — Campo "Poster Image" del componente synergos-video-player. Editor: editar manualmente para enriquecer documentación.
  - `chaptersJson` (string) — Campo "Chapters Json" del componente synergos-video-player. Editor: editar manualmente para enriquecer documentación.
  - `enableAnalytics` (string) — Campo "Enable Analytics" del componente synergos-video-player. Editor: editar manualmente para enriquecer documentación.
  - `integration` (string) — Hook opcional para integración custom — vacío por default


## Cómo se consume desde el CMS Razor

Cuando un ContentType (e.g. `elementSynHero`) renderiza, el partial Razor en
`Views/Partials/SynHost/{Block}.cshtml` invoca `ISynHostEmitter.EmitAsync` que:

1. Resuelve el bundle vía `IBundleRegistryClient` (default `FileSystemBundleRegistryClient`
   leyendo `C:\LOCAL_CDN\synergos\registry.json`).
2. Emite `<script type="module" defer src="/cdn-bundles/{name}/{framework}/{slot}/main.js"
   integrity="sha384-..." crossorigin="anonymous"></script>`.
3. Emite `<synergos-{name} config='{...JSON con culture+props+overrides}'></synergos-{name}>`.
4. Si el registry no resuelve (CDN offline), emite el offline fallback con
   `data-synergos-cdn-offline="true"` + skeleton shimmer (cap-310 default CSS).

## Edit policy

- **Rich shape (`element-config.contract.ts`)**: editar a mano. Es el contract
  canónico para los Web Components que evolucionaron a tener config editorial
  rico (translations, semantic fields, etc.). 64 elements actualmente.
- **Schema mirror (`elements-syn.contract.ts`)**: NO editar. Auto-regenerado
  por `tools/cms-sync.mjs` cada vez que cambia el schema uSync del CMS. 71
  interfaces `Syn{Pascal}Schema`.
- **Inputs JSON (`element-inputs.json`)**: editar manualmente para enriquecer
  las declaraciones públicas de cada Custom Element (default values, descriptions
  para editor docs). Es leído por el audit `element-contract-audit.mjs`.
- **Este catálogo**: NO editar — auto-regenerado.
