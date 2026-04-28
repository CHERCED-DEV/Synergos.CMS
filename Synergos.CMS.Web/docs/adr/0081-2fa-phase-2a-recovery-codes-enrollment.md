# ADR 0081 — 2FA Phase 2.A: recovery codes + Member self-service enrollment (Olas 197-198)

- **Status:** Accepted
- **Date:** 2026-04-27
- **Deciders:** Arquitecto + agente.

## Context

ADR 0076 (2FA Phase 1) shipped TOTP service + admin reset action.
**Phase 2 deferred items**:
- Recovery codes (8 single-use generated at enrollment).
- Member self-service enrollment view.
- Login flow extension (post-password challenge).
- Encryption-at-rest del secret + recovery codes.

Phase 2.A (este ADR) ships **recovery codes** + **enrollment view**.
**Phase 2.B deferred**: login challenge + encryption-at-rest +
QR rendering.

## Decision

### Ola 197 — Recovery codes en UmbracoMemberTwoFactorService

**Generation**:
- 8 codes × 8 chars del alphabet
  `"ABCDEFGHJKLMNPQRSTUVWXYZ23456789"` (32 chars, sin 0/O/I/1/L
  para evitar ambigüedad visual).
- Random via `RandomNumberGenerator.GetInt32`.

**Storage** (hashed, plain stored solo single-use cache):
- PBKDF2 SHA-256 + 100k iterations + 16-byte random salt per code.
- Format string `"{base64Salt}:{base64Hash}"` per code.
- `RecoveryCodes` field del `TwoFactorRecord` ahora populated
  (Phase 1 era empty array).

**Plaintext display** (UNA vez):
- `ConfirmEnrollmentAsync` genera codes + hashea + persiste +
  guarda plaintext en `ConcurrentDictionary<Guid, IReadOnlyList<string>>`
  static (caller-scoped).
- `ConsumeLastEnrollmentRecoveryCodes(memberKey)` devuelve plaintext
  UNA vez + borra del cache. Caller (controller) los muestra al
  member en la confirmation view.
- Single-use por diseño: si el caller no consume, los codes se
  pierden (member tendrá que re-enroll vía admin reset).

**Verification**:
- `VerifyAsync` extiende: si TOTP falla, itera `RecoveryCodes`
  hashes con PBKDF2 + `CryptographicOperations.FixedTimeEquals`.
- Si match, remove ese hash del array + persist updated record.
- Returns `RecoveryConsumed` (no `TotpOk`) para que el caller
  pueda warn al member que usó un recovery code.

### Ola 198 — Member self-service enrollment view

**`IMemberAccessGate.CurrentMemberKey`** extendido (Guid?). Lee
`ClaimTypes.NameIdentifier` del User principal — Umbraco
`MemberIdentityUser` persiste el Member.Key allí.

**`AccountController.TwoFactorSetup`** GET `/account/2fa-setup`:
- Si `IsEnabledAsync` true: mostrar status panel + nota sobre
  contactar admin para reset.
- Si false: `StartEnrollmentAsync` → secret + provisioning URI.
  View con secret (Base32) + URI collapsible (QR rendering
  deferred).

**`AccountController.TwoFactorSetupConfirm`** POST `/account/2fa-setup`:
- AntiForgeryToken validation.
- `ConfirmEnrollmentAsync(memberKey, secret, code)`.
- Si `Confirmed`: `ConsumeLastEnrollmentRecoveryCodes` + redirect
  view `TwoFactorSetupConfirmed.cshtml` mostrando los 8 codes UNA
  vez con warning "single-use, copy to safe place".
- Si `InvalidCode`: re-render setup con `ErrorCode="invalid-code"`
  + same secret (QR no cambia entre attempts).

## Consequences

**Positivas:**

- **Coverage end-to-end del enrollment**: member puede activar 2FA
  por sí mismo desde `/account/2fa-setup`. Antes solo admin podía
  reset (Phase 1).
- **Recovery codes secure**: PBKDF2 100k iterations + salt per code
  + constant-time compare. Si el archivo `App_Data/syn-2fa/*.json`
  se filtra, los codes plaintext NO están allí (solo hashes).
- **Single-use enforcement**: VerifyAsync persiste el array shrink
  por uso. No hay race window — si dos requests usan el mismo code,
  uno gana, otro retorna `Invalid`.
- **Plaintext shown once**: matches industry standard (GitHub,
  Google) — member sabe que tiene que copiarlos ahora.

**Negativas:**

- **Sin login flow integration**: enrollment + admin reset funcional,
  pero login NO pide TOTP aún. Member que enroll y luego se desloguea
  + entra → no pasa por TOTP challenge. Phase 2.B cierra eso.
- **QR rendering deferred**: member debe copiar/pegar el secret a
  la app. Más friction que QR scan. Fix futuro con QRCoder NuGet.
- **Plaintext en memory cache**: si el process restart entre
  ConfirmEnrollment y ConsumeLastEnrollmentRecoveryCodes (~milliseconds),
  se pierden. Aceptable — member ve error + reset → re-enroll.
- **Encryption-at-rest still deferred**: secret en plain JSON.
  `App_Data/syn-2fa/{key}.json` filesystem leak = 2FA bypass para
  todos. Phase 2.B con `IDataProtectionProvider`.

## Implementation summary

| # | Foco |
|---|---|
| 197 | `UmbracoMemberTwoFactorService` extends: GenerateRecoveryCodesAndHashes + PBKDF2 hash + ConsumeLastEnrollmentRecoveryCodes + VerifyAsync recovery branch con FixedTimeEquals. |
| 198 | `IMemberAccessGate.CurrentMemberKey` + `AccountController.TwoFactorSetup`/`TwoFactorSetupConfirm` actions + `TwoFactorSetup.cshtml` 2-step UI + `TwoFactorSetupConfirmed.cshtml` recovery codes display. |
| 0081 | (este) ADR consolidado |

## Próximas direcciones (Phase 2.B)

- **Login flow extension**: post-password verify if member.IsEnabled,
  redirect a `/account/2fa-challenge` view con TOTP/recovery input.
- **Encryption-at-rest**: wrap `TwoFactorRecord` con
  `IDataProtectionProvider`. Master key en KMS.
- **QR rendering**: NuGet QRCoder o equivalente, embed PNG inline
  como `<img src="data:image/png;base64,...">`.
- **Recovery codes regeneration**: action que invalida codes
  actuales + genera nuevos 8 (member-triggered desde profile).

## References

- ADR 0034 — Member self-service runtime (login/register baseline).
- ADR 0044 — Email templates (no usado here pero referenced for
  consistency en futuro).
- ADR 0067 — Audit trail seam (account.2fa-enrolled event tracked).
- ADR 0074 — IMemberTwoFactorService seam shape.
- ADR 0076 — 2FA Phase 1 (TOTP + admin reset).
- [RFC 2898](https://datatracker.ietf.org/doc/html/rfc2898) — PBKDF2.
- [GitHub recovery codes UX](https://docs.github.com/en/authentication/securing-your-account-with-two-factor-authentication-2fa) — pattern reference.
