# Layers and Dependencies

This document is the **authoritative rule sheet** for what can reference
what in the Synergos.CMS solution. Violations are caught by the compiler,
not by convention.

## The dependency graph (concrete)

```
Synergos.CMS.Tests       ──► Synergos.CMS.Web
Synergos.CMS.Web         ──► Synergos.CMS.Application
Synergos.CMS.Web         ──► Synergos.CMS.Interfaces
Synergos.CMS.Application ──► Synergos.CMS.Interfaces
Synergos.CMS.Interfaces  ──► (nothing internal)
```

## What each project *may* depend on

| Project | May `using` … | May NOT `using` … |
|---------|---------------|-------------------|
| `Synergos.CMS.Interfaces` | `System.*` only | Anything Synergos-internal. No Umbraco. No ASP.NET. |
| `Synergos.CMS.Application` | `System.*`, `Synergos.CMS.Interfaces`, hand-picked BCL (e.g. `System.Net.Http`) | `Umbraco.Cms.*`, `Microsoft.AspNetCore.*`, `Synergos.CMS.Web.*` |
| `Synergos.CMS.Web` | Everything above + `Umbraco.Cms.*`, `Microsoft.AspNetCore.*` | `Synergos.CMS.Tests` |
| `Synergos.CMS.Tests` | Everything above + `Xunit` | — |

## Rules that follow from the graph

### 1. No Umbraco types escape the Web project
`IPublishedContent`, `UmbracoHelper`, `ContentService`, etc. never appear
in `Application` or `Interfaces`. If Application needs a page's title, the
Web layer maps an Umbraco content node to a plain DTO in
`Application/Dto/Responses/` before handing it off.

**Why:** The first `Umbraco.Cms` reference in `Application` is the death
of the layer boundary. Both `epicfail` attempts lost it.

### 2. No circular dependencies
Enforced by `.csproj` graph. If you catch yourself wanting to
`ProjectReference` in the wrong direction, the contract belongs in
`Interfaces`, not in the layer you're trying to reach back into.

### 3. Composition lives in Web, wired by Interfaces
Services are **registered** inside `Synergos.CMS.Web/Composers/` using Umbraco's
`IComposer` pattern. The contract that Web consumes is either:
- A plain `public interface` in `Application/Services/` (most cases), or
- `ISynergosServiceBuilder` in `Interfaces/` for deep composition work.

### 4. Tests reach into Web, not the other way around
`Synergos.CMS.Tests` is a downstream consumer. Web doesn't know tests
exist. No `InternalsVisibleTo` until a real need appears — if you need to
test an internal type, question whether it should be internal.

## Cross-cutting concerns — where they live

| Concern | Home project | Folder |
|---------|--------------|--------|
| HTTP clients to external APIs | `Application` | `Proxies/Impl/` |
| App configuration POCOs | `Application` | `Configuration/` |
| Umbraco property value converters | `Synergos.CMS.Web` | `ValueConverters/` |
| Umbraco notifications (Saved, Published…) | `Synergos.CMS.Web` | `Notifications/` |
| Umbraco composers (IComposer) | `Synergos.CMS.Web` | `Composers/` |
| JSON runtime configuration files | `Synergos.CMS.Web` | `Config/` |
| Razor views / partials / macros | `Synergos.CMS.Web` | `Views/`, `Views/Partials/`, `Views/MacroPartials/` |
| Custom property editors (JS/HTML) | `Synergos.CMS.Web` | `App_Plugins/` |

## What "shouldn't exist" looks like

If you're tempted to create any of these, stop and file a question first:

- A `Synergos.CMS.Domain/` project — we deliberately don't have one (see ADR 0002).
- A `Shared/`, `Common/`, `Utils/`, or `Helpers/` folder — these never stay small.
  Prefer extension methods inside the project that needs them.
- An `IRepository<T>` abstraction — Umbraco already provides data access for
  content; external APIs go through `Proxies/`.
- A second `Interfaces` project — it means you tried to move DTOs there.
  Put them in `Application/Dto/` instead.
