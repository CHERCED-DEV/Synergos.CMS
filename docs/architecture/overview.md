# Architecture Overview

One-page view of the Synergos ecosystem. Read this first.

## The ecosystem (four worlds, one platform)

```
┌───────────────────────────────────────────────────────────────────┐
│                       SYNERGOS PLATFORM                            │
│                                                                    │
│   ┌──────────────────┐  ┌──────────────────┐  ┌──────────────┐    │
│   │  Synergos.CMS    │  │  Synergos.API    │  │ Synergos.UI  │    │
│   │  (this repo)     │  │  (sibling repo)  │  │ (sibling)    │    │
│   │                  │  │                  │  │              │    │
│   │  Umbraco 13      │  │  .NET API +      │  │  Angular +   │    │
│   │  content,        │  │  Flow Engine     │  │  Nx monorepo │    │
│   │  layout, macros  │  │  + Shop backend  │  │  + CDN bundle│    │
│   └────────┬─────────┘  └────────┬─────────┘  └──────┬───────┘    │
│            │                     │                    │            │
│            │  POST /webhook      │   GET bundles      │            │
│            │  (signed HMAC)      │   (CDN)            │            │
│            └─────► ◄──────────── ┘                    │            │
│                      ▲                                │            │
│                      └─ client fetches bundles ───────┘            │
└───────────────────────────────────────────────────────────────────┘
```

- **Synergos.CMS** (this repo) — Umbraco 13 based CMS. Owns the content tree,
  Document Types, element types, layout services, and Razor rendering. Calls
  `Synergos.API` via signed webhooks for Flow Engine orchestration.
- **Synergos.API** — .NET API hosting the Flow Engine (workflow orchestration)
  and Shop domain (catalog, cart, checkout). CMS doesn't import its code — it
  integrates via HTTP.
- **Synergos.UI** — Angular 17+ monorepo producing client-side Custom Elements
  (web components). CDN-hosted; the CMS renders `<script type="module">` tags
  pointing at bundles from `StaticAssetsSettings.Origin`.

## Content hierarchy (inside the CMS)

```
PlatformRoot (Level 1, alias: platformRoot)
├── GlobalSettings (Level 2)                  platform-wide SEO + Scripts fallbacks
├── Shared Content (Level 2)                  reusable across sites
│   ├── NavigationGroups
│   ├── ReusableBlocks
│   ├── Authors (Blog)
│   ├── FormDefinitions
│   └── Tags (PageTagsFolder)
├── SiteRoot "Synergos" (Level 2)             first site in the platform
│   ├── SiteSettings                          per-site overrides
│   ├── ThemeSettings                         per-site brand (colors, fonts, logo)
│   ├── Home / Nosotros / Servicios / Contacto (pageBase)
│   └── Blog (blogHome)                       → Categories → BlogPosts
├── SiteRoot "Flow Engine Demo" (Level 2)     demo site
│   └── Intro, Paso 1-5 pages
└── FlowSettingsRoot (Level 2)                flow definitions
    ├── approval-flow
    └── notification-pipeline
```

Every `SiteRoot` node is independent — its own settings, theme, pages, blog.
`GlobalSettings` provides platform-wide fallbacks; `SiteSettings` wins when both
have a value. Editors never need to touch `GlobalSettings` unless a platform-wide
default changes.

## Clean Architecture layers

```
Presentation            ← HTTP adapters, Umbraco routing
  └─ only references Application + Domain
        │
        ▼
Application             ← use cases, mappers, assemblers, abstractions
  └─ only references Domain
        │
        ▼
Domain                  ← interfaces, entities, value objects (no framework)
  └─ references nothing but itself
        ▲
        │ implements
Infrastructure          ← framework-specific adapters (Umbraco, HTTP, files)
  └─ references Domain + Application (implements their interfaces)
```

See [`clean-architecture.md`](clean-architecture.md) for the full rules and
self-check greps.

## Request → rendered HTML (summary)

```
HTTP GET /blog/post-slug
       │
       ▼
[Presentation] BlogPostController : RenderController
       │ CurrentPage is IPublishedContent (Umbraco matched route)
       │ injects: BlogAssembler (Application)
       │
       ▼
[Application] BlogAssembler.AssemblePost(page)
       │ reads compositions via IContentContextAccessor + readers
       │ calls Application services: IThemeService, ISeoService, …
       │
       ▼
[Application] view model returned to controller
       │
       ▼
[Presentation] return CurrentTemplate(model)
       │
       ▼
[Razor] Views/BlogPost.cshtml renders the model
       │ calls ViewComponents: LayoutHead, LayoutHeader, LayoutFooter, …
       │ which call focused services via their interfaces
       │
       ▼
       Each Block Grid section rendered by its Razor partial
       Each CDN macro injects IElementUrlResolver + emits <synergos-x> tag
```

See [`request-lifecycle.md`](request-lifecycle.md) for the full end-to-end trace.

## Where state lives

| State | Location | Who writes | Who reads |
|---|---|---|---|
| Content (pages, blog, settings) | Umbraco SQLite DB | Editors (backoffice) + `ContentSeeder` on first boot | All layout services via `IContentContextAccessor` |
| Document Type schema | DB + `Schema/Initializers` | Schema pipeline (idempotent) | Umbraco runtime |
| Dictionary (i18n) | Umbraco DB | `DictionaryInitializer` seeds, editors override | `IDictionaryCache` in services + views |
| Static brand / seed defaults | `SeedConfig` (appsettings.json) | Config, never code | `ContentSeeder` on first run |
| CDN manifest | `C:\LOCAL_CDN\synergos\registry.json` | Build pipeline (Synergos.UI) | `FileSystemArtifactResolver` |
| Dev server overrides | `C:\LOCAL_CDN\synergos\__dev-servers.json` | UI dev tools (runtime) | `FileSystemElementUrlResolver` |
| Flow definitions | Umbraco content (FlowDefinition doc type) | Editors | `FlowConfigurationService` → Synergos.API via webhook |

## Key services by layer

### Domain (interfaces)

- `IThemeService`, `ISiteResolver`, `INavigationService`, `IHeaderService`, `IFooterService`, `IAlertBarService`, `IBannerService`, `ITrackingService`, `ISeoService`, `ILayoutService` (composite)
- `IDictionaryCache`, `IFormEmailService`, `IFlowWebhookDispatcher`

### Application (use cases)

- `FormRenderService`, `FlowConfigurationService`, `BlogService`
- Assemblers: `PageAssembler`, `PageConfigAssembler`, `BlogAssembler`
- `SectionMapperDispatcher` + one `ISectionMapper` per element type
- Composition readers: `ContentHeadingReader`, `DomLayoutReader`, etc.
- `StaticUrlBuilder`, `IArtifactResolver`, `IElementUrlResolver`
- `IContentContextAccessor` (abstraction over Umbraco context)

### Infrastructure (implementations)

- `UmbracoContentContextAccessor`
- 11 × `UmbracoXxxService` (Umbraco/Services/)
- `UmbracoFormEmailService` (Email/)
- `HttpFlowWebhookDispatcher` (Http/)
- `FileSystemArtifactResolver`, `FileSystemElementUrlResolver` (Cdn/)
- `USyncFileCleanerService` (USync/)

### Presentation (HTTP)

- `RenderController` subclasses per page type: `PageBaseController`, `BlogPostController`, `BlogHomeController`, `CategoryController`, `ArticlePageController`, `SiteRootController`, `PlatformRootController`
- API controllers: `FormSubmissionController`, `FlowOrchestrationController`, `SynergosPageApiController`
- 7 view components: `LayoutHead`, `LayoutHeader`, `LayoutFooter`, `LayoutAlertBar`, `LayoutBanner`, `LayoutSite`, (+ one aggregator)

## Next reading

- **New to the codebase?** → [`clean-architecture.md`](clean-architecture.md)
- **Implementing a feature?** → [`../recipes/`](../recipes/)
- **Dealing with schema?** → [`../schema/pipeline.md`](../schema/pipeline.md)
- **Dealing with rendering?** → [`../rendering/overview.md`](../rendering/overview.md)
- **Booting for the first time?** → [`../operations/fresh-boot.md`](../operations/fresh-boot.md)
