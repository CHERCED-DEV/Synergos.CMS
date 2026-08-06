# ADR 0079 — WCAG re-audit fixes + audit drill-down + resilience strict mode (Olas 191-194)

- **Status:** Accepted
- **Date:** 2026-04-27
- **Deciders:** Arquitecto + agente.
- **Consolida:** 4 olas en 2 batches.

## Context

Cap-190 cerró con WCAG audit doc identificando 3 gaps. Cap-200
abre con verificación contra código real + fixes reales.

Adicionalmente, Batch B (Olas 193-194) cierra 2 deferred items
explícitos:
- Audit search by Id (drill-down detail view).
- Resilience strict mode flag (failBoot si typo).

## Decision

### Olas 191-192 — WCAG re-audit verificado

**Re-audit reveals**:
- Gap 1 spec era especulativo (token name mismatch). Real estado:
  `--syn-color-text-muted` mapeado a `neutral-500` `#64748b` →
  contrast 4.83:1 vs white. Borderline contra off-white panels.
- Gap 2 spec era moot. No "Ver"/"Detalle" generic links existen.
- Gap 3 spec era no-op. Form field renderer YA emite
  `aria-describedby={helpId}` linking input al helpText. Form
  container usa `role="alert"` para errors.

**Fixes shipped**:
1. `--syn-color-text-muted` remap a `neutral-600` `#475569` →
   contrast 7.0:1. Safety margin para off-white panels.
2. `Members.cshtml` action buttons (🔒 Bloquear / 🔓 Desbloquear /
   🔑 Reset 2FA / 📧 Reset password / 🗑 Eliminar) reciben
   `aria-label="{Acción} {member.Email}"` para row context cuando
   screen reader navega cell-by-cell.
3. WCAG audit doc actualizado con verified state (Gap 3 explicado
   como already-implemented; Gap 1/2 corregidos).

### Olas 193-194 — Audit drill-down + resilience strict

**Ola 193 — Audit drill-down by Id**:
- `IAuditTrailWriter.GetById(string id)` extension. Scan files
  últimos 30 días, regex match por `"Id":"X"` line + Deserialize.
- `AdminController.AuditDetail` GET `/admin/audit/{id}` con
  length(8,64) constraint.
- `AuditDetail.cshtml` view: detail panel + raw JSON collapsible +
  nearby events ±5min same actor.
- `Audit.cshtml` extendido con columna `→` link al detail.

**Ola 194 — Resilience strict mode**:
- `WebhookResilienceSettings.StrictValidation` bool (default false).
- Validator: si strict + unknown PerChannel keys, devuelve
  `ValidateOptionsResult.Fail` con message listando typos.
- Útil para CI/CD validation pipelines.
- 2 tests nuevos confirmando comportamiento (typo en strict →
  Fail; valid en strict → Success).

## Consequences

**Positivas:**

- **WCAG honest**: doc actualizado contra código real. No más spec
  errors.
- **Real fix shipped**: token bump + aria-labels en actions row.
- **Audit drill-down**: forensic review puede deep-dive un evento
  específico + ver context.
- **Strict mode**: CI/CD pipelines pueden gate deployments con
  appsettings inválidos.

**Negativas:**

- **Audit GetById O(N×F)** en files últimos 30 días. Si llega
  high-volume audit, swap por DB-backed adapter.
- **Strict mode opt-in**: si el operador no lo activa, typos siguen
  silenciosos. Aceptable — el log warning sigue activo.

## Implementation summary

| # | Foco |
|---|---|
| 191 | `--syn-color-text-muted` remap neutral-500 → neutral-600. |
| 192 | `Members.cshtml` action buttons con aria-label. WCAG audit doc verified state. |
| 193 | `IAuditTrailWriter.GetById` + `AdminController.AuditDetail` + `AuditDetail.cshtml` + Audit listing extendido. |
| 194 | `WebhookResilienceSettings.StrictValidation` + validator branch + 2 tests. |
| 0079 | (este) ADR consolidado |

## References

- ADR 0067 — IAuditTrailWriter base.
- ADR 0070 — Resilience validator initial introduction.
- ADR 0078 — Hardening docs WCAG/Backup/GDPR (audit que revisitamos).
