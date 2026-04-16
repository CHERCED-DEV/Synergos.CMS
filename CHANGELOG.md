# Changelog

All schema-affecting and architecturally-significant changes to Synergos CMS.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versions
mirror `Schema/Constants/SchemaVersion.cs`.

When you bump `SchemaVersion.Value`, add an entry here under `[Unreleased]`,
move it to a new dated section on commit. One bullet per *user-visible* change;
collapse internal refactors into a single line.

---

## [Unreleased]

(empty)

---

## [11.1.0] — 2026-04-15

### Added

- **Mapper smoke tests sweep** ([Synergos.CMS.Tests/Mapping/ElementMapperSmokeTests.Part2.cs](Synergos.CMS.Tests/Mapping/ElementMapperSmokeTests.Part2.cs)) — 34 new `[Fact]` tests covering representative mappers across families (3 Composition, 5 Action, 2 Blog, 6 Media including Avatar, 6 Informational, 9 Text, 3 Structural). Same pattern as Part1.
- **Composition reader tests** ([Synergos.CMS.Tests/Mapping/CompositionReaderSmokeTests.cs](Synergos.CMS.Tests/Mapping/CompositionReaderSmokeTests.cs)) — 26 new tests covering every public reader (12 Content, 6 Dom, 6 Behavior, 1 Seo, 1 Visibility). Smoke-verifies `Read(element)` returns a non-null model on an empty element without throwing.
- **FeatureGateMiddleware tests** ([Synergos.CMS.Tests/Middleware/FeatureGateMiddlewareTests.cs](Synergos.CMS.Tests/Middleware/FeatureGateMiddlewareTests.cs)) — 13 tests covering bypass paths, case-insensitive prefix matching, gate on/off, empty gate settings, and empty-prefix/empty-flag skip logic.
- **LayoutProfileResolver tests** ([Synergos.CMS.Tests/MultiApp/LayoutProfileResolverTests.cs](Synergos.CMS.Tests/MultiApp/LayoutProfileResolverTests.cs)) — 3 tests locking down null-safety and the page → site → null fallback chain.
- **`SchemaVersionHealthCheck`** ([Synergos.CMS/Infrastructure/Health/SchemaVersionHealthCheck.cs](Synergos.CMS/Infrastructure/Health/SchemaVersionHealthCheck.cs)) — reports Healthy/Degraded/Unhealthy on `/readyz` based on whether the DB-stored schema version matches `SchemaVersion.Value`.
- **`USyncFolderHealthCheck`** ([Synergos.CMS/Infrastructure/Health/USyncFolderHealthCheck.cs](Synergos.CMS/Infrastructure/Health/USyncFolderHealthCheck.cs)) — reports Degraded (never Unhealthy) when `uSync/v9/` folder is missing; surface for ops without blocking traffic.
- Both new checks are tagged `"ready"` so they run under `/readyz` only (not `/healthz` which stays lightweight).

Total test suite: **~167 passing** (up from 88 at the start of v11.1.0).
- **`ContentAuthorReader`** ([Application/Mapping/Compositions/Content/](Synergos.CMS/Application/Mapping/Compositions/Content/ContentAuthorReader.cs)) — reads `compContentAuthor` properties (authorName, authorRole, authorImage) into `ContentAuthorModel`. Ported from legacy; fills gap for elements that display authored content (testimonials, quotes, posts).
- **`BehaviorFeatureFlagReader`** ([Application/Mapping/Compositions/Behavior/](Synergos.CMS/Application/Mapping/Compositions/Behavior/BehaviorFeatureFlagReader.cs)) — reads `compBehaviorFeatureFlag` properties (featureKey, isEnabled) into `BehaviorFeatureFlagModel`. Lets editors gate element rendering per-instance.
- **`FeatureGateSettings` + `FeatureGateMiddleware`** — declarative route gate. `Synergos:FeatureGates.Gates` is a list of `{ PathPrefix, FeatureFlag }` pairs. Requests matching a prefix whose flag is off short-circuit to 404 before routing. Bypass list covers `/umbraco`, `/api`, `/App_Plugins`, `/css`, `/js`, `/healthz`, `/readyz`, `/error`.

### Fixed

- **`FlowDemoSiteSeeder`**: the 6 flow demo pages (Intro, Paso 1–5) were failing to publish with `FailedPublishContentInvalid` because `SaveAndPublish` was called without a culture argument on culture-variant `PageBase`. Now publishes only the default culture (es-CO), matching the pattern already used in `ContentSeeder`.
- **`GuidUniquenessTests.AllGuids_AreDistinct_AcrossRegistries`**: was failing due to the retired `LandingPage` GUID colliding with `PageBare` (both `d2000007`). Removed the 5 retired doctype GUIDs (`HomePage`, `AboutPage`, `ServicesPage`, `ContactPage`, `LandingPage`) from `ContentTypeKeys`. They had no code references and were flagged "Retired — kept for DB cleanup reference only" — the DB cleanup happens via `ContentSeeder.ConsolidatedPageAliases` (string list, unaffected).

### Internal

- `ServiceCollectionExtensions.AddSynergosCompositions` registers the two new readers as singletons.
- `AvatarMapper` promoted from `internal` to `public` (symmetry with other Media family mappers, enables testability — same change previously applied to `PricingCardMapper`).
- `MimeKit` bumped 4.15.1 → 4.16.0 (safe minor update). `Umbraco.Cms` stays pinned at **13.13.1** — latest available on the 13.x LTS branch. NU1902 (moderate severity) has no patch within 13.x; remediation requires migrating to Umbraco 14+/17 (needs .NET 9+/10), deferred until scheduled.
- `SchemaVersion` bumped 11.0.2 → **11.1.0** (new features added; no breaking).

---

## [11.0.0] — 2026-04-15

### BREAKING — Layout Config big-bang migration

All header / footer / alert-bar / banner configuration moves from SiteSettings tabs
to **dedicated nodes under `<site>/Config/Layout/`** (per-site) and
`PlatformRoot/SharedContent/Layout/` (platform-level). The pattern is composed by
a `LayoutProfile` node that references a `HeaderConfig`, `FooterConfig`, N
`AlertBarConfig` and one `BannerConfig`. Pages resolve their active profile via
a fallback chain: **page override → site default (`SiteSettings.defaultProfile`)
→ platform default**.

### Added

- **11 new Document Types**:
  - Structural folders: `LayoutFolder`, `HeaderConfigFolder`, `FooterConfigFolder`,
    `AlertBarConfigFolder`, `BannerConfigFolder`, `LayoutProfileFolder`,
    `NavigationFolder` — constrain `AllowedContentTypes` for a tidy
    "Add Header" / "Add Footer" UX instead of generic "Add Folder".
  - Config leaves: `HeaderConfig` (navigation / CTA / branding / style),
    `FooterConfig` (navigation / content / newsletter), `AlertBarConfig`
    (content / behavior / schedule), `BannerConfig` (bannerBlock picker +
    schedule).
- **`LayoutProfile` extended** with new composition pickers (`headerConfig`,
  `footerConfig`, `alertBars` multi-picker, `banner`, `platformStripEnabled`,
  `isDefault`) and cleaner tab layout (Layout / Composition / Style).
- **`SiteSettings.defaultProfile` picker** in a new "Layout" tab — points at
  the site's default `LayoutProfile` node.
- **`LayoutContentResolver.ResolveLayoutProfile(accessor, rootNodeId, page?)`** —
  new static method implementing the fallback chain (page → site → null).
- **`LayoutConfigSeeder`** — new seeder that builds the Platform-level layout
  tree (`SharedContent/Layout/…` + `SharedContent/Navigations/Platform Nav`)
  and the per-SiteRoot tree (`<site>/Config/Layout/{Header, Footer, Alerts,
  Banners, Profiles}/` + `<site>/Config/Navigations/`) on every boot, idempotent.
  Relocates any stray `NavigationGroup` from `<site>/Config/` directly into
  `<site>/Config/Navigations/`. Non-destructively migrates legacy SiteSettings
  values (`headerNavigation`, `footerCopy`, `showHeaderCta`, `headerCtaLabel`,
  `headerCtaUrl`, `footerNavigation`, `newsletterActionUrl`) into the
  freshly-created `HeaderConfig` / `FooterConfig` nodes.
- **Platform Nav (cross-world)** — `NavigationGroup` at
  `SharedContent/Navigations/Platform Nav` with items pointing at each
  `SiteRoot` (editor populates the BlockList). Rendered as the thin strip
  above each site's header when `layoutProfile.platformStripEnabled = true`.

### Changed

- **`UmbracoHeaderService`**, **`UmbracoFooterService`**, **`UmbracoAlertBarService`**,
  **`UmbracoBannerService`** — all four now prefer reading via the active
  `LayoutProfile` → Config/Layout node. Each service falls back to the legacy
  SiteSettings read path when no profile or config is yet seeded (transitional
  safety net for partial-boot scenarios; still-vital during the migration
  restart).
- **`AlertBarConfig` supports scheduling** (`startDate` / `endDate`) and the
  service returns the first currently-active bar from the profile's
  `alertBars` collection.
- **`BannerConfig` supports scheduling** via its own `startDate` / `endDate`
  instead of the SiteSettings campaign window.
- **SiteRoot `AllowedChildren`** now includes `SharedContentFolder` so each
  site can have its own `Config/` folder holding `Layout/` and `Navigations/`.
- **SharedContentFolder `AllowedChildren`** extended with `LayoutFolder` and
  `NavigationFolder`.

### Deprecated (removal in v11.1.0)

- Legacy SiteSettings tabs — **kept** but renamed and moved to sort order
  90–93 so editors can still see migrated values:
  - `Header` → `Header (Deprecated — use Config/Layout/Header)`
  - `Footer` → `Footer (Deprecated — use Config/Layout/Footer)`
  - `Alert Bar` → `Alert Bar (Deprecated — use Config/Layout/Alerts)`
  - `Banners & Campaigns` → `Banners (Deprecated — use Config/Layout/Banners)`

  Properties remain intact until v11.1.0 to guarantee `LayoutConfigSeeder`
  can complete its migration even on hot upgrades.

### Migration notes

1. Restart CMS → pipeline runs, 11 new doctypes exist, legacy tabs renamed,
   `LayoutConfigSeeder` creates Platform + Site structure and copies values.
2. Editors verify `<site>/Config/Layout/Header/Site Header` + `Footer/Site Footer`
   have the expected values.
3. The site's `SiteSettings.defaultProfile` auto-points at
   `<site>/Config/Layout/Profiles/Default`.
4. For cross-world nav: platform admin opens
   `SharedContent/Navigations/Platform Nav` and populates the `navItems`
   BlockList with entries pointing at each SiteRoot.
5. Export uSync → new configs regenerate.

### Internal

- `SchemaVersion` bumped 10.17.0 → **11.0.0** (breaking structural change).
- `PricingCardMapper` promoted from `internal` to `public` (symmetry with the
  rest of the informational family).

---

## [10.17.0] — 2026-04-15

### Added — production-readiness sweep

- **`/sitemap.xml`** — `SitemapController` walks the published content tree and
  emits one `<url>` per routable node, skipping settings/composition/folder
  aliases. Per-alias `changefreq`/`priority` (siteRoot=1.0, blogPost=weekly/0.7).
  Cached via `SiteSettings` output-cache policy.
- **`/robots.txt`** — `RobotsController` with three modes: explicit editor
  override (`Synergos:Runtime.RobotsTxtOverride`), non-production (disallow all),
  production (allow, disallow `/umbraco/`, `/App_Plugins/`, `/api/`, publish
  sitemap URL).
- **Health endpoints** — `/healthz` (liveness, `check.Tags.Contains("live")`
  filter) and `/readyz` (readiness, all checks) registered via
  `AddHealthChecks()`.
- **`CorrelationIdMiddleware`** — reads or mints `X-Correlation-ID`, echoes in
  the response header, and pushes `{CorrelationId}` into every log line of the
  request via `ILogger.BeginScope`.
- **Response compression** — brotli + gzip, extended MIME list
  (application/xml, image/svg+xml, application/manifest+json).
- **Global error page** — `ErrorController` (`/error` + `/error/{statusCode}`)
  renders `Views/Shared/Error.cshtml` using per-status Dictionary keys
  (`Error.404.Title`, `Error.500.Message`, …, `Error.BackHome`). Wired via
  `UseExceptionHandler("/error")` + `UseStatusCodePagesWithReExecute`.
- **Security headers** — CSP (with optional `Synergos:Runtime.CspAdditionalDirectives`
  merge, excluded from `/umbraco/` + `/App_Plugins/`) and HSTS (production only).
- **OutputCache profiles applied** — `BlogPost` / `BlogListing` / `PageContent`
  policies now decorate `BlogPostController`, `BlogHomeController`,
  `CategoryController`, `AuthorController`, `ArticlePageController`,
  `PageBaseController`, `PageBareController`.
- **`IFeatureFlags` gates** — `EnableBlog` guards `BlogHome` + `BlogPost`
  (returns 404); `EnableForms` guards `FormSubmissionController.Submit`
  (returns 503).
- **Mapper smoke tests** — `ElementMapperSmokeTests` covers Card, Hero,
  PricingCard, TestimonialItem, AccordionGroup. Uses an
  `UmbracoStaticServicesFixture` collection fixture that installs a minimal
  `StaticServiceProvider.Instance` with a mocked `IPublishedValueFallback` so
  `FriendlyPublishedElementExtensions.Value<T>()` resolves outside a running
  Umbraco host. `PricingCardMapper` promoted from `internal` to `public` for
  symmetry.

---

## [10.16.0] — 2026-04-15

### Added — backlog applied (post NS.Booking.CMS architectural review)

- **AuthorPageView + AuthorController + author.cshtml** — `/author/<slug>` route now
  renders the full author profile (photo, bio, social links) plus the feed of
  posts authored by them (resolved by walking site-root descendants where
  `postAuthor.Key == author.Key`). Replaces the 404 stub.
- **TimeoutMiddleware** — request timeout guard (`Synergos:Runtime.RequestTimeoutSeconds`,
  default 90s) with path exclusions for `/umbraco/`, `/App_Plugins/`, `/media/`,
  `/scripts/`, `/css/`. Returns 408 with elapsed-time logging.
- **Dictionary 3-tier fallback** — `Synergos:Cache.DefaultCulture` (default `es-CO`).
  Translation resolution order: requested culture → default culture → first
  available. Inspired by IbeCms's culture-aware caching.
- **Fluent HTTP builder** — `HttpRequestBuilder` (`Post(url).WithJson(payload)
  .WithHeader(name, value).Build()`). Used by `HttpFlowWebhookDispatcher`,
  testable in isolation.
- **OutputCache profiles** — `Synergos:OutputCache.Profiles` array binds named
  policies (PageContent / BlogListing / BlogPost / SiteSettings) with
  duration + vary-by-query + vary-by-header + tags. Controllers reference via
  `[OutputCache(PolicyName = "...")]`.
- **`ICdnConfigCache`** — per-request memo of CDN config JSON serializations
  keyed by FNV-1a 64-bit hash. Same payload repeated N times serializes once.
  Win on listings/grids with many identical CDN elements.
- **`IFeatureFlags`** + `FeatureFlagsSettings` — typed boolean flags
  (EnableBlog, EnableShop, EnableForms, EnablePreloadHints, …) with
  reflection-driven dynamic `IsEnabled(string)` accessor + `Snapshot()`.

### Removed

- 5 orphan template stubs (`HomePage.cshtml`, `AboutPage.cshtml`, `ContactPage.cshtml`,
  `ServicesPage.cshtml`, `LandingPage.cshtml`) — their doctypes were already
  retired by `CleanupLegacyTypes()`; the views were dead code.

### Notes

- `ISectionMapper<TRequest>` generalization investigated and **declined**:
  `items.cshtml`'s `item.Areas.Any()` branch goes directly to the
  `blockgrid/Components/<alias>.cshtml` partial without invoking a mapper, so
  there is no real coupling to refactor. The hack is the canonical Umbraco 13
  pattern.

---

## [10.15.0] — 2026-04-15

### Changed

- **Massive file granularity refactor** — every element ViewModel + Mapper now
  lives in its own file under a per-family subfolder (`Application/Elements/{Composition,
  Informational,Media,Action,Integration,Form,Blog,Text,Structural,Corporate,Experience}/`
  + mirror in `Application/Mapping/Elements/`). ~149 new files, 22 monolithic
  `*ViewModels.cs` / `*Mappers.cs` files removed. Namespace stays flat — zero
  ripple in consumers. CLAUDE.md §3.5 documents the convention.

---

## [10.14.0] — 2026-04-14

### Fixed

- **`ElementCompBlogHighlight` + `ElementCompArticleList` duplicate creation bug**
  — Phase 7 created them with wrong compositions; Phase 9b's `if (Cts.Get is not
  null) return` then silently skipped, so picker properties (`blogSource`,
  `articles`, `maxPosts`, …) never reached the schema. Both mappers were reading
  null. `ElementTypeInitializer` is now the sole creator with `PatchBlogHighlightProps`
  + `PatchArticleListProps`.
- **`category.cshtml` 404 stub** — replaced with the real `BlogCategory.cshtml`
  implementation (deleted the duplicate). The `category` doctype alias now
  resolves correctly.

---

## [10.13.0] — 2026-04-14

### Removed

- `ContentAuthorReader` and `BehaviorFeatureFlagReader` — orphan dead chains
  with zero consumers (CLAUDE.md §13 confirmed feature-flag middleware was
  removed; author resolution is inline in BlogAssembler).

### Changed

- **i18n LayoutComposer primitives** — 12 backoffice preview HTMLs (`block-section`,
  `block-container`, `block-column`, `block-stack`, `block-grid`,
  `block-layout-{1,2,3,4}col`, `block-layout-main-sidebar`, `block-separator`,
  `block-macro-host`) localized via `<localize>`. +20 keys in `lang/*.user.xml`.
  Backoffice now 100% free of hardcoded strings in element previews.

---

## [10.12.0] — 2026-04-14

### Added

- **Preload hints** — `CdnBundleRegistry` (alias→bundle) + `BundlePrescanner`
  (walks BlockGrid + Areas + MacroHost) + `IEmittedBundleTracker.EmittedBundles`
  + `<link rel="modulepreload">` partial integrated in PageBase / BlogPost /
  BlogHome / BlogCategory `<head>`. TTI win 150–300 ms on pages with ≥2
  interactive bundles.

### Tests

- +10 tests (`CdnBundleRegistryTests`, `EmittedBundleTracker.EmittedBundles`).

---

## [10.11.0] — 2026-04-14

### Fixed

- **`BlogPost.cshtml` and `BlogHome.cshtml` were `Layout=null` stubs** — the
  PageAssembler built complete VMs that the controllers passed via
  `CurrentTemplate(vm)`, but the templates rendered nothing. The whole blog
  reading/listing surface didn't display content.

### Added

- Real `BlogPost.cshtml`: SEO meta (article/og), breadcrumb of categories,
  PostType badge, hero, author byline, reading time, featured image, body,
  optional BlockGrid, tags, conditional author bio, conditional related
  posts grid.
- Real `BlogHome.cshtml`: header with article count, BlockGrid hero, featured
  post (page 1 only), posts grid with configurable layout, paginated nav,
  categories sidebar, footer CTA BlockGrid.
- 6 new dictionary keys: `Blog.ReadingTime`, `Blog.Published`, `Blog.Tags`,
  `Blog.Categories`, `Blog.AboutAuthor`, `Blog.NoPosts`.

---

## [10.10.0] — 2026-04-14

### Added

- **CDN script deduplication** — `IEmittedBundleTracker` (Domain) +
  `EmittedBundleTracker` (scoped Infrastructure impl). 65 Razor files (61
  CDN macros + 4 Blog/SSR views) wrap their `<script>` tag in
  `@if (Bundles.TryClaim("bundle-name")) { ... }`. Editors can drop N copies
  of the same CDN element on a page and only one `<script>` is emitted.

### Removed

- Banner/InfoBlock dead chains (VM + mapper + view + DI registration with no
  Element Type backing).

### Tests

- +17 tests across `EmittedBundleTracker`, `GuidUniqueness`, `BlogPostType`.

---

## [10.9.0] — 2026-04-13

### Removed

- Legacy containers `ElementCompFaqList`, `ElementCompTestimonialList`,
  `ElementCompLogoCloud`, `ElementCompAccordion` (they used the generic
  `CompContentCollection` that accepted any block). Superseded by typed/area
  containers `AccordionGroup`, `TestimonialCarousel`, `LogoCloudGrid`.

---

## [10.8.0] — 2026-04-13

### Changed

- **LayoutComposer universal preview** — single `block-universal.html`
  replaces 19 bespoke per-element previews + 9 group previews. Field-aware,
  resilient to empty editor input. Adding a new element type → preview is
  free, no HTML to write.

---

## See also

- [`Schema/Constants/SchemaVersion.cs`](Schema/Constants/SchemaVersion.cs) — current version constant.
- [`docs/schema/guid-registry.md`](docs/schema/guid-registry.md) — GUID allocation policy + changelog.
- [`docs/schema/pipeline.md`](docs/schema/pipeline.md) — pipeline phases + container patterns.
- [`docs/schema/content-model.md`](docs/schema/content-model.md) — type inventory.
