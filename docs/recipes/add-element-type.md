# Recipe — Add a new Element Type

Add a new block to the Block Grid that editors can drop onto page sections.

Example: `elementInfoTimeline` — a composite timeline with date/body entries.

## Decide first

- **Family:** `Info` (use the `elementInfo*` prefix).
- **SSR or CDN?** If it needs interactivity (zoom, filter, animate) → CDN via a
  macro (see [`add-cdn-macro.md`](add-cdn-macro.md)). Otherwise SSR Razor.
  We'll do SSR for this example.

## Steps

### 1. Add GUID + alias

`Schema/Constants/ContentTypeKeys.cs`:

```csharp
public static readonly Guid ElementInfoTimeline = new("b6000020-0000-0000-0000-000000000000");

public static class Aliases
{
    public const string ElementInfoTimeline = "elementInfoTimeline";
}
```

Four-source collision check ([`../schema/guid-registry.md`](../schema/guid-registry.md#the-four-source-collision-check)).

### 2. Add `Ensure` to `ElementTypeInitializer`

`Schema/Initializers/ElementTypeInitializer.cs`:

```csharp
private void EnsureElementInfoTimeline()
{
    if (Cts.Get(ContentTypeKeys.ElementInfoTimeline) is not null) return;

    var ct = new ContentType(Ssh, -1)
    {
        Key         = ContentTypeKeys.ElementInfoTimeline,
        Name        = "Timeline",
        Alias       = ContentTypeKeys.Aliases.ElementInfoTimeline,
        Description = "Timeline of dated entries.",
        Icon        = "icon-time",
        IsElement   = true
    };

    ct.ContentTypeComposition = new[]
    {
        ContentTypeKeys.CompCoreBase,
        ContentTypeKeys.CompDomClass,
        ContentTypeKeys.CompDomSpacing,
        ContentTypeKeys.CompContentHeading
    }
    .Select(k => Cts.Get(k))
    .Where(c => c is not null)
    .Cast<IContentTypeComposition>()
    .ToList();

    var tab = Tab("Timeline", "timeline", 0);
    tab.PropertyTypes!.Add(Prop("entries", "Entries", DataTypeKeys.BlockListTimelineEntries, 0,
        mandatory: true,
        description: "List of timeline entries in chronological order."));
    tab.PropertyTypes!.Add(Prop("orientation", "Orientation", DataTypeKeys.SelectOrientation, 10,
        description: "vertical | horizontal. Default vertical."));

    ct.PropertyGroups.Add(tab);

    TrySave(ct);
}
```

Call it from the existing `Initialize()`:

```csharp
public override void Initialize()
{
    // … existing elements
    EnsureElementInfoTimeline();
}
```

### 3. Register in Block Grid

Element types only show up in Block Grid if they're listed as blocks in the
`BlockGridPageSections` DataType. Add to
`DocumentTypeInitializer.EnsureBlockGridPageSections()`:

```csharp
cfg.Blocks = cfg.Blocks.Append(
    new BlockGridConfiguration.BlockGridBlockConfiguration
    {
        ContentElementTypeKey = ContentTypeKeys.ElementInfoTimeline,
        GroupName             = "Info",
        BackgroundColor       = "#3b82f6",   // picked per group
        IconColor             = "#ffffff",
        Label                 = "{{heading}}",
        View                  = "~/App_Plugins/LayoutComposer/views/block-info.html"
    }).ToArray();
```

`View` points to the backoffice preview HTML in
`App_Plugins/LayoutComposer/views/`. Use the existing family view
(`block-info.html`, `block-action.html`, etc.) unless you need a new variant.

### 4. Add the SectionMapper

`Application/Mapping/Elements/InformationalMappers.cs` (or wherever the family
lives):

```csharp
public sealed class TimelineMapper : ISectionMapper
{
    public string Alias => ContentTypeKeys.Aliases.ElementInfoTimeline;

    private readonly ContentHeadingReader _heading;
    private readonly DomSpacingReader     _spacing;
    private readonly DomClassReader       _classes;

    public TimelineMapper(
        ContentHeadingReader heading,
        DomSpacingReader     spacing,
        DomClassReader       classes)
    {
        _heading = heading; _spacing = spacing; _classes = classes;
    }

    public SectionView? Map(BlockGridItem item, IPublishedContent page)
    {
        var content = item.Content;
        var heading = _heading.Read(content);

        var entries = content.Value<BlockListModel>("entries")?
            .Select(e => new TimelineEntry(
                Date:   e.Content.Value<string>("date"),
                Title:  e.Content.Value<string>("title"),
                Body:   e.Content.Value<string>("body")
            ))
            .ToList() ?? [];

        return new TimelineSection(
            BlockClass:  "sg-timeline",
            CssClasses:  _classes.Read(content),
            Spacing:     _spacing.Read(content),
            ElementId:   content.Value<string>("elementId") ?? $"tl-{item.ContentUdi}",
            Heading:     heading,
            Entries:     entries,
            Orientation: content.Value<string>("orientation") ?? "vertical",
            ViewName:    "Partials/elements/informational/Timeline"
        ).ToSectionView();
    }
}
```

Register in `Application/ServiceCollectionExtensions.cs → AddSynergosMappers`:

```csharp
services.AddSingleton<ISectionMapper, TimelineMapper>();
```

### 5. Add the Section record

`Domain/Sections/TimelineSection.cs`:

```csharp
namespace Synergos.CMS.Domain.Sections;

public sealed record TimelineSection(
    string  BlockClass,
    string? CssClasses,
    DomSpacingData Spacing,
    string  ElementId,
    ContentHeadingData Heading,
    IReadOnlyList<TimelineEntry> Entries,
    string  Orientation,
    string  ViewName) : ISection;

public sealed record TimelineEntry(
    string? Date,
    string? Title,
    string? Body);
```

### 6. Create the Razor view

`Views/Partials/elements/informational/Timeline.cshtml`:

```razor
@model Synergos.CMS.Domain.Sections.TimelineSection

<section class="sg-timeline sg-timeline--@(Model.Orientation) @Model.CssClasses">
    @if (!string.IsNullOrWhiteSpace(Model.Heading.Title))
    {
        <h2 class="sg-timeline__heading">@Model.Heading.Title</h2>
    }

    <ol class="sg-timeline__entries">
        @foreach (var e in Model.Entries)
        {
            <li class="sg-timeline__entry">
                @if (!string.IsNullOrWhiteSpace(e.Date))
                {
                    <time>@e.Date</time>
                }
                @if (!string.IsNullOrWhiteSpace(e.Title))
                {
                    <h3>@e.Title</h3>
                }
                @if (!string.IsNullOrWhiteSpace(e.Body))
                {
                    <p>@e.Body</p>
                }
            </li>
        }
    </ol>
</section>
```

### 7. Add SCSS

`scss/elements/_timeline.scss`:

```scss
.sg-timeline {
    &__entries { list-style: none; padding: 0; }
    &__entry   { padding: var(--sg-spacing-4); }
    &--horizontal &__entries { display: flex; gap: var(--sg-spacing-6); }
}
```

Import in `scss/index.scss`:

```scss
@use "elements/timeline";
```

Rebuild CSS (via your sass toolchain) → `wwwroot/css/synergos.css`.

### 8. Bump `SchemaVersion`

`Schema/Constants/SchemaVersion.cs`:

```csharp
public const string Value = "10.0.3";
```

### 9. Build and verify

```bash
dotnet build Synergos.CMS/Synergos.CMS.csproj
```

Run the app. In the backoffice:
- `Settings → Document Types → Timeline` — should exist with `IsElement=true`.
- `Content → any page → Page Sections → Add content` — Timeline should appear in the `Info` group.
- Add a Timeline, fill entries, publish, verify it renders.

## Checklist

- [ ] GUID + alias in `ContentTypeKeys`
- [ ] `Ensure<Element>` in `ElementTypeInitializer`
- [ ] Block registered in `DocumentTypeInitializer.EnsureBlockGridPageSections`
- [ ] `ISectionMapper` implementation with `Alias` matching the type alias
- [ ] Mapper registered in DI
- [ ] Section record in `Domain/Sections/`
- [ ] Razor view in `Views/Partials/elements/<family>/`
- [ ] SCSS in `scss/elements/` and imported in `scss/index.scss`
- [ ] Dictionary keys added for any user-facing strings
- [ ] `SchemaVersion.Value` bumped
- [ ] Build returns 0 errors
- [ ] Boot test: element appears in Block Grid, renders correctly

## Alternative — CDN element

If the element needs interactivity (animations, filter, carousel, etc.),
follow [`add-cdn-macro.md`](add-cdn-macro.md) instead. The mapper emits a
CDN macro partial instead of a native Razor view, and the "view" is a
synergos-* Custom Element hydrated by a JS bundle.

## See also

- [`add-document-type.md`](add-document-type.md)
- [`add-cdn-macro.md`](add-cdn-macro.md)
- [`../rendering/overview.md`](../rendering/overview.md)
- [`../schema/content-model.md`](../schema/content-model.md)
