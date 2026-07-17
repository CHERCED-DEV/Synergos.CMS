# ADR 0076 — 2FA Phase 1: TOTP service + admin reset action (Olas 177-180)

- **Status:** Accepted
- **Date:** 2026-04-27
- **Deciders:** Arquitecto + agente.

## Context

ADR 0074 introdujo el shape de `IMemberTwoFactorService`. Phase 1
ships la implementación TOTP server-side + admin operational path
(reset). **Phase 2 deferred**: Member self-service enrollment view +
login flow extension + recovery codes + encryption-at-rest.

Razón del split: full 2FA flow tiene mucho surface area (TOTP +
recovery + encryption + enrollment UI + login challenge). Shipping
todo en un cap es alto-risk. Phase 1 prueba el seam con la pieza
operacional más útil para sysadmin (reset cuando un Member pierde
device + recovery codes).

## Decision

### Ola 177 — Otp.NET 1.4.1

Verificado en nuget.org. Agregado a `Directory.Packages.props` +
`Synergos.CMS.Web.csproj` PackageReference. Library para TOTP
RFC 6238 + base32 encoding del shared secret.

### Ola 178 — FileSystemMemberTwoFactorStore

Persiste `TwoFactorRecord(SecretBase32, IsEnabled, RecoveryCodes,
EnrolledUtc)` en `App_Data/syn-2fa/{memberKey}.json`. Mismo pattern
que `FileSystemAuditTrailWriter` + `FileSystemCommentRepository`.

**Trade-off**: file-based en lugar de Member custom property:
- ✅ No requiere uSync schema change (rapid iteration).
- ✅ No accidental export del secret via uSync ExportOnSave.
- ✅ Permite encryption-at-rest en Phase 2 sin migrar backoffice.
- ❌ No scale para multi-instance LB sin shared filesystem (deferred).

### Ola 179 — UmbracoMemberTwoFactorService impl

Implementa `IMemberTwoFactorService` usando `Otp.NET.Totp` para
TOTP generation/verification:

- `StartEnrollmentAsync`: 160-bit secret random + provisioning URI
  `otpauth://totp/Synergos:{email}?secret=...&issuer=Synergos&algorithm=SHA1&digits=6&period=30`.
- `ConfirmEnrollmentAsync`: valida primer código TOTP con drift
  window ±1 step (~30s tolerance) + persiste `IsEnabled=true`.
- `VerifyAsync`: TOTP runtime check con same drift window.
- `DisableAsync`: borra el record file (admin reset path).
- `IsEnabledAsync`: consulta `IsEnabled` del record.

**Recovery codes**: deferido a Phase 2. Por ahora `RecoveryCodes` es
empty array.

Wired transient en `SeamComposer`; `FileSystemMemberTwoFactorStore`
singleton.

### Ola 180 — AdminController.ResetMemberTwoFactor

POST `/admin/members/{key}/2fa-reset` member-gated:

```csharp
var ok = await _memberTwoFactor.DisableAsync(memberKey, ct);
await EmitAuditAsync("member.2fa-reset", $"memberKey={memberKey:N}",
    ok ? "success" : "failure", ct);
```

`Members.cshtml` view extendida con boton condicional "🔑 Reset 2FA"
visible solo cuando `IsEnabled` retorna true.

## Consequences

**Positivas:**

- **Foundation server-side completa**: el seam, persistence, service
  impl + admin op path están shipped. Phase 2 (UI + login flow + recovery)
  puede ramp up sin re-arquitectura.
- **Audit cubierto**: cada reset deja trazo en
  `App_Data/syn-audit/*.jsonl` con actor + timestamp + outcome.
- **Cero schema rompedor**: no DocType extension. File-based isolation.
- **Otp.NET 1.4.1 estable**: RFC 6238 standard, ampliamente usado en
  ecosystem .NET.

**Negativas:**

- **No usable end-to-end aún**: sin enrollment UI + login flow, los
  Members no pueden activar 2FA. La feature is dormant — Phase 2 la
  desbloquea.
- **Plain-text secret at rest**: `SecretBase32` y futuros recovery
  codes en plain JSON. Si `App_Data/syn-2fa/` se filtra (backup leak,
  filesystem misconfig), todos los secretos comprometidos. Phase 2
  agrega `IDataProtectionProvider` encryption.
- **No multi-instance**: file-based per host. Sticky sessions o
  DB-backed adapter requeridos para scale-out.

**Neutras:**

- 1 commit feat + 1 NuGet package (Otp.NET 1.4.1).
- 0 GUIDs nuevos.
- 0 schema rompedor.

## Implementation summary

| # | Foco |
|---|---|
| 177 | Otp.NET 1.4.1 a Directory.Packages.props + Synergos.CMS.Web.csproj. |
| 178 | `FileSystemMemberTwoFactorStore` + `TwoFactorRecord` POCO. |
| 179 | `UmbracoMemberTwoFactorService` impl + wiring SeamComposer (singleton store + transient service). |
| 180 | `AdminController.ResetMemberTwoFactor` POST action + audit emit + `Members.cshtml` view button. |
| 0076 | (este) ADR consolidado |

## Próximas direcciones (Phase 2)

- **Member self-service enrollment**: `/account/2fa-setup` view con
  QR rendering + first code input.
- **Login flow extension**: post-password challenge si `IsEnabledAsync`
  retorna true. Redirect a `/account/2fa-challenge`.
- **Recovery codes**: 8 single-use codes generados en enrollment +
  hashed con PBKDF2/Argon2 al persistir + branch en `VerifyAsync`.
- **Encryption-at-rest**: wrap secret + recovery codes via
  `IDataProtectionProvider`. Master key en KMS.
- **Tests**: unit tests sobre TOTP verification con vectores RFC 6238
  reference.

## References

- ADR 0034 — Member self-service runtime (login flow base).
- ADR 0067 — IAuditTrailWriter (audit emit).
- ADR 0074 — IMemberTwoFactorService seam shape.
- [RFC 6238 — TOTP](https://datatracker.ietf.org/doc/html/rfc6238)
- [Otp.NET on nuget.org](https://www.nuget.org/packages/Otp.NET).
