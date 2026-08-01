# UI Elements Catalog — 122 bundles publicados al CDN

> **AUTO-GENERATED** by `tools/refresh-skill-catalog.mjs`. Re-run via `npm run skill:refresh`
> o automáticamente al final de `npm run release:angular`. Edits manuales se pierden.
>
> Snapshot del CDN registry (`C:\LOCAL_CDN\synergos\registry.json`) + UI contracts
> (`vitals/contracts/src/{element-config,elements-syn,element-inputs}`).
>
> Generated: 2026-05-04T11:15:57.586Z

## Cómo leer este catálogo

Cada elemento listado tiene:
- **`tag`**: el custom element DOM name que el SSR Razor del CMS emite
  (`<synergos-{kebab}>`).
- **`alias`**: el alias CMS uSync (`elementSyn{Pascal}`) que aparece en los
  ContentTypes XMLs de `Synergos.CMS.Web/uSync/v9/ContentTypes/`.
- **`framework`(s)**: el(los) framework(s) en los que el bundle está publicado
  (angular | react | svelte | vanilla). Hoy todos son angular.
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


## Primitives (28)

**Primitives** — atómicos, sin lógica de negocio. Building blocks reutilizables (avatar, badge, divider, etc.). Pueden vivir solos o composarse.

### `<synergos-avatar>` — elementMediaAvatar

- **tag**: `<synergos-avatar>`
- **alias CMS**: `elementMediaAvatar`
- **tier**: primitive
- **frameworks**: svelte, angular
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

### `<synergos-badge>` — elementInfoBadge

- **tag**: `<synergos-badge>`
- **alias CMS**: `elementInfoBadge`
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
  - `itemsJson` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `includeStructuredData` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

### `<synergos-column>` — elementStructColumn

- **tag**: `<synergos-column>`
- **alias CMS**: `elementStructColumn`
- **tier**: primitive
- **frameworks**: angular
- **shape rich** (`ColumnElementConfig` — manual canónico):
  - `width`: string
  - `minWidth`: string
  - `alignment`: string
  - `padding`: string
  - `gap`: string
  - `variant`: string
  - `tone`: string
  - `theme`: string
  - `translations`: ComponentTranslations
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `width` (string) — Optional explicit width value
  - `minWidth` (string) — Optional explicit minimum width value
  - `alignment` (string) — Column align-self value
  - `padding` (string) — Inner spacing token
  - `gap` (string) — Gap between column children
  - `theme` (string) — Color theme (light | dark)

### `<synergos-container-block>` — elementStructContainer

- **tag**: `<synergos-container-block>`
- **alias CMS**: `elementStructContainer`
- **tier**: primitive
- **frameworks**: angular
- **shape rich** (`ContainerBlockElementConfig` — manual canónico):
  - `elementId`: string
  - `ariaLabel`: string
  - `containerType`: string
  - `maxWidth`: string
  - `padding`: string
  - `variant`: string
  - `tone`: string
  - `theme`: string
  - `translations`: ComponentTranslations
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `containerType` (string) — Container width mode
  - `maxWidth` (string) — Optional explicit max-width value
  - `padding` (string) — Inner padding token
  - `theme` (string) — Color theme (light | dark)
  - `elementId` (string) — DOM id from CMS config.
  - `ariaLabel` (string) — ARIA label from CMS config.
  - `variant` (string) — Visual variant key from CMS config.

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
  - `copyText` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `buttonLabel` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `feedbackLabel` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

### `<synergos-divider>` — elementStructDivider

- **tag**: `<synergos-divider>`
- **alias CMS**: `elementStructDivider`
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
  - `iconKey` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `actionLink` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `position` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

### `<synergos-grid>` — elementStructGrid

- **tag**: `<synergos-grid>`
- **alias CMS**: `elementStructGrid`
- **tier**: primitive
- **frameworks**: angular
- **shape rich** (`GridElementConfig` — manual canónico):
  - `columns`: number
  - `gap`: string
  - `minColumnWidth`: string
  - `variant`: string
  - `tone`: string
  - `theme`: string
  - `translations`: ComponentTranslations
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `columns` (number) — Preferred number of columns
  - `gap` (string) — Gap size token (sm | md | lg)
  - `minColumnWidth` (string) — Optional auto-fit minimum column width
  - `theme` (string) — Color theme (light | dark)

### `<synergos-hello-world>` — elementTemplateHelloWorld

- **tag**: `<synergos-hello-world>`
- **alias CMS**: `elementTemplateHelloWorld`
- **tier**: primitive
- **frameworks**: vanilla
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `heading` (string) — Main heading text
  - `message` (string) — Text to display
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
  - `ariaHidden` (boolean) — Marks the icon as decorative when true.

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
  - `iconKey` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `labelText` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

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
  - `triggerLabel` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `popoverContent` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `placement` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

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
  - `valueNow` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `valueMax` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

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
  - `data` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `size` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `ecLevel` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

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
  - `scrollThreshold` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `position` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

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
  - `style` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

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
  - `shape` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `count` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

### `<synergos-spacer>` — elementStructSpacer

- **tag**: `<synergos-spacer>`
- **alias CMS**: `elementStructSpacer`
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

### `<synergos-stack>` — elementStructStack

- **tag**: `<synergos-stack>`
- **alias CMS**: `elementStructStack`
- **tier**: primitive
- **frameworks**: angular
- **shape rich** (`StackElementConfig` — manual canónico):
  - `direction`: string
  - `gap`: string
  - `alignment`: string
  - `justify`: string
  - `wrap`: boolean
  - `variant`: string
  - `tone`: string
  - `theme`: string
  - `translations`: ComponentTranslations
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `direction` (string) — Layout direction
  - `gap` (string) — Gap size token
  - `alignment` (string) — Cross-axis alignment
  - `wrap` (boolean) — Allows wrapping onto multiple rows
  - `theme` (string) — Color theme (light | dark)
  - `justify` (string) — Justify-content override for stacked layouts.

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
  - `statValue` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `statLabel` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `statPrefix` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `statSuffix` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

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
  - `tagLabel` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `tagColor` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

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
  - `triggerText` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `tooltipText` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

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

### `<synergos-accordion>` — elementCompAccordion

- **tag**: `<synergos-accordion>`
- **alias CMS**: `elementCompAccordion`
- **tier**: composition
- **frameworks**: svelte, angular
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
  - `label` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `placeholder` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `suggestionsEndpoint` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

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
  - `avatarsJson` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `maxVisible` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

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
  - `label` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `uploadEndpoint` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

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
  - `badgesJson` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `layout` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

### `<synergos-button-group>` — elementActionButtonGroup

- **tag**: `<synergos-button-group>`
- **alias CMS**: `elementActionButtonGroup`
- **tier**: composition
- **frameworks**: angular
- **shape rich** (`ButtonGroupElementConfig` — manual canónico):
  - `alignment`: 'left' | 'center' | 'right'
  - `direction`: 'row' | 'column'
  - `gap`: 'xs' | 'sm' | 'md' | 'lg'
  - `items`: ReadonlyArray<ButtonGroupItemConfig>
  - `translations`: ComponentTranslations
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
  - `code` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `language` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `showLineNumbers` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

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
  - `label` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `initialColor` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `paletteJson` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

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
  - `swatchesJson` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `shape` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

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
  - `label` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `initialDate` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `minDate` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `maxDate` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

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
  - `triggerLabel` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `optionsJson` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `selectedValue` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `searchable` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

### `<synergos-external-widget>` — elementIntExternalWidget

- **tag**: `<synergos-external-widget>`
- **alias CMS**: `elementIntExternalWidget`
- **tier**: composition
- **frameworks**: angular
- **shape rich** (`ExternalWidgetElementConfig` — manual canónico):
  - `src`: string
  - `type`: string
  - `title`: string
  - `endpoint`: string
  - `translations`: ComponentTranslations
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object for the external widget.
  - `tagName` (string) — Host tag name created for the widget.
  - `scriptSrc` (string) — External script source URL.
  - `props` (json) — JSON object mapped to attributes on the widget host.
  - `textContent` (string) — Optional text content for the widget host.
  - `src` (string) — Canonical CMS widget script URL.
  - `type` (string) — Canonical CMS widget tag or type.
  - `title` (string) — Canonical CMS widget title payload.
  - `endpoint` (string) — Canonical CMS endpoint passed to the widget.

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
  - `stepsJson` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `submitEndpoint` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `allowSkip` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

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

### `<synergos-iframe-embed>` — elementIntIframeEmbed

- **tag**: `<synergos-iframe-embed>`
- **alias CMS**: `elementIntIframeEmbed`
- **tier**: composition
- **frameworks**: angular
- **shape rich** (`IframeEmbedElementConfig` — manual canónico):
  - `src`: string
  - `title`: string
  - `height`: string
  - `allowFullscreen`: boolean
  - `translations`: ComponentTranslations
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object for the iframe embed.
  - `src` (string) — Iframe source URL.
  - `title` (string) — Iframe title attribute.
  - `loading` (string) — Native iframe loading mode.
  - `allowFullscreen` (boolean) — Enables the allowfullscreen attribute.
  - `height` (string) — Iframe height value.

### `<synergos-info-block>` — elementCompInfoBlock

- **tag**: `<synergos-info-block>`
- **alias CMS**: `elementCompInfoBlock`
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

### `<synergos-media-text>` — elementCompMediaTextSplit

- **tag**: `<synergos-media-text>`
- **alias CMS**: `elementCompMediaTextSplit`
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
  - `triggerLabel` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `modalTitle` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `modalContent` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `modalSize` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

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
  - `label` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `length` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

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
  - `totalItems` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `itemsPerPage` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `currentPage` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `urlTemplate` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

### `<synergos-pricing-card>` — elementInfoPricingCard

- **tag**: `<synergos-pricing-card>`
- **alias CMS**: `elementInfoPricingCard`
- **tier**: composition
- **frameworks**: react
- **shape rich** (`PricingCardElementConfig` — manual canónico):
  - `title`: string
  - `price`: string
  - `period`: string
  - `description`: string
  - `ctaLabel`: string
  - `ctaUrl`: string
  - `badgeText`: string
  - `badgeTone`: string
  - `variant`: string
  - `tone`: string
  - `theme`: string
  - `featured`: boolean
  - `translations`: ComponentTranslations
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `planName` (string) — Plan or tier name
  - `price` (string) — Price label
  - `period` (string) — Billing period label
  - `features` (json) — JSON array of included feature strings
  - `ctaLabel` (string) — Call-to-action label
  - `ctaUrl` (string) — Call-to-action destination URL
  - `highlighted` (boolean) — Highlights the card visually
  - `variant` (string) — Presentation variant key
  - `theme` (string) — Color theme (light | dark)

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
  - `label` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `minValue` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `maxValue` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `step` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `initialValue` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

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
  - `valueNow` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `maxStars` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

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
  - `triggerText` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `tooltipContent` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `placement` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

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
  - `searchPlaceholder` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `searchEndpoint` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `searchParamName` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

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
  - `label` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `optionsJson` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `maxSelections` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

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
  - `platforms` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `shareLink` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `shareTitle` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

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
  - `label` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `width` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `height` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

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
  - `template` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `dataSource` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `rotationInterval` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

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
  - `leftContent` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `rightContent` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `orientation` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

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
  - `stepsJson` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `currentStep` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

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
  - `tabsJson` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `initialTab` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

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
  - `eventsJson` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `snapEnabled` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

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


## Modules (48)

**Modules** — features ricas con state propio + posiblemente fetch (carousel, hero, comments-widget, etc.). Self-contained but heavier.

### `<synergos-angular-host>` — elementIntAngularHost

- **tag**: `<synergos-angular-host>`
- **alias CMS**: `elementIntAngularHost`
- **tier**: module
- **frameworks**: angular
- **shape rich** (`AngularHostElementConfig` — manual canónico):
  - `component`: string
  - `endpoint`: string
  - `params`: Record<string, string>
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object for the Angular host.
  - `component` (string) — Catalog alias or custom element tag to mount. Preferred over tagName.
  - `endpoint` (string) — Optional API endpoint injected into the hosted element props.
  - `params` (json) — JSON object of lightweight params merged into hosted element props.
  - `scriptSrc` (string) — Optional script URL to load before mounting the hosted element.
  - `tagName` (string) — Legacy explicit custom element tag to render.
  - `props` (json) — JSON object mapped to attributes on the hosted element.
  - `textContent` (string) — Optional text content for the hosted element.

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
  - `audioFile` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `trackTitle` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `artistName` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

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
  - `eventsEndpoint` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `initialMonth` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

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
  - `slidesJson` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `autoplayInterval` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

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
  - `chartTitle` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `dataJson` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `orientation` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

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
  - `provider` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `threadId` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `configNote` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

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
  - `bannerText` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `acceptLabel` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `rejectLabel` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `settingsLabel` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `policyLink` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

### `<synergos-countdown-clock>` — experienceCountdownClock

- **tag**: `<synergos-countdown-clock>`
- **alias CMS**: `experienceCountdownClock`
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
  - `endDateTime` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `showLabels` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `style` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

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
  - `dataSource` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `columnsJson` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `pageSize` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

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
  - `triggerLabel` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `drawerContent` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `side` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

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
  - `label` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `acceptedTypes` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `uploadEndpoint` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

### `<synergos-faq-section>` — elementCompFaqList

- **tag**: `<synergos-faq-section>`
- **alias CMS**: `elementCompFaqList`
- **tier**: module
- **frameworks**: angular
- **shape rich** (`FaqSectionElementConfig` — manual canónico):
  - `headingText`: string
  - `theme`: string
  - `items`: ReadonlyArray<FaqSectionItemConfig>
  - `translations`: ComponentTranslations
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `headingText` (string) — Section heading text
  - `items` (json) — JSON array of FAQ items with question, answer and optional initiallyExpanded
  - `theme` (string) — Color theme (light | dark)

### `<synergos-feature-grid>` — elementCompFeatureGrid

- **tag**: `<synergos-feature-grid>`
- **alias CMS**: `elementCompFeatureGrid`
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
  - `uploadEndpoint` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `acceptedTypes` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `maxFileSizeMb` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `maxFiles` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `label` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

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
  - `title` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `subtitle` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `media` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `ctaLabel` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `ctaLink` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

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
  - `kpiLabel` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `kpiValue` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `kpiTrend` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `kpiDelta` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `kpiPeriod` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

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
  - `imagesJson` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `columns` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

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
  - `streamUrl` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `streamType` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `viewerCountEndpoint` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

### `<synergos-logo-cloud>` — elementCompLogoCloud

- **tag**: `<synergos-logo-cloud>`
- **alias CMS**: `elementCompLogoCloud`
- **tier**: module
- **frameworks**: angular
- **shape rich** (`LogoCloudElementConfig` — manual canónico):
  - `headingText`: string
  - `body`: string
  - `columns`: number
  - `variant`: string
  - `tone`: string
  - `theme`: string
  - `items`: ReadonlyArray<LogoCloudItemConfig>
  - `translations`: ComponentTranslations
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object for the logo cloud.
  - `headingText` (string) — Section heading text.
  - `body` (string) — Supporting introductory copy.
  - `items` (json) — JSON array of logo items with src, alt, href and label.
  - `columns` (number) — Preferred number of columns.
  - `variant` (string) — Presentation variant key.
  - `theme` (string) — Color theme key.

### `<synergos-macro-host>` — elementIntegrationMacroHost

- **tag**: `<synergos-macro-host>`
- **alias CMS**: `elementIntegrationMacroHost`
- **tier**: module
- **frameworks**: angular
- **shape rich** (`MacroHostElementConfig` — manual canónico):
  - `contentType`: string
  - `contentData`: Record<string, unknown>
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `contentType` (string) — Nested element alias to mount inside the macro host
  - `contentData` (json) — Nested element payload passed to the block mapper

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
  - `centerLat` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `centerLng` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `zoomLevel` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `pinsJson` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

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

### `<synergos-mf-host>` — elementIntMfHost

- **tag**: `<synergos-mf-host>`
- **alias CMS**: `elementIntMfHost`
- **tier**: module
- **frameworks**: angular
- **shape rich** (`MfHostElementConfig` — manual canónico):
  - `exposedModule`: string
  - `endpoint`: string
  - `remoteEntry`: string
  - `params`: Record<string, string>
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object for the remote host.
  - `component` (string) — Remote exposed module name, catalog alias or custom element tag to mount.
  - `endpoint` (string) — Optional API endpoint injected into the hosted remote element props.
  - `params` (json) — JSON object of lightweight params merged into hosted remote element props.
  - `scriptSrc` (string) — Preferred script URL to load before mounting.
  - `remoteEntry` (string) — Legacy remote entry script URL to load before mounting.
  - `exposedModule` (string) — Legacy exposed module name used as hosted component name.
  - `tagName` (string) — Legacy explicit custom element tag exposed by the remote bundle.
  - `props` (json) — JSON object mapped to attributes on the hosted remote element.

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
  - `fetchEndpoint` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `pollingInterval` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

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
  - `message` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `type` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `durationMs` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

### `<synergos-oembed>` — elementSynOEmbed

- **tag**: `<synergos-oembed>`
- **alias CMS**: `elementSynOEmbed`
- **tier**: module
- **frameworks**: angular
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `embedUrl` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

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
  - `question` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `optionsJson` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `voteEndpoint` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `resultsEndpoint` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

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
  - `quote` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `attribution` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `animationMode` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

### `<synergos-script-embed>` — elementIntScriptEmbed

- **tag**: `<synergos-script-embed>`
- **alias CMS**: `elementIntScriptEmbed`
- **tier**: module
- **frameworks**: angular
- **shape rich** (`ScriptEmbedElementConfig` — manual canónico):
  - `scriptType`: string
  - `content`: string
  - `translations`: ComponentTranslations
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object for the script embed.
  - `src` (string) — External script source URL.
  - `type` (string) — Script MIME type.
  - `inlineScript` (string) — Inline script body to inject.
  - `target` (string) — Document target for script injection (head | body).
  - `async` (boolean) — Sets the async script attribute.
  - `defer` (boolean) — Sets the defer script attribute.
  - `scriptType` (string) — Canonical CMS script type.
  - `content` (string) — Canonical CMS script payload or external URL.

### `<synergos-section>` — elementStructSection

- **tag**: `<synergos-section>`
- **alias CMS**: `elementStructSection`
- **tier**: module
- **frameworks**: angular
- **shape rich** (`SectionElementConfig` — manual canónico):
  - `headingText`: string
  - `headingLevel`: string
  - `containerType`: string
  - `alignment`: string
  - `direction`: string
  - `margin`: string
  - `padding`: string
  - `gap`: string
  - `variant`: string
  - `tone`: string
  - `theme`: string
  - `translations`: ComponentTranslations
- **inputs públicos** (HTML attributes, kebab-case en DOM):
  - `config` (json) — Base element configuration object. Prefer this payload for structural content; direct inputs act as state or override props.
  - `headingText` (string) — Section heading text
  - `headingLevel` (string) — HTML heading tag: h1-h6
  - `containerType` (string) — Section width container mode
  - `alignment` (string) — Cross-axis alignment for the inner layout
  - `direction` (string) — Flex direction for the inner layout
  - `margin` (string) — Optional CSS margin shorthand
  - `padding` (string) — Optional CSS padding shorthand
  - `gap` (string) — Gap between section children
  - `variant` (string) — Layout variant key
  - `theme` (string) — Color theme (light | dark)

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
  - `testimonialsJson` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `autoplayInterval` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

### `<synergos-testimonial-section>` — elementCompTestimonialList

- **tag**: `<synergos-testimonial-section>`
- **alias CMS**: `elementCompTestimonialList`
- **tier**: module
- **frameworks**: angular
- **shape rich** (`TestimonialSectionElementConfig` — manual canónico):
  - `headingText`: string
  - `theme`: string
  - `items`: ReadonlyArray<TestimonialSectionItemConfig>
  - `translations`: ComponentTranslations
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
  - `eventsJson` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `orientation` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

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
  - `position` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `maxVisible` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

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
  - `stepsJson` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `autoStart` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

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
  - `treeJson` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `expandAll` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.

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
  - `videoFile` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `posterImage` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `chaptersJson` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `enableAnalytics` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.
  - `integration` (string) — Auto-generated from CMS schema. Edit description manually for editor docs.


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
