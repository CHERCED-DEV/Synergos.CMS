# ADR 0056 — AdminSettings + CSV streaming + Soft-delete undo + Native dialog delete (Olas 124-126)

- **Status:** Accepted
- **Date:** 2026-04-26
- **Deciders:** Arquitecto + agente, durante batch tras Ola 123 — *"acotemos hasta la 135 + scopes más amplios"*.
- **Consolida:** 3 olas en un único ADR + nueva approach scope-amplio.

## Context

Tras Olas 122-123 (date range + cache + delete + spam) quedaban 4
deferred items del ADR 0055 + necesidad de runtime configurability:

1. **CSV streaming** para datasets > 5000 (memoria O(N) actual del
   StringBuilder).
2. **Soft-delete + undo window** para bulk-reject (30s).
3. **Native `<dialog>` confirm** en delete submission (reemplaza el
   `confirm()` inline).
4. **Pending counter cache TTL configurable** via settings.

## Decision

Ejecutar 3 olas paralelas + agregar `AdminSettings` POCO como sigle
source of tuning del dashboard.

### Ola 124 — AdminSettings + native dialog + TTL configurable

**Nuevo `AdminSettings`** (`Synergos:Admin` section) con 4 keys:
- `PendingCountCacheTtl` (default 30s) — TTL del badge cache
- `DefaultPageSize` (default 25) — listings page size default
- `CsvExportHardCap` (default 5000) — export hard limit
- `BulkUndoWindow` (default 30s) — undo window para bulk-reject

Wireado en `OptionsComposer.Configure<AdminSettings>` → DI via
`IOptionsMonitor`. AdminController inyecta y expone properties
shorthand (`DefaultPageSize`, `PendingCountCacheTtl`, etc.).

**Method signatures** con `pageSize = 0` (no se puede usar instance
property como default value de parameter); fallback `if (pageSize <= 0) pageSize = DefaultPageSize` adentro. Mismo pattern que el resto.

**Native `<dialog>` en `FormSubmissionDetail.cshtml`** reemplaza
`window.confirm()` inline. UX consistente con bulk confirm de Ola
116 — title + body con storageId destacado + warning irreversible +
2 actions Cancelar (ghost) / Eliminar (reject). Vanilla JS handler
captura click, `showModal()`, `close` event submita el form si
`returnValue === 'confirm'`.

### Ola 125 — CSV streaming

**Refactor de `ExportFormSubmissions`** de StringBuilder→ByteArray
(memoria O(N) del payload) a streaming via
`Response.Body.WriteAsync` (memoria O(1)).

Pasos:
1. BOM UTF-8 prepended.
2. Header line con union de columnas pre-pass.
3. Por cada item: `GetSubmission` + format row + WriteAsync.
4. `cancellationToken.ThrowIfCancellationRequested()` per row para
   abortar en client disconnect.
5. `Response.Body.FlushAsync` final.
6. Returns `EmptyResult()` (response ya escribió bytes).

`Hard cap` ahora viene de `CsvExportHardCap` setting (default 5000,
configurable up to lo que el sysadmin tolere).

### Ola 126 — Soft-delete + undo bulk-reject

**`ICommentRepository` extends** con 2 nuevos métodos:
- `ReadByRefs(refs)` — snapshot completo de los items por
  `(nodeId, commentId)` pairs. Read-only, no muta nada.
- `RestoreAsync(items, ct)` — re-agrega items al store. Idempotente
  (skip si ya existe el Id en el nodo). Usado para undo.

**`FileSystemCommentRepository`** impl ambas con `groupBy(NodeId)` +
`LoadAll/PersistAsync` delta — eficiente: 1 read + 1 write per
nodo afectado.

**`BulkRejectComments` extends**:
1. `ReadByRefs` → snapshot ANTES de delete.
2. `BulkRejectAsync` → physical delete.
3. Si `changed > 0 && snapshot.Count > 0`:
   - Genera token random 12 chars (`Guid.N[..12]`).
   - `_cache.Set("admin.bulk-undo:{token}", snapshot, BulkUndoWindow)`.
   - Redirect con `msg="rejected-N-undo:{token}"`.
4. Analytics event extends con `undoToken`.

**Nuevo `BulkUndoReject(token)` action**:
- Lee snapshot del cache por token.
- Si no encuentra → `msg="undo"` → flash "ventana expirió".
- `RestoreAsync(snapshot)` → cache.Remove(token) + cache.Remove(pendingCount).
- Analytics event `comment.moderation.bulk-undo-rejected`.
- Redirect con `msg="undone-N"`.

**`ModerationComments.cshtml` flash extends**:
- Parsea `messageCode` con format `"verb-count"` o `"rejected-N-undo:T"`.
- Render botón "↶ Deshacer" inline form que POST a undo endpoint.
- Cuando `verb=undone` → "↶ N comentario(s) restaurado(s) a la cola."
- Cuando `verb=undo` → "⏱ La ventana de undo expiró."

CSS: `.syn-admin__flash` flex con `margin-inline-start: auto`
empujando button al borde + `.syn-admin__flash-button` action
primary inline.

## Consequences

**Positivas:**

- **Runtime configurability sin recompile**: sysadmin ajusta TTLs +
  page sizes + caps en `appsettings.json`.
- **CSV memory-bounded**: export funciona con datasets de 100k+
  filas sin OOM. Browser empieza a recibir bytes inmediatamente
  (Time-to-First-Byte mejorado).
- **Undo bulk-reject reduce risk de errores destructivos**: el
  moderator que rechazó 100 comentarios por error tiene 30s para
  restaurarlos sin acceso al filesystem.
- **Native dialog consistente**: misma UX que bulk confirm.
  Accessible (focus trap, ESC para cancelar).
- **AdminSettings extensible**: futuras tuning options (badge color,
  default theme, etc.) caben en la misma POCO.
- **Cero schema rompedor**.
- **Cero NuGet packages nuevos**.

**Negativas:**

- **Snapshot en memory cache**: si el proceso reinicia entre
  bulk-reject y undo, se pierde la ventana. Aceptable — la ventana
  es 30s, restart en ese intervalo es raro. Para garantía cross-
  restart, swap por DB-backed shadow (deferred).
- **`ReadByRefs` + `BulkRejectAsync` son 2 file reads consecutivos**:
  el repo carga cada nodo 2 veces (snapshot + delete). Para
  optimizar, podría haber un `BulkRejectWithSnapshotAsync` que haga
  ambos en 1 pasada. Diferido — el cost es marginal.
- **Token 12 chars Guid.N**: ~ 12^16 entropy = suficientemente
  random para que un atacante con conocimiento del cache key prefix
  no pueda enumerar otros tokens, pero NO criptográficamente
  fuerte. OK para flash redirect.
- **Native `<dialog>` requiere browser moderno**: Chrome 37+,
  Firefox 98+, Safari 15.4+, Edge. IE11 muere — aceptable para
  admin tooling.
- **CSV streaming sin Content-Length**: el browser no puede mostrar
  progress bar de descarga. Aceptable — los CSVs típicos < 10MB.

**Neutras:**

- 1 commit feat batch + 1 docs ADR consolidado.
- 0 GUIDs nuevos.
- 0 dependency changes.

## Implementation summary

| # | Hash | Foco |
|---|---|---|
| 124-126 | `d9025d5` | AdminSettings POCO + 4 keys configurables + native dialog delete + CSV streaming + ICommentRepository.ReadByRefs/RestoreAsync + BulkUndoReject action + flash msg extends + .syn-admin__flash-button CSS |
| 0056 | (este) | ADR consolidado scope-amplio |

## Próximas direcciones

- **Adaptive Cards adapter** para Teams (próxima Ola 128) que
  reemplace MessageCard format.
- **Polly per-channel config** (próxima Ola 129) — extraer max
  retries / timeout / circuit breaker a `AdminSettings.Webhooks.{channel}`.
- **DB-backed shadow para snapshots cross-restart** (eventual).

## References

- ADR 0036 — Output caching via IMemoryCache (mismo cache provider)
- ADR 0048 — CSS design system aligned with Synergos.UI
- ADR 0051 — Admin moderation dashboard SSR
- ADR 0053 — Native HTML5 <dialog> bulk confirm (mismo pattern aquí)
- ADR 0054 — Form detail + Topbar partial + CSV export (refactor
  streaming aquí)
- ADR 0055 — Date range + Pending cache + Delete + Spam (deferred
  items cerrados aquí)
