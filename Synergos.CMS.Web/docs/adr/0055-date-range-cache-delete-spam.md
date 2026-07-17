# ADR 0055 — Date range filter + Pending cache + Delete + Mark-as-spam (Olas 122-123)

- **Status:** Accepted
- **Date:** 2026-04-26
- **Deciders:** Arquitecto + agente, durante batch tras Ola 120 — *"continua"*.
- **Consolida:** 2 olas en un único ADR.

## Context

Tras Olas 118-120 (form detail + topbar partial + CSV export) quedaron
4 deferred items concretos del ADR 0054:

1. **Filter por fecha en export** — listing y CSV solo aceptaban
   `formKey`, no podían restringir por ventana temporal.
2. **Pending badge cache** — cada GET en el admin hacía un
   `GetPendingPage(1, 1)` extra para el topbar = filesystem
   enumeration por hit.
3. **Detail actions: delete submission** — el detail era read-only, no
   había forma de purgar una submission corrupta/obsoleta sin SSH.
4. **Mark-as-spam** — el moderator solo podía aprobar o rechazar; no
   había way de tagear "esto es spam" para futuro training de filtro
   (Akismet, custom ML).

## Decision

### Ola 122 — Date range + pending cache (1 commit `03aba0a` + fix `ff4dc8a`)

**`IFormSubmissionReader.GetRecent` extends** con `fromUtc?` + `toUtc?`
opcionales (ambos inclusive). Default null = current behavior. Filter
inline durante enumeration.

**`AdminController.FormSubmissions` y `ExportFormSubmissions`** aceptan
`?from=&to=` query params, los pasan al reader.

**`FormSubmissions.cshtml`** agrega 2 inputs date "Desde"/"Hasta" en
filter bar. Helper Razor `AppendFilters(href)` agrega los 3 query
params activos a cualquier href (page, export, clear-link). `HasFilters`
helper para mostrar/ocultar clear link.

**`AdminController` inyecta `IMemoryCache`** (ya registrado por Ola 66
`AddMemoryCache()`).

**`SetTopbar` cache-aware**:
- Si caller pasa `pendingCountOverride` con valor fresh → usa direct y
  REFRESCA cache (`_cache.Set`).
- Si no → `_cache.GetOrCreate` con TTL 30s (filesystem enumeration solo
  cada 30s en lugar de cada page hit).

**Cache invalidation** en las 4 actions destructivas (single + bulk
approve/reject) via `_cache.Remove(PendingCountCacheKey)` antes del
redirect. Garantiza que el badge muestre count fresh inmediatamente
post-acción.

### Ola 123 — Delete submission + Mark-as-spam (1 commit `a55c6d9`)

**`IFormSubmissionReader.DeleteAsync(formKey, storageId, ct)`** →
`Task<bool>`. Idempotente — false si no existía. Para adapters
read-only sin delete capability, retornar false sin excepción.

**`FileSystemFormSubmissionHandler.DeleteAsync`**: sanitize +
resolve path + `File.Delete` con try-catch defensivo. Logea
Information al success, Error al fail.

**`AdminController.DeleteFormSubmission`** action en
`POST /admin/forms/{formKey}/{storageId}/delete`. Auth gate +
analytics event `form.submission.deleted` + redirect a list.

**`FormSubmissionDetail.cshtml`** agrega sección "Zona destructiva"
con `panel--danger` variant + form delete + `onsubmit="return confirm(...)"`
inline JS para confirm dialog (vanilla, sin native `<dialog>` aquí
porque es 1 acción simple).

**`AdminController.MarkCommentAsSpam`** action en
`POST /admin/moderation/comments/{nodeId}/{commentId}/spam`.
Backend = `RejectAsync` (mismo delete) pero analytics event distinto:
`comment.moderation.spam-reported`. La diferencia es semántica —
reject = "no me gustó", spam = "esto es spam confirmado". Futuro
training de filtro Akismet/ML/etc. consume eventos `spam-reported`,
no `rejected`.

**`ModerationComments.cshtml`**: 3er botón "🚫 Spam" inline + flash
message extends con verb "spam". `--spam` action variant
state-warning surface (amarillo).

## Consequences

**Positivas:**

- **Date range filter desbloquea reporting**: editorial puede
  exportar "noviembre solamente" o "última semana" sin filtros
  manuales en Excel.
- **Cache reduce filesystem load**: con TTL 30s, 100 page hits/min
  en el admin = 2 enumerations/min (vs 100). Significativo para
  sites con > 1000 pending comments.
- **Cache invalidation correcta**: post-acción el badge refleja count
  fresh, sin lag de 30s en feedback al moderator.
- **Delete cierra el loop editorial**: moderator puede purgar
  submissions corruptas/spam/obsoletas sin SSH ni tooling externo.
- **Mark-as-spam diferencia semántica**: separar reject vs spam
  permite al equipo entrenar un futuro classifier. Sin perder
  capacidad de "rechazar sin marcar spam" (e.g., off-topic legítimo).
- **Zone destructiva visualmente diferenciada**: `panel--danger`
  variant comunica "esto es serio" antes del click.
- **Cero schema rompedor**.
- **Cero NuGet packages nuevos**.

**Negativas:**

- **Confirm vanilla `confirm()` para delete submission**: feo pero
  funcional. Para upgrade visual, swap por native `<dialog>` (igual
  que bulk actions Ola 116). Diferido — la acción es 1 click, low
  frequency.
- **DeleteAsync en `IFormSubmissionReader`**: filosóficamente "reader"
  con un método destructivo es contradictorio. Alternativas
  consideradas: split en `IFormSubmissionAdmin : IFormSubmissionReader`
  o renombrar reader a "manager". Se decidió mantener para evitar
  cambio de naming en código existente — el doc-comment lo aclara.
- **Cache TTL fijo 30s**: no configurable en settings. Si el sitio
  necesita más fresco (5s) o más lazy (5min), hace falta tocar
  código. Mitigación futura: extraer a `AdminSettings.PendingCountCacheTtl`.
- **Mark-as-spam aún no entrena nada**: el evento se emite pero
  ningún consumer lo procesa. Útil para audit trail desde día 1;
  el filtro real (Akismet adapter) viene cuando volumen lo justifique.
- **Date range UTC vs local**: el date picker usa local time del
  browser; el storage es UTC. Para sitios con visitantes globales,
  el "noviembre" del moderator puede no coincidir con el "noviembre"
  del visitante japonés. Aceptable — moderator opera en su huso
  horario local.

**Neutras:**

- 3 commits feat (122 + 122.b fix + 123) + 1 docs ADR consolidado.
- 0 GUIDs nuevos.
- 0 dependency changes.
- ~8 archivos modificados/creados totales.

## Implementation summary

| # | Hash | Foco |
|---|---|---|
| 122 | `03aba0a` | IFormSubmissionReader.GetRecent extends fromUtc/toUtc + AdminController accepts query params + view date inputs + AppendFilters helper + IMemoryCache wrap en SetTopbar con TTL 30s |
| 122.b | `ff4dc8a` | Fix cache invalidation faltante en reject + bulk approve/reject |
| 123 | `a55c6d9` | IFormSubmissionReader.DeleteAsync + FileSystem impl + DeleteFormSubmission action + MarkCommentAsSpam variant del reject + 3er botón Spam en moderation list + Zone destructiva en detail view + .syn-admin__action--spam + .syn-admin__panel--danger CSS |
| 0055 | (este) | ADR consolidado |

## Próximas direcciones

- **CSV streaming** para datasets > 5000.
- **Soft-delete + undo window** para bulk-reject (sigue deferred).
- **Native `<dialog>` confirm** en delete submission detail.
- **Pending counter cache TTL configurable** via AdminSettings.
- **Akismet adapter** consumiendo `comment.moderation.spam-reported`
  events para entrenar el filtro.
- **DB-backed reader/repo** para volumen grande.
- **Adaptive Cards** para Teams (cuando MS deprecate MessageCard).

## References

- ADR 0030 — Forms internal submission runtime
- ADR 0036 — Output caching via IMemoryCache (mismo cache provider)
- ADR 0048 — CSS design system aligned with Synergos.UI
- ADR 0051 — Admin moderation dashboard SSR
- ADR 0053 — Admin landing + Form submissions dashboard + Bulk
  confirm dialog
- ADR 0054 — Form submission detail + Topbar partial + CSV export
  (deferred items cerrados aquí)
