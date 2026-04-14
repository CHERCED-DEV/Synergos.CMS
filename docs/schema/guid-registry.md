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

| Prefix | Family | Notes |
|---|---|---|
| `a1*` | Core platform types: PlatformRoot, SiteRoot, PageBase, GlobalSettings, SiteSettings, ThemeSettings | Watch for `a1000073` — previously collided |
| `a2*` | Compositions — Core (Lifecycle, Base, Ownership, …) | |
| `a3*` | Compositions — Content (Heading, Text, Media, …) | |
| `a4*` | Compositions — DOM (Class, Attributes, Layout, …) | |
| `a5*` | Compositions — Behavior (Tracking, Interaction, …) | |
| `a6*` | Compositions — Seo, Integration, Visibility | |
| `b1*` | Element types — Structural | |
| `b2*` | Element types — Textual | |
| `b3*` | Element types — Action | |
| `b4*` | Blog domain (BlogHome, BlogPost, Category) + Element types — Blog | |
| `b5*` | Element types — Media | |
| `b6*` | Element types — Informational | |
| `b7*` | Element types — Composition (Card, Hero, Banner, …) | |
| `b8*` | Element types — Integration (Mount, Embed) | |
| `b9*` | Element types — Corporate / Experience | |
| `c1*–c9*` | Data types by editor family | |
| `d1*–d9*` | Document types per domain (blog, forms, corporate, shop) | |
| `e1*–e9*` | Media types + media compositions | |
| `f1*` | Flow Engine types (FlowSettingsRoot, FlowDefinition, tracks) | Watch — previously collided |
| `fe*` | Block/Element instance UDIs inside Block Grid / Block List JSON | Watch — collided with f1 prefix in 2026-04-08 |

**Exact allocations per range live in the Keys files.** Read them before assigning.

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
