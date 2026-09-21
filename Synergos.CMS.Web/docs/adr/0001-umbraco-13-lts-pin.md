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

> **Amended by the addendum below (#149): the pin is now `13.16.2`.** What
> this ADR fixes is the **13 LTS branch**, not one patch inside it — the
> prohibition is on 14+. The literal version lives in
> `Directory.Packages.props` and is cross-checked by `VersionDeUmbracoTests`.

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

## Addendum — 2026-09-21 (#149): patch pin moved to 13.16.2

**What happened.** `NuGetAudit` started reporting **`NU1903`** — a *high*
severity advisory (`GHSA-wr57-hqmp-fgvh`) covering `Umbraco.Cms` in the range
`[12.0.0, 13.15.1)`. Since #134 the tree builds with
`TreatWarningsAsErrors=true`, so **the build went red without a single commit
being made**: what changed was the advisory database that NuGet downloads
during restore. It went red on every machine at once, with the whole tree
still compiling at zero warnings.

**Decision.** Move the pin from `13.13.1` to **`13.16.2`**, the newest release
published on the 13 LTS branch. This ADR's prohibition is untouched: 13.16.2
is still 13 LTS, and 14/15/16+ remain off-limits without a successor ADR.

**Why not `NoWarn`.** The advisory has a patch *inside the pinned branch*.
Suppressing it would have written down a reason that answers "why this isn't
fixed **yet**" rather than "why this is **not** fixed" — a ticket left unopened
wearing the costume of an exemption (see
`feedback_a_census_entry_is_how_a_defect_survives_its_own_gate`). The
distinction is now written next to the `NoWarn` list itself, so the next person
reaching for it has to check the advisory's vulnerable range against the
branch's published versions first.

`NU1902` stays suppressed, and its reason survives this change intact:
`GHSA-54mj-vcvj-q3v5` has the vulnerable range `(, 16.3.3]`, so **no** release
of the 13 branch closes it. That one really is a known-without-patch.

**What was measured before taking it** (not argued — run):

- `dotnet build Synergos.CMS.sln` with auditing **on**: 0 warnings, 0 errors.
- The three suites: 2247 + 633 + 400 = **3280 passing**, identical to the
  baseline at 13.13.1. No test moved. (The tree is at **3285** now: the five
  extra are `VersionDeUmbracoTests`, written by this same change — see below.)
- `13.15.1` — the *minimum* version that clears the advisory — was measured
  first and is equally green; `13.16.2` was chosen so the branch does not need
  touching again for the next few advisories.

**What this addendum does not change.** The Context section above is a record
of what was true on 2026-04-17 and is left alone. So are the historical
mentions of `13.13.1` in `CHANGELOG.md` and in ADR 0093, which describe probes
actually run against that version — rewriting a measurement to match today's
pin would be the one thing worse than a stale number.

**And this ADR now has a gate** (`VersionDeUmbracoTests`). Until #149 the
prohibition above was prose only: nothing stopped a `Version="14.0.0"` landing
in `Directory.Packages.props` and compiling. It is now a red build. The second
tooth closes what the bump itself exposed — the version was written by hand in
**seven** places and **nothing cross-checked them**, so the three suites stayed
green while `Directory.Packages.props` already said 13.16.2 and `CLAUDE.md` §1,
the guardrails skill and this ADR still said 13.13.1.
