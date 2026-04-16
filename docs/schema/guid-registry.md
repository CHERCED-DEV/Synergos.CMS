# GUID Registry

Policy for allocating, validating, and evolving the GUIDs that identify every
Content Type, Data Type, Media Type, and Block/Element instance in Synergos.

## Why GUIDs matter

Umbraco keys every entity by GUID. If the same GUID is used for two different
things, one of them fails to create with a `UNIQUE constraint` error. Worse:
if the conflict is between a Content Type GUID and a Block UDI
(`umb://element/<guid>`), it won't show in `umbracoNode` but **will** cause
unique-constraint violations at runtime.

GUID collisions have happened. Protocol exists to prevent them.

## Central registry files

- `Schema/Constants/ContentTypeKeys.cs` — Document Type + Element Type GUIDs + nested `Aliases` class with string aliases.
- `Schema/Constants/DataTypeKeys.cs` — Data Type GUIDs (TextBox, BlockGrid, etc.).
- `Schema/Constants/MediaTypeKeys.cs` — Media Type GUIDs.
- `Schema/Constants/SchemaVersion.cs` — version stamp.

**Never invent a GUID outside these files.** If you find yourself writing
`new Guid("…")` anywhere else, stop and add it to the registry.

## Reserved ranges (high nibbles)

Synergos uses prefix-based range allocation. First 8 hex chars encode the family.
Tabla sincronizada con el estado real del codebase a **2026-04-14**.

### Data Types (`a*`)

| Prefix | Rango en uso | Familia | Notas |
|---|---|---|---|
| `a1000001–a1000080` | 68 GUIDs | Data types (TextBox, Dropdowns, Pickers, BlockList, BlockGrid, etc.) | Próximo slot libre: `a1000081`. Watch: `a1000073` ya colisionó (ver incidente). |
| `a2*` | 5 GUIDs | Data types estructurales (BlockGridPageSections, BlockListNavItems, BlockListMountParams…) | |

### Compositions (`b1–b8`)

| Prefix | Count | Familia |
|---|---|---|
| `b1*` | 7 | Core (`CompCoreBase`, `CompCoreLifecycle`, `CompCoreOwnership`, `CompCoreTenant`, `CompCoreAccess`, `CompCoreVersioning`, `CompCoreAudit`) |
| `b2*` | 12 | Content (`CompContentText`, `CompContentMedia`, `CompContentCta`, `CompContentHeading`, `CompContentBadge`, `CompContentCollection`, `CompContentAuthor`, `CompContentDate`, `CompContentMetadata`, `CompContentEmbed`, `CompContentPricing`, `CompContentLocation`) |
| `b3*` | 8 | DOM (`CompDomClass`, `CompDomAttributes`, `CompDomLayout`, `CompDomSpacing`, `CompDomVisibility`, `CompDomVariant`, `CompDomLayoutPreset`, `CompDomLayoutProfile`) |
| `b4*` | 6 | Behavior (`CompBehaviorTracking`, `CompBehaviorInteraction`, `CompBehaviorNavigation`, `CompBehaviorFeatureFlag`, `CompBehaviorAsync`, `CompBehaviorScript`) |
| `b5*` | 1 | SEO (`CompSeo`) |
| `b6*` | 3 | Integration (`CompIntegration`, `CompAngularMount`, `CompMfMount`) |
| `b7*` | 1 | Visibility (`CompVisibility`) |
| `b8*` | 1 | Tagging (`CompTagging`) |
| `b9*` | 10 | Block Grid groups (`GroupLayout`, `GroupText`, `GroupAction`, `GroupMedia`, `GroupInfo`, `GroupComponents`, `GroupIntegration`, `GroupCorporate`, `GroupBlog`, `GroupExperiences`) |

### Element Types (`c3–cf`)

| Prefix | Count | Familia | Alias prefix |
|---|---|---|---|
| `c3*` | 12 | Structural + Layout Presets | `elementStruct*`, `layoutPreset*` |
| `c4*` | 6 | Text | `elementText*` |
| `c5*` | 3 | Action | `elementAction*` |
| `c6*` | 6 | Media | `elementMedia*` |
| `c7*` | 8 | Info | `elementInfo*` |
| `c8*` | 12 | Composition | `elementComp*` (Card, Hero, CtaBanner, MediaTextSplit, BlogHighlight, ArticleList, FormBlock, CardGrid, LogoCloudGrid, TestimonialCarousel, AccordionGroup) |
| `c9*` | 6 | Integration | `elementInt*` (ScriptEmbed, IframeEmbed, ExternalWidget, AngularHost, MfHost, MacroHost) |
| `ca*` | 9 | Corporate | `elementCorp*` (TabGroup, AlertBar, BannerSlider, NewsletterForm, SocialShare, DataTable, ContactInfo, MapEmbed, MissionBlock) |
| `cb*` | 1 | Navigation | `elementNavItem` |
| `cc*` | 2 | Forms | `elementFormField`, `elementFormEmbed` |
| `cd*` | 1 | Mount | `mountParam` |
| `ce*` | 9 | Experience | `experience*` (FeatureJourney, InsightExplorer, MediaExplorer, ContentCarousel, QuizFlow, FilterBoard, RatingWidget, CountdownClock, NotificationStack) |
| `cf*` | 8 | Shop | `elementShop*` (ProductCard, ProductGrid, ProductDetail, CartSummary, CartItem, PriceDisplay, QuantitySelector, VariantPicker) |

### Document Types (`d2–d8`)

| Prefix | Count | Familia |
|---|---|---|
| `d2*` | 8 | Site types (SiteRoot, PageBase, PageBare, ArticlePage, ReusableBlock, NavigationGroup, SharedContentFolder, Author) |
| `d3*` | 8 | Platform (PlatformRoot, GlobalSettings, SiteSettings, ThemeSettings, LayoutProfile, FlowSettingsRoot, FlowDefinition + 1) |
| `d4*` | 4 | Blog (BlogHome, BlogPost, Category + 1) |
| `d5*` | 1 | Forms (FormDefinition) |
| `d6*` | 2 | Tagging (PageTag, PageTagsFolder) |
| `d8*` | 5 | Shop (ShopRoot, ShopCatalogPage, ShopCategoryPage, ShopProductPage, ShopCartPage) |

### Media + BlockGrid areas + Flow Engine

| Prefix | Count | Familia |
|---|---|---|
| `e1*` | 5 | Media Types (MediaImageAsset, MediaVideoAsset, MediaFolder, etc.) |
| `fa*` | 19 | BlockGridAreaKeys (SectionContent, GridColumns, ColumnContent, StackContent, ContainerContent, Preset1–4ColMain/Left/Right, PresetMain/Sidebar, **CardGridCards**, **LogoCloudGridLogos**) |
| `fe*` | 4 | Flow Engine (FlowSettingsRoot, FlowDefinition, FlowExecutionModeKeys, `fe000001`/`fe000002` — watch, previously collided con Block UDIs) |

**Exact allocations per range live in the Keys files.** Read them before assigning.

## Changelog — Últimas adiciones

Trackea GUIDs nuevos desde el último sync del registry. Cuando añadas un GUID,
agrega una línea aquí.

**2026-04-14** — Containers tipados y area-based:
- `a1000076` — `SelectBlogPostType` (DataType dropdown)
- `a1000077` — `BlockListCardItems` — *retirado*, CardGrid migró a area-based
- `a1000078` — `BlockListLogoItems` — *retirado*, LogoCloudGrid migró a area-based
- `a1000079` — `BlockListTestimonialItems` (BlockList tipado, Testimonial Carousel)
- `a1000080` — `BlockListFaqItems` (BlockList tipado, Accordion Group)
- `c8000013` — `ElementCompCardGrid` (area-based container)
- `c8000014` — `ElementCompLogoCloudGrid` (area-based container)
- `c8000015` — `ElementCompTestimonialCarousel` (BlockList container, web component)
- `c8000016` — `ElementCompAccordionGroup` (BlockList container, web component)
- `fa000006` — `BlockGridAreaKeys.CardGridCards` (typed area, solo `elementCompCard`)
- `fa000007` — `BlockGridAreaKeys.LogoCloudGridLogos` (typed area, solo `elementMediaLogoItem`)
- `b2000011` — `CompContentPricing`
- `b2000012` — `CompContentLocation`
- `d2000007` — `PageBare` (page base sin layout)

**Retirados (c8000005–c8000009)** — legacy containers con `CompContentCollection` genérico; reemplazados por los nuevos typed/area containers. GUIDs **no reutilizar** para evitar conflicto con contenido histórico:
- `c8000005` — `ElementCompFaqList`
- `c8000006` — `ElementCompTestimonialList`
- `c8000007` — `ElementCompLogoCloud`
- `c8000009` — `ElementCompAccordion`

## The four-source collision check

Before writing a new GUID, verify it's unused in all four places:

```bash
GUID="a7000001-0000-0000-0000-000000000000"

# 1. C# constants
grep -r "$GUID" Synergos.CMS/Schema/Constants/

# 2. Existing uSync configs (if present)
grep -r "$GUID" Synergos.CMS/uSync/v9/

# 3. JSON of Block Grid / Block List data types — block UDIs
#    These are embedded in Configuration.PreValues of BlockList/BlockGrid DataTypes.
grep -rE "umb://element/$GUID" Synergos.CMS/

# 4. Any existing database (if you have a populated dev DB)
#    sqlite3 Synergos.CMS/umbraco/Data/Synergos.sqlite.db "SELECT uniqueId FROM umbracoNode WHERE uniqueId = '$GUID';"
```

All four must return nothing. Only then add the GUID to the registry.

## Incidents and what they teach

### 2026-04-08 — `f1000073` / `a1000073` collision
Two different initializers assigned identical GUIDs to different Content Types.
The second created fine locally but failed on a fresh DB with a `UNIQUE
constraint` on `umbracoContentType`. **Lesson:** grep the registry files, not
just one file.

### 2026-04-08 — `fe000001` / `fe000002` Block UDI collision
A Block inside `BlockListMountParams` JSON shared a UDI with a new Element Type.
No `umbracoNode` row existed for the block UDI, but Umbraco still enforces
uniqueness at save time. **Lesson:** grep the JSON of BlockList/BlockGrid
DataTypes too — Block UDIs don't appear in the database as rows.

## When assigning a new GUID

Copy this checklist before writing `new Guid("…")`:

- [ ] Is my feature part of an existing family? Use the next free slot in its range.
- [ ] Is it a brand new family? Pick an unused two-char prefix and document it in the table above + this doc.
- [ ] Have I run the four-source grep? (C# constants, uSync, block JSON, DB)
- [ ] Have I added it to the relevant `*Keys.cs` file with an XML doc comment describing what it's for?
- [ ] Have I bumped `SchemaVersion.Value`?

## Naming convention within Keys files

```csharp
namespace Synergos.CMS.Schema.Constants;

public static class ContentTypeKeys
{
    // Format: 8-4-4-4-12 hex with the family prefix as the first 2 chars.
    // Keep groups sorted by prefix for easy scanning.

    // ── Core platform (a1*) ─────────────────────────────────────────────
    public static readonly Guid PlatformRoot        = new("a1000001-…");
    public static readonly Guid SiteRoot            = new("a1000002-…");
    public static readonly Guid GlobalSettings      = new("a1000010-…");
    public static readonly Guid SiteSettings        = new("a1000020-…");
    public static readonly Guid ThemeSettings       = new("a1000021-…");
    public static readonly Guid LayoutProfile       = new("a1000022-…");

    // ── Compositions (a2*-a6*) ──────────────────────────────────────────
    public static readonly Guid CompCoreBase        = new("a2000001-…");
    // …

    /// <summary>String aliases used in Umbraco's content type alias slot.</summary>
    public static class Aliases
    {
        public const string PlatformRoot       = "platformRoot";
        public const string SiteRoot           = "siteRoot";
        public const string SiteSettingsAlias  = "siteSettings";
        // …
    }
}
```

## Historical context

See `synergos-guid-registry.md` at the workspace root for the full history of
allocations across migrations from earlier versions. This file captures policy;
that one captures history.

## See also

- [`pipeline.md`](pipeline.md) — how GUIDs are used by initializers.
- [`content-model.md`](content-model.md) — inventory of every type.
- [`../../CLAUDE.md`](../../CLAUDE.md) §3.4 — GUID rules for agents.
