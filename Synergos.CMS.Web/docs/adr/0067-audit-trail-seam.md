# ADR 0067 — IAuditTrailWriter seam + file-based audit log (Olas 153-154)

- **Status:** Accepted
- **Date:** 2026-04-27
- **Deciders:** Arquitecto + agente, scope-amplio batch.
- **Consolida:** 2 olas en un único ADR.

## Context

Las acciones admin (approve/reject/spam/delete/bulk) se observaban
solo en logs del proceso (rotaban) y como `IAnalyticsTracker.Track`
events (LoggerAnalyticsTracker default emite a `ILogger<>`). Para
forensic review post-incident — *"¿quién aprobó este comentario que
debería haber sido rejected?"* — necesitábamos un registro
durable + queryable + identificando al actor.

ADR 0037 definió `IAnalyticsTracker` para business events (search
queries, form submissions, cart events). Es read-only desde el
dashboard — los consumidores son systems externos (DataDog,
Splunk, Plausible). No es la abstracción correcta para audit
trail interno.

## Decision

### Ola 153 — IAuditTrailWriter seam

```csharp
public interface IAuditTrailWriter
{
    Task WriteAsync(AuditEvent evt, CancellationToken cancellationToken);
    IReadOnlyList<AuditEvent> GetRecent(
        int maxItems,
        string? actorEmailFilter = null,
        string? actionFilter = null);
}

public sealed record AuditEvent(
    string Id, DateTime OccurredAtUtc,
    string ActorEmail, string ActorName,
    string Action, string Resource,
    string Outcome, string Detail);
```

Append-only por diseño. Convención Action: `{resource}.{verb}` —
`comment.approve`, `member.lock`, `form.delete`. Outcome: `success` |
`failure` | `partial`.

`IMemberAccessGate` extendido con `CurrentMemberEmail` (de
`ClaimTypes.Email`) — el dashboard ya leía DisplayName + roles, faltaba
solo email para identificar al actor unívocamente en eventos.

### Ola 154 — FileSystemAuditTrailWriter

Persiste JSONL append-only en
`App_Data/syn-audit/{yyyy-MM-dd}.jsonl` — un archivo por día limita
el size de cada file y simplifica retention futura (delete files más
viejos que N días).

Concurrency via `lock` interno per-write. Read agrega los últimos 7
archivos diarios in-memory; filters aplicados post-fetch (actorEmail
exact match, action substring contains).

Idempotency por `Id` field — mismo Id no se escribe dos veces en el
mismo día (file contains check). Permite retry-safe writes desde el
caller sin duplicar.

Singleton en `SeamComposer` (depende solo de `IHostEnvironment` +
`ILogger`).

### Wiring en AdminController

Helper privado `EmitAuditAsync(action, resource, outcome, detail)`
captura email + display name del current member via gate y escribe
el evento. Wireado en:

- `ApproveComment` → `comment.approve`
- `RejectComment` → `comment.reject`
- `MarkCommentAsSpam` → `comment.spam`
- `BulkApproveComments` → `comment.bulk-approve`
- `BulkRejectComments` → `comment.bulk-reject` (con `undoToken` en detail)
- `DeleteFormSubmission` → `form.delete`

Acciones member (lock/unlock) llegaron en Ola 156 — instrumentadas
con `member.lock` / `member.unlock`.

### Vista `/admin/audit`

`GET /admin/audit` member-gated con filter por actor email + action
substring + limit 1-500 (default 100). View `Audit.cshtml` con table
(Cuándo / Actor / Action / Resource / Outcome / Detail) + filter form.

Topbar entry "Auditoría" agregado entre Webhooks y Health (1 GUID
nuevo verificado: `a9f63567`, Dictionary `Admin.Nav.Audit` traducido
es-CO/en-US).

## Consequences

**Positivas:**

- **Forensic review independiente de logs**: archivos JSONL persisten
  hasta que el operador los borre. Independiente del log retention
  policy del proceso.
- **Append-only por diseño**: imposible re-escribir history desde el
  dashboard. Mitigación contra moderator que aprueba algo y luego
  edita el log para ocultarlo — el audit file system es una linea
  defensiva extra.
- **Identificación canónica del actor**: email + display name via
  `IMemberAccessGate`. Si un Member es renombrado luego, el log
  preserva el nombre del momento del evento.
- **JSONL machine-readable**: `tail -f` + `jq` funcionan directo.
  Para agregación a SIEM, los archivos son log-shippable.

**Negativas:**

- **File-based no scale para multi-instance**: cada instancia escribe
  a su propio `App_Data/syn-audit/`. Si hay LB + N replicas, los logs
  se fragmentan. Para deploys así, swap por adapter sobre DB / event
  store con ADR aparte.
- **No retention automático**: archivos crecen indefinidamente. El
  operador debe gestionar manualmente (cron job que mueva archivos >
  90 días a S3 cold storage o similar). Mitigación futura: setting
  `AuditRetentionDays` con un hosted service que elimine.
- **Read N días limitado**: `GetRecent` solo carga los últimos 7
  archivos diarios. Para queries más viejos, leer los JSONL directos.
- **Bulk events agrupados**: `comment.bulk-approve` registra count en
  detail pero no enumera los IDs específicos. Para auditoría granular,
  emitir un evento per-item (deferred — costo vs valor depende del
  threat model).

**Neutras:**

- 1 commit feat batch (Olas 153+154 unificadas) + 1 docs ADR.
- 1 GUID nuevo (`admin.nav.audit`).
- 0 NuGet packages nuevos.
- 0 schema rompedor.

## Implementation summary

| # | Foco |
|---|---|
| 153 | `IAuditTrailWriter` seam + `AuditEvent` record + `IMemberAccessGate.CurrentMemberEmail`. |
| 154 | `FileSystemAuditTrailWriter` JSONL en App_Data/syn-audit/{yyyy-MM-dd}.jsonl + AdminController.EmitAuditAsync helper + 6 actions instrumentadas (approve/reject/spam/bulk-approve/bulk-reject/form-delete) + `/admin/audit` view + topbar entry. |
| 0067 | (este) ADR consolidado |

## Próximas direcciones

- **DB-backed audit trail** cuando llegue deploy multi-instance.
- **Retention automático** via hosted service que purge archivos >
  N días.
- **Audit per-item en bulk** — granularidad para sites con
  compliance estricto.
- **Audit export CSV** — paralelo del CSV de form submissions.
- **Cross-reference con analytics**: dashboard que correlate eventos
  audit con `IAnalyticsTracker` (mismo timestamp + actor) para vista
  unificada.

## References

- ADR 0037 — `IAnalyticsTracker` (business events, distinct seam).
- ADR 0051 — Admin moderation dashboard SSR (consumer original).
- ADR 0063 — Member roster admin (donde llegará Ola 156 con
  `member.lock` + `member.unlock` events).
