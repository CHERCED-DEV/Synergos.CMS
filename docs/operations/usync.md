# uSync Lifecycle

uSync is a backup/export format. **It is not a source of truth.** The schema
pipeline is.

## The rule

```
Schema pipeline (code)         ⟶ creates Document Types, Data Types, Macros, …
          │
          ▼
Umbraco DB (populated)          ⟵ editors add content
          │
          │  Editors click "Export" in backoffice
          ▼
uSync/v9/ configs (on disk)     ⟵ snapshot of DB state
```

Arrows only go down. Never up. Never import uSync to modify schema.

## Why

- The schema pipeline is versioned, idempotent, reviewable. Each change is a
  C# commit.
- uSync configs are derived state. Importing old configs brings back dead
  fields we've intentionally removed (example: the dead `cdnBaseUrl` field in
  GlobalSettings).
- uSync was designed for "content as code" workflows where editors and devs
  share the backoffice as the source of truth. That's not our workflow —
  our devs change schema via initializers, not the backoffice.

## Configuration

`appsettings.json`:

```json
"uSync": {
  "Settings": {
    "ImportOnStartup":           false,  // KEEP FALSE
    "ExportOnSave":              false,  // KEEP FALSE (editors export manually)
    "ReportDebug":               false,
    "IgnoreBrokenDependencies":  false,
    "CacheFolderKeys":           true
  }
}
```

Never set `ImportOnStartup: true`. That would bypass the schema pipeline.

## When to export

- **After a fresh boot** — captures the pipeline's output as a baseline. Commit
  the generated `uSync/v9/` to git as documentation.
- **After editors populate content in staging** — captures the content for
  promotion to production (when using uSync for content sync).
- **Before destructive operations** — gives you a rollback point.

## When NOT to import

- **Don't import on startup.** See above.
- **Don't import after a schema pipeline change.** The pipeline already created
  the correct state. Importing old uSync will try to revert it or produce a
  mess.
- **Don't import someone else's uSync configs without reading the diff.**
  Treat it like a code review.

## Manual export

In the Umbraco backoffice:

1. Go to `Settings → uSync`.
2. Click `Export` (or `Export Everything`).
3. uSync writes to `Synergos.CMS/uSync/v9/`.
4. `git status` shows the changed files.
5. `git diff uSync/v9/ContentTypes/` — review the diff.
6. Commit if the output matches your expectations.

## Manual import (rare)

Only when you explicitly want to import editor changes made on another
environment — e.g. bringing content from staging to dev for debugging.

1. Copy the `uSync/v9/` folder from the source environment to your dev machine.
2. In `Settings → uSync`, click `Import`.
3. Review the report. uSync tells you exactly what will change.
4. Confirm.

**Never** import a folder from an older project (e.g. `Synergos.CMS.epicfail`
— it has dead fields).

## What lives in `uSync/v9/`

```
uSync/v9/
├── ContentTypes/        Document Types, Element Types, Compositions
├── DataTypes/           Data Type configurations
├── Dictionary/          i18n dictionary items
├── Languages/           configured languages
├── Macros/              macro definitions
├── MediaTypes/          media types
├── MemberTypes/         member types (if any)
└── Templates/           template files (Views/<name>.cshtml metadata)
```

Each `.config` file is XML. Files are named by the entity alias (e.g.
`siteSettings.config`).

## Cleanup on fresh boot

When doing a fresh boot (see [`fresh-boot.md`](fresh-boot.md)):

```bash
rm -rf uSync/v9  # delete stale exports
```

After the fresh boot + manual export, the regenerated `uSync/v9/` reflects the
current schema pipeline output.

`USyncFileCleanerService` (in `Infrastructure/USync/`) may auto-clean stale
files on startup based on configured rules. Check its source for specifics.

## Deployment workflow

For a typical deploy-from-dev-to-prod flow:

1. **Dev:** pipeline runs on boot, produces schema.
2. **Dev:** editors populate content in backoffice.
3. **Dev:** manual export produces `uSync/v9/`.
4. **CI/CD:** commits + pushes updated configs for review (optional — schema
   part is redundant with code).
5. **Staging:** deploys. Pipeline runs, patches schema (no destructive changes
   thanks to `TryPatchExistingContentType`). Staging can import uSync content
   items from dev (NOT schema — schema already updated by pipeline).
6. **Production:** same pattern.

Key point: **schema flows via code, content flows via uSync (if at all).**

## Troubleshooting

### "Import report shows changes I didn't make"

The uSync configs on disk don't match the live DB state. Either:
- The DB has drift (someone edited after export).
- The configs are stale (old export).

Either delete the folder and re-export from the live DB, or inspect the report
to understand what would change.

### "Dependency missing" during import

A type references another type that isn't imported yet. Make sure the import
includes all dependencies, or set `IgnoreBrokenDependencies: true` to skip
broken refs (dangerous — the types become invalid).

### "Blog tab doesn't appear in SiteSettings after uSync import"

The configs show the old schema with a Blog tab. We removed it in a later
pipeline change. If you want the new schema, **don't import** — let the
pipeline re-run instead.

### "Stale uSync configs after a schema change"

Expected. Re-export after the schema change so configs reflect the new state.
Commit the re-export if you're tracking uSync in git.

## See also

- [`build-and-run.md`](build-and-run.md)
- [`fresh-boot.md`](fresh-boot.md)
- [`../schema/pipeline.md`](../schema/pipeline.md)
