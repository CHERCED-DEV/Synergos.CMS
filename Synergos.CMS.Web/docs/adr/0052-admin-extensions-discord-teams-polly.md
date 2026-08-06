# ADR 0052 — Admin extensions + Discord/Teams notifiers + HTTP resilience (Olas 109-113)

- **Status:** Accepted
- **Date:** 2026-04-26
- **Deciders:** Arquitecto + agente, durante batch tras Ola 107 — *"con todos"*.
- **Consolida:** 5 olas en un único ADR.

## Context

Tras Ola 107 (`AdminController` + moderation dashboard SSR) quedaron 6
deferred items concretos en ADRs 0049/0050/0051:

1. **Search analytics dashboard** en `/admin/analytics/search`.
2. **Pagination + filtros** en moderation dashboard.
3. **Bulk actions** (select-all + approve-many / reject-many).
4. **Discord adapter** clonando el pattern Slack.
5. **Teams adapter** clonando el pattern Slack.
6. **Polly retry** en los named HttpClients (requería NuGet + ADR).

## Decision

Ejecutar 5 olas en secuencia.

### Ola 109 — Search analytics dashboard (1 commit `ed4cbac`)

`AdminController` extends con action `GET /admin/analytics/search?from&to&limit`
gated por mismos roles. Default ventana últimos 30 días, limit 20
(clamp 1-100). View `AnalyticsSearch.cshtml` con:

- Filters bar (3 inputs date/date/limit + submit).
- 2-col split: panel **"Top queries"** (Query / Veces / Hits últimos /
  Última vez) + panel **"Sin resultados"** con hint editorial + tabla.

`syn-admin.css` extends con `.syn-admin__filters`, `.syn-admin__split`
(grid 2-col responsive), `.syn-admin__panel`, `.syn-admin__table`
(thead sticky + hover row + tabular-nums col), `.syn-admin__panel-count`
default brand + warning variant.

Topbar nav agrega "Búsqueda" como entry secundario.

### Ola 110 — Moderation pagination + filter + bulk (1 commit `409b599`)

`ICommentRepository` extends:
- `PendingCommentsPage` record (Items, Page, PageSize, TotalCount,
  derived TotalPages/HasNext/HasPrev).
- `CommentRef` record (NodeId, CommentId).
- `GetPendingPage(page, pageSize, nodeIdFilter?)`.
- `BulkApproveAsync(refs, ct)` → count.
- `BulkRejectAsync(refs, ct)` → count.

`FileSystemCommentRepository` impl: helper privado `LoadAllPending(filter)`
reutilizado. Bulk methods agrupan por nodo (1 read + 1 write per
affected node, no per-comment).

`AdminController.ModerationComments` ahora acepta
`?page=N&pageSize=M&nodeId=X&msg=...`. Nuevos endpoints
`POST /bulk-approve` y `/bulk-reject` que aceptan `targets[]` con
shape `"{nodeId}|{commentId}"`. Helper `ParseTargets` defensivo.

View extends con:
- Filter form (nodeId + pageSize) + clear-link cuando filter activo.
- Flash message tras bulk con count.
- Sticky bulk toolbar (select-all + bulk approve/reject).
- Checkbox per item. Node link clickeable filtra a ese nodo.
- Pagination prev/next + current/total con disabled-state.
- Vanilla JS inline (sin deps) para wire select-all checkbox.

`syn-admin.css` extends con flash, bulk-toolbar sticky con
backdrop-blur, item checkbox styled, pagination buttons.

### Ola 111 — Discord notifier channels (1 commit `c20823f`)

3 nuevos `DiscordXxxNotifier` con payload Discord embeds:
- Color brand indigo `5_205_751` (`0x4f6ef7`) o warning amber
  `14_251_782` (`0xd97706`) según contexto.
- Title con emoji (💬 / 📩 / 🛒) + description + fields inline +
  timestamp + footer con siteName.
- Limits respetados (25 fields max, 1024 chars per value, 1500
  description for comment body, 240 chars per form field).

3 named HttpClients + 3 channels Singleton. POST inline con
`JsonSerializer.SerializeToUtf8Bytes` + `ByteArrayContent`. Sin auth
header (URL contiene el secret); sin HMAC (Discord no valida
inbound).

Settings `DiscordWebhookUrl` + `TeamsWebhookUrl` agregados a los 3
dominios (Teams populated en Ola 112).

### Ola 112 — Teams notifier channels (1 commit `66df6c7`)

3 nuevos `TeamsXxxNotifier` con payload formato Office 365 Connector
`MessageCard` (compatible con incoming webhooks `/webhookb2/...`).
Adaptive Cards diferido (shape muy distinto, otro adapter aparte).

Shape:
```json
{
  "@type": "MessageCard",
  "@context": "https://schema.org/extensions",
  "summary": "...",
  "themeColor": "4f6ef7",
  "title": "...",
  "sections": [{ "activityTitle", "activitySubtitle", "text", "facts": [...] }]
}
```

3 named HttpClients + 3 channels Singleton + composer wire.

### Ola 113 — Polly retry via Microsoft.Extensions.Http.Resilience (1 commit `c301a8e`)

**Nuevo NuGet** `Microsoft.Extensions.Http.Resilience` 8.10.0 (8.x
alineado con .NET 8, verificado en nuget.org). Justificación:

- Cierra "Sin retry/backoff" documentado como negativa en ADRs 0047/0049/0050.
- Es el package oficial de MS — alineado con la plataforma BCL,
  sin third-party drift.
- Defaults sensatos para webhooks (3 retries, exponential backoff,
  circuit breaker con min throughput 100).

Cada uno de los **12 named HttpClients** (3 webhook genérico + 3
Slack + 3 Discord + 3 Teams) ahora chain
`.AddStandardResilienceHandler()` después del `AddHttpClient(name)`.

**Standard policies aplicadas**:
- Retry: 3 attempts con exponential backoff + jitter (base 2s).
- Circuit breaker: opens > 10% failure ratio en sampling 30s,
  min throughput 100 (no opens en low-volume).
- Timeout: 10s per attempt, 30s total request.

Logging behavior: el `LogWarning` existente en cada notifier reporta
SOLO el intento final (post-retries) — comportamiento correcto.

Para tuning per-canal, swap `.AddStandardResilienceHandler()` por
`.AddResilienceHandler(name, builder => ...)` custom — diferido.

## Consequences

**Positivas:**

- **Dashboard SSR completo**: moderator hoy tiene UI usable para
  comments (con paginación + filtro + bulk) Y para search analytics.
  Editorial puede investigar "qué buscan que no encuentran" sin
  abrir terminal.
- **5 plataformas de notificación**: cada dominio (Comments / Forms /
  Cart) tiene 5 canales (email + webhook genérico + Slack + Discord +
  Teams). Cada uno opt-in independiente, todos disparan en paralelo
  cuando configurados.
- **Resiliencia HTTP automática**: webhooks sobreviven blips de
  network o receivers temporalmente caídos sin perder eventos.
  Circuit breaker protege contra cascading failures.
- **Notifier pattern uniforme y replicable**: agregar PagerDuty /
  Pushover / SMS / WhatsApp sigue siendo 1 archivo nuevo + 1 setting
  + 1 línea en composer.
- **Bulk actions reduce drudge**: limpiar 100 comentarios spam ahora
  es 1 click select-all + 1 click reject (vs 100 clicks previos).
- **Filter por nodo**: moderator puede focalizar en un blog post
  específico sin scroll por toda la cola.
- **Pagination**: cola con 1000+ pendientes ahora es navegable.
- **Cero schema rompedor**.

**Negativas:**

- **NuGet package nuevo**: `Microsoft.Extensions.Http.Resilience`
  agrega ~2MB transitive dependencies (Polly + System.Threading.RateLimiting).
  Aceptable — es package oficial de MS, no third-party.
- **Latencia extra en fallos**: si un webhook receptor está caído,
  la operación bloquea hasta 30s totales (Polly retries). Para
  contextos await (CommentsController.Submit), el visitante puede
  notar lag. Mitigación: el composite ya try-catch por canal y
  Webhook genérico es opt-in. Para fire-and-forget completo, swap
  por enqueue + worker (diferido).
- **Discord embed limits**: max 6000 chars total, max 25 fields per
  embed. Para form submissions con > 20 campos, truncamos a 20.
- **MessageCard deprecation roadmap**: MS empuja Adaptive Cards.
  MessageCard sigue funcionando pero podría ser deprecated en
  futuro. Migración a Adaptive Cards diferida — el wire entre
  notifier y payload está aislado, swap es 1 archivo.
- **Bulk actions sin confirm dialog**: rechazar 100 comments con
  un click es destructivo y sin confirmación. Mitigación futura:
  agregar `<dialog>` confirm modal o un undo window de 10s con
  soft-delete.
- **No CSRF protection en bulk endpoints**: igual que el resto del
  Admin (ADR 0051 documenta el trade-off).
- **Pagination in-memory**: `FileSystemCommentRepository.GetPendingPage`
  enumera TODOS los archivos JSON cada llamada. Para sites con
  > 10k pending, swap por DB-backed adapter.

**Neutras:**

- 5 commits feat + 1 docs ADR consolidado.
- 0 GUIDs nuevos.
- 1 NuGet package nuevo (justificado above).
- ~22 archivos modificados / creados totales.

## Implementation summary

| # | Hash | Foco |
|---|---|---|
| 109 | `ed4cbac` | AdminController.AnalyticsSearch + AnalyticsSearch.cshtml + syn-admin.css filters/panels/tables/split |
| 110 | `409b599` | ICommentRepository extends paged + bulk + CommentRef + PendingCommentsPage; FileSystemCommentRepository impl; AdminController paginated + bulk endpoints; ModerationComments view extends; syn-admin.css bulk/pagination/checkbox |
| 111 | `c20823f` | 3 DiscordXxxNotifier channels (Comments/Forms/Cart) con Discord embeds; DiscordWebhookUrl + TeamsWebhookUrl en 3 settings |
| 112 | `66df6c7` | 3 TeamsXxxNotifier channels con MessageCard format |
| 113 | `c301a8e` | NuGet Microsoft.Extensions.Http.Resilience 8.10.0 + AddStandardResilienceHandler() en los 12 named HttpClients |
| 0052 | (este) | ADR consolidado |

## Próximas direcciones

- **Form submissions dashboard** en `/admin/forms` — análogo al
  search analytics, lista submissions persistidas con filter por
  formKey + date range. Requiere extender `IFormSubmissionHandler`
  con `GetRecent(filter, page)`. Diferido.
- **Confirmation dialog para bulk actions**: `<dialog>` nativa o
  undo window con soft-delete.
- **Adaptive Cards adapter** para Teams (replace MessageCard
  cuando MS lo deprecate).
- **PagerDuty / Pushover / SMS / WhatsApp adapters** siguiendo
  el mismo pattern Channel.
- **DB-backed comment repository** cuando volumen excede in-memory.
- **Polly per-canal tuning**: extraer config a settings (max
  retries, timeout) para que el operador ajuste sin rebuild.

## References

- ADR 0030 — Forms internal submission runtime
- ADR 0038 — Comments runtime end-to-end
- ADR 0045 — Comments moderation API
- ADR 0047 — Composite + Channel notifier pattern
- ADR 0049 — Cleanup + Manrope + Webhook HMAC + Cart notifier
- ADR 0050 — Slack channels + Webhook replay protection
- ADR 0051 — Admin moderation dashboard SSR
