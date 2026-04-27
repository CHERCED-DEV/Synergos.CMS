# ADR 0051 — Admin moderation dashboard SSR (Ola 107)

- **Status:** Accepted
- **Date:** 2026-04-26
- **Deciders:** Arquitecto + agente, durante batch tras Ola 105 — *"con la que tu quieras"*.

## Context

Tras Ola 87 (`CommentsModerationController`) los endpoints de moderación
quedaron disponibles via API REST (`/api/comments/moderation/...`),
y tras Olas 89/102/104 los moderators reciben notificaciones por email,
webhook genérico y Slack. Pero **NO había UI** para que un moderator
ejecute aprobaciones/rechazos sin escribir un cliente API o usar curl.

ADR 0045/0046/0047 listaron como deferred persistente "backoffice
section custom AngularJS" (Ola 78). Esa es la solución completa pero
requiere infraestructura considerable: `App_Plugins/{name}/package.manifest`,
AngularJS controllers, sectionTrees, custom views, integration con
Umbraco User auth, etc. Para sitios donde la moderación es por
**Members** (no Users del backoffice), el AngularJS no aplica
naturalmente — los Members se loguean en el frontend, no en
`/umbraco/`.

## Decision

Construir un **dashboard SSR Razor** alternativo y complementario:

- **`AdminController`** en `/admin` (MVC puro, no Umbraco
  RenderController) con 3 actions:
  - `GET /admin/moderation/comments` — lista pending via
    `ICommentRepository.GetAllPending(50)`.
  - `POST /admin/moderation/comments/{nodeId}/{commentId}/approve` —
    llama `ApproveAsync` + redirect.
  - `POST /admin/moderation/comments/{nodeId}/{commentId}/reject` —
    llama `RejectAsync` + redirect.
- **Auth gate** `IMemberAccessGate.HasAnyRole("admin,moderator,editor")`
  per action → `Forbid()` si falla. Sin antiforgery porque el flow es
  member-authenticated y el risk de CSRF es bajo en este contexto
  editorial. Misma decisión que `CommentsModerationController` Ola 87.
- **Views/Admin/ModerationComments.cshtml** — `Layout=null` (no
  hereda chrome del siteRoot, evita cascadas innecesarias) con topbar
  brand/nav/user + page header con count + empty state ✨ + lista
  cards con autor/fecha/nodo/id metadata + body blockquote + 2
  botones inline form approve/reject (PRG pattern).
- **Views/Admin/_AdminHead.cshtml** partial — meta + Manrope link +
  bundle CSS (tokens + base + admin) para todas las views Layout=null
  del Admin.
- **wwwroot/css/syn-admin.css** — bundle propio: sticky topbar con
  backdrop-blur, brand-mark gradient brand→accent, cards con hover
  lift, action buttons con state surfaces success/danger.
- **Analytics events** — emite `comment.moderation.approved/rejected`
  con `source: "admin-dashboard"` para distinguir de las API calls
  externas (que también emiten esos eventos sin source).

## Consequences

**Positivas:**

- **Moderation usable desde día uno**: un moderator hace login en el
  frontend (`/account/login`), navega a `/admin/moderation/comments`
  y aprueba/rechaza con clicks. Sin tooling externo.
- **Member-roles vs User-roles correctos**: alineado con el modelo
  actual del sitio donde los moderators son Members del frontend.
  Para deploys donde la moderación es User-only del backoffice, swap
  por backoffice section custom (Ola 78 deferred — sigue siendo válido
  como complemento, no reemplazo).
- **Design system reutilizado**: el dashboard usa los mismos tokens
  CSS que el resto del sitio (`syn-tokens.css`, `syn-base.css`).
  Sensación de coherencia visual.
- **Zero hardcoded paths**: las endpoints usan `RedirectToAction`
  apuntando a su propia action — refactor de routes seguro.
- **Audit trail preservado**: analytics events con `source` permiten
  saber si una aprobación vino del API directo o del dashboard.
- **Cero schema rompedor**.
- **Cero NuGet packages nuevos**.

**Negativas:**

- **Sin paginación**: limit fijo en 50 pending. Para sites con cola
  alta (>50), hace falta agregar `?page=N` con `Skip()` en el
  repository. Diferido.
- **Sin filtros por nodo / autor / fecha**: el query es plano. Para
  workflows pesados, el moderator querría filtrar. Diferido.
- **Sin acción bulk**: ahora es 1 click por comentario. Para limpiar
  100 comentarios spam ⇒ 100 clicks. Bulk-select + bulk-approve/reject
  diferido.
- **Sin UX de spam-flagging**: aprobar y rechazar son las únicas
  opciones. Para "marcar como spam" (entrenando un futuro filtro
  Akismet o similar) hace falta un 3er botón + endpoint backend. Diferido.
- **Solo comments**: el dashboard cubre comments moderation pero NO
  search analytics, form submissions, member roster, etc. Para
  esos, agregar más actions y views al `AdminController`. Diferido.
- **Sin antiforgery**: ataques CSRF teóricos posibles si un moderator
  visita una página externa que tenga un form auto-POST a las routes
  approve/reject. Mitigación: cookies SameSite=Lax (default ASP.NET),
  Member auth required, y el riesgo es bajo en el contexto.

**Neutras:**

- 1 commit feat (107) + 1 docs ADR (esta).
- 0 GUIDs nuevos.
- 0 dependency changes.
- 4 archivos nuevos (~550 líneas combined).

## Implementation summary

| # | Hash | Foco |
|---|---|---|
| 107 | `ce9e4cb` | AdminController (3 actions) + Views/Admin/ModerationComments + _AdminHead partial + wwwroot/css/syn-admin.css |
| 0051 | (este) | ADR consolidado |

## Próximas direcciones

- **Search analytics dashboard** en `/admin/analytics/search` —
  consume `ISearchAnalyticsStore` con date-range picker + top
  queries + top no-results.
- **Form submissions dashboard** en `/admin/forms` — lista submissions
  recientes por formKey con filter.
- **Pagination + filtros** en `/admin/moderation/comments`.
- **Bulk actions** (select-all + approve-many / reject-many).
- **Spam endpoint** (`POST .../spam`) que rechaza + entrena filtro
  futuro (Akismet adapter via `ICommentSpamFilter` seam diferido).
- **Backoffice section AngularJS** sigue siendo válido como
  COMPLEMENTO para deploys donde Users (no Members) administran —
  ambos pueden coexistir.

## References

- ADR 0034 — Member self-service runtime (Members = identidad de
  los moderators)
- ADR 0038 — Comments runtime end-to-end
- ADR 0045 — Comments moderation API (los endpoints REST que este
  dashboard NO reemplaza, solo complementa)
- ADR 0048 — CSS design system aligned with Synergos.UI (`syn-admin.css`
  sigue la misma convención modular)
