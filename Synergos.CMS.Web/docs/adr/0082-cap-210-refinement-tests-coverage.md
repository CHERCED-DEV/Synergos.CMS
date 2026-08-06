# ADR 0082 — Cap-210 refinement: 2FA login flow + tests coverage gaps (Olas 201-210)

- **Status:** Accepted
- **Date:** 2026-04-27
- **Deciders:** Arquitecto + agente, post-audit honesto.

## Context

Tras un audit honesto de los 4 subprojects (delegado a un Explore
agent), 2 categorías de gaps reales surgieron:

1. **2FA Phase 2.B login flow no shipped** — el feature flagship
   del cap-200 estaba incompleto. ADR 0076 + 0081 shipped seam +
   enrollment view + admin reset, pero `AccountController.Login`
   no consumía `IMemberTwoFactorService.VerifyAsync`. Un Member que
   activaba 2FA en `/account/2fa-setup` **no obtenía seguridad real**:
   el siguiente login no le pedía TOTP. Documentamos como "Phase 2.B
   deferred" pero esto convertía 2FA en decoración pura.

2. **Test coverage spotty** en seams nuevos (cap-150-200): muchos
   compilaban + tenían consumers, pero sin contract tests. Riesgo
   de regresión silenciosa.

## Decision

### Olas 201-204 — Cierre del 2FA login flow

`IMemberAuthService` extends:
- `ValidateCredentialsAsync(emailOrUsername, password)` →
  `LoginValidationResult(Success, Email, MemberKey, ErrorCode)`.
  Valida sin firmar sesión.
- `SignInByEmailAsync(email, isPersistent)` — completa sign-in
  post-challenge.

`DefaultMemberAuthService` impl:
- `ValidateCredentialsAsync` usa `_memberManager.FindByEmailAsync`
  o `FindByNameAsync` + `IsLockedOutAsync` + `CheckPasswordAsync` +
  `AccessFailedAsync`/`ResetAccessFailedCountAsync` — preserva el
  contador estándar de Identity.
- `SignInByEmailAsync` simple wrapper sobre
  `_signInManager.SignInAsync`.

`AccountController.LoginPost` rewrite:
1. ValidateCredentialsAsync.
2. Si `IsEnabledAsync(memberKey)` true → cache pending challenge
   (`PendingTwoFactorLogin` record con Email/MemberKey/IsPersistent/
   ReturnUrl) en `IMemoryCache` con token random + TTL 5 min.
3. Redirect a `/account/2fa-challenge?token=X`.
4. Sino → SignInByEmailAsync directo + redirect.

Nuevas actions:
- `GET /account/2fa-challenge?token=X` — render view si token cache hit.
- `POST /account/2fa-challenge` — VerifyAsync + sign-in. Si
  `RecoveryConsumed`, analytics method=recovery; si `TotpOk`,
  method=totp.

`TwoFactorChallenge.cshtml` view: form con AntiForgeryToken + hidden
token + input code (acepta TOTP O recovery). Error rendering si
invalid-code. Link de vuelta al login.

Analytics events nuevos:
- `account.login-2fa-required`
- `account.login-2fa-failed`
- `account.login-2fa-success` (con method=totp|recovery)

### Olas 205-207 — Tests HIGH severity

**`FileSystemAuditTrailWriterTests` extends** (5 tests nuevos):
- `GetById_ExistingEvent_ReturnsIt`
- `GetById_NonexistentId_ReturnsNull`
- `GetById_EmptyOrWhitespaceId_ReturnsNull`
- `GetById_FindsAcrossMultipleFiles`
- `GetById_ReturnsExactJsonShape`

**`FileSystemMemberTwoFactorStoreTests` nuevo** (6 tests):
- Read/Write/Save-overwrites/Delete/Read-corrupt-json.
- StubHostEnvironment + IDisposable temp dir cleanup.

**`TwoFactorRecoveryCodes` extracted como pure helper static**:
- Refactor de `UmbracoMemberTwoFactorService` — el código de
  generación/hash/verify estaba inline + privado, ahora helper
  static testeable directamente.
- Cero cambio funcional.
- 12 tests cubriendo: count + length + alphabet safe + uniqueness +
  hash-plaintext pairing + verify-correct/wrong/empty/malformed +
  case-sensitivity + random salt produces unique hashes.

### Olas 208-209 — Tests MEDIUM severity

**`CompositeNotifiersTests` nuevo** (6 tests, 3 dominios):
- Comments + Forms + Cart composite dispatch fan-out.
- BrokenChannel isolation (try-catch per channel preserva los demás).
- Empty channels no-op.

Counting + Throwing channel stubs locales para count assertions.

**`FileSystemFormSubmissionHandlerTests` nuevo** (6 tests):
- SubmitAsync persists.
- DeleteAsync removes file + listing reflects.
- Nonexistent + empty args → false.
- Path traversal `../../etc/passwd` rejected by sanitizer (security).
- GetSubmission post-delete returns null.

### Ola 210 — Cierre cap-210

Este ADR + actualización current-state §11.18 + README index.

## Consequences

**Positivas:**

- **2FA real shipped**: el feature flagship no es decoración —
  Member que enroll obtiene seguridad real en login.
- **Coverage cap-200 cubierta**: los seams nuevos tienen tests
  unitarios. Total tests: 113 → 148 (+35 tests nuevos, +30%).
- **Crypto helper extracted**: `TwoFactorRecoveryCodes` reusable +
  testeable independientemente. Pattern para futuras crypto helpers.
- **Path traversal verificado**: el sanitizer del FormSubmissionHandler
  ahora tiene un test contra el ataque conocido.
- **Honest audit**: el reporte del Explore agent identificó issues
  reales (no especulativos) y los cerramos.

**Negativas:**

- **Member CRUD destructive sin tests** (Delete/SetRoles/SendPasswordReset):
  requieren stubs heavy de `IMemberService`+`IMemberAuthService`+
  `IEmailService`+`RazorEmailTemplateRenderer`+`IBrandingProvider`+
  `IHttpContextAccessor`. Skipped — necesitaría un Umbraco TestContext
  o Moq/NSubstitute. Tracked como deferred.
- **2FA challenge cache es in-memory single-instance**: si el deploy
  escala a > 1 instancia, sticky sessions o DB-backed pending
  challenge necesarios. Documentado como trade-off.
- **Token no rotates entre re-renders**: un atacante con el token
  puede reintentar code múltiples veces hasta TTL 5min. Aceptable —
  necesita ya tener password validada.

**Neutras:**

- 4 commits feat + 1 commit docs ADR.
- 0 GUIDs nuevos, 0 NuGet packages nuevos.
- 0 schema rompedor.

## Implementation summary

| # | Foco | Commit |
|---|---|---|
| 201-204 | 2FA login flow (ValidateCredentials/SignInByEmail seams + AccountController + TwoFactorChallenge.cshtml + audit) | `711c95f` |
| 205-207 | Tests HIGH: audit GetById (5) + 2FA store (6) + recovery codes helper (12) | `25b7cd5` |
| 208-209 | Tests MEDIUM: composite notifiers (6) + form submission handler (6) | `aeea975` |
| 210 | (este) ADR consolidado |

## Próximas direcciones (post-Ola 210)

- **Member CRUD destructive tests**: cuando llegue Moq/NSubstitute o
  un Umbraco TestContext.
- **2FA challenge cache DB-backed**: para multi-instance LB.
- **2FA encryption-at-rest**: wrap secret + recovery codes via
  `IDataProtectionProvider`. Phase 2.C.
- **QR rendering** del enrollment URI (NuGet QRCoder o equivalent).

## References

- ADR 0034 — Member self-service runtime base.
- ADR 0067 — IAuditTrailWriter (GetById extension testada).
- ADR 0075 — Tests gate revisitado (este ADR continúa el flujo).
- ADR 0076 — 2FA Phase 1.
- ADR 0081 — 2FA Phase 2.A (este cierra Phase 2.B login flow).
