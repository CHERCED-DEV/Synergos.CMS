# Content Model

Inventory of everything the schema pipeline creates. Use as a reference when
choosing compositions for a new type or locating existing functionality.

## Top-level map

```
Data Types        → reusable field editors (TextBox, Dropdown, BlockGrid, …)
Compositions     → reusable property groups (no template; IsElement=true)
Element Types    → Block Grid blocks (single-purpose, inside Block Grid)
Media Types      → Image, File, Folder, Video
Document Types   → renderable pages (templates + routing)
Macros           → inline rich-text embeds (Native + CDN families)
```

All GUIDs and aliases live in `Schema/Constants/`:
- `ContentTypeKeys.cs` — Document + Element types.
- `DataTypeKeys.cs` — Data types.
- `MediaTypeKeys.cs` — Media types.

## Document Types (renderable pages)

| Alias | Name | Template | Purpose | Owner |
|---|---|---|---|---|
| `platformRoot` | Platform Root | `PlatformRoot.cshtml` | Top-level container, allows SiteRoots + settings | `PlatformInitializer` |
| `siteRoot` | Site Root | `SiteRoot.cshtml` | One per "world" — owns its SiteSettings, Theme, pages | `DocumentTypeInitializer` |
| `pageBase` | Page Base | `PageBase.cshtml` | Generic page with Block Grid | `DocumentTypeInitializer` |
| `articlePage` | Article Page | `ArticlePage.cshtml` | Editorial long-form | `DocumentTypeInitializer` (legacy alias kept) |
| `globalSettings` | Global Settings | — | Platform-wide SEO + Scripts fallbacks | `SiteSettingsInitializer` |
| `siteSettings` | Site Settings | — | Per-site identity, contact, social, SEO, scripts, header, footer, alert bar, banners, forms | `SiteSettingsInitializer` |
| `themeSettings` | Theme Settings | — | Per-site brand (colors, fonts, logo, spacing, variants, custom CSS) | `SiteSettingsInitializer` |
| `layoutProfile` | Layout Profile | — | Reusable layout override (nav, flags) | `SiteSettingsInitializer` |
| `blogHome` | Blog Home | `BlogHome.cshtml` | Blog landing + pagination + config | `BlogInitializer` |
| `blogPost` | Blog Post | `BlogPost.cshtml` | Editorial blog article | `BlogInitializer` |
| `blogCategory` | Blog Category | `BlogCategory.cshtml` | Category index | `BlogInitializer` |
| `category` | Category | `category.cshtml` | Shared taxonomy node | `PlatformInitializer` |
| `author` | Author | `author.cshtml` | Blog author profile | `PlatformInitializer` |
| `pageTag` | Page Tag | `pageTag.cshtml` | Single tag taxonomy | `TaxonomyInitializer` |
| `pageTagsFolder` | Page Tags Folder | `pageTagsFolder.cshtml` | Tag container | `TaxonomyInitializer` |
| `sharedContentFolder` | Shared Content Folder | `sharedContentFolder.cshtml` | Container for NavigationGroup, ReusableBlock, Author, FormDefinition | `PlatformInitializer` |
| `navigationGroup` | Navigation Group | `navigationGroup.cshtml` | Menu with NavigationItem block list | `PlatformInitializer` |
| `reusableBlock` | Reusable Block | `reusableBlock.cshtml` | Picker target for shared content | `PlatformInitializer` |
| `formDefinition` | Form Definition | `formDefinition.cshtml` | Form schema + recipient + action | `PlatformInitializer` |
| `shopRoot` | Shop Root | `ShopRoot.cshtml` | E-commerce root | `ShopInitializer` |
| `shopCatalogPage` | Shop Catalog Page | `ShopCatalogPage.cshtml` | Product grid page | `ShopInitializer` |
| `shopCategoryPage` | Shop Category Page | `ShopCategoryPage.cshtml` | Category listing | `ShopInitializer` |
| `shopProductPage` | Shop Product Page | `ShopProductPage.cshtml` | PDP (product detail) | `ShopInitializer` |
| `shopCartPage` | Shop Cart Page | `ShopCartPage.cshtml` | Cart + checkout entry | `ShopInitializer` |
| `flowSettingsRoot` | Flow Settings Root | — | Container for FlowDefinition | `FlowEngineInitializer` |
| `flowDefinition` | Flow Definition | — | Flow config (alias, webhookTargetUrl, tracks, outcomes) | `FlowEngineInitializer` |

## Element Types (Block Grid blocks)

58+ element types across 10 families. Prefix convention: `element<Family>*`.

### Structural (`elementStruct*`) — 7
`Section`, `Container`, `Grid`, `Column`, `Stack`, `Spacer`, `Divider`.
Purpose: layout scaffolding. Contain areas for child blocks.

### Textual (`elementText*`) — 5
`Heading`, `Paragraph`, `RichText`, `Quote`, `CodeBlock`.

### Action (`elementAction*`) — 3
`CtaButton`, `CtaGroup`, `LinkBlock`.

### Media (`elementMedia*`) — 4
`Image`, `Video`, `Gallery`, `MediaText`.

### Informational (`elementInfo*`) — 6
`AlertBox`, `Stat`, `Badge`, `KeyValue`, `Timeline`, `FeatureJourney`.

### Composition (`elementComp*`) — ~10
`Card`, `Hero`, `Banner`, `FeatureItem`, `FeatureGrid`, `LogoItem`, `LogoCloud`, `FaqItem`, `FaqSection`, `TestimonialItem`, `TestimonialSection`, `SocialShare`, `InfoBlock`.

### Integration (`elementIntegration*`) — 4
`IframeEmbed`, `ScriptEmbed`, `ExternalWidget`, `AngularMount`, `MfMount`.

### Corporate (`elementCorporate*`) — subset for corporate marketing.

### Experience (`elementExperience*`) — 8 interactive CDN modules
`InsightExplorer`, `MediaExplorer`, `ContentCarousel`, `QuizFlow`, `FilterBoard`, `RatingWidget`, `CountdownClock`, `NotificationStack`.

### Blog (`elementBlog*`) — 2
`BlogHighlight` (featured post card block), `ArticleList` (list of posts with filters).

### Mount params
`ElementMountParam` — single key/value row used inside `BlockListMountParams` for declarative dynamic props.

Exact GUIDs and properties per element: see `ElementTypeInitializer.cs`.

## Compositions (reusable property groups)

Applied to both Document Types and Element Types via `ContentTypeComposition`.

### Core (7)
`CompCoreBase`, `CompCoreLifecycle`, `CompCoreOwnership`, `CompCoreTenant`,
`CompCoreAccess`, `CompCoreVersioning`, `CompCoreAudit`.

### Content (10)
`CompContentHeading`, `CompContentText`, `CompContentMedia`, `CompContentCta`,
`CompContentBadge`, `CompContentCollection`, `CompContentAuthor`,
`CompContentDate`, `CompContentMetadata`, `CompContentEmbed`.

### DOM (8)
`CompDomClass`, `CompDomAttributes`, `CompDomLayout`, `CompDomSpacing`,
`CompDomVisibility`, `CompDomVariant`, `CompDomLayoutPreset`,
`CompDomLayoutProfile`.

### Behavior (6)
`CompBehaviorTracking`, `CompBehaviorInteraction`, `CompBehaviorNavigation`,
`CompBehaviorFeatureFlag`, `CompBehaviorAsync`, `CompBehaviorScript`.

### Other
`CompSeo`, `CompIntegration`, `CompAngularMount`, `CompMfMount`,
`CompVisibility`, `CompTagging`.

All compositions are `IsElement = true` (Umbraco's "composition-only" flag).
Enforced by `PatchCompositionsIsElement()` in `SynergosSchemaComposer`.

## Data Types (field editors)

Centralized in `DataTypeInitializer.cs`. Families:

- **Text:** `TextTitle`, `TextSubtitle`, `TextSummary`, `TextIdentifier`, `TextUrl`, `TextHexColor`, `TextMetaDesc`, `TextAreaNotes`, `TextScriptContent`
- **Number:** `NumberInteger`, `NumberDecimal`
- **Toggle:** `ToggleBoolean`
- **Date:** `DateTimePicker`, `DatePicker`
- **Picker:** `ContentPicker`, `MultiContentPicker`, `LinkUrl`, `MultiLinkUrl`
- **Media:** `MediaImage`, `MediaFile`, `MediaVideo`, `MediaMulti`
- **Dropdown (SelectXxx):** `SelectRobots`, `SelectPageLayout`, `SelectButtonStyle`, `SelectCardStyle`, `SelectHeaderStyle`, `SelectAlertVariant`, `SelectIdentityPreset`, `SelectTrack`, …
- **Tags:** `TagsContent`
- **BlockList:** `BlockListNavItems`, `BlockListFormFields`, `BlockListMountParams`
- **BlockGrid:** `BlockGridPageSections`, `BlockGridReusable`

## Media Types

From `MediaTypeInitializer.cs`:

- `Image` — upload with metadata (alt text, caption, width/height)
- `File` — generic file upload
- `Folder` — media container
- `Video` — video upload (mp4, webm)

## Macros

60+ macros in `MacroInitializer.cs` across two families:

### Native (11) — SSR-only Razor partials
Used inside Rich Text Editors. No client-side JavaScript.

`macroCtaButton`, `macroImage`, `macroAlertBox`, `macroVideoEmbed`, `macroQuote`,
`macroCodeBlock`, `macroStat`, `macroBadge`, `macroDivider`, `macroNewsletter`,
`macroCardPreview`.

### CDN (49) — client-side Custom Elements
Emit a `<script type="module">` + `<synergos-*>` tag. Config passed via JSON.

Families: Modules (hero, banner, faq-section, feature-grid, logo-cloud,
testimonial-section, tab-group, data-table, section, script-embed,
banner-slider), Compositions (card, media-text, alert-bar, button-group,
cta-group, faq-item, feature-item, gallery-item, iframe-embed, info-block,
key-value, logo-item, newsletter-form, social-share, testimonial-item,
timeline-item, external-widget, stat, pricing-card), Primitives (badge,
button-container, column, container-block, divider, grid, icon-block,
image-block, link-block, spacer, stack, text-block, video-block, avatar),
Experiences (feature-journey, insight-explorer, media-explorer,
content-carousel, quiz-flow, filter-board, rating-widget, countdown-clock,
notification-stack), Shop (product-card, product-grid, product-detail,
cart-summary, cart-item, price-display, quantity-selector, variant-picker).

## Dictionary (i18n)

Seeded by `DictionaryInitializer.cs`. Key naming convention:

- `Nav.*` — navigation labels (`Nav.Home`, `Nav.Blog`, `Nav.MobileNavLabel`, `Nav.LanguageSelector`, `Nav.FooterNavLabel`)
- `Blog.*` — blog UI (`Blog.Featured`, `Blog.RecentPosts`, `Blog.ReadMore`, `Blog.BackToBlog`, `Blog.ArticleCount`, `Blog.AuthorSocial`, …)
- `Form.*` — form UI (`Form.Submit`, `Form.ThankYou`, `Form.Validation.Required`, …)
- `Aria.*` — accessibility labels (`Aria.AlertBar`, `Aria.Breadcrumbs`, `Aria.MainNav`, …)
- `Page.*`, `Shop.*`, `Auth.*`, `Error.*` — feature-specific.

Always add a dictionary key instead of hardcoding a string in a view.

## See also

- [`pipeline.md`](pipeline.md) — how the pipeline creates all this.
- [`guid-registry.md`](guid-registry.md) — GUID allocation rules.
- [`../recipes/add-document-type.md`](../recipes/add-document-type.md) — add a new Document Type.
- [`../recipes/add-element-type.md`](../recipes/add-element-type.md) — add a new Element Type.
