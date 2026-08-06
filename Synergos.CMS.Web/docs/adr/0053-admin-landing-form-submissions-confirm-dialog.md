# ADR 0053 — Admin landing + Form submissions dashboard + Bulk confirm dialog (Olas 115-116)

- **Status:** Accepted
- **Date:** 2026-04-26
- **Deciders:** Arquitecto + agente, durante batch tras Ola 113 — *"sigue con todo"*.
- **Consolida:** 2 olas en un único ADR.

## Context

Tras Olas 109-113 (search analytics + paginación moderation + Slack/
Discord/Teams + Polly retry) quedaban 2 deferred items concretos del
ADR 0052:

1. **Form submissions dashboard** en `/admin/forms` — los submissions
   se persisten a `App_Data/syn-form-submissions/{formKey}/*.json`
   pero nadie podía verlos sin abrir el filesystem.
2. **Confirm dialog para bulk actions** — bulk-reject de N
   comentarios era 1 click destructivo sin confirmación, riesgo de
   pérdida accidental.

## Decision

Ejecutar 2 olas en secuencia + landing page nueva como complemento
natural del nav.

### Ola 115 — Form submissions dashboard + Admin landing (1 commit `6fd4caa`)

**Nueva interfaz read-only** `IFormSubmissionReader` separada de
`IFormSubmissionHandler` porque el write path (despacho a queue/
webhook/storage) y el read path (admin inspection) tienen requisitos
distintos. Adapters fire-and-forget no la implementan; el dashboard
muestra empty si no hay reader registrado.

```csharp
FormSubmissionsPage GetRecent(int page, int pageSize, string? formKeyFilter = null);
IReadOnlyList<string> ListFormKeys();
```

**Records nuevos**: `FormSubmissionListItem` (FormKey, ReceivedAtUtc,
ClientIp, FieldCount, StorageReference) + `FormSubmissionsPage`
(Items, Page, PageSize, TotalCount, derived TotalPages/HasNext/
HasPrev — paralelo de `PendingCommentsPage`).

**`FileSystemFormSubmissionHandler` implementa AMBAS interfaces**.
`GetRecent` enumera `{storage}/{formKey}/*.json`, lee con
`JsonDocument` para extraer summary (no full content — ligero
para listing).

**SeamComposer registra una sola instance bajo los 2 contratos**
via factory delegates (`AddSingleton<FileSystemFormSubmissionHandler>()`
+ 2 factory registrations).

**`AdminController` extends**:

- `GET /admin` → Index landing con summary cards (pending count
  + formKeys count + top queries 7d).
- `GET /admin/forms?page&pageSize&formKey` → list paginated +
  filterable.

**Views**:
- `Index.cshtml` con 3 summary cards (gradient brand→accent text
  numbers + emoji icons + label + hint preview) + mini-table
  top queries 7d.
- `FormSubmissions.cshtml` con dropdown formKey filter populated
  from `ListFormKeys()` + table (Form/Recibido/IP/Campos) +
  pagination prev/next.

**Topbar nav update** en las 4 views: Inicio / Moderación / Forms /
Búsqueda con current state highlight.

**`syn-admin.css` extends**: `.syn-admin__cards` grid auto-fit +
`.syn-admin__card` hover lift + gradient text + select styled.

### Ola 116 — Confirm dialog bulk actions (1 commit `7ad5bdf`)

**Native HTML5 `<dialog>`** (sin polyfill — soportado en Chrome 37+,
Firefox 98+, Safari 15.4+, Edge). Vanilla JS minimal intercepta
bulk clicks, muestra count + descripción específica per acción, y
submita el form solo si moderator confirma.

**Cambios**:

- Bulk approve/reject buttons: `type="submit"` → `type="button"`
  con `data-bulk-action="approve|reject"`. El form NO submita
  directo — JS captura el click.
- Nuevo `<dialog id="bulk-confirm">` al final del body con title +
  body dinámicos + 2 actions (Cancelar ghost + Continuar con
  variant approve/reject).
- Script extends:
  - Cuenta checked targets (alert si 0).
  - Setea `title`/`body`/`className` del dialog según action.
  - Listen `close` event: si `returnValue=="confirm"`, apunta el
    bulk-form al endpoint correcto (`/bulk-approve` o
    `/bulk-reject`) y submit.
- Mensaje específico per acción: reject advierte irreversible,
  approve dice "items quedarán visibles públicamente".

**`syn-admin.css` extends**: `.syn-admin__dialog` con backdrop blur
+ shadow floating + form/title/body/actions layout +
`.syn-admin__action--ghost` variant transparent con border default.

## Consequences

**Positivas:**

- **Operacional completo**: el moderator/admin tiene UI usable para
  los 4 dominios principales (moderation, forms, search analytics,
  landing summary). Sin terminal, sin filesystem.
- **Form submissions visible**: equipo editorial puede revisar
  submissions persistidas con filter por formKey, sin abrir el
  filesystem ni decoder JSON manual.
- **IFormSubmissionReader separada**: adapters fire-and-forget
  (queue/webhook/email) no fuerzan implementar listing. La seam
  es opcional y el composer la wirea bajo el FileSystem handler
  por defecto.
- **Bulk safety net**: el confirm dialog reduce risk de errors
  destructivos. Reject especifica "irreversible" en el body.
- **Native `<dialog>`**: sin dep externa, accesible (focus trap +
  ESC para cancelar built-in), backdrop-blur premium feel.
- **Consistencia de nav**: 4 entries en topbar siempre, current
  state visualmente clear, fácil saltar entre áreas.
- **Cero schema rompedor**.
- **Cero NuGet packages nuevos**.

**Negativas:**

- **JsonDocument re-parse per item**: para listings de 100+
  submissions, abrir + parse cada JSON file es lento. Mitigación
  futura: cachear summary en un archivo manifest o swap por
  DB-backed reader.
- **`alert()` para "selecciona algo"**: el handler usa `alert()`
  cuando no hay items checked. Funcional pero feo. Mejorar con
  inline error message diferido.
- **Dialog no es polyfilled**: navegadores muy viejos (IE11,
  Safari < 15.4) no muestran el dialog. Aceptable — moderators
  probablemente usan browser moderno.
- **Sin "deshacer" tras bulk**: una vez confirmado, no hay way
  de revertir bulk-reject. Mitigación futura: soft-delete con
  ventana 30s + undo button.
- **No hay link a "abrir submission detail"**: el listing muestra
  metadata pero no permite ver fields completos. Para drill-down
  hace falta una nueva view `/admin/forms/{storageRef}` que sirva
  el JSON. Diferido.
- **Sin counter en topbar**: el badge de pending comments en el
  landing no aparece en el topbar de las otras pages. Para
  awareness persistente, agregar a `_AdminHead` o partial
  `_AdminTopbar`. Diferido.

**Neutras:**

- 2 commits feat + 1 docs ADR consolidado.
- 0 GUIDs nuevos.
- 0 dependency changes.
- ~10 archivos nuevos/modificados totales.

## Implementation summary

| # | Hash | Foco |
|---|---|---|
| 115 | `6fd4caa` | IFormSubmissionReader seam + FileSystemFormSubmissionHandler implementa ambas + AdminController.Index + AdminController.FormSubmissions + 2 views + topbar nav 4 entries + cards grid CSS |
| 116 | `7ad5bdf` | <dialog> native confirm modal + JS handler + ghost button variant + CSS dialog backdrop |
| 0053 | (este) | ADR consolidado |

## Próximas direcciones

- **Drill-down view** `/admin/forms/{storageRef}` con full fields.
- **Pending counter en topbar** (partial `_AdminTopbar` o widget).
- **Soft-delete + undo** para bulk-reject (30s window).
- **Inline validation msg** vs `alert()` para "selecciona algo".
- **Bulk export** de submissions a CSV.
- **DB-backed reader/repo** cuando volumen excede in-memory enumerar.
- **Adaptive Cards** para Teams (replace MessageCard cuando MS deprecate).

## References

- ADR 0030 — Forms internal submission runtime
- ADR 0038 — Comments runtime end-to-end
- ADR 0048 — CSS design system aligned with Synergos.UI
- ADR 0051 — Admin moderation dashboard SSR (paralelo del nuevo
  Forms dashboard)
- ADR 0052 — Admin extensions + Discord/Teams + Polly (deferred
  items cerrados aquí)
