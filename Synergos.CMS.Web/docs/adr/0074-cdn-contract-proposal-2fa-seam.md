# ADR 0074 — CDN contract proposal + IMemberTwoFactorService seam (Olas 171-172)

- **Status:** Accepted
- **Date:** 2026-04-27
- **Deciders:** Arquitecto + agente.
- **Consolida:** 2 olas (preparatory).

## Context

Tras cap-170 cerrado, el arquitecto pidió "atacar todo" — el bloqueador
externo CDN + production hardening (2FA, destructive CRUD, multi-instance
DB-backed). Este batch atacó las dos preparatorias high-leverage:

1. **CDN contract**: bloqueado externamente desde Ola 8.5 — el CDN
   team debe publicar 5 puntos antes de implementar
   `HttpBundleRegistryClient`. Aceleramos el unblock escribiendo
   nosotros el draft propuesto que el team puede rubber-stamp o
   pushback.
2. **2FA seam shape**: definir el contrato sin implementación todavía.
   Permite escribir tests + UI scaffolding contra la interface antes
   de que el adapter exista.

## Decision

### Ola 171 — CDN contract proposal section

Extiende `Synergos.CMS.Web/docs/umbraco/cdn-contract.md` con sección
nueva "Proposal — defaults the CMS would accept" con valores concretos:

- **Endpoint shape**: `GET /registry/v1/bundles/{elementKey}` con
  `Authorization: Bearer` opcional + cache hint header.
- **Response schema** JSON con `mainEntry`, `dependencies[]`,
  `version` semver, optional `integrity{}`, `frameworkHint` (no usado
  per ADR 0015), `publishedAtUtc` (telemetry).
- **Error semantics** map por status code: 200/404/401/429/5xx →
  acción específica del CMS.
- **Versioning** path-based (`/v1/` → `/v2/`) con field-additions
  non-breaking.
- **Dev/staging** endpoints sugeridos.
- **Settings shape** `Synergos:Cdn:*` con `RegistryBaseUrl`,
  `RegistryPathTemplate`, `ApiKey`, `TimeoutSeconds`. Resilience
  vía FactoryName `bundle-registry` reuso de ADR 0069 PerChannel pattern.

El draft permite al CDN team responder **"OK con esto"** o **"todo
igual excepto X"** — más rápido que diseñar from scratch.

### Ola 172 — IMemberTwoFactorService seam shape

Nuevo seam en `Synergos.CMS.Interfaces/IMemberTwoFactorService.cs`:

```csharp
public interface IMemberTwoFactorService
{
    Task<TwoFactorEnrollmentChallenge> StartEnrollmentAsync(...);
    Task<EnrollmentResult> ConfirmEnrollmentAsync(...);
    Task<VerificationResult> VerifyAsync(...);
    Task<bool> DisableAsync(...);
    Task<bool> IsEnabledAsync(...);
}
```

Records POCO (`TwoFactorEnrollmentChallenge`) + tipados enums
(`EnrollmentResult`, `VerificationResult`) para distinguir edge cases.

**Phase 1 scope** (Olas 177-180, ADR 0076): TOTP only, file-based
storage, admin reset path.

**Phase 2 deferred**: recovery codes (8 single-use), encryption-at-rest,
Member self-service enrollment view, login flow extension.

## Consequences

**Positivas:**

- **CDN unblock acelera**: el team no escribe contract from scratch.
- **2FA seam precede impl**: tests + UI scaffolding pueden empezar
  contra la interface antes de que el adapter shipped.
- **Cero schema rompedor**: solo docs + interface — implementación es
  separadas Olas posteriores.

**Negativas:**

- **Proposal puede ser rechazado**: el CDN team puede pushback en
  todos los puntos. Aceptable — es el punto de partida de la
  conversación, no la decisión final.
- **2FA interface puede revisarse Phase 2**: si recovery codes flow
  necesita un nuevo método, no es breaking change (additive).

**Neutras:**

- 1 commit feat + 1 docs ADR.
- 0 GUIDs nuevos.
- 0 NuGet packages nuevos.

## Implementation summary

| # | Foco |
|---|---|
| 171 | `cdn-contract.md` "Proposal — defaults the CMS would accept" section. |
| 172 | `IMemberTwoFactorService` interface + `TwoFactorEnrollmentChallenge` record + `EnrollmentResult`/`VerificationResult` enums. |
| 0074 | (este) ADR consolidado |

## References

- ADR 0012 — CDN contract is consumed, not owned.
- ADR 0015 — SynHost framework-agnostic integration.
- ADR 0034 — Member self-service runtime.
- `feedback_cdn_integration_is_core` (memory).
