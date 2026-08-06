# Architecture — Overview

Synergos.CMS is a four-project .NET 8 solution built on Umbraco 13 LTS.

The structure is a **pragmatic Clean Architecture**: layers are enforced
physically by `.csproj` boundaries, not only by namespace convention.
That enforcement was chosen deliberately — two single-project attempts
collapsed under their own weight because capable developers found it too
easy to reach across boundaries that only existed as folder names.

## The four projects

```
┌─────────────────────────────────────────────────────────────────┐
│                     Synergos.CMS.Tests                          │
│  (xUnit — references Synergos.CMS.Web for integration coverage) │
└──────────────────────────────┬──────────────────────────────────┘
                               │
┌──────────────────────────────▼──────────────────────────────────┐
│                      Synergos.CMS.Web                           │
│   Web host · Umbraco 13 runtime · Controllers · Views           │
│   Composers · ValueConverters · Notifications · App_Plugins     │
└──────────┬───────────────────────────────────────┬──────────────┘
           │                                       │
           ▼                                       │
┌───────────────────────────┐                      │
│   Synergos.CMS.Application │                     │
│   Services · Proxies · Dto │                     │
│   Configuration · Ext.     │                     │
└─────────────┬──────────────┘                     │
              │                                    │
              ▼                                    ▼
        ┌──────────────────────────────────────────┐
        │         Synergos.CMS.Interfaces          │
        │   Composition contracts (no impl).       │
        │   Referenced by everyone. References     │
        │   nobody.                                │
        └──────────────────────────────────────────┘
```

### Synergos.CMS.Web
- The Umbraco 13 runtime host. Contains `Program.cs`, configuration, Views,
  controllers, composers, and value converters.
- Depends on `Application` (to consume services) and `Interfaces` (to be a
  composition host). Never depended on by the other projects.
- This is the only project that references `Umbraco.Cms` directly.

### Synergos.CMS.Application
- Pure business/application logic. No Umbraco types, no `Microsoft.AspNetCore.*`
  in the public API.
- Holds DTOs (`Dto/Requests`, `Dto/Responses`), service contracts and
  implementations (`Services/` + `Services/Impl/`), and adapters to external
  HTTP APIs (`Proxies/` + `Proxies/Impl/`).
- Depends only on `Interfaces`.

### Synergos.CMS.Interfaces
- Minimal. A single contract today (`ISynergosServiceBuilder`) that acts as
  the composition anchor.
- References nothing. Referenced by every other project. This is the
  "foundation" of the dependency graph — everything points down to it.

### Synergos.CMS.Tests
- xUnit test project. Mirrors the folder structure of `Synergos.CMS.Web`
  (and, where useful, `Application`).
- `FakeConfigFiles/` holds JSON fixtures for config-driven tests.

## Dependency rules

1. **Dependencies only point down**. `Interfaces` never references anything
   internal. `Application` references only `Interfaces`. `Synergos.CMS.Web`
   references `Application` and `Interfaces`. Tests reference `Synergos.CMS.Web`.
2. **No skipping**: `Synergos.CMS.Web` never skips `Application` to reach
   across to hypothetical domain types living elsewhere — because there
   is no "elsewhere". Everything non-web lives under `Application`.
3. **No Umbraco in Application**: `Umbraco.Cms` is a package reference only
   in `Synergos.CMS.Web.csproj`. If an Application service needs CMS-ish
   knowledge (content types, publishing events), the Web layer translates
   and calls in via Application DTOs.
4. **Interfaces stays thin**: resist the urge to park DTOs, enums, or
   cross-cutting concerns there. If it compiles, it grows. Anything that
   isn't a literal composition contract belongs in `Application/Dto/`.

## Why this shape

See the ADRs in [`../adr/`](../adr/) for every decision. In particular:
- ADR 0002 — why multi-project (vs. single-project like `epicfail2`)
- ADR 0005 — why Composers live only in the Web layer
- ADR 0004 — why Central Package Management

## What this project is *not*

- **Not DDD with aggregates**. There are no `Domain/`, `DomainEvents/`,
  `ValueObjects/` folders. Umbraco is itself a content-centric model;
  layering DDD on top historically creates duplicate taxonomies. We defer
  that until a bounded context in `Application` actually demands it.
- **Not hexagonal/ports-and-adapters**. We only have one inbound "port"
  (HTTP via Umbraco) and a handful of outbound ones (the `Proxies/`
  folder). A formal ports layer would be ceremony without value.
- **Not microservices-ready by default**. If that day comes, the
  `Application` project is what gets lifted out. Until then, one
  deployable unit.
