# ADR 0077 — Members destructive CRUD: delete + password reset + role-toggle (Olas 181-184)

- **Status:** Accepted
- **Date:** 2026-04-27
- **Deciders:** Arquitecto + agente.

## Context

ADR 0068 introdujo `IMemberRosterWriter` con solo lock/unlock
(reversible). Diferido §11.13 listó "Members admin CRUD destructivo
(delete/password reset/role-toggle) — necesita threat model por feature"
como next step.

Cap-190 cierra el subset destructivo con threat model documentado.
**Hard delete** sin undo (la operación es irreversible por diseño,
distinct del soft-delete pattern usado en bulk-reject de comments —
ADR 0056). **Password reset** delega al flow self-service existente
(ADR 0034 / 0044). **Role-toggle** con replace-set semantics.

## Decision

### Threat model

| Threat | Mitigation |
|---|---|
| Moderator malicioso elimina admin de competencia | Admin role inheritance hierarchy no implementado — todos admins/moderators/editors tienen permission delete. **Mitigación**: audit trail (ADR 0067) preserva quien hizo qué. Forensic post-incident. Future: role hierarchy guard. |
| Self-delete accidental | `DeleteMember` action verifica `_gate.CurrentMemberEmail` vs target email; si match, audit failure detail "self-delete blocked" + redirect sin tocar el repo. |
| Bulk delete por error | UI requiere click-confirm dialog `<dialog>` per item. No bulk endpoint. |
| Comment authorship orphan tras delete | Comments retienen `authorEmail`/`authorName` literales en JSON (file-based). El Member borrado no rompe comments existentes — quedan con author orphan. GDPR RTBF documenta el anonymization manual (`docs/hardening/gdpr-rtbf.md`). |
| Password reset via admin = login bypass | El admin reset NO setea contraseña directamente — solo dispara el mismo email flow del self-service `/account/forgot-password`. Token expira en 1h (Umbraco default). Member ve link y elige nueva contraseña. Audit `member.password-reset-sent`. |
| Role-toggle escalation | Member-gated CSV `admin/moderator/editor` aplica al endpoint. Si un editor agrega "admin" a un Member, ese Member tras login tendrá permission admin. **Mitigación**: future role hierarchy validation. Current state: trust + audit. |

### Ola 181 — IMemberRosterWriter extends

3 nuevos métodos:

```csharp
Task<bool> DeleteAsync(Guid memberKey, CancellationToken ct);
Task<bool> SendPasswordResetAsync(Guid memberKey, CancellationToken ct);
Task<bool> SetRolesAsync(Guid memberKey, IReadOnlyCollection<string> roleNames, CancellationToken ct);
```

### Ola 182 — UmbracoMemberRosterWriter impl

`DeleteAsync`:
- `IMemberService.GetByKey` → si null, return false.
- `IMemberService.Delete(member)` — Umbraco hard-delete + cascade
  notifications.
- Logger.LogWarning con email para forensic.

`SendPasswordResetAsync` — composes el email reusing `PasswordReset.cshtml`
template:
- Inyecta `IMemberAuthService` (token gen) + `IEmailService` (send) +
  `RazorEmailTemplateRenderer` (template) + `IBrandingProvider` +
  `IHttpContextAccessor` (URL).
- Reset URL absoluta con scheme/host del request actual.
- Audit emit en AdminController, no aquí.

`SetRolesAsync` — replace-set semantics:
- Compute diff entre current roles + target roles.
- `IMemberService.AssignRoles` para added.
- `IMemberService.DissociateRoles` para removed.
- Idempotent: empty diff = no-op.

### Ola 183 — AdminController POST actions

3 nuevas actions con member-gating + audit emit:

- `POST /admin/members/{key}/delete` — con self-delete guard +
  auto 2FA reset al éxito.
- `POST /admin/members/{key}/password-reset`.
- `POST /admin/members/{key}/roles` — `[FromForm] string[] roles`
  binding, normaliza con Trim + Distinct.

### Ola 184 — Members.cshtml view

- Action column nuevos buttons: 📧 Reset password (form POST), 🗑
  Eliminar (button con `data-member-delete` que abre dialog).
- Sub-row con role checkboxes per role en `allRoles`. Form POST
  `/admin/members/{key}/roles` con todos los checked como array.
- Native `<dialog id="member-delete-confirm">` con script vanilla
  setting form.action dinámicamente al click. Cancel button.
- Reusa CSS classes `syn-admin__action--reject` (para delete) +
  `--ghost` (para reset). Nada nuevo en CSS.

## Consequences

**Positivas:**

- **Operacional completo**: admin puede gestionar Members destructive
  ops desde dashboard sin abrir backoffice.
- **Audit trail**: cada operación deja trazo (`member.delete`,
  `member.password-reset-sent`, `member.set-roles`).
- **Self-delete guard**: defensive even si UI mal configura.
- **Reuso de password-reset email template**: UX consistency con
  el self-service flow.

**Negativas:**

- **Sin role hierarchy validation**: un editor puede agregar role
  admin a otro Member. Por ahora trust + audit; future ADR puede
  agregar guard.
- **Hard-delete no undo**: a diferencia de bulk-reject. Documented
  + dialog confirm es la mitigación.
- **Comments orphan**: GDPR RTBF requiere manual anonymization.
  Automated en cap futuro.
- **Email composition embebida en writer**: `UmbracoMemberRosterWriter`
  ahora depende de IEmailService + RazorEmailTemplateRenderer +
  IBrandingProvider + IHttpContextAccessor. God-object risk. Refactor
  a `IPasswordResetCoordinator` cuando llegue a Rule of Three.

**Neutras:**

- 1 commit feat + 1 docs ADR.
- 0 GUIDs nuevos.
- 0 NuGet packages nuevos.

## Implementation summary

| # | Foco |
|---|---|
| 181 | `IMemberRosterWriter` + 3 métodos + records. |
| 182 | `UmbracoMemberRosterWriter` impl con 4 deps adicionales. |
| 183 | `AdminController.DeleteMember`/`SendMemberPasswordReset`/`SetMemberRoles` actions con member-gate + audit emit + self-delete guard. |
| 184 | `Members.cshtml` view extendida: action buttons + role checkboxes sub-row + native dialog confirm. |
| 0077 | (este) ADR consolidado |

## Próximas direcciones

- **Role hierarchy guard**: editor no puede asignar admin role.
- **Bulk delete con confirm**: si llegan requests de bulk ops.
- **Audit query by Member**: filter audit trail por `Resource`
  contains `memberKey`.
- **Comment anonymization automated** post-delete (GDPR RTBF flow,
  ADR 0078 docs).

## References

- ADR 0034 — Member self-service runtime (origen del flow
  `/account/forgot-password`).
- ADR 0044 — Email templates (PasswordReset.cshtml reusado).
- ADR 0063 — Member roster reader.
- ADR 0067 — Audit trail seam.
- ADR 0068 — Member roster writer (lock/unlock, este lo extiende).
- `docs/hardening/gdpr-rtbf.md` — GDPR right-to-be-forgotten flow.
