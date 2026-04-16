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
| `pageBare` | Page Bare | `PageBare.cshtml` | Page base **without layout** — agnostic shell for full-bleed / iframe-embedded pages | `DocumentTypeInitializer` |
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

60+ element types across 10 families. Prefix convention: `element<Family>*`.

### Structural (`elementStruct*`) — 7 (+ 5 Layout Presets)
`Section`, `Container`, `Grid`, `Column`, `Stack`, `Spacer`, `Divider`.
Plus layout presets: `LayoutPreset1Col`, `LayoutPreset2ColEqual`, `LayoutPreset3ColEqual`, `LayoutPreset4ColEqual`, `LayoutPresetMainSidebar`.
All contain **Block Grid Areas** for child blocks.

### Textual (`elementText*`) — 6
`Heading`, `Paragraph`, `RichText`, `Eyebrow`, `Quote`, `Label`.

### Action (`elementAction*`) — 3
`Button`, `Link`, `CtaGroup`.

### Media (`elementMedia*`) — 6
`Image`, `Video`, `Icon`, `GalleryItem`, `LogoItem`, `Avatar`.

### Info (`elementInfo*`) — 8
`Badge`, `Stat`, `Feature`, `KeyValue`, `TimelineItem`, `FaqItem`, `TestimonialItem`, `PricingCard`.
Note: `Feature` (single) is reused via `FeatureGrid` composition; `FaqItem`/`TestimonialItem` are the atomic items consumed by Accordion/Testimonial containers.

### Composition (`elementComp*`) — 10
- **Presentation:** `Card`, `Hero`, `CtaBanner`, `MediaTextSplit`, `FeatureGrid`
- **Area-based containers (SSR, zero scripts):** `CardGrid`, `LogoCloudGrid`
- **Typed BlockList containers (web component + single script for N items):** `TestimonialCarousel`, `AccordionGroup`
- **Domain-specific:** `FormBlock`, `BlogHighlight`, `ArticleList`

**Retired (10.9.0):** `FaqList`, `TestimonialList`, `LogoCloud`, `Accordion` (legacy `CompContentCollection` variants) — superseded by the typed/area containers above.
**Retired (10.10.0):** `Banner`, `InfoBlock` (dead chains with no Element Type registration).

### Integration (`elementInt*`) — 6
`ScriptEmbed`, `IframeEmbed`, `ExternalWidget`, `AngularHost`, `MfHost`, `MacroHost`.

### Corporate (`elementCorp*`) — 9
`TabGroup`, `AlertBar`, `BannerSlider`, `NewsletterForm`, `SocialShare`, `DataTable`, `ContactInfo`, `MapEmbed`, `MissionBlock`.

### Experience (`experience*`) — 9 interactive CDN modules
`FeatureJourney`, `InsightExplorer`, `MediaExplorer`, `ContentCarousel`, `QuizFlow`, `FilterBoard`, `RatingWidget`, `CountdownClock`, `NotificationStack`.

### Shop (`elementShop*`) — 8
`ProductCard`, `ProductGrid`, `ProductDetail`, `CartSummary`, `CartItem`, `PriceDisplay`, `QuantitySelector`, `VariantPicker`.

### Forms + Mount
`elementFormField`, `elementFormEmbed`, `elementNavItem`, `mountParam` — atomic items used inside BlockList editors.

Exact GUIDs and properties per element: see `ElementTypeInitializer.cs`.

## Compositions (reusable property groups)

Applied to both Document Types and Element Types via `ContentTypeComposition`.

### Core (7)
`CompCoreBase`, `CompCoreLifecycle`, `CompCoreOwnership`, `CompCoreTenant`,
`CompCoreAccess`, `CompCoreVersioning`, `CompCoreAudit`.

### Content (12)
`CompContentHeading`, `CompContentText`, `CompContentMedia`, `CompContentCta`,
`CompContentBadge`, `CompContentCollection`, `CompContentAuthor`,
`CompContentDate`, `CompContentMetadata`, `CompContentEmbed`,
`CompContentPricing`, `CompContentLocation`.

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
- **Dropdown (editorial):** `SelectBlogPostType` (article, news, tutorial, caseStudy, interview, opinion, release)
- **BlockList (generic):** `BlockListNavItems`, `BlockListFormFields`, `BlockListMountParams`, `BlockListCollection`
- **BlockList (typed — one-script containers):** `BlockListTestimonialItems` (accepts only `elementInfoTestimonialItem`), `BlockListFaqItems` (accepts only `elementInfoFaqItem`)
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
- `Blog.*` — blog UI (`Blog.Featured`, `Blog.RecentPosts`, `Blog.ReadMore`, `Blog.BackToBlog`, `Blog.ArticleCount`, `Blog.AuthorSocial`, `Blog.ReadingTime`, `Blog.Published`, `Blog.Tags`, `Blog.Categories`, `Blog.AboutAuthor`, `Blog.NoPosts`, `Blog.PostType.<alias>`, …)
- `Form.*` — form UI (`Form.Submit`, `Form.ThankYou`, `Form.Validation.Required`, …)
- `Aria.*` — accessibility labels (`Aria.AlertBar`, `Aria.Breadcrumbs`, `Aria.MainNav`, …)
- `Page.*`, `Shop.*`, `Auth.*`, `Error.*` — feature-specific.

Always add a dictionary key instead of hardcoding a string in a view.

## See also

- [`pipeline.md`](pipeline.md) — how the pipeline creates all this.
- [`guid-registry.md`](guid-registry.md) — GUID allocation rules.
- [`../recipes/add-document-type.md`](../recipes/add-document-type.md) — add a new Document Type.
- [`../recipes/add-element-type.md`](../recipes/add-element-type.md) — add a new Element Type.
