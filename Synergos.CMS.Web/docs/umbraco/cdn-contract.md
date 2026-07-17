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

## Proposal — defaults the CMS would accept (Ola 171)

To accelerate the unblock, here is a concrete proposal the CDN team can
rubber-stamp or push back on. Every value is negotiable; the goal is to
remove "we don't know what to ask for" from the path.

### 1. Registry endpoint

Suggested:

```
GET https://cdn.synergos.example/registry/v1/bundles/{elementKey}
Headers:
  Accept: application/json
  Authorization: Bearer {SYNERGOS_CDN_API_KEY}        ; optional read-only
  X-Synergos-Cache-Key: {elementKey}                  ; optional, edge cache hint
```

`{elementKey}` is the schema alias (e.g. `elementSynAccordion`). URL
format opaque to consumers — the client owns it; CMS never assembles.

Alternative shapes that work equally well:
- POST batch: `POST /registry/v1/bundles` with `{ "keys": ["elementSynA", "elementSynB"] }` returning a map. Better for SSR rendering many blocks per page (avoid N round-trips). Defer until perf data shows it matters.
- GraphQL endpoint with `bundle(key: String!)` query.

### 2. Response schema

Suggested JSON shape:

```jsonc
{
  "version": "1.4.2",                                  // semver
  "mainEntry": "https://cdn.synergos.example/elements/elementSynAccordion/1.4.2/main.js",
  "dependencies": [
    "https://cdn.synergos.example/elements/elementSynAccordion/1.4.2/styles.css",
    "https://cdn.synergos.example/shared/web-components-runtime/1.0.0/runtime.js"
  ],
  "integrity": {                                       // optional, not in BundleDescriptor today
    "mainEntry": "sha384-...",
    "dependencies": ["sha384-...", "sha384-..."]
  },
  "frameworkHint": "vanilla|angular|react|lit",        // optional, NOT used for routing per ADR 0015
  "publishedAtUtc": "2026-04-27T15:00:00Z"             // optional, observability
}
```

Mapping to `BundleDescriptor`:

| CDN field | `BundleDescriptor` field | Notes |
|---|---|---|
| `mainEntry` | `MainEntryUri` (Uri) | Required. Must be absolute https. |
| `dependencies` | `Dependencies` (IReadOnlyList&lt;Uri&gt;) | Order = load order. Empty array OK. |
| `version` | `Version` (string) | Free-form, used for cache busting. |
| `integrity` | (not yet in record) | When mandated, add via ADR successor. |
| `frameworkHint` | (not used) | Per ADR 0015: framework is invisible to schema. |
| `publishedAtUtc` | (not used) | Reserved for future telemetry. |

### 3. Error semantics

Suggested:

| Status | CMS interpretation | Action |
|---|---|---|
| 200 | Bundle exists | Return `BundleDescriptor` |
| 404 | Bundle does NOT exist for this key | Return `null` (placeholder rendered) |
| 401 / 403 | Auth misconfigured | Return `null` + log error |
| 429 | Rate limit | Return `null` + warn; fall back to placeholder |
| 5xx | CDN failure | Return `null` + warn; resilience handler retries 3x then gives up |
| Connect timeout | CDN unreachable | Same as 5xx |

CMS uses `Microsoft.Extensions.Http.Resilience` standard handler
(ADR 0064 + 0069) — retries are automatic with exponential backoff.
The CDN team only needs to commit to the status semantics above; retry
policy is owned by CMS.

### 4. Versioning policy

Suggested:

- **Path-based major versions**: `/registry/v1/...` → `/registry/v2/...`
  for breaking changes. CMS opts in by config flip; old version stays
  online for at least 90 days for rollback.
- **Field additions are non-breaking** — CMS deserialiser ignores
  unknown fields. CDN can add `integrity`, `publishedAtUtc`, etc. at
  any time.
- **Field removals or type changes** require new major path.
- **`version` field of the response payload** is free-form (no
  semver enforcement at the wire).

### 5. Dev / staging endpoints

Suggested:

- **Dev**: `https://cdn-dev.synergos.example/registry/v1/...` — open
  read access, no auth header required. CMS dev environments point
  here; integration tests against fixtures (still in-memory).
- **Staging**: same auth scheme as prod, separate host.
- **Prod**: `https://cdn.synergos.example/registry/v1/...` — auth
  required.

If only prod exists, CMS uses `StubBundleRegistryClient` in dev (current
state). Acceptable.

### Settings the CMS will expose at boot

The composer that wires `HttpBundleRegistryClient` will read:

```jsonc
{
  "Synergos": {
    "Cdn": {
      "Mode": "real|stub",                             // default "real" once contract freezes
      "RegistryBaseUrl": "https://cdn.synergos.example",
      "RegistryPathTemplate": "/registry/v1/bundles/{elementKey}",
      "ApiKey": "${SYNERGOS_CDN_API_KEY}",             // env var resolution
      "TimeoutSeconds": 5                              // per-attempt
    }
  }
}
```

Resilience inherits from `WebhookResilience` PerChannel via FactoryName
`bundle-registry` — reuses ADR 0069 pattern.

### What needs CDN team sign-off, not negotiation

- The exact 5 points above with concrete values. Anything they
  disagree with, they propose alternative; the table above is the
  default position.
- Confirmation that field-additions are non-breaking (so we don't
  block on every field add).

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
