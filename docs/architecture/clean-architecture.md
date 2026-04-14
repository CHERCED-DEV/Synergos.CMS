# Clean Architecture in Synergos

The Synergos CMS is a Clean Architecture project with an enforced dependency
rule. This doc explains the rule, its rationale, and how to verify compliance.

## The dependency rule (the single hard invariant)

> **A layer may only reference layers drawn below it in the diagram.**
> Arrows point toward things you are allowed to depend on.

```
┌───────────────────────────────────────────────────────────┐
│  Presentation                                              │
│  Synergos.CMS/Presentation/                                │
│  Synergos.CMS/Views/                                       │
│  Synergos.CMS/App_Plugins/                                 │
└───────────────────────────────────────────────────────────┘
              │                                ▲
              ▼                                │
┌───────────────────────────────────────────────────────────┐
│  Application                                               │
│  Synergos.CMS/Application/                                 │
└───────────────────────────────────────────────────────────┘
              │                                ▲
              ▼                                │
┌───────────────────────────────────────────────────────────┐
│  Domain                                                    │
│  Synergos.CMS/Domain/                                      │
└───────────────────────────────────────────────────────────┘
                                               ▲
                                               │ implements
                                               │
┌───────────────────────────────────────────────────────────┐
│  Infrastructure                                            │
│  Synergos.CMS/Infrastructure/                              │
└───────────────────────────────────────────────────────────┘
```

- **Domain** depends on nothing but itself.
- **Application** depends on Domain.
- **Infrastructure** depends on Domain and Application (it implements their interfaces).
- **Presentation** depends on Domain and Application (not Infrastructure).
- **Program.cs** is the only place that wires Infrastructure implementations to the interfaces Application and Domain declare.

## Why the rule matters

1. **Testability.** Domain and Application have no framework dependencies — unit tests need no Umbraco, no HTTP, no filesystem.
2. **Portability.** Swap Umbraco for another CMS by rewriting Infrastructure only. Use cases and domain rules stay.
3. **Reviewability.** A reader can understand business logic without first learning Umbraco.
4. **Contained blast radius.** An Umbraco upgrade touches Infrastructure. A Flow Engine change touches Application. They don't tangle.

## What each layer holds

### Domain (`Synergos.CMS/Domain/`)

Pure C# interfaces and value objects. No framework references.

```
Domain/
├── Services/                  I*Service interfaces: IThemeService, IFooterService, IDictionaryCache, IFormEmailService, …
├── Orchestration/             FlowConfig, FlowPublicationResult, IFlowWebhookDispatcher
├── Compositions/              Composition contract aliases
├── Sections/                  Section records used by view models
├── Integration/               Integration contracts
└── Shared/                    FormDefinitionModel, shared DTOs
```

**Allowed:** `using System.*`, `using System.Collections.*`, own types.
**Forbidden:** anything else. No `Umbraco.*`, no `Microsoft.AspNetCore.*`, no `System.Net.Http`.

### Application (`Synergos.CMS/Application/`)

Use cases, orchestration, and abstractions over framework-specific context.

```
Application/
├── IContentContextAccessor.cs     abstraction over Umbraco published content cache
├── Services/                      FormRenderService, SiteSettingsAccessor
├── Orchestration/                 FlowConfigurationService
├── Rendering/                     IArtifactResolver, IElementUrlResolver, StaticUrlBuilder, PageAssembler, PageConfigAssembler, ElementResolver
├── Mapping/                       SectionMapperDispatcher, assemblers, readers, element mappers
├── Blog/                          BlogService, BlogAssembler
├── Cdn/Configs/                   CDN config DTOs (records serialized to JSON)
├── Forms/                         form view models
├── MultiApp/                      LayoutContentResolver (static helper), LayoutRootContext
└── Theme/                         IdentityRegistry (design tokens, not config)
```

**Allowed:** Domain references, `using System.*`, `Microsoft.Extensions.Options`,
`Microsoft.Extensions.Logging`, and `Umbraco.Cms.Core.Models.PublishedContent`
(the `IPublishedContent` type is the unavoidable data contract for a CMS-bound
project, we accept this as a pragmatic leak).

**Forbidden:** `IUmbracoContextAccessor`, `IContentService`, `IMacroService`,
`ILocalizationService`, `IFileService`, `IDataTypeService`, `IContentTypeService`
— all Umbraco write/admin services. Use `IContentContextAccessor` for reads;
define a Domain interface and implement in Infrastructure for writes.

### Infrastructure (`Synergos.CMS/Infrastructure/`)

Framework-specific adapters. Each sub-folder is one concern.

```
Infrastructure/
├── Cdn/                      FileSystemArtifactResolver, FileSystemElementUrlResolver
├── Email/                    UmbracoFormEmailService (implements IFormEmailService via Umbraco IEmailSender)
├── Http/                     HttpFlowWebhookDispatcher (implements IFlowWebhookDispatcher via HttpClient)
├── Umbraco/
│   ├── UmbracoContentContextAccessor.cs       implements IContentContextAccessor
│   ├── Services/                              11 UmbracoXxxService implementations
│   └── ValueConverters/                       Umbraco value converters
└── USync/                    USyncFileCleanerService (cleans stale uSync files)
```

Naming convention: framework prefix in the class name makes it obvious.
`Umbraco*`, `Http*`, `FileSystem*`. A reviewer sees the name and knows the
dependency.

**Allowed:** Domain, Application, any framework library (`Umbraco.*`,
`System.Net.Http`, `System.IO`, etc.).
**Forbidden:** Presentation references.

### Presentation (`Synergos.CMS/Presentation/`, `Views/`, `App_Plugins/`)

HTTP adapters — controllers, view components, views, route filters.

```
Presentation/
├── Api/                      [ApiController] classes, attribute-routed
├── Controllers/              RenderController subclasses for Umbraco routing
├── ViewComponents/           LayoutHead, LayoutHeader, LayoutFooter, LayoutAlertBar, LayoutBanner, LayoutSite
├── ViewModels/               typed view models per feature
└── Filters/                  ApiKeyAuthFilter
```

**Allowed:** Domain, Application, `Microsoft.AspNetCore.*`,
`Umbraco.Cms.Web.Common.Controllers` (RenderController base class).
**Forbidden:** Infrastructure references. If Presentation needs an interface,
Application or Domain must declare it.

## The `IContentContextAccessor` trick

Umbraco's read API (`IUmbracoContextAccessor` → `UmbracoContext.Content.GetById`)
is an Infrastructure concern. But Application needs to read content (forms, flow
definitions, layout).

Solution: `IContentContextAccessor` in Application.

```csharp
// Application/IContentContextAccessor.cs
namespace Synergos.CMS.Application;

public interface IContentContextAccessor
{
    IPublishedContent? GetById(int id);
    IPublishedContent? GetCurrentPage();
    IEnumerable<IPublishedContent> GetAtRoot();
}
```

```csharp
// Infrastructure/Umbraco/UmbracoContentContextAccessor.cs
namespace Synergos.CMS.Infrastructure.Umbraco;

public sealed class UmbracoContentContextAccessor : IContentContextAccessor
{
    private readonly IUmbracoContextAccessor _ctx;
    public UmbracoContentContextAccessor(IUmbracoContextAccessor ctx) => _ctx = ctx;

    public IPublishedContent? GetById(int id)
        => _ctx.TryGetUmbracoContext(out var c) ? c.Content?.GetById(id) : null;

    public IPublishedContent? GetCurrentPage()
        => _ctx.TryGetUmbracoContext(out var c) ? c.PublishedRequest?.PublishedContent : null;

    public IEnumerable<IPublishedContent> GetAtRoot()
        => _ctx.TryGetUmbracoContext(out var c) ? c.Content?.GetAtRoot() ?? [] : [];
}
```

Application services inject `IContentContextAccessor`. Tests mock it. Infrastructure
provides the real Umbraco-backed implementation.

## Self-check commands

Run these before committing. Each must return 0 results.

```bash
# Domain must not depend on anything outside itself or stdlib
grep -rE "^using (Umbraco|Microsoft\.(AspNetCore|Extensions))" Synergos.CMS/Domain/

# Application must not depend on Infrastructure or Umbraco write services
grep -r "using Synergos.CMS.Infrastructure" Synergos.CMS/Application/
grep -rE "IUmbracoContextAccessor|IContentService|IMacroService|IFileService|IDataTypeService|IContentTypeService|ILocalizationService" Synergos.CMS/Application/

# Presentation must not depend on Infrastructure
grep -r "using Synergos.CMS.Infrastructure" Synergos.CMS/Presentation/
```

All four produce zero matches in the current codebase. If your PR introduces
any, that's a regression.

## Dependency injection wiring

The dependency rule is enforced by where interfaces are implemented and where
implementations are registered.

```csharp
// Program.cs — the only place that knows Infrastructure types
using Synergos.CMS.Domain.Services;             // interfaces
using Synergos.CMS.Application.Rendering;       // interfaces
using Synergos.CMS.Domain.Orchestration;        // interfaces
using Synergos.CMS.Infrastructure.Cdn;          // concrete
using Synergos.CMS.Infrastructure.Email;        // concrete
using Synergos.CMS.Infrastructure.Http;         // concrete
using Synergos.CMS.Infrastructure.Umbraco;      // concrete
using Synergos.CMS.Infrastructure.Umbraco.Services;

builder.Services.AddSingleton<IContentContextAccessor, UmbracoContentContextAccessor>();
builder.Services.AddScoped<IThemeService,     UmbracoThemeService>();
builder.Services.AddScoped<IHeaderService,    UmbracoHeaderService>();
// … 11 UmbracoXxxService registrations …
builder.Services.AddSingleton<IArtifactResolver,   FileSystemArtifactResolver>();
builder.Services.AddSingleton<IElementUrlResolver, FileSystemElementUrlResolver>();
builder.Services.AddTransient<IFormEmailService,   UmbracoFormEmailService>();
builder.Services.AddTransient<IFlowWebhookDispatcher, HttpFlowWebhookDispatcher>();

builder.Services.AddSynergosApplication();      // registers pure Application services only
```

`AddSynergosApplication()` (in `Application/ServiceCollectionExtensions.cs`)
registers only abstractions and pure Application classes (assemblers, mappers,
readers). It **does not** register Infrastructure. Attempting to do so produces
a compile error because Application doesn't reference Infrastructure namespaces.

## Interface segregation — `ILayoutService` and friends

`ILayoutService` is a composite that aggregates seven focused interfaces:

- `IThemeService`
- `IHeaderService`
- `IFooterService`
- `IAlertBarService`
- `IBannerService`
- `ITrackingService`
- `ISeoService`

A view component that only needs the footer injects `IFooterService` directly,
not `ILayoutService`. The composite exists for callers that legitimately need
multiple concerns (e.g. a controller building a full page view model).

This is the Interface Segregation Principle in action: clients depend only on
what they use.

## Trade-offs acknowledged

- **`IPublishedContent` leak.** Application exposes Umbraco's content model
  type. We accept this because wrapping it would duplicate hundreds of Umbraco
  features (navigation, value converters, culture resolution) with no real
  benefit — we are, by design, a CMS-bound app. If we ever port to a different
  CMS, this is the one place we'd need a Domain-level wrapper.
- **`LayoutContentResolver` is static.** It takes `IContentContextAccessor`
  as a parameter and is a pure function over it. Making it injectable would
  add DI ceremony with no testability gain.
- **`Application/Theme/IdentityRegistry` has hardcoded values.** These are
  design tokens (brand palettes for presets like "aurora", "graphite"), not
  runtime config. Treat them like resource files.

## Adding a new service (quick reference)

1. Define an interface in `Domain/Services/I<Name>Service.cs` (or in
   `Application/<Feature>/I<Name>.cs` if the feature is purely applicative).
2. If the implementation is pure application logic, put it in
   `Application/Services/<Name>Service.cs` with no framework prefix.
3. If the implementation needs a framework library, put it in
   `Infrastructure/<Concern>/<Prefix><Name>Service.cs` where `<Prefix>` names
   the framework (`Umbraco`, `Http`, `FileSystem`, `Azure`, …).
4. Register in `Program.cs` if it's an Infrastructure class.
5. Register in `Application/ServiceCollectionExtensions.cs` if it's a pure
   Application class.
6. Update the DI registration section of any relevant doc.
7. Verify the four self-check greps still return 0 results.

## See also

- [`overview.md`](overview.md) — the whole system on one page.
- [`request-lifecycle.md`](request-lifecycle.md) — how a request flows through layers.
- [`../../CLAUDE.md`](../../CLAUDE.md) §2 — rules agents must follow.
