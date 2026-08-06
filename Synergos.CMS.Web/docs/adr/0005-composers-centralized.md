# ADR 0005 — Composers live only in the Web project, in one folder

- **Status:** Accepted
- **Date:** 2026-04-17
- **Deciders:** Project owner

## Context

Umbraco uses an `IComposer` pattern to wire up DI, notifications, and
runtime configuration at startup. Umbraco auto-discovers composers via
assembly scan — wherever an `IComposer` lives, it fires.

In `Synergos.CMS.epicfail2`, composers were created anywhere they were
convenient:

- `Schema/SynergosSchemaComposer.cs`
- `Infrastructure/Umbraco/Notifications/NotificationsComposer.cs`
- `Infrastructure/USync/USyncComposer.cs`
- `Infrastructure/Umbraco/Services/DictionaryCacheComposer.cs`

That dispersion created three problems:

1. **Startup order was implicit**. Which composer ran first depended on
   assembly scan order, which is unstable across refactors.
2. **Debugging a boot failure meant grep-hunting**. "Why is service X
   not registered?" required searching for `AddSingleton<IX`.
3. **New features duplicated composer responsibilities** because no
   one knew the full list existed.

## Decision

All `IComposer` implementations live in **`Synergos.CMS.Web/Composers/`**,
and nowhere else. Composers are not created in `Application` or
`Interfaces` — those projects don't reference Umbraco.

Within `Composers/`:

- One composer per concern (DI wiring, notifications, uSync handlers,
  runtime config), named `{Concern}Composer.cs`.
- Composers are `sealed` unless there's a specific reason to allow
  derivation.
- Composers orchestrate — they don't implement. An Umbraco notification
  handler lives in `Notifications/`, and the composer just registers it.

## Consequences

**Positive**
- `ls Synergos.CMS.Web/Composers/` answers "what happens at startup?"
- Startup ordering can be made explicit by composers referencing each
  other via `ComposeAfter<T>` / `ComposeBefore<T>` — centralization
  makes that graph visible.
- Reduces the number of Umbraco-aware files to one folder, which is
  the blast radius for any Umbraco version upgrade.

**Negative**
- A larger feature might have its composer visually separated from its
  service and notification files. Tolerated — the composer is a small
  wiring artifact; the rest of the feature reads together.

## Alternatives considered

- **Feature-colocated composers** (composer lives next to the feature
  it wires) — rejected, this was the `epicfail2` pattern.
- **One mega-composer per project** — rejected, reading a 500-line
  composer is worse than reading six 80-line ones.
