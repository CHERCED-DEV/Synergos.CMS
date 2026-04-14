# Recipe — Add a new CDN macro

Add a client-side Custom Element that hydrates from a CDN-hosted JS bundle
and receives its props via a JSON `config` attribute.

Example: `cdnFlipCard` — a 3D card that flips on hover/click.

## Prerequisites

- The element exists in Synergos.UI (Angular package) and publishes to the CDN
  (`registry.json` contains its alias/tag).
- You've picked the macro alias (`cdnFlipCard`), display name (`Cdn.FlipCard`),
  and parameter names (label, body, imageSrc, …).

## Steps

### 1. Register the macro

`Schema/Initializers/MacroInitializer.cs` — add to the CDN Compositions section:

```csharp
EnsureMacro("cdnFlipCard", "Cdn.FlipCard", "Compositions/CdnFlipCard",
    ("frontTitle",  "Front Title",       TextBox),
    ("frontBody",   "Front Body",        TextArea),
    ("backTitle",   "Back Title",        TextBox),
    ("backBody",    "Back Body",         TextArea),
    ("imageSrc",    "Image URL",         TextBox),
    ("imageAlt",    "Image Alt",         TextBox),
    ("flipTrigger", "Trigger (hover|click)", TextBox),
    ("variant",     "Variant",           TextBox),
    ("theme",       "Theme",             TextBox));
```

Parameters are `(alias, label, editorAlias)`. Use:
- `TextBox` — single-line string
- `TextArea` — multi-line string
- `TrueFalse` — boolean toggle

### 2. Add the Config DTO

`Application/Cdn/Configs/CompositionCdnConfigs.cs` — add a new record:

```csharp
public sealed record FlipCardCdnConfig(
    string? FrontTitle,
    string? FrontBody,
    string? BackTitle,
    string? BackBody,
    string? ImageSrc,
    string? ImageAlt,
    string? FlipTrigger,
    string? Variant,
    string? Theme,
    IReadOnlyDictionary<string, string>? Translations);
```

All fields nullable — null is serialized-out by
`DefaultIgnoreCondition = WhenWritingNull`, so the element-side default wins.

### 3. Create the macro partial

`Views/MacroPartials/Compositions/CdnFlipCard.cshtml`:

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

    var cfg = JsonSerializer.Serialize(new FlipCardCdnConfig(
        FrontTitle:   Get("frontTitle"),
        FrontBody:    Get("frontBody"),
        BackTitle:    Get("backTitle"),
        BackBody:     Get("backBody"),
        ImageSrc:     Get("imageSrc"),
        ImageAlt:     Get("imageAlt"),
        FlipTrigger:  Get("flipTrigger"),
        Variant:      Get("variant"),
        Theme:        Get("theme"),
        Translations: translations
    ), opt);
}
<script src="@ElementUrl.ResolveBundle("flipCard")" type="module" defer></script>
<synergos-flip-card config='@cfg' class="sg-cdn sg-cdn--flip-card"></synergos-flip-card>
```

Notes:
- **`ElementUrl.ResolveBundle("flipCard")`** — the string passed here is the
  element's **name** in the CDN registry (not the alias). Usually camelCase.
- **`<synergos-flip-card>`** — custom element tag, declared by the UI package.
  Convention: `synergos-<kebab-case-of-name>`.
- **`class="sg-cdn sg-cdn--flip-card"`** — site-wide CSS can target `.sg-cdn`
  for layout concerns (margins, max-width) outside the shadow DOM.
- **`Translations: translations`** — entire dictionary passed in; the element
  picks the keys it needs.

### 4. Ensure the CDN element exists

Check `C:\LOCAL_CDN\synergos\registry.json`:

```json
{
  "elements": [
    {
      "alias": "cdnFlipCard",
      "tag":   "synergos-flip-card",
      "name":  "flipCard",
      "tier":  "composition",
      "implementations": { "angular": { "latest": "1.0.0" } }
    }
  ]
}
```

If the element isn't built yet, the macro still works — just emits a broken
`<script>` tag and a no-op custom element. Users see a blank space.

### 5. Verify dev-server override (optional)

If you're developing the UI locally, the Synergos.UI dev tools write a
`__dev-servers.json` so the CMS serves the local bundle:

```json
{
  "servers": {
    "flipCard": { "url": "http://localhost:4242", "framework": "angular" }
  }
}
```

`FileSystemElementUrlResolver` picks this up via `FileSystemWatcher` and the
`ResolveBundle("flipCard")` call returns `http://localhost:4242/main.js`.

### 6. Bump `SchemaVersion`

Macros are schema. Bump it:

```csharp
public const string Value = "10.0.4";
```

### 7. Build and verify

```bash
dotnet build Synergos.CMS/Synergos.CMS.csproj
# expect: 0 Errores
```

Run the app. In the backoffice:
- `Settings → Macros → Cdn.FlipCard` — should exist with all parameters.
- Edit a RichText field on a page → insert macro → pick `Cdn.FlipCard` → fill parameters → save → publish.
- View the page → inspect the DOM for `<synergos-flip-card>`.
- Browser devtools → network → the bundle should load from the expected URL.

### 8. Add dictionary keys if needed

If the element's UI needs localized strings (e.g. "Click to flip"), add them:

```csharp
// Schema/Initializers/DictionaryInitializer.cs
EnsureItem("FlipCard.ClickToFlip", new Dictionary<string, string>
{
    { "es-CO", "Click para voltear" },
    { "en-US", "Click to flip" }
});
```

The element reads `config.translations["FlipCard.ClickToFlip"]`.

## Checklist

- [ ] Macro registered in `MacroInitializer` with the right partial path
- [ ] Config DTO record added in `Application/Cdn/Configs/`
- [ ] Macro partial created at the registered path
- [ ] CDN registry has the element (or UI team has it in the works)
- [ ] Dev-server override configured (if UI dev)
- [ ] Dictionary keys added for user-facing strings
- [ ] `SchemaVersion.Value` bumped
- [ ] Build returns 0 errors
- [ ] Boot test: macro shows up, renders script + element tag, bundle loads

## Common pitfalls

### The macro shows nothing on the page

- Check the browser network tab — is the bundle 200 OK?
- Check `ElementUrl.ResolveBundle("flipCard")` — does the URL match where the
  bundle lives?
- Check the CDN registry — is the element present?

### `<synergos-flip-card>` appears but is empty

- The custom element is not registered. The bundle may have loaded but the UI
  code may crash on startup. Check console for errors.
- The `config` attribute may be invalid JSON — check DevTools for parse errors.

### `ResolveBundle` returns the wrong URL

- `__dev-servers.json` may have a stale entry. Delete it or restart the dev tool.
- `ElementServersSettings.Overrides` in appsettings may override it.
- Check the resolution order in [`../rendering/cdn-integration.md`](../rendering/cdn-integration.md).

## Deleting a macro

1. Remove the `EnsureMacro(...)` call from `MacroInitializer.Initialize()`.
2. Add the alias to `CleanupLegacyMacros()` in the same file.
3. Delete the partial from `Views/MacroPartials/`.
4. Delete the Config DTO record if unused.
5. Bump `SchemaVersion`.

## See also

- [`../rendering/cdn-integration.md`](../rendering/cdn-integration.md)
- [`../rendering/macros.md`](../rendering/macros.md)
- [`add-element-type.md`](add-element-type.md)
