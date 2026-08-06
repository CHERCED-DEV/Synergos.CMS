# Commit and PR Style

## Commit messages — Conventional Commits

Every commit subject follows:

```
<type>(<scope>): <short imperative description>
```

### Types

| Type | When to use |
|------|-------------|
| `feat` | A user- or developer-visible new capability |
| `fix` | A bug fix |
| `refactor` | Code restructure with no behavioural change |
| `docs` | Documentation only |
| `test` | Tests only |
| `build` | Build system, NuGet, MSBuild, `.csproj` |
| `ci` | CI/CD pipelines |
| `chore` | Maintenance with no user or dev impact |
| `perf` | Performance improvement |

### Scope (optional)

The top-level affected area. Common scopes:

- `cms` — the Web project
- `app` — the Application project
- `interfaces` — the Interfaces project
- `tests` — the Tests project
- `docs` — documentation
- `adr` — a new or superseded ADR

### Body

- Wrap at ~72 characters.
- Answer **why**, not what. The diff shows *what* changed.
- Reference ADRs when a commit implements or supersedes one.

### Examples

```
feat(cms): add BookingController skeleton with feature folder

Implements the lookup table from docs/architecture/folder-layout.md:
Controllers/Booking/ holds user-facing MVC endpoints, BookingService
lives in Application/Services/Impl/. See ADR 0002 for rationale.
```

```
docs(adr): ADR 0008 — production database choice (SQL Server)

Supersedes the "deferred" note in ADR 0003. Confirmed SQL Server for
production after hosting target selection.
```

```
refactor(cms): centralize value converters into ValueConverters/

No behavioural change. Moves three converters previously scattered
across Notifications/ and Composers/ into ValueConverters/ per
docs/architecture/folder-layout.md.
```

## Breaking changes

A breaking change is any change that would require an existing
deployment of Synergos.CMS to take manual action beyond a redeploy:

- Schema changes that need a data migration.
- Renamed document types or aliases.
- Changed API contracts consumed by Synergos.UI.
- Configuration keys that moved or changed type.

Mark them with `!` in the subject and a `BREAKING CHANGE:` footer:

```
feat(cms)!: rename ArticlePage alias to article

BREAKING CHANGE: existing content nodes using the old `articlePage`
alias need a uSync re-import. See MIGRATION-CHANGELOG.md entry for
v0.4.0.
```

Breaking changes also get a line in `MIGRATION-CHANGELOG.md`.

## Pull requests

- **Title**: same format as a commit subject.
- **Description**: 2–3 bullet summary + a "Test plan" section.
- **One ADR per PR** when the PR introduces an architectural decision.
- **No mixed PRs** — a PR doesn't ship a feature and a refactor and a
  doc overhaul together. Split them.

## What does *not* belong in a commit

- Generated files that belong in `.gitignore`.
- Commented-out code ("// just in case"). Delete it; Git is the
  history.
- Personal scratch files, diagrams, or notes. Those go in `docs/` if
  they're valuable, or stay out of the repo.
