# Request Lifecycle

End-to-end trace of what happens when a browser requests a page. Useful when
debugging why a block doesn't render, why the wrong theme loads, or why a CDN
bundle 404s.

## HTTP GET `/blog/my-post`

### 1. Kestrel receives the request

Configured via `appsettings.Development.json`:

```json
"Kestrel": {
  "Endpoints": {
    "Http":  { "Url": "http://synergos.local:5000" },
    "Https": { "Url": "https://synergos.local:5001", "Certificate": {…} }
  }
}
```

Pipeline middleware (from `Program.cs`, in order):
1. **Security headers** — X-Content-Type-Options, X-Frame-Options, Referrer-Policy, Permissions-Policy.
2. **Static files** — `/css/*` and `/js/*` served with `Cache-Control: public, max-age=604800`.
3. **CORS** — `SynergosPolicy` allows `Synergos:Runtime:AllowedOrigins` (CDN origin).
4. **OutputCache** — ASP.NET output cache.
5. **UI culture** — `CultureInfo.CurrentUICulture` set from `Umbraco:CMS:Global:DefaultUILanguage`.
6. **Umbraco** — `UseUmbraco()` takes over routing.

### 2. Umbraco router matches the URL

`Umbraco.Cms.Core.Routing` finds the `IPublishedContent` at `/blog/my-post`:
- Walks content tree starting from the site's root.
- Matches `SiteRoot` → `BlogHome` → `BlogPost` with `urlName = "my-post"`.
- Sets `CurrentPage` on the request context.

No `IPublishedContent` match → 404 (handled by Umbraco's default not-found handler).

### 3. Umbraco dispatches to the RenderController

For `blogPost` (content type alias), Umbraco looks for a controller named
`BlogPostController`. Our `Presentation/Controllers/BlogPostController` matches:

```csharp
public sealed class BlogPostController : RenderController
{
    private readonly BlogAssembler _assembler;

    public BlogPostController(
        ILogger<BlogPostController>  logger,
        ICompositeViewEngine          engine,
        IUmbracoContextAccessor       ctx,
        BlogAssembler                 assembler)
        : base(logger, engine, ctx) { _assembler = assembler; }

    public override IActionResult Index()
    {
        if (CurrentPage is null) return NotFound();
        var model = _assembler.AssemblePost(CurrentPage);
        return CurrentTemplate(model);
    }
}
```

### 4. Application layer — assembler builds the view model

`BlogAssembler` (in `Application/Mapping/`) orchestrates a read-only workflow:

```
BlogAssembler.AssemblePost(page)
  │
  ├─ Read composition data
  │    ContentHeadingReader.Read(page)    → BlogPostHeading
  │    ContentMediaReader.Read(page)      → BlogPostMedia (featured image)
  │    ContentAuthorReader.Read(page)     → BlogPostAuthor
  │    ContentDateReader.Read(page)       → BlogPostDate
  │    SeoReader.Read(page)               → BlogPostSeo
  │    TaggingCompositionReader.Read(page) → tags, categories
  │
  ├─ Read layout config (via focused services)
  │    IThemeService.GetLayoutConfig(siteRootId) → colors, fonts, spacing
  │    IHeaderService.GetHeaderConfig(siteRootId)
  │    IFooterService.GetFooterConfig(siteRootId)
  │    …
  │
  ├─ Read site settings & resolve fallback chain
  │    SiteSettingsAccessor.ResolveSiteSettings(accessor, rootNodeId)
  │    SiteSettingsAccessor.ResolveGlobalSettings(accessor, rootNodeId)
  │
  └─ Build BlogPostPageView record with all the data
```

**Key invariant:** `BlogAssembler` uses `IContentContextAccessor` — never
`IUmbracoContextAccessor` directly. This keeps Application framework-agnostic.

### 5. Controller returns `CurrentTemplate(model)`

`CurrentTemplate` is an Umbraco helper that resolves the template path
(`/Views/BlogPost.cshtml`) and invokes the Razor engine with the view model.

### 6. Razor renders the page

`BlogPost.cshtml`:

```razor
@model Synergos.CMS.Application.Output.BlogPostPageView
@{ Layout = "_Layout"; }

@await Component.InvokeAsync("LayoutHead")
<body>
  @await Component.InvokeAsync("LayoutHeader")
  @await Component.InvokeAsync("LayoutAlertBar")
  @await Component.InvokeAsync("LayoutBanner")

  <main>
    <article class="blog-post">
      <h1>@Model.Title</h1>
      @Model.Body
      @await Html.PartialAsync("Blog/AuthorBio", Model.Author)
      @await Html.GetBlockGridHtmlAsync(Model.PageSections)
    </article>
  </main>

  @await Component.InvokeAsync("LayoutFooter")
</body>
```

Each `Component.InvokeAsync("LayoutXxx")` invokes a view component.

### 7. ViewComponents render layout slots

Each view component injects **only** the focused services it needs (ISP).
Example — `LayoutHeader`:

```csharp
public sealed class LayoutHeaderViewComponent : ViewComponent
{
    private readonly ISiteResolver _site;
    private readonly IThemeService _theme;
    private readonly IHeaderService _header;

    public LayoutHeaderViewComponent(ISiteResolver s, IThemeService t, IHeaderService h)
    { _site = s; _theme = t; _header = h; }

    public Task<IViewComponentResult> InvokeAsync()
    {
        var site   = _site.Resolve();
        var theme  = _theme.GetLayoutConfig(site.RootNodeId);
        var header = _header.GetHeaderConfig(site.RootNodeId);
        return Task.FromResult<IViewComponentResult>(View(new LayoutHeaderViewModel(…)));
    }
}
```

The interfaces (`ISiteResolver`, `IThemeService`, `IHeaderService`) live in
Domain. DI resolves them to the `UmbracoXxxService` implementations in
Infrastructure.

### 8. BlockGrid renders each section

`@await Html.GetBlockGridHtmlAsync(Model.PageSections)` iterates the Block Grid
data and invokes `Views/Partials/blockgrid/default.cshtml` → `items.cshtml` →
each block renders via `Views/Partials/elements/MacroDispatcher.cshtml`.

`MacroDispatcher` looks up the section's `ViewName` (set by the
`ISectionMapper`) and invokes the appropriate partial. For a CDN element it
injects `IElementUrlResolver` and emits the web component tag.

### 9. CDN elements emit their script + tag

Example from `Views/MacroPartials/Compositions/CdnCard.cshtml`:

```razor
@inject Synergos.CMS.Application.Rendering.IElementUrlResolver ElementUrl
@inject Synergos.CMS.Domain.Services.IDictionaryCache Dict
@{
    var cfg = JsonSerializer.Serialize(new CardCdnConfig(…), opt);
}
<script src="@ElementUrl.ResolveBundle("card")" type="module" defer></script>
<synergos-card config='@cfg' class="sg-cdn sg-cdn--card"></synergos-card>
```

`ElementUrl.ResolveBundle("card")` chain:

1. Check `__dev-servers.json` for a dynamic override (hot-reloaded).
2. Check `ElementServersSettings.Overrides["card"]` (from appsettings).
3. Fall through to `StaticUrlBuilder.ElementBundle("card")`:
   `{Origin}/{UiBasePath}/card/{UiFramework}/{UiSlot}/main.js`

### 10. Browser loads the bundle and hydrates

- Script tag → browser fetches the Angular Element bundle.
- Bundle registers `<synergos-card>` as a Custom Element.
- Element reads the `config` attribute, parses JSON, renders its shadow DOM.
- Global stylesheets on the page (`/css/synergos.css`) apply to shadow DOM via
  `:host` rules declared in the element.

## Key properties of this lifecycle

- **No Umbraco framework references above Presentation.** Application services use `IContentContextAccessor` and `IPublishedContent`, not `IUmbracoContextAccessor`.
- **No concrete dependencies in views.** Views inject interfaces (`IElementUrlResolver`, `IDictionaryCache`).
- **No hardcoded brand strings.** All user-facing strings come from `IDictionaryCache.Get(key, culture, fallback)`.
- **No hardcoded URLs.** All URLs built via `StaticUrlBuilder` or `IElementUrlResolver`.
- **Fallback chain for settings.** `SiteSettings → GlobalSettings → null` (via `SiteSettingsAccessor`).

## Where to intervene

| Problem | Look at |
|---|---|
| Wrong content renders | `Umbraco route matcher`, `CurrentPage` value in controller |
| Missing data on page | The composition reader for that data (e.g. `ContentHeadingReader`) |
| Wrong theme/colors | `IThemeService` + `ThemeSettings` node in content tree |
| Header nav wrong | `IHeaderService` → `SiteSettings.headerNavigation` |
| CDN element not loading | `IElementUrlResolver.ResolveBundle`, check `__dev-servers.json` + registry |
| Stylesheet 404s | `wwwroot/css/*` (compiled from `scss/`), cache-control |
| Localization missing | `IDictionaryCache.Get(key)` key + culture, check `DictionaryInitializer` |
| Form doesn't submit | `FormSubmissionController` + `FormRenderService` |
| Flow webhook fails | `IFlowWebhookDispatcher` → `HttpFlowWebhookDispatcher` logs |

## See also

- [`overview.md`](overview.md)
- [`clean-architecture.md`](clean-architecture.md)
- [`../rendering/overview.md`](../rendering/overview.md)
