# Seed Configuration

How `ContentSeeder` uses `SeedConfig` + `SeedTheme` + `SeedPage` to bootstrap
a fresh install with brand defaults — and how any deployment can rebrand by
overriding config without touching code.

## Goal

Zero hardcoded brand values in `ContentSeeder.cs`. The seeder reads from
`SeedConfig`, which has sensible defaults in C# and is overridable per
environment via `appsettings.json`.

## Types involved

### `Configuration/SeedConfig.cs`

Top-level config. Bound from `Synergos:Seed` section.

```csharp
public sealed class SeedConfig
{
    public const string SectionPath = "Synergos:Seed";

    public bool Enabled { get; init; } = true;

    // Identity
    public string PlatformName    { get; init; } = "Synergos Platform";
    public string SiteName        { get; init; } = "Synergos";
    public string SiteTagline     { get; init; } = "…";
    public string SiteDisplayName { get; init; } = "Synergos";
    public string SiteTaglineExt  { get; init; } = "…";
    public string SiteCulture     { get; init; } = "es-CO";

    // Contact
    public string ContactEmail    { get; init; } = "…";
    public string ContactPhone    { get; init; } = "…";
    public string ContactAddress  { get; init; } = "…";

    // Social
    public string FacebookUrl   { get; init; } = "…";
    public string LinkedInUrl   { get; init; } = "…";
    public string TwitterUrl    { get; init; } = "…";
    public string YouTubeUrl    { get; init; } = "…";

    // SEO
    public string SeoTitleSuffix        { get; init; } = " | Synergos";
    public string SeoDefaultDescription { get; init; } = "…";
    public string GlobalSeoDescription  { get; init; } = "…";

    // Forms
    public string FormRecipientEmail { get; init; } = "contact@example.com";

    // Header
    public string HeaderCtaLabel { get; init; } = "Contáctanos";

    // Theme defaults
    public SeedTheme Theme { get; init; } = new();

    // Business pages
    public List<SeedPage> Pages { get; init; } = [
        new() { Name = "Home",      Subtitle = "…", SeoTitle = "…", SeoDescription = "…", OgTitle = "…", OgDescription = "…" },
        new() { Name = "Nosotros",  …  },
        new() { Name = "Servicios", …  },
        new() { Name = "Contacto",  …  }
    ];
}
```

### `Configuration/SeedTheme.cs`

Brand theme values written to `ThemeSettings` on first boot.

```csharp
public sealed class SeedTheme
{
    // Colors
    public string ColorPrimary      { get; init; } = "#1a56db";
    public string ColorSecondary    { get; init; } = "#0e4fb5";
    public string ColorAccent       { get; init; } = "#0694a2";
    public string ColorBackground   { get; init; } = "#ffffff";
    public string ColorSurface      { get; init; } = "#f9fafb";
    public string ColorText         { get; init; } = "#111827";
    public string ColorTextInverse  { get; init; } = "#ffffff";
    public string ColorBorder       { get; init; } = "#e5e7eb";
    public string ColorSuccess      { get; init; } = "#0e9f6e";
    public string ColorWarning      { get; init; } = "#ff5a1f";
    public string ColorError        { get; init; } = "#f05252";

    // Typography
    public string FontFamilyHeading { get; init; } = "Manrope, sans-serif";
    public string FontFamilyBody    { get; init; } = "Manrope, sans-serif";
    public string FontBaseSize      { get; init; } = "16px";

    // Spacing & Layout
    public string ContainerMaxWidth { get; init; } = "1280px";
    public string BorderRadius      { get; init; } = "8px";
    public string SectionPaddingY   { get; init; } = "5rem";

    // Component variants (DropDown.Flexible aliases)
    public string ButtonStyle       { get; init; } = "rounded";
    public string CardStyle         { get; init; } = "elevated";
    public string HeaderStyle       { get; init; } = "sticky";
}
```

### `Configuration/SeedPage.cs`

A single page's seed spec.

```csharp
public sealed class SeedPage
{
    public string Name           { get; init; } = string.Empty;
    public string Subtitle       { get; init; } = string.Empty;
    public string SeoTitle       { get; init; } = string.Empty;
    public string SeoDescription { get; init; } = string.Empty;
    public string OgTitle        { get; init; } = string.Empty;
    public string OgDescription  { get; init; } = string.Empty;
}
```

## How ContentSeeder consumes it

```csharp
// ContentSeeder.PatchThemeSettings
private void PatchThemeSettings(IContent node)
{
    var t = _config.Theme;
    var dirty = false;

    dirty |= SetIfEmpty(node, "colorPrimary",     t.ColorPrimary);
    dirty |= SetIfEmpty(node, "colorSecondary",   t.ColorSecondary);
    // … and so on for every theme field

    if (dirty) _contentService.SaveAndPublish(node, userId: SuperUserId);
}
```

`SetIfEmpty` only writes when the property is currently empty — editor values
are never overwritten.

```csharp
// ContentSeeder.EnsureBusinessPages
private void EnsureBusinessPages(int rootId)
{
    if (_config.Pages.Count == 0) return;

    var existing = /* names of existing children */;

    for (var i = 0; i < _config.Pages.Count; i++)
    {
        var spec = _config.Pages[i];
        if (string.IsNullOrWhiteSpace(spec.Name)) continue;

        EnsurePage(rootId, existing, spec.Name, PageBaseAlias, "PageBase", i, page =>
        {
            if (!string.IsNullOrWhiteSpace(spec.Subtitle))
                page.SetValue("pageSubtitle", spec.Subtitle, culture: SeedCulture);
            if (!string.IsNullOrWhiteSpace(spec.SeoTitle))
                page.SetValue("seoTitle", spec.SeoTitle);
            if (!string.IsNullOrWhiteSpace(spec.SeoDescription))
                page.SetValue("seoDescription", spec.SeoDescription);
        });
    }
}
```

Empty strings are skipped so config can partially override — e.g. set a new
`Subtitle` without specifying the rest.

## Overriding per deployment

### Example: different brand

`appsettings.Local.json` for the ClientX deployment:

```json
{
  "Synergos": {
    "Seed": {
      "SiteName":        "ClientX",
      "SiteDisplayName": "ClientX",
      "SiteTagline":     "Soluciones empresariales de próxima generación",
      "SiteCulture":     "en-US",
      "ContactEmail":    "hello@clientx.com",
      "FacebookUrl":     "https://facebook.com/clientx",
      "Theme": {
        "ColorPrimary":      "#c10037",
        "ColorSecondary":    "#8a0028",
        "FontFamilyHeading": "Inter, sans-serif",
        "FontFamilyBody":    "Inter, sans-serif"
      },
      "Pages": [
        { "Name": "Home",      "Subtitle": "Welcome",     "SeoTitle": "ClientX — Home",     "SeoDescription": "…" },
        { "Name": "About",     "Subtitle": "Our story",   "SeoTitle": "About ClientX",      "SeoDescription": "…" },
        { "Name": "Services",  "Subtitle": "What we do",  "SeoTitle": "ClientX Services",   "SeoDescription": "…" },
        { "Name": "Contact",   "Subtitle": "Get in touch","SeoTitle": "Contact ClientX",    "SeoDescription": "…" }
      ]
    }
  }
}
```

Fresh boot produces a ClientX-branded site with English page names, custom
colors, and new page structure — without any code changes.

### Example: disable seeding

For a staging environment where you want the schema but an empty content tree:

```json
{
  "Synergos": {
    "Seed": {
      "Enabled": false
    }
  }
}
```

The schema pipeline still runs. `ContentSeeder` logs one line and exits.

### Example: add a new page after boot

Editors add pages via the backoffice. If you want a new page pre-seeded on
**every** future fresh boot (onboarding docs, status pages, etc.), add it to
`SeedConfig.Pages` defaults or an environment-specific override.

## Rules for adding a new seed property

1. Add a property to `SeedConfig`, `SeedTheme`, or `SeedPage` with a sensible default.
2. Add a JSON entry to `appsettings.json` so new installs reflect the default
   (optional — the C# default is already the "new install" value).
3. Consume in `ContentSeeder` via `_config.X`:
   ```csharp
   dirty |= SetIfEmpty(node, "myField", _config.MyField);
   ```
4. **Never** hardcode a string default inside `ContentSeeder`. The whole
   point of `SeedConfig` is that there's one place to change defaults.

## Idempotency guarantees

`ContentSeeder` runs on every boot. Each `SetIfEmpty` call:

```csharp
private bool SetIfEmpty(IContent node, string alias, string value)
{
    if (!node.HasProperty(alias)) return false;
    if (!string.IsNullOrWhiteSpace(node.GetValue<string>(alias))) return false;
    node.SetValue(alias, value);
    return true;
}
```

- If the content type doesn't have that property → no-op.
- If the property is already filled → no-op (editor data preserved).
- Otherwise → write the seed value.

This means a re-seed after editors change values never overwrites their work.

## Out of scope (deliberately not in SeedConfig)

These are **not** seeded from `SeedConfig`:

- **Identity preset** — editors pick in `ThemeSettings` from the `Aurora / Graphite / Solaris / custom` dropdown. The preset values come from `Application/Theme/IdentityRegistry.cs` (design tokens, not runtime config).
- **Dictionary items** — seeded by `DictionaryInitializer` directly in code because the keys are tied to view usage and shouldn't vary per deployment.
- **Form definitions** — editors author these in the backoffice under `Shared Content → Forms`.
- **Blog posts** — editors.
- **Menu structure** — editors pick `NavigationGroup` nodes in the `SiteSettings → Header` tab.
- **CDN config** — lives in `appsettings:StaticAssets`, not `Synergos:Seed`.

## See also

- [`reference.md`](reference.md) — all Synergos:* sections.
- [`../operations/fresh-boot.md`](../operations/fresh-boot.md) — first-run procedure.
- [`../../CLAUDE.md`](../../CLAUDE.md) §6 — anti-hardcoding rules.
