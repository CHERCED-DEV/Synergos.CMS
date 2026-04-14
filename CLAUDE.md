# Synergos — Software Factory Guide for Agents

> **Audience:** Claude Code, Claude skills, cloud agents, any LLM tasked with
> writing or modifying code in this workspace.
>
> **Purpose:** This file is the single source of truth for how code is produced
> in Synergos. Every agent reads it before making changes. If you're about to
> write C#, Razor, or config in this repo and you haven't read the relevant
> section of this file — stop and read it first.
>
> **Scope:** This file is auto-loaded by Claude Code. For other agents, point
> them at this file explicitly.

---

## 0. The Ten Commandments

These rules override any individual preference. When the rule and "the quick way"
conflict, the rule wins.

1. **Respect the dependency rule.** `Domain ← Application ← Infrastructure ← Presentation`. Inner layers never import outer layers. Never. See §2.
2. **Depend on abstractions.** If a concrete class has a framework-specific name prefix (`Umbraco*`, `Http*`, `FileSystem*`, `File*`), it lives in `Infrastructure/` and has an interface in `Domain/Services/` or `Application/<feature>/`.
3. **Never hardcode brand or deployment values in business logic.** Colors, fonts, page names, emails, culture codes, tenant names — all belong in `Configuration/SeedConfig.cs` (or a typed settings class). See §7.
4. **Never hardcode framework-specific values outside Infrastructure.** Umbraco service types, HTTP clients, file system paths — those stay in Infrastructure.
5. **The schema pipeline is the source of truth.** Not uSync configs, not the backoffice. Initializers create types; uSync is derivative. See §4.
6. **Idempotency or nothing.** Every initializer, seeder, and patch runs on every boot. It must be safe to re-run. Guard with `Cts.Get(key) is not null` or equivalent. See §4.3.
7. **Named constants, not literals.** Property aliases, tab aliases, GUIDs, magic numbers get a named constant when reused ≥ 2 times. See §6.
8. **Dictionary for strings the user sees.** No hardcoded Spanish/English in views or view components. Inject `IDictionaryCache` and call `Dict.Get("Some.Key", culture, fallback)`. See §9.3.
9. **Commit atomic slices.** One commit = one coherent change (refactor, feature, fix). Never mix a refactor with a feature. See §10.
10. **Verify the build after every edit session.** `dotnet build` must return 0 errors. Analyzer warnings should trend toward zero.

---

## 1. Project layout

```
synergos/                           ← workspace root (this repo)
├── CLAUDE.md                       ← you are here
├── .gitignore                      ← excludes Synergos.API, Synergos.UI, *.epicfail
├── Synergos.CMS/                   ← the CMS project (primary focus)
├── Synergos.CMS.Tests/             ← unit tests (Domain + Application only)
├── Synergos.CMS.epicfail/          ← LEGACY — do not edit, do not read as reference
│                                     except when explicitly validating a migration hole.
├── Synergos.API/                   ← sibling repo (own .git) — out of scope here
├── Synergos.UI/                    ← sibling repo (own .git) — out of scope here
├── multimedia/                     ← brand assets (logos, icons)
└── *.md                            ← planning docs (plan-maestro-*, auditoria-*, etc.)
```

Sibling repos (`Synergos.API`, `Synergos.UI`) are git-ignored here. If an agent
needs to touch them, it should change directory into them and work inside their
own git history.

---

## 2. Clean Architecture layers

### 2.1 The dependency graph (enforced, never violate)

```
┌──────────────────────────────────────────────────────────────┐
│  Presentation (controllers, view components, views)         │
│  may reference: Application, Domain                          │
│  may NOT reference: Infrastructure                           │
└──────────────────────────────────────────────────────────────┘
                 │
                 ▼
┌──────────────────────────────────────────────────────────────┐
│  Application (use cases, mappers, assemblers, abstractions)  │
│  may reference: Domain                                       │
│  may NOT reference: Infrastructure, Presentation             │
└──────────────────────────────────────────────────────────────┘
                 │                   ▲
                 ▼                   │ implements
┌──────────────────────────────────────────────────────────────┐
│  Domain (interfaces, entities, value objects, contracts)     │
│  may reference: nothing but itself                           │
└──────────────────────────────────────────────────────────────┘
                                     ▲
                                     │ implements Domain / Application
┌──────────────────────────────────────────────────────────────┐
│  Infrastructure (framework adapters: Umbraco, HTTP, files)   │
│  may reference: Domain, Application                          │
│  may NOT reference: Presentation                             │
└──────────────────────────────────────────────────────────────┘
```

### 2.2 Folder map inside `Synergos.CMS/`

```
Domain/                             ← zero framework dependencies, pure C#
├── Services/                         interfaces: I*Service, IDictionaryCache, IFormEmailService
├── Orchestration/                    FlowContracts, IFlowWebhookDispatcher
├── Compositions/                     schema composition keys/aliases
├── Sections/                         Section records
├── Integration/                      integration contracts
└── Shared/                           shared value objects (FormDefinitionModel, etc.)

Application/                        ← use cases and pure orchestration
├── IContentContextAccessor.cs        abstraction over Umbraco content cache
├── Services/                         FormRenderService, SiteSettingsAccessor (no Umbraco* prefix here)
├── Orchestration/                    FlowConfigurationService (reads Umbraco via accessor)
├── Rendering/                        IArtifactResolver, IElementUrlResolver, StaticUrlBuilder, ElementResolver, PageAssembler, PageConfigAssembler
├── Mapping/                          Section mappers, composition readers, dispatcher
│   ├── Compositions/                   one folder per composition family
│   └── Elements/                       one file per element family
├── Blog/                             BlogService, BlogAssembler
├── Cdn/Configs/                      CDN config DTOs (serialized to JSON for web components)
├── Components/                       shared component view models
├── Forms/                            form models
├── MultiApp/                         LayoutContentResolver (static utility), LayoutRootContext
└── Theme/                            IdentityRegistry (design tokens, not config)

Infrastructure/                     ← framework-specific implementations
├── Cdn/                              FileSystemArtifactResolver, FileSystemElementUrlResolver
├── Email/                            UmbracoFormEmailService
├── Http/                             HttpFlowWebhookDispatcher
├── Umbraco/                          UmbracoContentContextAccessor
│   ├── Services/                       11 UmbracoXxxService classes (Theme, Header, Footer, AlertBar, Banner, Tracking, Seo, Layout, Navigation, SiteResolver, DictionaryCache)
│   └── ValueConverters/                property value converters
└── USync/                            USyncFileCleanerService

Presentation/                       ← HTTP / Umbraco routing adapters
├── Api/                              API controllers (attribute-routed)
├── Controllers/                      Umbraco RenderControllers (PageBase, BlogHome, BlogPost, Category, SiteRoot, PlatformRoot, ArticlePage)
├── ViewComponents/                   LayoutHead, LayoutHeader, LayoutFooter, LayoutAlertBar, LayoutBanner, LayoutSite
├── ViewModels/                       typed view models
└── Filters/                          ApiKeyAuthFilter

Configuration/                      ← strongly-typed settings, all bound from appsettings.json
├── CacheSettings.cs                  Synergos:Cache      — dictionary TTLs
├── ElementServersSettings.cs         Synergos:ElementServers — dev override map
├── FlowEngineSettings.cs             Synergos:FlowEngine — webhook secret, timeouts
├── RuntimeSettings.cs                Synergos:Runtime    — host, ports, CORS
├── SecuritySettings.cs               Synergos:Security   — API key
├── SeedConfig.cs                     Synergos:Seed       — brand + contact + social + SEO + forms + theme + pages
├── SeedPage.cs                         page spec for ContentSeeder
├── SeedTheme.cs                        brand theme defaults (colors, fonts, spacing)
└── StaticAssetsSettings.cs           Synergos:StaticAssets — CDN origin, UI base path, framework, slot

Schema/                             ← domain types created on startup
├── Constants/
│   ├── ContentTypeKeys.cs              stable GUID registry — NEVER reuse
│   ├── DataTypeKeys.cs
│   ├── MediaTypeKeys.cs
│   └── SchemaVersion.cs                bump to force full pipeline re-run
├── Initializers/
│   ├── SchemaInitializerBase.cs        base class with helper methods
│   ├── CultureInitializer.cs           Phase 0
│   ├── DataTypeInitializer.cs          Phase 1a
│   ├── CompositionInitializer.cs       Phase 1b (core compositions)
│   ├── ContentCompositionInitializer.cs       Phase 2
│   ├── TaggingCompositionInitializer.cs       Phase 3
│   ├── DomCompositionInitializer.cs           Phase 4
│   ├── BehaviorCompositionInitializer.cs      Phase 5
│   ├── SeoCompositionInitializer.cs           Phase 6a
│   ├── IntegrationCompositionInitializer.cs   Phase 6b
│   ├── VisibilityCompositionInitializer.cs    Phase 6c
│   ├── ElementTypeInitializer.cs       Phase 7
│   ├── MediaTypeInitializer.cs         Phase 8
│   ├── DocumentTypeInitializer.cs      Phase 9a (SiteRoot, PageBase) + invokes ShopInitializer
│   ├── ShopInitializer.cs              Phase 9a (extracted — Shop types)
│   ├── PlatformInitializer.cs          Phase 9b (Platform + shared) + invokes SiteSettingsInitializer
│   ├── SiteSettingsInitializer.cs      Phase 9b (extracted — GlobalSettings, ThemeSettings, SiteSettings, LayoutProfile)
│   ├── TaxonomyInitializer.cs          Phase 9c
│   ├── BlogInitializer.cs              Phase 9d
│   ├── MacroInitializer.cs             Phase 10
│   ├── DictionaryInitializer.cs        Phase 11
│   └── FlowEngineInitializer.cs        Phase 12
├── Seeders/
│   ├── ContentSeeder.cs                content tree bootstrap
│   ├── PageBlockGridSeeder.cs          sample block grid content
│   ├── FlowDemoSiteSeeder.cs           flow demo SiteRoot
│   └── FlowEngineDemoSeeder.cs         flow demo definitions
└── SynergosSchemaComposer.cs         Umbraco composer + notification handler that drives the pipeline

App_Plugins/LayoutComposer/         ← Umbraco backoffice custom UI (Block Grid previews)
scss/                               ← source SCSS (compiled into wwwroot/css)
Views/                              ← Razor views + ViewComponents
├── MacroPartials/                    61 CDN macros + 11 native macros
├── Partials/blockgrid/               Block Grid component views
├── Partials/elements/                Macro dispatcher
├── Partials/Blog/                    Blog card partials
└── Shared/Components/                ViewComponent views (layout slots)
```

### 2.3 Quick self-check before saving a file

Run this mental grep:

```
grep -r "using Synergos.CMS.Infrastructure" Synergos.CMS/Domain         → must be 0
grep -r "using Synergos.CMS.Infrastructure" Synergos.CMS/Application    → must be 0
grep -r "using Synergos.CMS.Infrastructure" Synergos.CMS/Presentation   → must be 0
grep -r "using Synergos.CMS.Presentation"   Synergos.CMS/Application    → must be 0
grep -r "IUmbracoContextAccessor"           Synergos.CMS/Application    → must be 0 (use IContentContextAccessor instead)
```

If your change introduces any of these, stop. Fix the design.

---

## 3. Naming conventions

### 3.1 Classes

| Pattern | Location | Meaning |
|---|---|---|
| `IXxxService` | Domain/Services | contract |
| `UmbracoXxxService` | Infrastructure/Umbraco/Services | Umbraco-backed implementation |
| `HttpXxxDispatcher` | Infrastructure/Http | HTTP-backed implementation |
| `FileSystemXxxResolver` | Infrastructure/Cdn | filesystem-backed implementation |
| `XxxAssembler` | Application/Mapping | builds a view model from content |
| `XxxMapper` | Application/Mapping/Elements | maps a single element to a section |
| `XxxReader` | Application/Mapping/Compositions | reads a composition's properties |
| `XxxInitializer` | Schema/Initializers | schema phase |
| `XxxSeeder` | Schema/Seeders | content seeder |
| `XxxConfig` | Domain or Configuration | DTO / settings class |
| `XxxSettings` | Configuration | bound from appsettings.json |

### 3.2 Umbraco content type aliases

- `camelCase` — e.g. `siteRoot`, `pageBase`, `blogHome`, `globalSettings`
- Element types prefix by family: `elementStruct*`, `elementText*`, `elementAction*`, `elementMedia*`, `elementInfo*`, `elementComp*`, `elementIntegration*`, `elementCorporate*`, `elementBlog*`, `elementExperience*`
- Composition types prefix `comp*`: `compCoreBase`, `compContentHeading`, `compDomSpacing`, etc.

### 3.3 Property aliases

- `camelCase` — e.g. `pageTitle`, `siteDisplayName`, `headerCtaLabel`
- Boolean flags: `show*`, `enable*`, `is*`, `has*` — e.g. `showHeaderCta`, `alertDismissible`
- URL fields: `*Url` — e.g. `contactEmail` (exception: email), `headerCtaUrl`, `seoDefaultOgImage` (images can be `*Image`)

### 3.4 GUIDs

All Content Type, Data Type, and Media Type GUIDs are centralized in
`Schema/Constants/*Keys.cs`. **Never invent a new GUID in a one-off file.**

When assigning a new GUID:
1. Check `ContentTypeKeys.cs` and `DataTypeKeys.cs` for the reserved range of the family you're in.
2. `grep -r "your-guid-prefix" Synergos.CMS/uSync/v9/ || true` to ensure no collision.
3. Check the block/element JSON in Block Grid/BlockList DataType configs — block element UDIs (`umb://element/…`) share the GUID namespace but don't appear in `umbracoNode`.
4. Add to the appropriate Keys file with a comment describing the range it occupies.

See `synergos-guid-registry.md` at the root for historical allocations.

---

## 4. Schema pipeline

### 4.1 Execution order (strict, never reorder without bumping SchemaVersion)

```
Phase 0   CultureInitializer          languages MUST exist first
Phase 1a  DataTypeInitializer         data types (text, number, dropdowns, pickers, block lists, block grids)
Phase 1b  CompositionInitializer      core: Lifecycle, Base, Ownership, Tenant, Access, Versioning, Audit
Phase 2   ContentCompositionInitializer    Heading, Text, Media, Cta, Badge, Collection, Author, Date, Metadata, Embed
Phase 3   TaggingCompositionInitializer    Tagging
Phase 4   DomCompositionInitializer        Class, Attributes, Layout, Spacing, Visibility, Variant, LayoutPreset, LayoutProfile
Phase 5   BehaviorCompositionInitializer   Tracking, Interaction, Navigation, FeatureFlag, Async, Script
Phase 6a  SeoCompositionInitializer        Seo
Phase 6b  IntegrationCompositionInitializer  Integration, AngularMount, MfMount
Phase 6c  VisibilityCompositionInitializer   Visibility
Phase 6d  PatchCompositionsIsElement       ensures all compositions have IsElement = true
Phase 7   ElementTypeInitializer           all element types (structural, textual, action, media, info, comp, integration, corporate, blog, experience)
Phase 7.5 PatchMountParamsBlockList        wires ElementMountParam into BlockListMountParams after element types exist
Phase 8   MediaTypeInitializer             media types
Phase 9a  DocumentTypeInitializer          SiteRoot, PageBase → then invokes ShopInitializer
Phase 9b  PlatformInitializer              element types + block lists + shared content + PlatformRoot → then invokes SiteSettingsInitializer (GlobalSettings, ThemeSettings, SiteSettings, LayoutProfile)
Phase 9c  TaxonomyInitializer              PageTag, PageTagsFolder
Phase 9d  BlogInitializer                  BlogHome, BlogPost (Category lives under PlatformInitializer)
Phase 10  MacroInitializer                 60+ macros (Native + CDN families)
Phase 11  DictionaryInitializer            dictionary items for i18n
Phase 12  FlowEngineInitializer            FlowSettingsRoot, FlowDefinition
```

Driven by `Schema/SynergosSchemaComposer.cs`. The pipeline runs only when
`SchemaVersion.Value` differs from the version stored in Umbraco's key/value store.
To force a full re-run, bump `SchemaVersion.Value`.

### 4.2 Adding a new phase or changing order

1. Pick the latest safe insertion point (always after required dependencies).
2. Bump `SchemaVersion.Value`.
3. Add a comment above the call in `SynergosSchemaComposer` documenting the phase number and dependency.
4. Ensure the initializer extends `SchemaInitializerBase` (unless it manages non-ContentType state, e.g. Macros or Languages).

### 4.3 Writing an idempotent initializer

**Every** initializer must be safe to re-run on every boot. Use the `TrySave`
pattern when the underlying service can throw on stale state:

```csharp
// Pattern for ContentTypes
if (Cts.Get(ContentTypeKeys.MyType) is not null) return;  // already exists, skip

var ct = new ContentType(Ssh, folderId) { Key = ContentTypeKeys.MyType, ... };
// build tabs + properties
Cts.Save(ct);

// Pattern for existing ContentTypes that need patching
if (TryPatchExistingContentType(ContentTypeKeys.MyType, "My Type", folderId, description)) return;
// … else create fresh
```

**Never** use `Cts.Save()` on a ContentType that was resurrected from a partial
previous run unless you've verified the DB state. If in doubt, add a `TrySave()`
method to the initializer that catches duplicate-key exceptions and logs
diagnostically (see `ElementTypeInitializer.TrySave` for the canonical pattern).

### 4.4 Adding a new Document Type

1. Add a stable GUID to `Schema/Constants/ContentTypeKeys.cs` and an alias to `ContentTypeKeys.Aliases`.
2. Add an `Ensure<TypeName>()` method to the appropriate initializer (or create a new one and register it in `SynergosSchemaComposer`).
3. Define composition list, tabs, and properties. Reuse existing compositions when possible.
4. If the type has a template, add a `.cshtml` file under `Views/` matching the alias (PascalCase for the template alias).
5. If children are allowed, update `PatchAllowedChildren()` in the owning initializer.
6. If seed content is required, extend `ContentSeeder.Seed()`.
7. Bump `SchemaVersion.Value`.

### 4.5 Adding a new Element Type

1. Add GUID + alias to `ContentTypeKeys` (element families are grouped — use the next reserved slot).
2. Add `EnsureElement<Name>()` to `ElementTypeInitializer`.
3. Pick compositions (usually `CompCoreBase`, `CompDomClass`, `CompDomSpacing`, etc.).
4. Add the element to the Block Grid blocks list in `DocumentTypeInitializer.EnsureBlockGridPageSections()` — assign a LayoutComposer custom view (`~/App_Plugins/LayoutComposer/views/block-<family>.html`).
5. Create an `ISectionMapper` for the element in `Application/Mapping/Elements/<Family>Mappers.cs`. Register it in `ServiceCollectionExtensions.AddSynergosMappers()`.
6. Create a Razor view or macro for it. SSR Razor lives in `Views/Partials/elements/…`; CDN macros live in `Views/MacroPartials/<Family>/Cdn<Name>.cshtml` (see §5).
7. If the element has a CDN config DTO, add the record to `Application/Cdn/Configs/<Family>CdnConfigs.cs`.
8. Bump `SchemaVersion.Value`.

### 4.6 SchemaVersion semantics

- Format: `MAJOR.MINOR.PATCH` e.g. `10.0.1`.
- **Any structural change to schema** (new type, new tab, new property, new composition) → bump PATCH.
- **Breaking change** (property removed that is read by live code, tab renamed destructively) → bump MINOR.
- **Never** ship code that creates new properties without bumping the version — it won't run on deployments that already stored an earlier version.

---

## 5. CDN, macros, and element rendering

### 5.1 Rendering model

Synergos renders pages in two complementary modes:

**SSR Razor (server-side)**
- `PageBaseController` → `PageAssembler` → `SectionView[]` → `Html.GetBlockGridHtmlAsync()` → per-section Razor partial
- Used for: every page, standard Block Grid elements, native macros
- Styling: `wwwroot/css/*.css` (compiled from `scss/`)

**CDN web components (client-side hydration)**
- Server emits `<script src="…/element/angular/latest/main.js" type="module">` + `<synergos-element config='…'></synergos-element>`
- Config is a JSON DTO from `Application/Cdn/Configs/`
- Resolved via `IElementUrlResolver.ResolveBundle("elementName")`
- Used for: CDN macros (`Views/MacroPartials/Modules|Compositions|Primitives|Experiences|Shop/Cdn*.cshtml`) and elements marked `ClientHosted` in `StaticAssetsSettings`

Both paths live in the same page. An SSR section can contain a CDN macro.

### 5.2 Adding a new CDN macro

1. Create the Razor partial in `Views/MacroPartials/<Family>/Cdn<Name>.cshtml`:
   ```cshtml
   @inherits Umbraco.Cms.Web.Common.Macros.PartialViewMacroPage
   @using System.Text.Json
   @using System.Text.Json.Serialization
   @using Synergos.CMS.Application.Cdn.Configs
   @inject Synergos.CMS.Application.Rendering.IElementUrlResolver ElementUrl
   @inject Synergos.CMS.Domain.Services.IDictionaryCache Dict
   @{
       var p = Model.MacroParameters;
       string? Get(string key) => p.ContainsKey(key) ? p[key]?.ToString() : null;
       var opt = new JsonSerializerOptions {
           DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
           PropertyNamingPolicy   = JsonNamingPolicy.CamelCase
       };
       var translations = Dict.GetAll();
       var cfg = JsonSerializer.Serialize(new MyCdnConfig(
           /* props */,
           Translations: translations
       ), opt);
   }
   <script src="@ElementUrl.ResolveBundle("myElement")" type="module" defer></script>
   <synergos-my-element config='@cfg' class="sg-cdn sg-cdn--my-element"></synergos-my-element>
   ```
2. Define `MyCdnConfig` record in the appropriate file under `Application/Cdn/Configs/`.
3. Register the macro in `Schema/Initializers/MacroInitializer.cs` with its parameter list.
4. Build + publish the element bundle in `Synergos.UI` (outside this repo). Ensure `registry.json` lists it.
5. If it's client-hosted (no SSR fallback), add the alias to `StaticAssetsSettings.ClientHosted` in `appsettings.json`.

### 5.3 CDN configuration boundaries

- `Synergos:StaticAssets` (appsettings.json) — **the** source of truth for CDN origin, base path, framework, slot, local registry path. Consumed by `StaticUrlBuilder` and `FileSystemArtifactResolver`.
- `Synergos:ElementServers.Overrides` (appsettings.json) — static per-element overrides for local development. E.g. `{"hero": "http://localhost:4202"}`.
- `C:\LOCAL_CDN\synergos\__dev-servers.json` (runtime) — dynamic per-element overrides with hot-reload. Written by UI dev tools on startup, cleaned on exit.
- **Never** put CDN config in `GlobalSettings` (Umbraco content). That was removed; don't add it back. Editors shouldn't edit CDN infrastructure.

---

## 6. Configuration and anti-hardcoding

### 6.1 Rules

1. **Brand values** (colors, fonts, page names, tenant name, SEO copy, social URLs, emails) → `SeedConfig` + `SeedTheme` + `SeedPage`.
2. **Infrastructure values** (CDN origin, ports, hostnames, timeouts, TTLs, feature flags) → typed settings class + `Synergos:*` section in `appsettings.json`.
3. **Design tokens** (identity preset palettes like `Aurora`, `Graphite`, `Solaris`) → `Application/Theme/IdentityRegistry.cs`. These are bundled design tokens, not runtime config.
4. **Magic numbers** reused 2+ times → named private const in the same class (e.g. `RegistryReloadDebounceMs = 500`).
5. **Property aliases / tab aliases** reused 2+ times → class-level `private const string` at the top of the class.

### 6.2 Adding a new typed settings class

```csharp
namespace Synergos.CMS.Configuration;

public sealed class MyFeatureSettings
{
    public const string SectionPath = "Synergos:MyFeature";

    public int SomeTimeoutMs { get; init; } = 5000;
    public string[] AllowedIds { get; init; } = [];
}
```

Register in `Program.cs`:
```csharp
builder.Services.Configure<MyFeatureSettings>(
    builder.Configuration.GetSection(MyFeatureSettings.SectionPath));
```

Consume via `IOptions<MyFeatureSettings>` or `IOptionsSnapshot<T>` (if values can change at runtime).

### 6.3 Adding a new SeedConfig field

`SeedConfig.cs` holds per-deployment brand defaults. Add a property:

```csharp
public string MyNewField { get; init; } = "Default value for Synergos brand";
```

Override per environment:
```json
"Synergos": {
  "Seed": {
    "MyNewField": "Different for this client"
  }
}
```

Consume in `ContentSeeder` via `_config.MyNewField`. **Do not** hardcode the default in `ContentSeeder` — put it in the property default.

---

## 7. Services, DI, and lifetime rules

### 7.1 DI registration home

| Target | Registered in |
|---|---|
| Application services, assemblers, mappers (no framework deps) | `Application/ServiceCollectionExtensions.cs` → `AddSynergosApplication()` |
| Configuration (`IOptions<T>`) | `Program.cs` near top |
| Infrastructure implementations (Umbraco, HTTP, filesystem) | `Program.cs` (see "framework-specific" section) |
| DI aggregates (`SchemaServices`, `SeedDependencies` records) | `Program.cs` + `Schema/SynergosSchemaComposer.cs` |

`ServiceCollectionExtensions` **must not** reference any Infrastructure type.
If you write `services.AddSingleton<IFoo, UmbracoFooAdapter>()` and the
namespace is `Synergos.CMS.Infrastructure.*`, register it in `Program.cs`
instead.

### 7.2 Lifetimes

| Lifetime | When to use |
|---|---|
| `Singleton` | stateless or startup-loaded state (URL builders, resolvers with hot-reload) |
| `Scoped` | per-request services needing content cache (layout services, site resolver) |
| `Transient` | stateless per-call (mappers, assemblers, DB write operations) |

Prefer `Scoped` over `Singleton` for anything that reads the published content
cache — Umbraco's context is request-scoped.

---

## 8. Presentation patterns

### 8.1 Controllers

All page controllers extend `Umbraco.Cms.Web.Common.Controllers.RenderController`.

Pattern:
```csharp
public sealed class MyPageController : RenderController
{
    private readonly MyAssembler _assembler;

    public MyPageController(
        ILogger<MyPageController> logger,
        ICompositeViewEngine       engine,
        IUmbracoContextAccessor    ctx,
        MyAssembler                assembler)
        : base(logger, engine, ctx)
    {
        _assembler = assembler;
    }

    public override IActionResult Index()
    {
        if (CurrentPage is null) return NotFound();
        return CurrentTemplate(_assembler.AssemblePage(CurrentPage));
    }
}
```

**Always** guard `CurrentPage is null` even if Umbraco promises non-null —
defensive guards protect against edge cases (unpublished content, 404 routing).

### 8.2 API controllers

API controllers live in `Presentation/Api/`. Attributes:

```csharp
[Route("api/feature")]
[ApiController]
[ServiceFilter(typeof(ApiKeyAuthFilter))]  // if authenticated
public sealed class MyApiController : UmbracoApiController
{
    [HttpGet]
    [OutputCache(Duration = 30, VaryByQueryKeys = ["path", "culture"])]
    public IActionResult Get(...) { ... }
}
```

`VaryByQueryKeys` must include `culture` for any endpoint that serves
culture-variant content.

### 8.3 View components

One view component per layout slot (`LayoutHead`, `LayoutHeader`, …). Each
injects only the focused services it needs (ISP). Use the focused interface
(`IHeaderService`) rather than the aggregate (`ILayoutService`) unless multiple
concerns are required.

### 8.4 Views

- Never hardcode user-facing strings. Use `@inject IDictionaryCache Dict` and `Dict.Get("Some.Key", Model.Culture, "fallback")`.
- Never construct URLs by concatenation. Use `@inject StaticUrlBuilder`, `@inject IElementUrlResolver`, or model properties.
- Never call Umbraco services directly from a view. Always go through a typed view model or injected service.

---

## 9. i18n / Dictionary

### 9.1 Source of truth

`Schema/Initializers/DictionaryInitializer.cs` seeds all keys at startup. Editors
can override values per culture in the backoffice.

### 9.2 Reading

In C#: inject `IDictionaryCache` and call `_dictionary.Get(key, culture, fallback)`.

In Razor: `@inject IDictionaryCache Dict` → `@Dict.Get("Nav.Home", Model.Culture, "Inicio")`.

### 9.3 Adding a new key

1. Add to `DictionaryInitializer` under the appropriate section (`Nav.*`, `Blog.*`, `Form.*`, `Aria.*`, etc.) with a default value.
2. Reference it in the code (C# or Razor).
3. Never ship a hardcoded user-facing string. "It's just one label" always becomes ten.

---

## 10. Commit conventions

### 10.1 Message format

```
<type>: <short imperative summary — under 70 chars>

<optional body: WHY the change, not WHAT — the diff shows WHAT>

<optional bullet list of sub-changes if atomic commit touches several files>
```

Types:
- `feat` — new user-facing capability
- `fix` — bug fix
- `refactor` — structural change, no behaviour change
- `chore` — tooling, deps, repo hygiene
- `docs` — documentation only

### 10.2 Atomicity rules

- One commit = one coherent change. Never mix a refactor with a feature or fix.
- Schema changes (new Document Type, property, etc.) may span many files — that's fine as long as they all serve the same purpose.
- If you refactored while implementing a feature: two commits. Refactor first, feature on top.

### 10.3 Authorship

`git config user.name` and `user.email` are set locally in the repo — do not
update global config. If the agent's identity needs to appear, use
`Co-Authored-By: <agent-name>` trailer.

---

## 11. Build and run

### 11.1 Prerequisites

- .NET 8 SDK
- SQLite DB (created automatically on first boot)
- hosts file (for Kestrel): `127.0.0.1 synergos.local` + `127.0.0.1 static.synergos.local`
- HTTPS cert at `C:\LOCAL_CDN\synergos-dev.crt` + `.key` (Kestrel only)
- CDN registry at `C:\LOCAL_CDN\synergos\registry.json`

### 11.2 Launch profiles

- **IIS Express** (`launchSettings.json`) — runs on `http://localhost:4046` + `https://localhost:44382`. Locks DLLs during rebuilds.
- **Umbraco.Web.UI** (`launchSettings.json`) — runs via Kestrel on `synergos.local:5000/5001`. Preferred for dev to match the CDN/CORS architecture.

### 11.3 Fresh boot

1. Stop all web hosts (IIS Express, Kestrel).
2. Delete `Synergos.CMS/umbraco/Data/*.sqlite.db*` (wipe DB).
3. Delete `Synergos.CMS/uSync/v9/` (wipe exports).
4. Delete `Synergos.CMS/bin/Debug/` + `obj/Debug/` (clean build).
5. `dotnet build Synergos.CMS/Synergos.CMS.csproj` — must return 0 errors.
6. Run the app. Schema pipeline runs → creates types → `ContentSeeder` seeds content → logs `Synergos schema complete (<version>)`.
7. First login at `/umbraco`, create admin user, verify content tree.

### 11.4 uSync lifecycle

uSync is a **backup format**, not the source of truth:

- `uSync:Settings:ImportOnStartup: false` (default) — pipeline runs, uSync does not import.
- Export uSync manually from the backoffice after schema pipeline + seeding completes.
- Do not commit stale uSync configs — regenerate after every structural change and commit.

---

## 12. Forbidden patterns (anti-examples)

These come up naturally. Always push back:

### 12.1 "Let me just add a quick property to `GlobalSettings`"
No. `GlobalSettings` is trimmed to five actually-consumed fields. If the new
property has no service consuming it yet, don't add it. Either (a) add the
service first, (b) put it in a typed settings class, or (c) don't add it.

### 12.2 "Let me just hardcode this one default in the seeder"
No. Defaults live in `SeedConfig` properties (with overrides in appsettings.json).
If you hardcode in `ContentSeeder`, every deployment has to change code to
rebrand.

### 12.3 "Let me just inject `IContentService` in Application"
No. `IContentService` is Umbraco's write API — it's Infrastructure. Application
uses `IContentContextAccessor` (read-only abstraction). If you truly need to
write content from Application, create a domain interface and implement it in
Infrastructure.

### 12.4 "Let me just use `IUmbracoContextAccessor` here"
No. That's the whole point of `IContentContextAccessor`. `IUmbracoContextAccessor`
is confined to `Infrastructure/Umbraco/` and to `Presentation/` (where it's
unavoidable for `RenderController`).

### 12.5 "Let me import uSync to get back the old GlobalSettings tabs"
No. The schema pipeline is the source of truth. Importing stale uSync brings
back dead fields. Delete the DB + uSync, let the pipeline run fresh, then
export uSync.

### 12.6 "Let me put the view partial in `Application/`"
No. Views are Presentation. Anything that's Razor `.cshtml` lives under
`Views/`. Period.

### 12.7 "Let me register this Infrastructure type from `ServiceCollectionExtensions`"
No. Infrastructure registrations live in `Program.cs`. Application's extension
method registers only abstractions + pure Application classes.

### 12.8 "Let me add a `TODO` comment and move on"
No. Either do it or file a tracked issue in the plan-maestro docs. TODOs rot.

---

## 13. Known pending work

These items are consciously deferred. If you're asked to work on them, reference
this file so the user confirms scope.

- **Test coverage** — only 38 tests for Domain + Application exist. Refactored pieces have no new tests yet.
- **Feature toggle middleware** — `enableBlog`, `enableForms`, etc. were removed because no middleware consumed them. Re-adding them requires a new `IFeatureGate` service + middleware + schema bump.
- **`LayoutContentResolver` as static** — reviewed and accepted. It takes its dependencies as parameters and has no state; injectable version would add ceremony without benefit.
- **Umbraco advisory NU1902** — 13.13.1 has a moderate advisory. Upgrade to latest 13.x when schedule permits.
- **PowerShell scripts / CI** — not set up. All builds are manual via `dotnet build`.

---

## 14. Quick reference card

| I want to… | Where to look |
|---|---|
| Add a new Document Type | §4.4 + `Schema/Initializers/DocumentTypeInitializer.cs` |
| Add a new Element Type | §4.5 + `Schema/Initializers/ElementTypeInitializer.cs` |
| Add a new CDN macro | §5.2 + `Views/MacroPartials/<family>/` |
| Add a new typed setting | §6.2 |
| Add a new SeedConfig field | §6.3 |
| Add a new service | §7 + `Application/ServiceCollectionExtensions.cs` |
| Add a new controller | §8.1 |
| Add a new dictionary key | §9.3 |
| Understand the pipeline phase order | §4.1 |
| Commit changes | §10 |
| Boot from scratch | §11.3 |

---

**When you finish a change, verify:**
1. `dotnet build Synergos.CMS/Synergos.CMS.csproj` → 0 errors.
2. No new cross-layer imports (§2.3 self-check).
3. No new hardcoded brand/framework values.
4. Commit message follows §10.1.
5. If you bumped `SchemaVersion.Value`, note it in the commit body.
