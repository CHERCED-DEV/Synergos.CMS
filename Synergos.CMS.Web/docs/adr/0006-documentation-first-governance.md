# ADR 0006 — Documentation-first governance

- **Status:** Accepted
- **Date:** 2026-04-17
- **Deciders:** Project owner

## Context

Two prior failures had roughly the same root cause: team knowledge
wasn't written down, and when a refactor was urgent, no one had an
authoritative reference to cite. Conventions drifted. Composers multiplied.
`Application/` ballooned. By the time the drift was visible, the cost of
undoing it exceeded the cost of starting over.

The reference project `NS.Booking.CMS` has the opposite failure mode:
reasonable structure, stable for years, but a completely empty
`README.md` and no Architecture Decision Records. A new contributor has
to infer conventions from the code — and the code doesn't explain *why*
it's shaped the way it is.

This project aims to combine the discipline of `NS.Booking.CMS`'s
project boundaries with an explicit documentation layer that the
reference project lacks.

## Decision

Governance is **documentation-first**:

1. Every architectural choice is captured in an ADR under
   `Synergos.CMS/docs/adr/` before the code that implements it is
   merged.
2. The `Synergos.CMS/README.md` is never allowed to be empty or
   generic. It explains what the project is, how to run it, and how
   to find more.
3. Conventions (naming, folder layout, commit style) live in
   `Synergos.CMS/docs/conventions/` and are linked from `README.md`.
4. A new-developer setup guide lives in
   `Synergos.CMS/docs/onboarding/` and is validated by actually running
   through it on any fresh machine setup.
5. Every release cuts an entry in `CHANGELOG.md` with semver tags.
6. Breaking Umbraco migrations have their own log in
   `MIGRATION-CHANGELOG.md`.

Documents that are wrong are treated as bugs. Fixing documentation is a
valid PR on its own.

## Consequences

**Positive**
- A future contributor (including future-me) can answer "why is it
  like this?" in minutes.
- Onboarding reduces to "read `docs/README.md` in order".
- Incorrect decisions are visible and can be superseded explicitly
  rather than quietly worked around.

**Negative**
- Writing ADRs takes 15–30 minutes per decision. This is budgeted
  explicitly — not an optional overhead.
- ADRs become stale if the code moves and the ADR doesn't. Mitigated
  by the rule "if a doc is wrong, fix it in the same PR".

## Alternatives considered

- **README-only governance** — rejected, a single README doesn't
  separate decisions (immutable) from conventions (living).
- **Wiki-based governance** — rejected, docs drift from code. In-repo
  keeps docs and code atomic.
- **No governance layer, rely on code review** — rejected, this was
  the `epicfail` approach.
