# ADR 0045 — Resend confirmation + brand-aware emails + search analytics + comments moderation (Olas 84-87)

- **Status:** Accepted
- **Enmienda (2026-08-02):** el *deferred* de abajo —«search analytics in-memory only: pierde
  datos en restart… swap por adapter persistente»— **se cumplió**. `InMemorySearchAnalyticsStore`
  fue reemplazado por `FileSystemSearchAnalyticsStore`, JSONL append-only por día en
  `App_Data/syn-search-analytics/`, que es justo el directorio que `SearchAnalyticsRetentionPolicy`
  ya barría sin encontrar nada. Cierra tres cosas de una: la analítica sobrevive al reinicio, el
  diccionario en memoria sin tope indexado por texto del visitante deja de existir, y ese texto
  **caduca** a los 30 días por defecto (el arquitecto lo bajó de 90 al aprobar el cambio). Lo que se acepta a cambio: las consultas de los
  visitantes ahora quedan en disco, así que esa retención pasa de decorativa a load-bearing.
  Cuando exista la API de sesión, se enchufa otro adapter sobre el mismo seam.
- **Date:** 2026-04-26
- **Deciders:** Arquitecto + agente, durante batch tras Ola 83 — *"continuemos"*.
- **Consolida:** 4 olas en un único ADR.

## Context

Tras Ola 83 (email confirmation post-registro) quedaban 4 deferred
items concretos heredados del ADR 0044 *Próximas direcciones*:

1. **Resend email confirmation** — sin endpoint, miembro que perdió
   el email queda atascado.
2. **Brand-aware `SiteName` en emails** — hardcoded "Synergos" en los
   3 consumers de `IEmailTemplateRenderer`. Multi-brand deploy
   inconsistente.
3. **Search analytics surface** — Ola 78 sembró el evento
   `search.executed` via `IAnalyticsTracker` pero sin store
   consultable. El equipo editorial no podía ver "qué buscan los
   visitantes" sin tooling externo.
4. **Comments moderation** (deferred ADR 0038) — `RequireModeration`
   marcaba comments como `Approved=false` pero la única forma de
   aprobar era editar el JSON a mano. Sin moderation queue, sin
   approval endpoints.

## Decision

Ejecutar las 4 olas en secuencia.

### Ola 84 — Resend email confirmation (1 commit `66d273f`)

**`IMemberAuthService` reusa** `RequestEmailConfirmationAsync` (ADR 0044).
Idempotente: ya devuelve `MemberExists=false` para anti-enumeration y
`AlreadyConfirmed=true` si el email ya está confirmado, lo que evita
generar tokens redundantes.

**`AccountController` extends**:
- GET `/account/resend-confirmation` → render del form.
- POST `/account/resend-confirmation` → llama
  `RequestEmailConfirmationAsync`, envía email solo si
  `MemberExists && !AlreadyConfirmed`. Anti-enumeration: el redirect
  responde idéntico para ambos casos (`MessageCode=sent`).

**View** `Account/ResendConfirmation.cshtml`: form + mensaje pre/post
con dictionary fallbacks + back-to-login link. `Layout=null`,
`<meta name="robots" content="noindex">`.

### Ola 85 — Brand-aware email `SiteName` (1 commit `5cef58f`)

**`AccountController` + `FormSubmissionsController`** reciben
`IBrandingProvider` por DI. En cada call site donde se construye el
`*EmailModel`, sustituye `"Synergos"` literal por:

```csharp
var brand = _branding.GetCurrent();
var siteName = string.IsNullOrWhiteSpace(brand.DisplayName) ? "Synergos" : brand.DisplayName;
```

3 call sites refactorizados:
- `AccountController.SendPasswordResetEmailAsync`
- `AccountController.SendEmailConfirmationLinkAsync`
- `FormSubmissionsController.SendNotificationAsync`

`IBrandingProvider` ya existe (ADR 0010) y se resuelve por host —
multi-brand deploy automáticamente respeta el branding del request
que dispara el email.

### Ola 86 — Search analytics store + endpoint (1 commit `4b09d5e`)

**Nuevo seam** `Synergos.CMS.Interfaces/ISearchAnalyticsStore.cs`:
```csharp
void Record(string query, int resultCount, long elapsedMilliseconds);
IReadOnlyList<SearchQueryStat> GetTopQueries(DateTime fromUtc, DateTime toUtc, int limit);
IReadOnlyList<SearchQueryStat> GetTopNoResultQueries(DateTime fromUtc, DateTime toUtc, int limit);
```

`SearchQueryStat(Query, Count, LastResultCount, LastSeenUtc)` —
record agregado por query lowercase trimmed.

**Default impl** `InMemorySearchAnalyticsStore` (Singleton):
- `ConcurrentDictionary<string, AggregateRecord>` interno.
- `NormalizeQuery`: trim + ToLowerInvariant para agrupar variantes.
- `GetTopQueries`: filter por LastSeenUtc en ventana, orden Count desc.
- `GetTopNoResultQueries`: filter adicional `LastResultCount == 0`.

Para producción retención larga: swap por adapter sobre Timescale /
Influx / CloudWatch — el seam aísla.

**`SearchController` extends**:
- Inyecta `ISearchAnalyticsStore`. En el GET principal, después de
  emitir analytics events, hace `Record(query, totalEstimated,
  elapsedMs)` si `Query` no es vacía.
- NUEVO `GET /api/search/analytics?from=...&to=...&limit=20` →
  `SearchAnalyticsResponse(FromUtc, ToUtc, TopQueries, TopNoResultQueries)`.

Defaults: ventana últimos 30 días, limit clamped 1-100. Sin auth en
primer pase — los datos son agregados (no PII de visitantes
individuales). Para gating editorial agregar role-check en futura ola.

### Ola 87 — Comments moderation (1 commit `71f3576`)

**`ICommentRepository` extends** con 4 nuevos métodos:
```csharp
IReadOnlyList<Comment> GetPendingForNode(int nodeId);
IReadOnlyList<Comment> GetAllPending(int limit);
Task<bool> ApproveAsync(int nodeId, string commentId, CancellationToken ct);
Task<bool> RejectAsync(int nodeId, string commentId, CancellationToken ct);
```

**`FileSystemCommentRepository` impl**:
- `GetPendingForNode`: filter `!Approved` + sort `CreatedAtUtc` desc
  (orden cola moderation).
- `GetAllPending(limit)`: enumera todos los `{nodeId}.json` bajo
  `StorageRoot`, agrega pendientes, sort + take(limit clamped 1-500).
- `ApproveAsync`: localiza por Id, `Comment with { Approved = true }`,
  persiste. Idempotente (ya aprobado → no-op true).
- `RejectAsync`: `RemoveAll(Id == commentId)`, persiste.
- `PersistAsync` privado refactorizado del flow `AddAsync` original.

**Nuevo `CommentsModerationController`** en `/api/comments/moderation`:
- 4 endpoints: GET `pending` global, GET `{nodeId}/pending`, POST
  `{nodeId}/{commentId}/approve`, POST `{nodeId}/{commentId}/reject`.
- Gate **explícito** `IMemberAccessGate.HasAnyRole("admin,moderator,editor")`
  en cada acción (return `Forbid()` si falla). Sin `[Authorize]`
  attribute porque los visitantes son Members (no Users del backoffice)
  y ASP.NET `[Authorize(Roles=...)]` bindea contra Identity nativo
  que aquí no aplica.
- Analytics events: `comment.moderation.approved` /
  `comment.moderation.rejected` con `moderator` (display name del gate)
  para auditoría.

Sin antiforgery — la UI futura es SPA cliente, gate de roles primario.

## Consequences

**Positivas:**

- **Member self-service completo**: registro → confirmación email →
  resend si se perdió → password reset. Cierra el flow ADR 0034.
- **Multi-brand emails consistentes**: deploy con 2+ siteRoots envía
  emails con `DisplayName` del brand del request — cero hardcode
  "Synergos" filtrado en producción.
- **Editorial insights inmediatos**: `/api/search/analytics` permite
  responder *"qué buscan que no encuentran"* sin abrir el repo. Input
  para crear contenido / sinónimos / corregir typos editoriales.
- **Comments moderation operacional**: queue global + per-node, dos
  acciones (approve/reject) con audit trail. Sites con contenido
  controversial pueden activar `RequireModeration=true` y operar el
  módulo en producción.
- **Cero schema rompedor** — todo runtime + seams.
- **Cero NuGet packages nuevos**.

**Negativas:**

- ~~**Search analytics in-memory only**: pierde datos en restart. Para
  retención semanal/mensual swap por adapter persistente (deferred).~~
  **Resuelto el 2026-08-02** — ver la enmienda de la cabecera.
- **Comments moderation sin UI**: los 4 endpoints existen pero la
  consumiría una SPA o el backoffice section custom (deferred Ola 78).
  Por ahora se opera con curl o tooling editor-side.
- **Search analytics endpoint no auth**: agregados pero exponen
  patrones de búsqueda. Para sitios sensibles agregar role gate.
- **Comments moderation Members-based**: si el deploy quiere
  moderation desde backoffice Users, requiere policy provider sobre
  `IBackOfficeSecurityAccessor` — diferido.

**Neutras:**

- 5 commits (4 feat + 1 docs ADR consolidado).
- 0 GUIDs nuevos.
- 0 dependency changes.

## Implementation summary

| # | Hash | Foco |
|---|---|---|
| 84 | `66d273f` | `/account/resend-confirmation` GET + POST + view (anti-enumeration) |
| 85 | `5cef58f` | `IBrandingProvider` injection en Account/Forms controllers; SiteName brand-aware en 3 call sites |
| 86 | `4b09d5e` | `ISearchAnalyticsStore` + `InMemorySearchAnalyticsStore` + `SearchController.Analytics` endpoint |
| 87 | `71f3576` | `ICommentRepository` extends 4 métodos + `FileSystemCommentRepository` impl + `CommentsModerationController` con gate roles |
| 0045 | (este) | ADR consolidado |

## Próximas direcciones

- **Ola 78** (deferred persistente): backoffice section custom AngularJS
  para comments moderation queue + transversales editor UI.
- **Ola 84+** (typed views remaining): `_Layout`, `PageBase`,
  `Account/Login`, `Account/Register`, `Account/Profile`,
  `Error.cshtml`. Refactor más invasivo (PageBaseResponse intermedio,
  Layout=null sin Model).
- **Search analytics persistencia**: adapter Timescale o Influx
  reemplazando `InMemorySearchAnalyticsStore` para retención larga.
- **Search analytics gate editorial**: role-check sobre `/api/search/analytics`.
- **Comments moderation Notifications**: emit evento al crear comment
  pendiente para que un adapter futuro mande notificación al moderator.

## References

- ADR 0034 — Member self-service runtime (resend cierra el flow)
- ADR 0035 — Email transactional runtime (brand-aware completion)
- ADR 0037 — Analytics tracker instrumentation (search store consume el seam)
- ADR 0038 — Comments runtime end-to-end (moderation cierra el loop)
- ADR 0010 — Branding provider (consumido en Ola 85)
- ADR 0044 — Email templates + confirmation (próximas direcciones cumplidas)
