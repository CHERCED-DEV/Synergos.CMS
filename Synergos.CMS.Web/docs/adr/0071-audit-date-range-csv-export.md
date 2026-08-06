# ADR 0071 — Audit date range query + CSV streaming export (Olas 163-164)

- **Status:** Accepted
- **Date:** 2026-04-27
- **Deciders:** Arquitecto + agente, scope-amplio batch.
- **Consolida:** 2 olas en un único ADR.

## Context

ADR 0067 introdujo `IAuditTrailWriter.GetRecent(maxItems, ...)` que
limita a los últimos 7 archivos diarios. Para compliance review más
amplios (e.g. *"qué hizo este moderator hace 2 meses"*), faltaba
date range query + export.

Compliance review pattern: filtros + export → spreadsheet → analyst.
Forms submissions ya tienen este pattern (ADR 0055 + 0056). Audit
trail debería seguir el mismo.

## Decision

### Ola 163 — `IAuditTrailWriter.GetByDateRange`

```csharp
IReadOnlyList<AuditEvent> GetByDateRange(
    DateTime fromUtc,
    DateTime toUtc,
    int maxItems,
    string? actorEmailFilter = null,
    string? actionFilter = null);
```

`FileSystemAuditTrailWriter` impl:
1. Iterar archivos `*.jsonl`, parsear filename como `yyyy-MM-dd`.
2. Pre-filter al rango de fechas (file-level).
3. Sort desc por filename.
4. Read con filter fino in-memory (event-level por
   `OccurredAtUtc >= fromUtc && <= toUtc`).
5. Take(maxItems).

`AdminController.Audit` cae a `GetByDateRange` cuando query string
tiene `from` o `to`; sigue usando `GetRecent` cuando no.

View `Audit.cshtml` extendido con 2 inputs `<input type="date">`
para Desde/Hasta UTC.

### Ola 164 — `AuditExportCsv` action

`GET /admin/audit/export?actor=...&action=...&from=...&to=...` con
streaming via `Response.Body.WriteAsync` (memoria O(1)). Hard cap
de `AdminSettings.CsvExportHardCap` (default 5000 events).

8 columnas: `OccurredAtUtc, ActorEmail, ActorName, Action, Resource,
Outcome, Detail, Id`.

BOM UTF-8 + `EscapeCsvField` reusado del FormSubmissions export.
`Content-Disposition: attachment; filename="synergos-audit-{from}-{to}.csv"`.

**Recursive instrumentation**: la action emite ella misma un evento
audit `audit.export` con el rango + count en detail. Permite forensic
de quien exporta el audit (turtles all the way down — pero parar ahí
tiene sentido).

Botón export en `Audit.cshtml` view con CSS class `syn-admin__filter-export`
que reusa estilo del FormSubmissions export.

## Consequences

**Positivas:**

- **Compliance-ready**: moderator/admin puede exportar audit window
  específico para legal review sin abrir el filesystem.
- **Memoria O(1)**: streaming via `Response.Body.WriteAsync` permite
  exports de 5000 rows sin OOM. Mismo pattern de Forms CSV (ADR 0056).
- **Reuse helpers**: `EscapeCsvField` con `SearchValues<char>` de
  ADR 0059 sirve para ambos exports.
- **Self-audit**: cada export deja trazo en el audit trail (recursive).

**Negativas:**

- **CsvExportHardCap = 5000** events: para windows muy amplios el
  caller debe segmentar por sub-rangos. Mitigación: el operador puede
  hacer múltiples exports manualmente.
- **Read O(N×F)** donde N=files, F=lines/file. Si crece > 100MB de
  audit, consideración de migrar a DB-backed.

**Neutras:**

- 1 commit feat batch (Olas 163+164) + 1 docs ADR.
- 0 GUIDs nuevos.
- 0 NuGet packages nuevos.

## Implementation summary

| # | Foco |
|---|---|
| 163 | `IAuditTrailWriter.GetByDateRange` + `FileSystemAuditTrailWriter` impl con pre-filter de filename + post-filter de timestamp + view date range inputs. |
| 164 | `AdminController.AuditExportCsv` streaming + 8-column CSV + recursive `audit.export` event + view export button. |
| 0071 | (este) ADR consolidado |

## Próximas direcciones

- **Audit search por Id**: drill-down a evento específico `/admin/audit/{id}`.
- **Audit dashboard panel**: stats summary (events/day, top actors,
  top actions) en el `/admin` landing.

## References

- ADR 0056 — AdminSettings + CSV streaming (Forms export pattern reusado).
- ADR 0059 — `SearchValues<char>` cached para EscapeCsvField (helper reusado).
- ADR 0067 — IAuditTrailWriter base (extended).
