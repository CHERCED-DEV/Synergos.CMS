# ADR 0084 — Cap-230: 2FA Phase 2.C + tokens sync + Member CRUD tests (Olas 221-230)

- **Status:** Accepted
- **Date:** 2026-04-28
- **Deciders:** Arquitecto + agente.

## Context

Cap-220 cerró la alineación CMS↔UI via contratos pero dejó 3
deferred items concretos en §11.19:

1. **2FA Phase 2.C**: encryption-at-rest del secret + QR rendering.
2. **Sync script tokens** automático para evitar drift manual.
3. **Member CRUD destructive tests** (Moq/NSubstitute decisión pendiente).

Cap-230 los cierra todos.

## Decision

### Olas 221-222 — 2FA encryption-at-rest

`FileSystemMemberTwoFactorStore` ahora encrypts el JSON serializado
via `IDataProtectionProvider` (ASP.NET Core data protection):

- **Save**: serialize JSON + `IDataProtector.Protect(json)` → write.
- **Read**: try `Unprotect` → si `CryptographicException`, fallback a
  parse directo (legacy plain de Olas 178-180).
- **Migration transparente**: legacy files re-graban encrypted en el
  próximo `Save` sin acción del operador.

Master key bajo `App_Data/Keys/` (default ASP.NET Core). Para
multi-instance con shared filesystem o KeyVault, configurar
`AddDataProtection().PersistKeysTo*` en composer (deferred).

Tests:
- `Save_PersistsEncrypted_NotPlaintext`: verifica disk content NO
  contiene secret literal "JBSWY3DPEHPK3PXP".
- `Read_LegacyPlaintext_MigratesToEncryptedOnNextSave`: simula
  legacy + verifica re-encryption tras Save.

Constructor del test sut usa `UseEphemeralDataProtectionProvider`
(in-memory keyring para tests).

### Olas 223-224 — 2FA QR rendering inline

`QRCoder 1.8.0` agregado (verificado nuget.org). Helper service
`QrCodeRenderer` (singleton) con:
- `RenderSvg(content, pixelSize=4)` → SVG string.
- `RenderSvgDataUri(content)` → `data:image/svg+xml;base64,...`
  para src inline del `<img>` sin extra endpoint.

ECC level M (15% recovery, balance size+robustness).

`AccountController.TwoFactorSetup` inyecta `[FromServices]
QrCodeRenderer` y popula `ViewData["QrDataUri"]` en path enrollment
fresh.

`TwoFactorSetup.cshtml` view extendida:
- "Paso 1 — Escanea el QR con tu app" con `<img>` 240×240px + alt
  accesible.
- Manual entry collapsible `<details>` como fallback.
- Removed "QR rendering deferred" stub.

Trade-off: data URI inline aumenta HTML response ~5KB. Aceptable —
enrollment es low-frequency (1 vez por member).

### Olas 225-226 — Tokens sync automation

`tools/sync-tokens.mjs` Node.js script en UI repo:

- Lee `Synergos.CMS.Web/wwwroot/css/syn-tokens.css` (source of truth).
- **Parser depth-tracking** robusto contra `@media` + comma-separated
  selectors + nested rules + comments. Filtra blocks con `--syn-`.
- Genera `libs/shared/src/styles/_tokens-bridge.scss` con header
  AUTO-GENERATED + `DO NOT EDIT MANUALLY` warning.
- Path resolution: sibling dirs convention con fallback
  `SYNERGOS_CMS_PATH` env var.
- Trailer template añade reduced-motion @media query.

`npm run sync:tokens` alias en `package.json`. Run inicial regenera:
- 4 blocks captured (`:root` + 2 theme overrides + `@media
  prefers-color-scheme`).
- 499 token references en output (vs ~90 hand-written previo —
  cobertura completa del CMS source).

Future: pre-commit hook o CI gating (deferred — sync explícito da
control al developer).

### Olas 227-228 — Member CRUD destructive tests

**NSubstitute 5.3.0** agregado test-only (verificado nuget.org)
para stubbear `IMemberService` (50+ members). Hand-writing todos
los members era impractical — Moq/NSubstitute es estándar industry.

`UmbracoMemberRosterWriterTests` (11 tests):

| Action | Tests |
|---|---|
| LockAsync | NotLocked_LocksAndSaves; AlreadyLocked_NoOp; NotFound_False |
| UnlockAsync | Locked_UnlocksAndResetsFailedAttempts; NotLocked_NoOp |
| DeleteAsync | Existing_HardDeletes; NotFound_False |
| SetRolesAsync | AddsNewAndRemovesOld; NoChange_NoOp; NotFound_False; EmptyTarget_RemovesAll |

`SendPasswordResetAsync` deferred — necesita stub adicional de
`RazorEmailTemplateRenderer` (concrete class, no interface). Refactor
a `IEmailTemplateRenderer` + integration test scope futuro.

### Olas 229-230 — Cierre

Este ADR + actualización current-state §11.20 + memory.

## Consequences

**Positivas:**

- **2FA realmente seguro**: secret + recovery codes hashes encrypted
  at rest. Filesystem leak NO compromete 2FA.
- **2FA UX polished**: QR scan elimina copy/paste manual del secret.
- **Tokens sin drift**: sync explícito mantiene UI espejo del CMS
  source con un comando.
- **Member CRUD covered**: 11 tests cubriendo el writer completo
  (sin SendPasswordReset que necesita refactor).
- **NSubstitute foundation**: pattern reusable para stubbear otros
  Umbraco-heavy interfaces (IMemberManager, IUmbracoContextAccessor,
  etc.) en olas futuras.

**Negativas:**

- **Encryption-at-rest single-instance**: master key en `App_Data/Keys/`
  no se replica. Multi-instance requiere `PersistKeysToX` config.
  Deferred.
- **QR data URI inline**: ~5KB HTML overhead per enrollment view.
  Acceptable trade-off por simplicidad (no extra endpoint).
- **Sync tokens manual run**: developer debe `npm run sync:tokens`
  cuando CMS source cambia. Pre-commit hook sería automático pero
  agregar Husky + lint-staged es over-engineering por ahora.
- **SendPasswordReset sin tests**: el método más complejo del writer
  (5 deps además de IMemberService) sigue uncovered. Tracked como
  deferred next cap.
- **NSubstitute añadido**: test project ahora tiene una dep adicional.
  Trade-off vs hand-writing 50+ member stubs por interface. Worth
  it — el pattern se reutiliza.

**Neutras:**

- 4 commits feat batch + 1 commit ADR docs.
- 0 GUIDs nuevos.
- 2 NuGet packages nuevos (QRCoder runtime + NSubstitute test-only).
- 0 schema rompedor.

## Implementation summary

| # | Foco | Repo | Commit |
|---|---|---|---|
| 221-222 | Encryption-at-rest + tests | CMS | `ddcbae9` |
| 223-224 | QRCoder + QR inline en enrollment view | CMS | `7722623` |
| 225-226 | sync-tokens.mjs + auto-regen _tokens-bridge.scss | UI | `64ae49c` |
| 227-228 | NSubstitute + UmbracoMemberRosterWriterTests | CMS | `b0eefc5` |
| 229-230 | (este) ADR + current-state §11.20 |

## Próximas direcciones

- **`IEmailTemplateRenderer` interface refactor**: para que
  `SendPasswordResetAsync` sea testeable sin stubbing concrete class.
- **2FA multi-instance**: configurar `PersistKeysToX` en composer
  cuando llegue scale-out requirement.
- **Sync tokens pre-commit hook**: Husky + lint-staged si el equipo
  decide automation > control explícito.
- **Contract tests UI**: Vitest harness validando cada custom
  element fires `syn:component:ready`.
- **GDPR RTBF coordinator**: `IGdprRtbfCoordinator` + admin endpoint.
- **Performance benchmarks**: BenchmarkDotNet harness + targets.

## References

- ADR 0034 — Member self-service runtime base.
- ADR 0067 — IAuditTrailWriter (encryption pattern reusable).
- ADR 0076 — 2FA Phase 1 (TOTP + admin reset).
- ADR 0081 — 2FA Phase 2.A (recovery codes + enrollment view).
- ADR 0082 — Cap-210 (2FA Phase 2.B login flow).
- ADR 0083 — Cap-220 CMS↔UI alignment via contracts.
- [QRCoder on nuget.org](https://www.nuget.org/packages/QRCoder).
- [NSubstitute on nuget.org](https://www.nuget.org/packages/NSubstitute).
- [ASP.NET Core Data Protection](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/).
