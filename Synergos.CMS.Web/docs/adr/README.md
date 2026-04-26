# Architecture Decision Records

An ADR is a short, immutable document that captures **one** architectural
decision, its context, and its consequences. We write one every time we
choose between options that a future reader would otherwise second-guess.

## Index

| # | Title | Status |
|---|-------|--------|
| [0001](0001-umbraco-13-lts-pin.md) | Umbraco 13 LTS pin | Accepted |
| [0002](0002-multi-project-solution.md) | Multi-project solution structure | Accepted |
| [0003](0003-sqlite-dev-database.md) | SQLite for development database | Accepted |
| [0004](0004-central-package-management.md) | Central Package Management (CPM) | Accepted |
| [0005](0005-composers-centralized.md) | Composers live only in the Web project | Accepted |
| [0006](0006-documentation-first-governance.md) | Documentation-first governance | Accepted |
| [0007](0007-xunit-test-framework.md) | xUnit as the test framework | Accepted |
| [0008](0008-usync-hybrid-source-of-truth.md) | uSync hybrid source-of-truth | Accepted |
| [0009](0009-extension-seams-mandatory.md) | Extension seams are mandatory | Accepted |
| [0010](0010-branding-via-provider.md) | Branding via provider, no conditional branching | Accepted |
| [0011](0011-feature-flags-typed-config.md) | Feature flags via typed config | Accepted |
| [0012](0012-cdn-contract-consumed.md) | CDN contract is consumed, not owned | Accepted |
| [0013](0013-no-automatic-seeders.md) | No automatic seeders; dev tooling behind flag | Accepted |
| [0014](0014-document-type-page-basic.md) | Document Type `PageBasic` (first product case, static pages) | Accepted |
| [0015](0015-synhost-framework-agnostic-integration.md) | SynHost framework-agnostic integration (CDN↔CMS) | Accepted |
| [0017](0017-layout-system-dropdown-compositions.md) | Layout system per-block compositions con dropdowns | Accepted |
| [0018](0018-forms-dual-path.md) | Forms dual-path (custom SSR + iframe bridge) | Accepted |
| [0019](0019-navigation-flat-groups.md) | Navigation flat groups, no recursion (SSR + a11y first) | Accepted |
| [0020](0020-platform-settings-split.md) | Platform/Settings tree separado + multi-brand via compBranding | Accepted |
| [0021](0021-datatype-semantics-by-intent.md) | DataType semantics: one type per editorial intent | Accepted |
| [0022](0022-page-composition-standard.md) | Page Composition Standard (Standard/Canvas/Bare/Landing + orchestration cascade) | Accepted |
| [0023](0023-componentization-layered-architecture.md) | Componentization Layered Architecture (5 capas + global components pattern) | Accepted |
| [0024](0024-page-minimal-and-editor-facing-descriptions.md) | Pages mínimas + descripciones editor-facing (refinamiento Ola 51) | Accepted |
| [0025](0025-global-components-extension-and-members-runtime.md) | Global components extension (cfgBanner/FooterNote/Modal) + Members runtime (Ola 52) | Accepted |
| [0026](0026-brand-runtime-completion-and-head-enrichment.md) | Brand runtime completion (HostBasedBrandingProvider) + `<head>` enrichment (Twitter Card + JSON-LD + hreflang) | Accepted |
| [0027](0027-blog-runtime-and-members-settings.md) | Blog runtime (IBlogQuery + PostPage/PostCategoryPage templates) + Members settings (LoginPath configurable) | Accepted |
| [0028](0028-shop-runtime-cart-and-query.md) | Shop runtime (ICartService cookie HMAC + IShopQuery + ProductPage/ProductCategoryPage templates + ShopController) | Accepted |
| [0029](0029-flow-templates-closure.md) | Flow templates closure (FlowDefinition + FlowStep templates + DefaultTemplate asignado) | Accepted |
| [0030](0030-forms-internal-submission-runtime.md) | Forms internal submission runtime (IFormSubmissionHandler + FileSystem default + FormSubmissionsController + honeypot + rate limit) | Accepted |
| [0031](0031-search-infrastructure-examine.md) | Search infrastructure on Examine ExternalIndex (ISearchQuery + ExamineSearchProvider + SearchController) | Accepted |
| [0032](0032-search-page-ux.md) | Search UX (searchPage DocType + SearchPage.cshtml + 5 dictionary keys + siteRoot Structure fix) | Accepted |
| [0033](0033-seo-infrastructure-sitemap-robots-rss.md) | SEO infrastructure (sitemap.xml + robots.txt + blog/rss.xml dynamic controllers) | Accepted |
| [0034](0034-member-self-service-runtime.md) | Member self-service runtime (IMemberAuthService + DefaultMemberAuthService + AccountController + Login/Register/Profile views) | Accepted |
| [0035](0035-email-transactional-runtime.md) | Email transactional runtime (IEmailService seam + DefaultEmailService adapter sobre Umbraco IEmailSender) | Accepted |
| [0036](0036-output-caching-sitemap-rss.md) | Output caching via IMemoryCache para Sitemap + Blog RSS (OutputCacheSettings POCO + multi-brand keys + bypass flag) | Accepted |
| [0037](0037-analytics-tracker-instrumentation.md) | Analytics tracker + instrumentación de 4 módulos (IAnalyticsTracker + LoggerAnalyticsTracker + 11 evento slugs en Search/Forms/Shop/Account) | Accepted |
| [0038](0038-comments-runtime-end-to-end.md) | Comments runtime end-to-end (ICommentRepository + FileSystemCommentRepository + CommentsController + elementCommentThread schema + renderer + reuso rate-limit) | Accepted |
| [0039](0039-site-chrome-editable-and-per-site-config.md) | Site Chrome editable + PlatformRoot landing + per-site Configuration folder (compSiteChrome 2 BlockGrid slots + PlatformRoot template + siteConfigFolder UX) | Accepted |
| [0040](0040-architectural-consolidation-theme-chrome-config-transversals-brand.md) | Gran Consolidación Arquitectónica: theme inheritance pura (siteRoot only) + chrome triádico (header/footer/aside) + siteConfiguration unificado + compTransversalSelectors drop-down + brand inheritance pura + ModelsBuilder SourceCodeAuto setup | Accepted |
| [0041](0041-lego-canonical-map-and-coupling-audit.md) | Mapa Lego canónico + auditoría de acoplamientos (30 compositions verified, DTSelect overlaps non-duplicate, pageBasic vs pageBare clarified, bug crítico compBranding fix, "regla del Lego ensamblable" 5 puntos) | Accepted |

## Rules

1. ADRs are **numbered sequentially**. Never reuse a number, even if an ADR
   is rejected.
2. ADRs are **immutable once accepted**. To change a decision, write a
   new ADR with a later number that supersedes the previous one, and
   update the status of the superseded one to `Superseded by ADR-XXXX`.
3. **Status** is one of: `Proposed`, `Accepted`, `Rejected`, `Superseded`,
   `Deprecated`.
4. Keep them **short**. One page or less is the target. If the context
   needs pages of background, it probably needs its own long-form doc in
   `architecture/` and the ADR just links to it.

## Template

Copy this into `NNNN-short-slug.md`:

```markdown
# ADR NNNN — <Short Title>

- **Status:** Proposed | Accepted | Rejected | Superseded by ADR-XXXX | Deprecated
- **Date:** YYYY-MM-DD
- **Deciders:** <names or roles>

## Context

What is the problem? What forces are at play (technical, organizational,
external)? Keep it factual — no opinions yet.

## Decision

The choice that was made. One or two sentences. No hedging.

## Consequences

What becomes easier, harder, or impossible because of this decision?
List both positive and negative consequences honestly. A future reader
uses this section to decide if the decision still applies.

## Alternatives considered

Brief. What else was on the table, and why it lost.
```

## When to write a new ADR

- Choosing between two technologies that could both work
- Removing or adding an abstraction layer
- Changing naming, folder, or dependency rules at the architecture level
- Pinning a version with a specific rationale (e.g. "stay on LTS")
- Any decision where "why is it like this?" will be asked more than once

## When NOT to write an ADR

- Picking a variable name
- Refactoring a single file
- Fixing a bug
- Adding a NuGet package for a feature (note it in `CHANGELOG.md` instead)
