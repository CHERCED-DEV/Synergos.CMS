# ADR 0003 — SQLite for the development database

- **Status:** Accepted
- **Date:** 2026-04-17
- **Deciders:** Project owner

## Context

Umbraco supports both SQL Server and SQLite as primary data stores.
During local development, a solo contributor needs:

- Zero-setup database (no LocalDB install, no Docker, no SQL Server image).
- Fast cold-start for experimentation.
- Trivially disposable — "delete the folder, start over" is a valid
  recovery step during the scaffolding phase.

Production hosting target is not yet decided. SQL Server is the default
for Umbraco production deployments; SQLite is supported but has
concurrency and replication limitations.

## Decision

Use **SQLite** as the development database. Connection strings live in
`appsettings.Development.json` (already configured by `dotnet new umbraco`).

Production database choice is deferred to a separate ADR when deployment
requirements are known.

## Consequences

**Positive**
- `dotnet run` works on a clean clone with zero database setup.
- The `umbraco.sqlite.db` file is a single disposable artifact.
- No SQL Server license or Docker requirement on dev machines.

**Negative**
- SQLite doesn't exercise SQL Server-specific behaviour (collations,
  locking semantics, JSON column behaviour). Production parity is not
  guaranteed during dev.
- When a second developer joins or staging/production goes live with
  SQL Server, a follow-up ADR must either promote SQL Server to dev
  too or document that dev-vs-prod drift is an accepted risk.

## Alternatives considered

- **SQL Server LocalDB** — rejected, requires a Windows-only install
  and slows dev onboarding.
- **SQL Server in Docker** — rejected for now, Docker is not yet
  required by any other part of the stack.
- **SQLite in-memory** — rejected, Umbraco persistence between runs is
  desired for iterative schema work.
