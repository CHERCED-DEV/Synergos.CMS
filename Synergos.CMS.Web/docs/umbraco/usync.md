# Umbraco — uSync (deferred)

uSync is the package most Umbraco teams use to serialize backoffice
state (document types, data types, dictionary items, content) into
version-controlled files under `uSync/`.

## Current status: NOT INSTALLED

As of the initial scaffolding (v0.1.0), **uSync is not a package
reference**. This is intentional — we install it the moment we have
schema to serialize, not before. See the anti-premature-abstraction
rule in the root `README.md`.

## When to add uSync

Add it when the first of these is true:
- You've created a document type in the backoffice that should live
  in version control.
- A second developer joins and needs a way to import schema.
- A CI pipeline needs to validate schema on pull requests.

## How to add uSync (future)

When the time comes:

1. Add to `Directory.Packages.props`:
   ```xml
   <PackageVersion Include="uSync" Version="13.*" />
   ```
   (pin to a specific version, not floating — see ADR 0004).
2. Add `<PackageReference Include="uSync" />` (no version) to
   `Synergos.CMS.Web/Synergos.CMS.Web.csproj`.
3. Add a `USyncComposer.cs` in `Synergos.CMS.Web/Composers/` if any
   runtime configuration is needed.
4. Add a line in `CHANGELOG.md` under `Unreleased`.
5. Write an ADR describing the uSync handler configuration choices
   (which handlers are active, export/import direction on startup).

## Folder conventions (once installed)

uSync files live at `Synergos.CMS.Web/uSync/v9/...` (the `v9` is uSync's
internal schema version, not our project version). That folder:

- IS committed to Git.
- Is listed explicitly in `.gitignore` exclusions — only artefacts
  from uSync's temp state are excluded.
- Is never hand-edited when a corresponding backoffice UI exists —
  edit in the backoffice, export with uSync.

## Known traps from prior fails

- **Floating versions** (`uSync 13.*`) caused a silent drift between
  the host project and CI. ADR 0004 bans floating versions.
- **Block Grid UDIs** (`umb://element/...`) do not create rows in
  `umbracoNode` but still cause `UNIQUE constraint failed` if a GUID
  collision happens. When uSync is added, its JSON payloads must be
  grepped for UDIs during GUID assignment — see the
  root `CONTRIBUTING.md` §1 "GUID Assignment".
