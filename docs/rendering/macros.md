# Macros — Native vs CDN

Synergos uses Umbraco Macros for two purposes:

1. **Inline composition in Rich Text Editors** — an editor drops a `{macro}`
   into a paragraph to embed a CTA button, image, or video.
2. **Block Grid element rendering** — the BlockGrid `MacroDispatcher`
   delegates to the macro corresponding to the element type.

Two families. Same mechanism.

## Family: Native

**Alias prefix:** `macro*` (e.g. `macroCtaButton`, `macroImage`).
**View location:** `Views/MacroPartials/Native/<Name>.cshtml`.
**Rendering:** SSR Razor only. No JavaScript.

Use for:
- Rich-text embeds where editors need consistent styling.
- Backwards-compatible inserts (old content references these).
- Simple, presentational components with no interactivity.

### Example — `macroCtaButton`

Registration (in `MacroInitializer.cs`):
```csharp
EnsureMacro("macroCtaButton", "Macro.CtaButton", "Native/CtaButton",
    ("label",   "Label",   TextBox),
    ("url",     "URL",     TextBox),
    ("target",  "Target",  TextBox),
    ("variant", "Variant", TextBox));
```

Partial (`Views/MacroPartials/Native/CtaButton.cshtml`):
```razor
@inherits Umbraco.Cms.Web.Common.Macros.PartialViewMacroPage
@{
    var p = Model.MacroParameters;
    string? Get(string k) => p.ContainsKey(k) ? p[k]?.ToString() : null;

    var label   = Get("label")   ?? "Learn more";
    var url     = Get("url")     ?? "#";
    var target  = Get("target")  ?? "_self";
    var variant = Get("variant") ?? "primary";
}
<a class="sg-cta sg-cta--@variant" href="@url" target="@target">@label</a>
```

Zero JavaScript, server-rendered, styled by `wwwroot/css/synergos.css`.

## Family: CDN

**Alias prefix:** `cdn*` (e.g. `cdnCard`, `cdnFeatureGrid`, `cdnProductCard`).
**View location:** `Views/MacroPartials/<Family>/Cdn<Name>.cshtml`.
**Rendering:** server emits `<script type="module">` + `<synergos-*>` tag with
a JSON config attribute. Browser hydrates via Custom Elements.

Families:
- **Modules** — larger compound components (Hero, Banner, FeatureGrid, FaqSection, LogoCloud, TestimonialSection, TabGroup, DataTable, Section, ScriptEmbed, BannerSlider)
- **Compositions** — medium reusable groupings (Card, MediaText, AlertBar, ButtonGroup, CtaGroup, FaqItem, FeatureItem, GalleryItem, IframeEmbed, InfoBlock, KeyValue, LogoItem, NewsletterForm, SocialShare, TestimonialItem, TimelineItem, ExternalWidget, Stat, PricingCard)
- **Primitives** — atomic building blocks (Badge, ButtonContainer, Column, ContainerBlock, Divider, Grid, IconBlock, ImageBlock, LinkBlock, Spacer, Stack, TextBlock, VideoBlock, Avatar)
- **Experiences** — highly interactive (FeatureJourney, InsightExplorer, MediaExplorer, ContentCarousel, QuizFlow, FilterBoard, RatingWidget, CountdownClock, NotificationStack)
- **Shop** — e-commerce (ProductCard, ProductGrid, ProductDetail, CartSummary, CartItem, PriceDisplay, QuantitySelector, VariantPicker)

### Example — `cdnCard`

Registration (in `MacroInitializer.cs`):
```csharp
EnsureMacro("cdnCard", "Cdn.Card", "Compositions/CdnCard",
    ("title",     "Title",      TextBox),
    ("subtitle",  "Subtitle",   TextBox),
    ("body",      "Body",       TextArea),
    ("imageSrc",  "Image URL",  TextBox),
    ("imageAlt",  "Image Alt",  TextBox),
    ("ctaLabel",  "CTA Label",  TextBox),
    ("ctaUrl",    "CTA URL",    TextBox),
    ("badgeText", "Badge Text", TextBox),
    ("badgeType", "Badge Type", TextBox),
    ("variant",   "Variant",    TextBox),
    ("theme",     "Theme",      TextBox));
```

Partial (`Views/MacroPartials/Compositions/CdnCard.cshtml`):
```razor
@inherits Umbraco.Cms.Web.Common.Macros.PartialViewMacroPage
@using System.Text.Json
@using System.Text.Json.Serialization
@using Synergos.CMS.Application.Cdn.Configs
@inject Synergos.CMS.Application.Rendering.IElementUrlResolver ElementUrl
@inject Synergos.CMS.Domain.Services.IDictionaryCache Dict
@{
    var p = Model.MacroParameters;
    string? Get(string k) => p.ContainsKey(k) ? p[k]?.ToString() : null;

    var opt = new JsonSerializerOptions {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase
    };
    var translations = Dict.GetAll();

    var cfg = JsonSerializer.Serialize(new CardCdnConfig(
        Title:        Get("title"),
        Subtitle:     Get("subtitle"),
        Body:         Get("body"),
        ImageSrc:     Get("imageSrc"),
        ImageAlt:     Get("imageAlt"),
        CtaLabel:     Get("ctaLabel"),
        CtaUrl:       Get("ctaUrl"),
        BadgeText:    Get("badgeText"),
        BadgeType:    Get("badgeType"),
        Variant:      Get("variant"),
        Theme:        Get("theme"),
        Translations: translations
    ), opt);
}
<script src="@ElementUrl.ResolveBundle("card")" type="module" defer></script>
<synergos-card config='@cfg' class="sg-cdn sg-cdn--card"></synergos-card>
```

The `CardCdnConfig` record lives in `Application/Cdn/Configs/CompositionCdnConfigs.cs`.

## Config DTO pattern

CDN elements receive ONE config object via the `config` attribute. The
server-side representation is a typed C# record. The UI side deserializes it.

```csharp
// Application/Cdn/Configs/CompositionCdnConfigs.cs
namespace Synergos.CMS.Application.Cdn.Configs;

public sealed record CardCdnConfig(
    string? Title,
    string? Subtitle,
    string? Body,
    string? ImageSrc,
    string? ImageAlt,
    string? CtaLabel,
    string? CtaUrl,
    string? BadgeText,
    string? BadgeType,
    string? Variant,
    string? Theme,
    IReadOnlyDictionary<string, string>? Translations);
```

JSON serialization rules (always):
- `PropertyNamingPolicy = JsonNamingPolicy.CamelCase`
- `DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull`

Null-valued fields are omitted from the JSON so the element-side default wins.

## Translations — `IDictionaryCache`

CDN elements receive the full Synergos dictionary as `translations` in their
config. The element reads keys for its UI strings (button labels, aria labels,
error messages, etc.). This is how CDN elements respect the CMS's i18n without
needing their own localization infrastructure.

Adding a new dictionary key means it becomes available to all CDN elements
automatically. No element rebuild required.

## MacroDispatcher and Block Grid integration

When a Block Grid element renders, `Views/Partials/blockgrid/default.cshtml` →
`items.cshtml` → `Partials/elements/MacroDispatcher.cshtml`:

```razor
@model Synergos.CMS.Application.Output.SectionView
@{
    var classes = new string?[] { "sg-block", Model.Section.BlockClass, Model.CssClasses };
}
<div id="@Model.ElementId"
     class="@await Html.PartialAsync("Partials/elements/shared/_Classes", classes)">
    @await Html.PartialAsync(Model.Section.ViewName, Model.Section)
</div>
```

`Model.Section.ViewName` is set by the element's `ISectionMapper`. Common values:
- `Partials/blockgrid/Components/elementStructSection` — structural block
- `MacroPartials/Compositions/CdnCard` — CDN macro partial

The dispatcher is agnostic to SSR vs CDN — it just resolves a partial name.

## Editing macros in uSync vs code

`MacroInitializer` is the source of truth. uSync will export current macros
when you export, but you should **not** rely on uSync to recreate macros on a
fresh DB — the initializer is what runs on pipeline.

Adding a new macro = adding a call in `MacroInitializer.Initialize()` + the
partial file. Bump `SchemaVersion`.

Removing a macro = add its alias to `CleanupLegacyMacros()` to be removed on
next pipeline run.

## See also

- [`overview.md`](overview.md) — SSR vs CDN rendering.
- [`cdn-integration.md`](cdn-integration.md) — URL resolution, registry.
- [`../recipes/add-cdn-macro.md`](../recipes/add-cdn-macro.md) — step-by-step recipe.
