# Schema Pipeline

The schema pipeline is the authoritative creator of Umbraco types in Synergos.
It runs on startup, is idempotent, and defines every Document Type, Element
Type, Data Type, Composition, Macro, and Dictionary item.

**The pipeline is the source of truth. uSync is a backup format.**

## Why a code-driven pipeline?

Alternative CMSes often rely on database migrations or GUI-authored schema
exported to files. We chose a code-driven pipeline because:

- **Atomic code + schema.** A new Document Type's C# model, its schema, its
  mapper, and its view ship in one commit. No "run the migration" step.
- **Reviewable.** Schema changes show up in code review.
- **Idempotent.** Runs on every boot, creates only what's missing, safe to
  re-run arbitrarily.
- **Tenant-safe.** No deployment-specific data in the repo — `ContentSeeder`
  uses `SeedConfig` which any deployment can override.

## High-level flow

```
On UmbracoApplicationStartedNotification:
  ┌──────────────────────────────────────────────────────────────────┐
  │ SynergosSchemaComposer                                           │
  │                                                                   │
  │  1. Read stored SchemaVersion from Umbraco key/value store       │
  │  2. Compare to SchemaVersion.Value in code                        │
  │  3. If equal → skip pipeline, only run EnsureContentVariations   │
  │  4. If different → run full pipeline (Phase 0 → Phase 12)        │
  │  5. Store new SchemaVersion.Value                                │
  │  6. Always run ContentSeeder (if SeedConfig.Enabled)              │
  └──────────────────────────────────────────────────────────────────┘
```

Driver: `Schema/SynergosSchemaComposer.cs` registered via `IComposer` interface
(auto-discovered by Umbraco's `AddComposers()`).

## Phase order (strict)

Each phase depends on the previous ones. Never re-order without understanding
the dependency graph.

| Phase | Initializer | Creates | Depends on |
|---|---|---|---|
| 0 | `CultureInitializer` | Umbraco languages (es-CO, en-US, …) | — |
| 1a | `DataTypeInitializer` | All data types (TextBox, Dropdown, BlockList, BlockGrid, …) | — |
| 1b | `CompositionInitializer` | Core compositions: Lifecycle, Base, Ownership, Tenant, Access, Versioning, Audit | 1a |
| 2 | `ContentCompositionInitializer` | Heading, Text, Media, Cta, Badge, Collection, Author, Date, Metadata, Embed | 1a, 1b |
| 3 | `TaggingCompositionInitializer` | Tagging | 1a, 1b |
| 4 | `DomCompositionInitializer` | Class, Attributes, Layout, Spacing, Visibility, Variant, LayoutPreset, LayoutProfile | 1a, 1b |
| 5 | `BehaviorCompositionInitializer` | Tracking, Interaction, Navigation, FeatureFlag, Async, Script | 1a, 1b |
| 6a | `SeoCompositionInitializer` | Seo | 1a, 1b |
| 6b | `IntegrationCompositionInitializer` | Integration, AngularMount, MfMount | 1a, 1b |
| 6c | `VisibilityCompositionInitializer` | Visibility | 1a, 1b |
| 6d | `PatchCompositionsIsElement` | Sets `IsElement = true` on all compositions | 1b–6c |
| 7 | `ElementTypeInitializer` | 60+ element types across 10 families (Structural, Text, Action, Media, Info, Composition, Integration, Corporate, Experience, Shop) | 1–6 |
| 7.5 | `PatchMountParamsBlockList` | Wires `ElementMountParam` into `BlockListMountParams` | 7 |
| 7.6 | `PatchTypedItemBlockLists` | Wires typed item BlockLists — `BlockListTestimonialItems` → `ElementInfoTestimonialItem`, `BlockListFaqItems` → `ElementInfoFaqItem`. Enables the "one script for N items" pattern in interactive containers (TestimonialCarousel, AccordionGroup) | 7 |
| 8 | `MediaTypeInitializer` | Media types (Image, File, Folder, Video) | — |
| 9a | `DocumentTypeInitializer` + `ShopInitializer` | SiteRoot, PageBase + 5 Shop types | 1–8 |
| 9b | `PlatformInitializer` + `SiteSettingsInitializer` | Platform infrastructure (PlatformRoot, GlobalSettings, ThemeSettings, SiteSettings, LayoutProfile, SharedContentFolder, NavigationGroup, ReusableBlock, Author, Category, FormDefinition, NavigationItem element, FormField element, FormEmbed element, BlogHighlight element, ArticleList element) | 1–9a |
| 9c | `TaxonomyInitializer` | PageTag, PageTagsFolder | 9b |
| 9d | `BlogInitializer` | BlogHome, BlogPost | 9b |
| 10 | `MacroInitializer` | 60+ macros (11 Native + 49 CDN) | 1–9 |
| 11 | `DictionaryInitializer` | i18n dictionary items | 0 |
| 12 | `FlowEngineInitializer` | FlowSettingsRoot, FlowDefinition | 9b |

After phases: `PatchAllowedChildren()` and `PatchPlatformAllowedChildren()` set
which child types are allowed under each parent.

## SchemaInitializerBase — what you inherit

Most initializers extend `SchemaInitializerBase` which provides:

```csharp
protected readonly IContentTypeService Cts;
protected readonly IDataTypeService    Dts;
protected readonly IShortStringHelper  Ssh;

// Folder management
protected int EnsureRootFolder(string name);
protected int EnsureChildFolder(int parentId, string name);

// Property group & type factories
protected static PropertyGroup Tab(string name, string alias, int sortOrder);
protected PropertyType Prop(string alias, string name, Guid dataTypeKey, int sortOrder,
                            bool mandatory = false, string description = "");

// Composition sync
protected bool SyncCompositions(IContentType ct, params Guid[] compositionKeys);

// Patch helpers
protected static bool PatchTypeDescription(IContentType ct, string description);
protected static bool PatchPropertyDescription(IContentType ct, string alias, string description);
protected static bool PatchCultureVariation(IContentType ct, params string[] propertyAliases);
protected bool TryPatchExistingContentType(Guid key, string name, int folderId, string description);
```

Use these helpers. They encode idempotency correctly. Don't write raw
`Cts.Save(ct)` without guarding for existence first.

## Idempotency patterns

### Create-if-missing (most common)

```csharp
if (Cts.Get(ContentTypeKeys.MyType) is not null) return;

var ct = new ContentType(Ssh, folderId) { /* ... */ };
// build tabs & properties
Cts.Save(ct);
```

### Create-or-patch (for types that may have additional fields added later)

```csharp
const string description = "My type description";
if (TryPatchExistingContentType(ContentTypeKeys.MyType, "My Type", folderId, description)) return;

// not found — create fresh
var ct = new ContentType(Ssh, folderId) { /* ... */ };
Cts.Save(ct);
```

`TryPatchExistingContentType` only patches `Name`, `ParentId`, `Description`.
It does **not** add new properties to existing types. For that, use the
"ensure missing properties" pattern:

```csharp
var existing = Cts.Get(ContentTypeKeys.MyType);
if (existing is not null)
{
    var dirty = EnsureMyTypeMissingProps(existing);
    if (dirty) Cts.Save(existing);
    return;
}
```

See `SiteSettingsInitializer.EnsureSiteSettingsMissingProps()` for a full
example. It covers the case where you add a new property to an existing
Document Type in a new version.

### TrySave (defensive — for types that may have orphaned rows)

```csharp
private void TrySave(IContentType ct)
{
    try { Cts.Save(ct); }
    catch (Exception ex)
    {
        var existing = Cts.Get(ct.Key);
        if (existing is not null)
        {
            _logger.LogWarning(ex,
                "Initializer.TrySave: '{Alias}' exists (Id={Id}) after save error — continuing.",
                ct.Alias, existing.Id);
            return;
        }
        _logger.LogError(ex,
            "Initializer.TrySave: '{Alias}' ({Key}) NOT created ({ExType}).",
            ct.Alias, ct.Key, ex.GetType().Name);
        throw;
    }
}
```

Used by `ElementTypeInitializer` and `FlowEngineInitializer` where partial
previous runs could leave orphaned rows with matching keys.

## SchemaVersion semantics

`Schema/Constants/SchemaVersion.cs`:

```csharp
public static class SchemaVersion
{
    public const string Key   = "Synergos.Schema.Version";
    public const string Value = "10.13.0"; // as of 2026-04-14
}
```

**When to bump:**

| Change | Bump |
|---|---|
| Add a new property to an existing type | PATCH (10.0.1 → 10.0.2) |
| Add a new Document Type | PATCH |
| Add a new composition | PATCH |
| Add a new Element Type | PATCH |
| Rename a property (destructive) | MINOR (10.0.x → 10.1.0) — editors must re-enter data |
| Remove a property | MINOR |
| Rearrange pipeline phase order | MAJOR (10.x.x → 11.0.0) |

**When NOT to bump:**

- Refactoring code that doesn't change schema output.
- Moving initializers between files (e.g. the `ShopInitializer` split).
- Renaming classes without changing their schema emission.
- Pure documentation changes.

## Logging

On every boot, look for these log lines to verify the pipeline ran:

```
Synergos schema mismatch (stored=none, expected=10.0.1). Running pipeline.
Synergos schema complete (10.0.1).
ContentSeeder: verifying content tree...
ContentSeeder: seed complete.
```

If you see:
```
Synergos schema up to date (10.0.1).
```
…then the pipeline was skipped because the stored version matches. Bump
`SchemaVersion.Value` to force a re-run.

## Adding a new phase

1. Create `Schema/Initializers/MyFeatureInitializer.cs` extending `SchemaInitializerBase`.
2. Implement `Initialize()` with idempotent logic.
3. Register in `SynergosSchemaComposer.Handle()` at the correct dependency position (after all its dependencies, before anything that depends on it).
4. Bump `SchemaVersion.Value`.
5. Add to the phase table in this doc.

## Troubleshooting

### "DataType X not found" on first boot

Your initializer runs before `DataTypeInitializer`. Move its registration
later in `SynergosSchemaComposer.Handle()`.

### Properties don't show up after bump

- Confirm you bumped `SchemaVersion.Value`.
- Check stored version in DB: Umbraco's `UmbracoKeyValue` table, key `Synergos.Schema.Version`.
- If stored matches your bumped value, the Umbraco cache may be stale. Restart.
- If you added properties to an existing type, make sure you used the "ensure missing properties" pattern — `TryPatchExistingContentType` alone won't add them.

### Content Type creation fails with "duplicate key"

Usually means a previous run created partial state. Use `TrySave` (see pattern
above) or delete the offending row from `umbracoContentType` and re-run.

### "No implementation for IContentTypeService" etc.

Those are Umbraco services. They're injected into `SchemaServices` (an aggregate
record in `SynergosSchemaComposer.cs`) and reach the initializer via its base
class constructor. If you wrote an initializer that directly injects
`IContentTypeService`, refactor to extend `SchemaInitializerBase` and receive
it through `SchemaServices`.

## Container patterns (10.6.0+)

Containers that aggregate N children (card grid, logo cloud, testimonial
carousel, FAQ accordion) follow two distinct patterns — picked per use case:

### Pattern A — Block Grid Areas (SSR layout-only)

For presentation-only grids where children render independently and no
shared JS is needed: **`EnsureAreaContainer()` helper + `AreaContainerBlock()` block config + typed Area with `ElementTypeKey` allowance**.

Used by: `ElementCompCardGrid`, `ElementCompLogoCloudGrid`.

- Editor sees drag-and-drop area in backoffice (native Umbraco 13 UX).
- Each child card/logo renders SSR via its own partial — **zero scripts** emitted.
- The parent view (`Views/Partials/blockgrid/Components/<alias>.cshtml`) wraps
  the area output with the grid CSS (cols/gap).
- No section mapper for the parent — `items.cshtml` detects `item.Areas.Any()`
  and routes to the structural branch.

### Pattern B — Typed BlockList (interactive web component)

For interactive carousels/accordions where a shared JS bundle orchestrates
all items: **dedicated `BlockListXxxItems` DataType constrained via Phase 7.6
to one child element type + element with its own `items` property + mapper
that aggregates children into a single CDN config**.

Used by: `ElementCompTestimonialCarousel`, `ElementCompAccordionGroup`.

- One `<script type="module">` per container (not per item).
- Children serialized as `items: [...]` inside the container's JSON config.
- Web component iterates items internally (no hydration of N instances).

### When to pick which

| Use case | Pattern |
|---|---|
| Layout grid, cards/logos render independently | A — Areas + SSR |
| Interactive UX (swipe carousel, toggle accordion) | B — Typed BlockList + web component |

## CDN script deduplication (10.10.0+)

`IEmittedBundleTracker` (Domain, request-scoped Infrastructure impl) dedupes
`<script type="module">` tags when the same CDN element appears N times on
a page. All CDN macros and Blog partials wrap their script in
`@if (Bundles.TryClaim("bundle-name")) { ... }`.

Companion `BundlePrescanner` (`Application/Rendering/`) walks the BlockGrid
(plus nested Areas and MacroHost's `macroAlias`) **before** rendering to
emit `<link rel="modulepreload">` hints in `<head>`. Enabled on PageBase,
BlogPost, BlogHome, BlogCategory. See `CdnBundleRegistry` for the
alias → bundle mapping (small by design — only interactive typed containers).

## See also

- [`content-model.md`](content-model.md) — inventory of types created.
- [`guid-registry.md`](guid-registry.md) — GUID allocation policy.
- [`../recipes/add-document-type.md`](../recipes/add-document-type.md) — step-by-step recipe.
