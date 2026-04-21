# Umbraco — ModelsBuilder

ModelsBuilder generates strongly-typed C# classes from Umbraco document
and element types, so Razor views and controllers can consume
`IPublishedContent` as `ArticlePage`, `BlogHome`, etc., instead of
stringly-typed property lookups.

## Mode: InMemoryAuto

The scaffold sets ModelsBuilder to `InMemoryAuto`, Umbraco's default.
Configuration lives in `Synergos.CMS.Web/appsettings.json` under
`Umbraco:CMS:ModelsBuilder`.

In this mode:

- Models are regenerated automatically when a document type changes.
- Models are held in memory — no `.generated.cs` files in source control.
- `RazorCompileOnBuild = false` and `RazorCompileOnPublish = false` in
  `Synergos.CMS.Web.csproj`. Required by Umbraco for InMemoryAuto.

## Why not `SourceCodeAuto` or `SourceCodeManual`?

The reference project (`NS.Booking.CMS`) uses SourceCode modes that
commit `Models/Builder/*.generated.cs` into the repo. That approach has
two downsides:

- Commit noise on every schema tweak.
- The generated files are referenced by Razor views, so a rename in the
  backoffice breaks compilation until someone regenerates.

We chose `InMemoryAuto` to avoid both. When production hardening
arrives, we may pin `SourceCodeManual` and commit generated models —
that will get its own ADR.

## How to use the types in views

Once a document type exists, its model is available in Razor:

```cshtml
@inherits Umbraco.Cms.Web.Common.Views.UmbracoViewPage<Synergos.CMS.Web.Models.ArticlePage>
@* Model.Title is strongly typed, IDE autocompletes, compiler catches typos *@
```

The namespace is configurable; default is `Synergos.CMS.Web.Models` (matches
the `Models/` folder convention).

## Gotchas

| Gotcha | Explanation |
|--------|-------------|
| `Model.SomeProperty` returns `null` after renaming in backoffice | Restart the app — `InMemoryAuto` only regenerates on schema change detection. |
| `Cannot find type 'Synergos.CMS.Web.Models.Foo'` at runtime | Document type alias `foo` doesn't exist yet, or was deleted. |
| Razor intellisense lags behind backoffice | IDE limitation. Rebuild and reopen the file. |
| Need to version-control generated models | Switch to `SourceCodeManual` and write an ADR first. |
