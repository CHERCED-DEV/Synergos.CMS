# ADR 0004 — Central Package Management (CPM)

- **Status:** Accepted
- **Date:** 2026-04-17
- **Deciders:** Project owner

## Context

NuGet package versions can live in two places:

1. Inside each `.csproj` (`<PackageReference Include="X" Version="1.2.3" />`)
2. Centralized in a root-level `Directory.Packages.props` via
   Central Package Management (CPM), introduced in NuGet 6.2 / .NET 7.

The reference project `NS.Booking.CMS` keeps versions inside each
`.csproj`, and uses floating versions (`Version="13.*"`) for some
packages. That caused:

- Silent minor/patch drift between the host and test project.
- Difficulty auditing "what version of Umbraco is this solution on?" —
  the answer depended on which `.csproj` you read.
- Merge conflicts on unrelated version updates.

Synergos.CMS starts multi-project on day one. The cost of getting
versioning right is zero now; retrofitting CPM later would be a whole
sweep.

## Decision

Enable **Central Package Management** via a root `Directory.Packages.props`.

- `ManagePackageVersionsCentrally` is `true`.
- `CentralPackageTransitivePinningEnabled` is `true` (transitive
  dependencies are pinned to known-good versions, not floated).
- No floating versions (`*`, `[1.0,*)`) anywhere.
- Every `<PackageReference>` in every `.csproj` has no `Version`
  attribute — CPM resolves from the central file.

Adding a new package requires three changes in the same PR:

1. Add `<PackageVersion Include="X" Version="..." />` to
   `Directory.Packages.props`.
2. Add `<PackageReference Include="X" />` (no version) to the
   consuming `.csproj`.
3. Append a line to `CHANGELOG.md` under `Unreleased`.

## Consequences

**Positive**
- Single grep to answer "what version of X do we use?"
- No version drift between projects.
- Dependabot / version bumps touch one file.
- Transitive pinning prevents the surprise where a minor update of a
  direct dependency drags in a new major of something transitive.

**Negative**
- Devs unfamiliar with CPM may try to add `Version=` to a
  `PackageReference` and get a build warning. The workflow in
  `conventions/` must be read.
- Some tooling (older Visual Studio UI) doesn't surface CPM as cleanly
  as per-project versions. Command-line `dotnet add package` still works.

## Alternatives considered

- **Per-project versions** — rejected, prior project suffered from drift.
- **`packages.lock.json` only** — rejected, locks reproducibility but
  doesn't solve the readability/audit problem.
