# ADR 0002 — Multi-project solution structure

- **Status:** Accepted
- **Date:** 2026-04-17
- **Deciders:** Project owner

## Context

Two previous attempts at this codebase (`Synergos.CMS.epicfail` and
`Synergos.CMS.epicfail2`) were organized as a single ASP.NET/Umbraco
project, with Clean Architecture layers represented only by top-level
folders (`Application/`, `Domain/`, `Infrastructure/`, `Presentation/`).

In both attempts:

- `Application/` grew to 234+ classes with no effective boundary.
- Domain types acquired Umbraco dependencies because nothing stopped them.
- Composers ended up in three different folders.
- The "layer boundary" was a social convention, and social conventions
  lose to deadlines.

The reference project used for structural comparison (`NS.Booking.CMS`,
Umbraco 10) uses a multi-project solution: Web, Application, Interfaces,
Tests. That project's boundaries have survived years of contributors
because the `.csproj` graph refuses compilations that cross them.

## Decision

Synergos.CMS is a **four-project solution**:

1. `Synergos.CMS.Web` — Web host (Umbraco runtime, Views, Controllers, Composers).
2. `Synergos.CMS.Application` — pure business/application logic.
3. `Synergos.CMS.Interfaces` — composition contracts only, references
   nothing internal.
4. `Synergos.CMS.Tests` — xUnit.

Dependencies are enforced by `ProjectReference` and point strictly
downward: `Tests → Web → Application → Interfaces`.

Umbraco and ASP.NET packages are referenced only by the Web project.
`Umbraco.Cms` types must not appear in `Application` or `Interfaces`.

## Consequences

**Positive**
- Layer violations fail at compile time, not at code review.
- New contributors have an unambiguous answer to "where does this go?"
  (see `architecture/folder-layout.md`).
- `Application` becomes liftable into a separate service or shared
  library if the need arises.
- Tests compile against the Web project but not against internals
  unless explicit `InternalsVisibleTo` is granted, forcing tests to
  exercise the real public surface.

**Negative**
- Small changes that would fit in one project now touch two or three.
- Three extra `.csproj` files increase cold-build time marginally.
- There is a temptation to pre-build abstractions in `Interfaces` that
  aren't needed yet. That is rejected explicitly — `Interfaces` stays
  thin. See ADR 0005.

## Alternatives considered

- **Single project with layer folders** — rejected, two prior failures.
- **Three projects (no Interfaces)** — rejected, would create a cycle
  the first time Web and Application needed a shared contract.
- **Full DDD multi-project (Domain, Application, Infrastructure,
  Web, Interfaces)** — rejected as ceremony. Umbraco is itself the
  domain store; a `Domain/` project would duplicate that taxonomy.
