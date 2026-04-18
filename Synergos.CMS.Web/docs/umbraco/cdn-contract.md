# CDN contract — consumed, not owned

- **Status:** BLOCKED externally — waiting for the CDN team to publish
  the registry contract.
- **ADR:** [0012 CDN contract is consumed, not owned](../adr/0012-cdn-contract-consumed.md).
- **Migration plan:** Ola 6 of [`refactor-docs/migration/04-migration-strategy-phase-plan.md`](../../../../refactor-docs/migration/04-migration-strategy-phase-plan.md).

## What this doc is for

Every other integration surface of Synergos.CMS is owned by Synergos:
we choose the schema, the lifecycle, the failure modes. The CDN bundle
registry is the exception — the CDN team publishes it and we consume
it. This file is the single place where that asymmetry is made
explicit so nobody (human or agent) tries to "simplify" the problem
by owning the contract from the CMS side.

## What the CMS provides today

| Artefact | Location | Role |
|---|---|---|
| `IBundleRegistryClient` | [`Synergos.CMS.Interfaces/IBundleRegistryClient.cs`](../../../Synergos.CMS.Interfaces/IBundleRegistryClient.cs) | Contract: `Task<BundleDescriptor?> TryResolveAsync(string elementKey, CancellationToken)`. Declared in Ola 1. |
| `BundleDescriptor` | Same file | Record `(Uri MainEntryUri, IReadOnlyList<Uri> Dependencies, string Version)`. Grows only when CDN publishes new mandatory fields. |
| `StubBundleRegistryClient` | [`Synergos.CMS.Application/Proxies/Impl/StubBundleRegistryClient.cs`](../../../Synergos.CMS.Application/Proxies/Impl/StubBundleRegistryClient.cs) | Production-adjacent placeholder. Always returns `null`. Not registered in DI. |
| `FakeBundleRegistryClient` | [`Synergos.CMS.Tests/Proxies/FakeBundleRegistryClient.cs`](../../../Synergos.CMS.Tests/Proxies/FakeBundleRegistryClient.cs) | Test double. Three constructors: default (null), fixed descriptor, per-key resolver delegate. |

## What the CDN team must publish before we implement the real adapter

1. **Registry endpoint** — exact URL shape (base URL, path template,
   query parameters, required headers, auth scheme if any).
2. **Response schema** — JSON (or other) shape and how each field maps
   to `BundleDescriptor` (`MainEntryUri`, `Dependencies`, `Version`).
3. **Error semantics** — how "not found" vs "server error" are
   distinguished; retry policy expectations.
4. **Versioning policy** — how breaking changes are signalled (new
   base path? media-type? header?).
5. **Dev / staging endpoints** — if any; else we point against prod
   with a read-only key.

Only after those five are frozen does the real adapter get written.

## When CDN unblocks — execution outline (do NOT improvise)

1. **ADR successor to 0012** freezing the final contract. Move the
   current ADR 0012 to status `Superseded by NNNN`.
2. **Implement `HttpBundleRegistryClient`** under
   `Synergos.CMS.Application/Proxies/Impl/`:
   - Inject typed `HttpClient` via
     `services.AddHttpClient<IBundleRegistryClient, HttpBundleRegistryClient>()`.
   - No `string.Format` URL assembly — the client owns the path shape.
3. **Gate registration** in a composer under
   `Synergos.CMS.Web/Composers/`:
   ```csharp
   var mode = builder.Config["Synergos:CDN:Mode"];
   if (mode == "stub")
       services.AddSingleton<IBundleRegistryClient, StubBundleRegistryClient>();
   else
       services.AddHttpClient<IBundleRegistryClient, HttpBundleRegistryClient>(/* … */);
   ```
   Default (no value or any other value) → real adapter. Stub is
   opt-in, never default. This satisfies the ADR 0012 guardrail that
   the stub never ships to staging or prod.
4. **Contract tests** in `Synergos.CMS.Tests/Proxies/` that:
   - Serialise/deserialise representative JSON payloads from the CDN
     against `BundleDescriptor`.
   - Verify error-path mapping (404 → `null`, 5xx → `null` with
     logged warning — exact policy freezes with the ADR successor).
5. **CHANGELOG entry** in [`../CHANGELOG.md`](../CHANGELOG.md) noting
   CDN contract freeze.

## Hard rules while the block is in effect

- ❌ Do **not** construct CDN URLs by `string.Format` or string
  concatenation anywhere in the codebase. Every consumer asks
  `IBundleRegistryClient`.
- ❌ Do **not** publish `StubBundleRegistryClient` to staging or
  production configurations. Its presence in Dev is the upper bound.
- ❌ Do **not** add fields to `BundleDescriptor` speculatively.
  Fields added without a matching confirmed CDN field are hostages
  to refactoring when the real contract arrives.
- ❌ Do **not** revive the Epic Fail 2 `StaticUrlBuilder` pattern
  (cabled path format in code). Forensic §4.6 [A6] and ADR 0012 are
  unambiguous.

## Reference

- [ADR 0012](../adr/0012-cdn-contract-consumed.md).
- [Forensic §4.6 [A6] — hardcoding semántico del esquema de URLs CDN](../../../../refactor-docs/architecture/01-epic-fail-2-forensic-analysis.md).
- [feedback_cdn_contract_consumed](../../../../) (agent memory).
