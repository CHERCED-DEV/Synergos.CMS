# Recipe — Add a new Document Type

Step-by-step recipe for adding a brand-new renderable page type (e.g.
`EventPage` under `SiteRoot`). Every step, in order. Expected outcome at the
bottom.

## Prerequisites

- You know which compositions you want (Heading, Media, Seo, Layout, Spacing,
  etc.). Check [`../schema/content-model.md`](../schema/content-model.md) for
  the inventory.
- You've picked a GUID from an unreserved slot. See
  [`../schema/guid-registry.md`](../schema/guid-registry.md).

## Example

We'll add `EventPage` — a Document Type for event landing pages.

### 1. Register the GUID and alias

`Schema/Constants/ContentTypeKeys.cs`:

```csharp
public static class ContentTypeKeys
{
    // … existing
    public static readonly Guid EventPage = new("d7000001-0000-0000-0000-000000000000");

    public static class Aliases
    {
        // … existing
        public const string EventPage = "eventPage";
    }
}
```

**Before picking the GUID**, run the four-source check
([`../schema/guid-registry.md`](../schema/guid-registry.md#the-four-source-collision-check)).

### 2. Add the `Ensure` method to an initializer

Pick the right initializer (usually `DocumentTypeInitializer` for generic page
types, or a domain-specific one like `BlogInitializer`). For `EventPage`, use
`DocumentTypeInitializer`:

```csharp
// Schema/Initializers/DocumentTypeInitializer.cs

public override void Initialize()
{
    // … existing
    EnsureEventPage(pagesFolderId);
    PatchAllowedChildren();
}

private void EnsureEventPage(int folderId)
{
    if (Cts.Get(ContentTypeKeys.EventPage) is not null) return;

    var template = EnsureTemplate("Event Page", "EventPage");

    var ct = new ContentType(Ssh, folderId)
    {
        Key           = ContentTypeKeys.EventPage,
        Name          = "Event Page",
        Alias         = ContentTypeKeys.Aliases.EventPage,
        Description   = "Event landing page with date, location, registration.",
        Icon          = "icon-calendar-alt",
        AllowedAsRoot = false
    };

    ct.ContentTypeComposition = new[]
    {
        ContentTypeKeys.CompCoreBase,
        ContentTypeKeys.CompSeo,
        ContentTypeKeys.CompContentHeading,
        ContentTypeKeys.CompContentMedia,
        ContentTypeKeys.CompDomSpacing,
        ContentTypeKeys.CompDomVisibility
    }
    .Select(k => Cts.Get(k))
    .Where(c => c is not null)
    .Cast<IContentTypeComposition>()
    .ToList();

    var tab = Tab("Event", "event", 0);
    tab.PropertyTypes!.Add(Prop("eventDate",      "Event Date",     DataTypeKeys.DateTimePicker,     0, mandatory: true,
        description: "Start of the event."));
    tab.PropertyTypes!.Add(Prop("eventLocation",  "Location",       DataTypeKeys.TextTitle,          10,
        description: "Venue name and address."));
    tab.PropertyTypes!.Add(Prop("registrationUrl","Registration URL", DataTypeKeys.LinkUrl,           20));
    tab.PropertyTypes!.Add(Prop("pageSections",   "Page Sections",  DataTypeKeys.BlockGridPageSections, 30));

    ct.PropertyGroups.Add(tab);
    ct.AllowedTemplates = new[] { template };
    ct.SetDefaultTemplate(template);
    Cts.Save(ct);
}
```

Notes:
- **Idempotency:** `if (Cts.Get(...) is not null) return;` guards re-runs.
- **Helpers:** `Tab`, `Prop`, `EnsureTemplate` come from `SchemaInitializerBase`.
- **Compositions:** pull standard functionality from existing compositions —
  don't duplicate properties.
- **Template:** `EnsureTemplate` creates a DB template + expects a cshtml file.

### 3. Update allowed children

In the same initializer's `PatchAllowedChildren()`:

```csharp
private void PatchAllowedChildren()
{
    SetAllowedChildren(ContentTypeKeys.SiteRoot,
        ContentTypeKeys.PageBase,
        ContentTypeKeys.ShopRoot,
        ContentTypeKeys.EventPage);  // ← NEW
    // …
}
```

Without this, editors can't create `EventPage` nodes under `SiteRoot`.

### 4. Create the Razor template

`Views/EventPage.cshtml`:

```razor
@inherits Umbraco.Cms.Web.Common.Views.UmbracoViewPage<Synergos.CMS.Presentation.ViewModels.EventPageView>
@{
    Layout = "_Layout";
    ViewBag.Title = Model.Title;
}

@await Component.InvokeAsync("LayoutHead")
<body>
  @await Component.InvokeAsync("LayoutHeader")

  <main class="event-page">
    <article>
      <header>
        <time datetime="@Model.EventDateIso">@Model.EventDateFormatted</time>
        <h1>@Model.Title</h1>
        @if (!string.IsNullOrWhiteSpace(Model.Location))
        {
            <p class="event-page__location">@Model.Location</p>
        }
      </header>

      @if (!string.IsNullOrWhiteSpace(Model.RegistrationUrl))
      {
          <a class="event-page__cta" href="@Model.RegistrationUrl">
              @Dict.Get("Event.RegisterNow", Model.Culture, "Register now")
          </a>
      }

      @await Html.GetBlockGridHtmlAsync(Model.PageSections)
    </article>
  </main>

  @await Component.InvokeAsync("LayoutFooter")
</body>
```

### 5. Create the controller (if custom logic needed)

For most page types, Umbraco's default `RenderController` is fine. Create a
specific controller only when you need custom data assembly or routing.

If you do:

```csharp
// Presentation/Controllers/EventPageController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Web.Common.Controllers;
using Synergos.CMS.Application.Mapping;

namespace Synergos.CMS.Presentation.Controllers;

public sealed class EventPageController : RenderController
{
    private readonly EventPageAssembler _assembler;

    public EventPageController(
        ILogger<EventPageController> logger,
        ICompositeViewEngine         engine,
        IUmbracoContextAccessor      ctx,
        EventPageAssembler           assembler)
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

Umbraco auto-discovers controllers by convention (`<alias>Controller` →
matches type alias `eventPage`).

### 6. Create the Assembler (if you made a controller)

`Application/Mapping/EventPageAssembler.cs`:

```csharp
using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.Mapping.Compositions.Content;
using Synergos.CMS.Application.Mapping.Compositions.Seo;
using Synergos.CMS.Domain.Services;
using Synergos.CMS.Presentation.ViewModels;

namespace Synergos.CMS.Application.Mapping;

public sealed class EventPageAssembler
{
    private readonly ContentHeadingReader _heading;
    private readonly SeoReader             _seo;
    private readonly ISiteResolver         _site;
    private readonly IThemeService         _theme;

    public EventPageAssembler(
        ContentHeadingReader heading, SeoReader seo,
        ISiteResolver site, IThemeService theme)
    {
        _heading = heading; _seo = seo; _site = site; _theme = theme;
    }

    public EventPageView AssemblePage(IPublishedContent page)
    {
        var site    = _site.Resolve();
        var theme   = _theme.GetLayoutConfig(site.RootNodeId);
        var heading = _heading.Read(page);
        var seo     = _seo.Read(page);

        return new EventPageView(
            Title:              heading.Title ?? page.Name ?? string.Empty,
            EventDateIso:       page.Value<DateTime>("eventDate").ToString("O"),
            EventDateFormatted: page.Value<DateTime>("eventDate").ToString("D"),
            Location:           page.Value<string>("eventLocation"),
            RegistrationUrl:    page.Value<string>("registrationUrl"),
            PageSections:       page.Value<Umbraco.Cms.Core.Models.Blocks.BlockGridModel>("pageSections"),
            Seo:                seo,
            Theme:              theme,
            Culture:            page.GetCultureFromDomains() ?? "es-CO"
        );
    }
}
```

Register in `Application/ServiceCollectionExtensions.cs`:
```csharp
services.AddTransient<EventPageAssembler>();
```

### 7. Create the view model

`Presentation/ViewModels/EventPageView.cs`:

```csharp
namespace Synergos.CMS.Presentation.ViewModels;

public sealed record EventPageView(
    string Title,
    string EventDateIso,
    string EventDateFormatted,
    string? Location,
    string? RegistrationUrl,
    BlockGridModel? PageSections,
    SeoReaderResult Seo,
    LayoutConfig Theme,
    string Culture);
```

### 8. Seed business pages if desired

If you want sample Event pages on a fresh boot, add to `SeedConfig.Pages` —
but `SeedConfig` currently uses `pageBase`. Either:
- Add `Pages` entries with `DocumentTypeAlias` (requires extending `SeedPage`).
- Create a new `SeedEventPage[]` list in `SeedConfig` with `EventPage`-specific fields.

For generic "just seed a few pages", extend `SeedPage` with a `DocumentTypeAlias`
property and update `ContentSeeder.EnsureBusinessPages` to honor it.

### 9. Add dictionary keys

For any user-facing string in the view (like `Event.RegisterNow`):

```csharp
// Schema/Initializers/DictionaryInitializer.cs
EnsureItem("Event.RegisterNow", new Dictionary<string, string>
{
    { "es-CO", "Regístrate" },
    { "en-US", "Register now" }
});
```

### 10. Bump `SchemaVersion`

`Schema/Constants/SchemaVersion.cs`:

```csharp
public const string Value = "10.0.2";  // was 10.0.1
```

Without this, the pipeline won't re-run on existing installs.

### 11. Build and verify

```bash
dotnet build Synergos.CMS/Synergos.CMS.csproj
# expect: 0 Errores, 1 Advertencia (NU1902)
```

Run the app. Watch for:
```
[INF] Synergos schema mismatch (stored=10.0.1, expected=10.0.2). Running pipeline.
[INF] Synergos schema complete (10.0.2).
```

In the backoffice:
- `Settings → Document Types → Event Page` — should exist with expected tabs.
- `Content → Synergos → (right-click) → Create → Event Page` — should be allowed.
- Create a test Event Page, fill required fields, publish, visit URL.

## Checklist

- [ ] GUID added to `ContentTypeKeys` with four-source check
- [ ] Alias added to `ContentTypeKeys.Aliases`
- [ ] `Ensure<Type>` method added to appropriate initializer
- [ ] Compositions picked (reuse existing)
- [ ] `PatchAllowedChildren` updated
- [ ] Razor template created under `Views/`
- [ ] Controller + assembler + view model (if custom logic)
- [ ] Services registered in `ServiceCollectionExtensions`
- [ ] Dictionary keys added for user-facing strings
- [ ] `SchemaVersion.Value` bumped
- [ ] `dotnet build` returns 0 errors
- [ ] Boot test: type appears in backoffice, can be created, renders correctly
- [ ] Commit follows [§10](../../CLAUDE.md#10-commit-conventions)

## See also

- [`add-element-type.md`](add-element-type.md) — Block Grid element recipe.
- [`add-cdn-macro.md`](add-cdn-macro.md) — CDN web component recipe.
- [`../schema/pipeline.md`](../schema/pipeline.md) — how the pipeline works.
- [`../schema/guid-registry.md`](../schema/guid-registry.md) — GUID policy.
