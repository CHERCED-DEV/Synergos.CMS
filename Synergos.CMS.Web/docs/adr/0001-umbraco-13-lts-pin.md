# ADR 0001 — Umbraco 13 LTS pin

- **Status:** Accepted
- **Date:** 2026-04-17
- **Deciders:** Project owner

## Context

Umbraco publishes both LTS (Long-Term Support) and non-LTS major versions.
At the time of this scaffolding:

- **Umbraco 13.13.1** is the latest LTS release. Support runs through
  October 2026 with security extended until 2027.
- **Umbraco 14.x and 15.x** are non-LTS, with shorter support windows
  and an ongoing backoffice rewrite (Bellissima).
- The solution is greenfield but intended to run in production, and the
  team is one person.

A single-maintainer project cannot afford to chase backoffice rewrites or
ride quarterly major bumps. Stability over feature velocity is the
explicit preference.

There is a known moderate-severity advisory on 13.x (NU1902, GHSA-54mj-vcvj-q3v5)
with no patched version in the 13.x line at the time of this ADR. The
Umbraco team has acknowledged it; a fix is expected within the 13 LTS
window.

## Decision

Pin Umbraco to **`13.13.1`** via Central Package Management. Do not
propose a major-version migration to 14 or 15 within this project's
lifetime on 13 LTS.

When the next LTS (14 LTS, 15 LTS, or later — Umbraco has not announced
which) becomes available and stable for ≥3 months, a successor ADR may
supersede this one.

## Consequences

**Positive**
- Stable runtime and backoffice behaviour for the life of this codebase.
- Freedom to adopt packages that explicitly target 13 LTS (uSync 13, etc.)
- Avoids the Bellissima transition risk.

**Negative**
- No access to new Umbraco 14+ features (new backoffice API, improved
  management APIs, new content delivery APIs).
- The NU1902 advisory will appear in every build until Umbraco ships a
  patch. Do not treat it as a build failure — add it to the known-issues
  list in `docs/operations/run-build-test.md` instead.
- When migration does happen, it will be a significant effort because
  the gap will have grown.

## Alternatives considered

- **Umbraco 15 latest**: rejected for LTS absence and Bellissima churn risk.
- **Upgrade-as-you-go**: rejected because a solo maintainer has no capacity
  for quarterly major bumps.
