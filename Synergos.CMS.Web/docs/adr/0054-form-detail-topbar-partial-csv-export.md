# ADR 0054 — Form submission detail + Topbar partial + CSV export (Olas 118-120)

- **Status:** Accepted
- **Date:** 2026-04-26
- **Deciders:** Arquitecto + agente, durante batch tras Ola 116 — *"continua"*.
- **Consolida:** 3 olas en un único ADR.

## Context

Tras Olas 115-116 (form submissions dashboard + bulk confirm dialog)
quedaron 3 deferred items concretos del ADR 0053:

1. **Drill-down view** `/admin/forms/{formKey}/{storageId}` — el
   listing mostraba metadata pero no permitía ver fields completos.
2. **Pending counter en topbar** — awareness persistente del backlog
   de moderation cross-page.
3. **Bulk export submissions a CSV** — para que editorial pueda
   compartir/importar a CRM/Excel.

## Decision

Ejecutar 3 olas en secuencia.

### Ola 118 — Form submission detail (1 commit `d6340e8`)

`IFormSubmissionReader` extends:
- `GetSubmission(formKey, storageId)` → `FormSubmissionDetail?`
- `FormSubmissionDetail` record (FormKey/StorageId/ReceivedAtUtc/
  ClientIp/UserAgent/Referrer/Fields).
- `FormSubmissionListItem` extends con `StorageId` (URL-safe id —
  filename sin extensión en FileSystem). `StorageReference` sigue
  como path absoluto interno solo para logs/debugging.

`FileSystemFormSubmissionHandler.GetSubmission` sanitize input +
resolve path + JsonDocument parse + extract fields como
`Dictionary<string,string>`. Try-catch defensivo.

`AdminController.FormSubmissionDetail` action en
`GET /admin/forms/{formKey}/{storageId}`. Returns `NotFound()` si
reader devuelve null.

**View `FormSubmissionDetail.cshtml`** con:
- Breadcrumbs Forms / {formKey} / Submission.
- 2 panels: Metadata (formKey/id/recibido/ip/UA/referrer) + Campos
  (table key/value con `<pre>` para preservar whitespace).

CSS extends: `.syn-admin__breadcrumbs` + `.syn-admin__row-action` +
`.syn-admin__field-value` (whitespace-pre-wrap).

`FormSubmissions.cshtml` agrega columna "Ver detalle →".

### Ola 119 — Topbar partial + pending badge (1 commit `26750ca`)

**Nuevo partial `Views/Admin/_AdminTopbar.cshtml`** que cada view
incluye con `@await Html.PartialAsync("_AdminTopbar")`. Lee 3
ViewData keys: `AdminCurrentSection` (slug "home"/"moderation"/
"forms"/"search"), `ModeratorName`, `AdminPendingCount`.

Helper local `CurrentClass(slug)` marca el link activo. Badge red
aparece next to "Moderación" cuando `pendingCount > 0`; switch a
brand-tone cuando ese link es current.

**`AdminController.SetTopbar(sectionSlug, pendingCountOverride?)`**
helper privado setea las 3 ViewData keys. El override permite a
`Index` y `ModerationComments` reusar el count que ya computaron
del page total (evita doble query). Otras actions hacen
`GetPendingPage(1, 1)` — read barato.

**5 views** reemplazan ~17 líneas de topbar HTML cada una con 1
línea del partial — **reducción ~75 líneas net** de duplicación.

CSS: `.syn-admin__nav-link` agrega gap entre text + badge;
`.syn-admin__nav-badge` red-tone pill con tabular-nums; `--current`
override switch a brand-tone.

### Ola 120 — CSV export (1 commit `dc9d9f4`)

`AdminController.ExportFormSubmissions` en
`GET /admin/forms/export?formKey=X&limit=N`. Default 500, hard cap
5000.

Implementación:
1. `_formReader.GetRecent(1, clamped, formKeyFilter)` recolecta
   listing.
2. Por cada item, `_formReader.GetSubmission(formKey, storageId)`
   recupera fields completos.
3. **Union de columnas**: `SortedSet<string>` con keys de fields
   across todas las submissions (cada una puede tener fields
   distintos — el CSV los unifica con valores vacíos donde falte).
4. **Header CSV**: meta cols (`formKey`, `storageId`,
   `receivedAtUtc`, `clientIp`, `userAgent`, `referrer`) + field
   cols sorted.
5. **Helper `EscapeCsvField`**: wraps en comillas dobles + duplica
   internas si contiene comma/quote/newline.
6. **BOM UTF-8** prepended para que Excel no malinterprete encoding.
7. Filename con timestamp + formKey filter (si hay) para
   identificación clara en disco.
8. Returns `File(content, "text/csv; charset=utf-8", fileName)`.

`FormSubmissions.cshtml`: botón "⬇ Exportar CSV" en filter bar.
Hereda formKey filter activo — exporta solo el scope visible.

CSS: `.syn-admin__filter-export` con `margin-inline-start: auto`
empuja el botón al borde derecho.

## Consequences

**Positivas:**

- **Drill-down completa el flow editorial**: equipo editorial puede
  revisar/auditar submissions individuales sin abrir filesystem.
  Útil para investigar reportes de spam, validar entries, o copiar
  contenido a CRM.
- **Awareness persistente del backlog**: el badge en topbar mantiene
  visibilidad del moderation queue cross-page. Reduce risk de
  comentarios olvidados.
- **CSV export unblocks workflows**: editorial puede compartir
  submissions con stakeholders no-técnicos sin acceso al admin
  (envío por email, importar a Sheets/Excel, feed de un CRM).
- **Topbar duplication eliminada**: ~75 líneas net removidas. Cambios
  futuros al topbar tocan 1 archivo, no 5.
- **URL-safe storage ID**: el `StorageId` (filename sin extensión)
  es safe para route params. Path absoluto interno (`StorageReference`)
  queda solo para logs/debugging — nunca expuesto al UI.
- **Cero schema rompedor**.
- **Cero NuGet packages nuevos**.

**Negativas:**

- **CSV export lee N+1 archivos**: cada item del listing requiere
  un segundo read del JSON file completo. Para 5000 items, son
  10000 file opens. Mitigación futura: extender
  `IFormSubmissionReader.GetRecentDetailed(...)` que combine listing
  + fields en una sola pasada del filesystem.
- **CSV no streaming**: build el StringBuilder en memoria. Para
  100k submissions hace OOM. Mitigación: stream con
  `ChunkedFile` response (futuro).
- **CSV con union de columnas**: si Form A tiene campo `email` y
  Form B tiene campo `Email` (case difference), aparecen como 2
  columnas porque comparator es `OrdinalIgnoreCase` en SortedSet
  pero capitalization se preserva del primer encontrado. OK por
  ahora — mejorable normalizando a lowercase.
- **Pending counter requiere read extra**: cada GET action que NO es
  Index/Moderation hace `GetPendingPage(1,1)` para el badge.
  Filesystem repo enumera archivos cada vez. Para sites con > 10k
  pending, agregar caching (memo 30s) o swap por DB-backed.
- **Detail view sin actions**: solo lectura. No hay "delete this
  submission" o "mark as spam" — diferido. Para ahora, el moderator
  borra manualmente del filesystem si necesita.
- **Sin filter por fecha en export**: limit-based solo. Para "todas
  las submissions de noviembre" hace falta `?from=&to=` params —
  diferido.

**Neutras:**

- 3 commits feat + 1 docs ADR consolidado.
- 0 GUIDs nuevos.
- 0 dependency changes.
- ~10 archivos modificados/creados totales.

## Implementation summary

| # | Hash | Foco |
|---|---|---|
| 118 | `d6340e8` | IFormSubmissionReader extends + FileSystem GetSubmission + AdminController.FormSubmissionDetail + FormSubmissionDetail.cshtml + breadcrumbs + row-action + field-value CSS |
| 119 | `26750ca` | _AdminTopbar partial + AdminController.SetTopbar helper + 5 views refactored a usar partial + nav-badge CSS |
| 120 | `dc9d9f4` | AdminController.ExportFormSubmissions con CSV writer inline + BOM UTF-8 + EscapeCsvField helper + boton en filter bar + filter-export CSS |
| 0054 | (este) | ADR consolidado |

## Próximas direcciones

- **CSV streaming** para datasets grandes (100k+).
- **Filter por fecha** en export (?from=&to=).
- **Detail actions**: delete submission + mark as spam.
- **Pending badge cache**: memo 30s para reducir filesystem reads.
- **Soft-delete + undo** para bulk-reject (deferred desde ADR 0053).
- **DB-backed reader** cuando volumen excede in-memory.
- **Adaptive Cards** para Teams (cuando MS deprecate MessageCard).

## References

- ADR 0030 — Forms internal submission runtime
- ADR 0048 — CSS design system aligned with Synergos.UI (consume el
  mismo design system)
- ADR 0051 — Admin moderation dashboard SSR
- ADR 0052 — Admin extensions + Discord/Teams + Polly
- ADR 0053 — Admin landing + Form submissions dashboard + Bulk
  confirm dialog (deferred items cerrados aquí)
