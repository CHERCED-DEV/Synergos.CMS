# ADR 0078 — Hardening docs: WCAG audit + Backup/DR + GDPR RTBF (Olas 185-187)

- **Status:** Accepted
- **Date:** 2026-04-27
- **Deciders:** Arquitecto + agente.

## Context

Tras los caps anteriores cubriendo features (audit, telemetry, 2FA,
destructive CRUD), 3 categorías de **operational hardening** seguían
sin documentación formal:

1. **Accessibility**: claims aria/skip-link en algunas vistas pero
   sin audit formal contra un standard.
2. **Backup/DR**: cero docs sobre cómo respaldar + restaurar el
   stack. Cada deploy resolvía ad-hoc.
3. **GDPR RTBF**: el comportamiento de delete + audit immutability
   no estaba tracked formalmente contra Article 17.

Este batch ships **3 docs** + cero código (los gaps identificados
quedan como deferred concretos).

## Decision

### Ola 185 — `docs/hardening/wcag-audit.md`

WCAG 2.1 AA self-audit con tabla de los 30+ success criteria. Status
por criterio: Pass / Gap / N/A.

**3 gaps identificados** con remediación clara:

1. **Contrast text-subtle** en light theme: `--syn-color-text-subtle`
   actual 4.2:1 vs 4.5:1 mínimo. Bump a `#595d68` arregla.
2. **Link purpose en admin lists**: links "Detalle" sin contexto.
   Fix: `<span class="syn-visually-hidden">` con info específica.
3. **Form validation aria-describedby**: errores no vinculan al field.
   Fix: form renderer agrega `aria-describedby` + `id` + `role="alert"`.

Próximas direcciones documentadas: axe-core CI integration +
prefers-reduced-motion respect.

### Ola 186 — `docs/hardening/backup-and-recovery.md`

Persistence inventory de las 11 surfaces (DB + uSync + 7× App_Data +
media + appsettings) con criticidad + backup recipe per surface.

RPO/RTO targets propuestos: 15min / 60min. Recipes para SQL Server
(full + diff + tx log) + SQLite (PowerShell copy con timestamp) +
App_Data (rsync / S3).

Restore procedure 9-step incluyendo:
- Provision fresh host.
- Restore DB.
- Restore App_Data.
- Re-import secrets manualmente (NUNCA from leaked backup).
- uSync non-destructive Import para schema verification.
- Smoke test golden paths.

Recovery testing cadence: quarterly drill + monthly read-back.
Encryption-at-rest para 2FA secrets como deferred. Multi-instance
shared state notes (sticky sessions vs DB-backed adapters).

### Ola 187 — `docs/hardening/gdpr-rtbf.md`

Personal data inventory por surface (8 surfaces × erasure path).

**Audit trail special case**: events son inmutables for legal
compliance per GDPR 17(3). Anonymization después del retention
period (`AuditRetentionDays` default 90).

**Manual procedure 8-step** (current state):
1. Receive request.
2. Identify Member.
3. Hard-delete via `/admin/members/{key}/delete` (cascades 2FA).
4-6. Manual filesystem grep + edit comments + form submissions to
   anonymize PII fields.
7. Log erasure explícitamente con `gdpr.rtbf-processed` event.
8. Notify requester.

**Automated procedure proposed**: `IGdprRtbfCoordinator` seam con
`ProcessRequestAsync` que orquesta los 6 pasos + retorna
`RtbfResult(MemberDeleted, CommentsAnonymized, FormsAnonymized,
AuditPreserved)`. Admin endpoint nuevo
`POST /admin/members/{key}/gdpr-erase`. Deferred — flow complejo,
necesita ADR aparte con threat model.

Data minimization guidelines per surface:
- Comments: solo authorName + email.
- Form submissions: per-form schema declares.
- Audit: solo ActorEmail (admin staff), no del Member.
- Search analytics: query text + timestamp + count, sin email/IP.

Cookie consent rough plan (signed cookie con consent decision +
re-consent UI flow). Diferido per traffic profile.

## Consequences

**Positivas:**

- **Standard-aligned**: claims explícitos de qué passe (la mayoría) +
  qué falla (3 gaps concretos). External auditor / customer compliance
  team puede leer sin hacer adivinanza.
- **Operational runbook**: backup-and-recovery.md tiene recipes
  copy-paste runnables para SQL/SQLite/App_Data. Restore drill puede
  ejecutarse desde el doc.
- **GDPR-preparado**: la jurisdicción EU es viable hoy con manual
  procedure documentado. Automated flow deferred pero clear.

**Negativas:**

- **Solo docs, cero código**: los 3 gaps WCAG no se arreglaron en
  este cap. Quedan tracked como next direction.
- **Procedure manual GDPR es lento**: 8 pasos manuales no escalan
  para sites con > 1 RTBF/mes. Automated flow es deferred.
- **Backup/DR docs son guidelines, no scripts shipped**: el operador
  debe adaptar a su infra. Aceptable — no hay "infra Synergos
  oficial".

**Neutras:**

- 1 docs commit + 1 ADR.
- 0 GUIDs nuevos.
- 0 NuGet packages nuevos.
- 0 código tocado.

## Implementation summary

| # | Foco |
|---|---|
| 185 | `wcag-audit.md` — WCAG 2.1 AA self-audit + 3 gaps + remediation. |
| 186 | `backup-and-recovery.md` — persistence inventory + RPO/RTO + recipes + restore procedure. |
| 187 | `gdpr-rtbf.md` — RTBF flow manual + automated proposal + data minimization guidelines. |
| 0078 | (este) ADR consolidado |

## Próximas direcciones

- **WCAG gaps fixes**: token contrast bump + link purpose visually-hidden
  + form aria-describedby. ~3 olas más.
- **GDPR RTBF automated**: `IGdprRtbfCoordinator` seam +
  `/admin/members/{key}/gdpr-erase` action + threat model ADR.
- **Backup encryption**: `IDataProtectionProvider` wrap del 2FA store
  + custom config keys for KMS.
- **axe-core CI integration**: Playwright + axe scan en pipeline.

## References

- ADR 0034 — Member self-service runtime.
- ADR 0067 — IAuditTrailWriter (audit immutability per GDPR 17(3)).
- ADR 0068 + 0077 — Member roster writer + destructive CRUD.
- ADR 0076 — 2FA Phase 1 (encryption-at-rest deferred).
- [WCAG 2.1 AA](https://www.w3.org/TR/WCAG21/).
- [GDPR Article 17](https://gdpr-info.eu/art-17-gdpr/).
