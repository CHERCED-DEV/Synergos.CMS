# ADR 0046 — Search analytics role gate + brand-aware email subjects + comments moderation notifier (Olas 88-89)

- **Status:** Accepted
- **Date:** 2026-04-26
- **Deciders:** Arquitecto + agente, durante batch tras Ola 87 — *"continuemos"*.
- **Consolida:** 2 olas en un único ADR.

## Context

Tras Ola 87 (comments moderation endpoints) quedaban 3 negativos
documentados en ADR 0045:

1. **Search analytics endpoint sin auth gate** — `/api/search/analytics`
   exponía agregados public, lo cual revela patrones de búsqueda interna
   de visitantes (no PII directa, pero sí inteligencia editorial).
2. **Email subjects no brand-aware** — los 3 emails transaccionales
   ("Restablece tu contraseña", "Confirma tu email", "[Form] Nueva
   submission") tenían subject hardcoded, sin mencionar el brand.
   En multi-brand deploy el destinatario no podía distinguir el origen.
3. **Comments moderation sin notification** — los moderators tenían que
   polear `/api/comments/moderation/pending` para enterarse de nuevos
   comments — UX pobre en sitios donde la moderación es esporádica.

## Decision

Ejecutar 2 olas en secuencia.

### Ola 88 — Search analytics gate + brand-aware email subjects (1 commit `198be8c`)

**`SearchSettings.AnalyticsAdminRolesCsv`** (default `"admin,editor"`):
- CSV case-insensitive de roles habilitados para `/api/search/analytics`.
- Vacío/null = endpoint queda abierto (override explícito para dev).

**`SearchController` extends**:
- Inyecta `IMemberAccessGate` + `IOptions<SearchSettings>`.
- En el endpoint Analytics: si `rolesCsv` no es vacío y
  `gate.HasAnyRole(rolesCsv)` falla → `Forbid()`.

**Subjects brand-aware** — 3 call sites refactorizados:
- `AccountController.ForgotPasswordPost`:
  `"{siteName} · Restablece tu contraseña"`.
- `AccountController.SendEmailConfirmationLinkAsync`:
  `"{siteName} · Confirma tu email"`.
- `FormSubmissionsController.SendNotificationAsync`:
  `"{siteName} · Nueva submission del form: {formKey}"`.

`siteName` resuelto via `IBrandingProvider.GetCurrent().DisplayName`
con fallback `"Synergos"` (helper `ResolveSiteName()` que ya existía
desde Ola 85).

### Ola 89 — Comments moderation notifier (1 commit `6c6f04b`)

**Nuevo seam** `Synergos.CMS.Interfaces/ICommentModerationNotifier.cs`:
```csharp
Task NotifyPendingAsync(Comment comment, CancellationToken ct);
```

**`CommentsSettings` extends** con `NotifyEmailAddress` (default null).
Solo efectivo si `RequireModeration=true`.

**Default impl** `EmailCommentModerationNotifier` (Singleton):
- Inyecta `IEmailService` + `IEmailTemplateRenderer` + `IBrandingProvider`
  + `IOptions<CommentsSettings>` + `ILogger`.
- Si `NotifyEmailAddress` vacío → no-op (no logea, no envía, no falla).
- Renderiza nueva view `Views/Emails/CommentPendingModeration.cshtml`
  con `CommentPendingModerationEmailModel`.
- Subject: `"{siteName} · Comentario pendiente de moderación"`.
- Try-catch defense: fallos SMTP loguean Warning pero NO rompen el
  flow del visitante.

**`CommentsController.Submit` hook**: después de `AddAsync`, si
`!comment.Approved` → `await _moderationNotifier.NotifyPendingAsync(...)`
fire-and-forget (await porque ya estamos en async pipeline pre-redirect,
pero el notifier mismo es try-catched).

**Wire**: `SeamComposer.AddSingleton<ICommentModerationNotifier, EmailCommentModerationNotifier>()`.

## Consequences

**Positivas:**

- **Search analytics seguro by-default**: deploys nuevos no exponen
  patrones de búsqueda. Para investigación libre dev, override con
  CSV vacío en appsettings.Development.json.
- **Multi-brand emails distinguibles**: visitor que recibe emails de
  2+ siteRoots ahora ve `"Brand A · Restablece tu contraseña"` vs
  `"Brand B · Restablece tu contraseña"`. Cierra negativa Ola 85.
- **Moderation push, no pull**: site con `RequireModeration=true` y
  `NotifyEmailAddress` poblada notifica activamente — moderator no
  pierde comentarios pendientes por olvido.
- **Notifier swappable**: `ICommentModerationNotifier` desacopla la
  lógica de cómo notificar. Adapter Slack / Teams / webhook /
  Discord es 1 archivo nuevo + cambio de wire.
- **Cero schema rompedor**.
- **Cero NuGet packages nuevos**.

**Negativas:**

- **Notification url relativa**: `ModerationQueueUrl` en el email es
  `/api/comments/moderation/pending` sin host. Moderator agrega el
  host manual desde su email client. Razón: el notifier puede correr
  fire-and-forget sin HttpContext (futuro adapter background). Para
  URL absoluta, inyectar host del brand activo o resolver via
  `IServer.Features`. Diferido.
- **Email-only default**: sites con preferencia Slack-first deben
  escribir adapter custom. Aceptable — email es el LCD de
  notification across orgs.
- **Notifier es await, no fire-and-forget puro**: el notifier se
  awaitea dentro del request handler. Si SMTP es lento, el visitante
  ve latencia extra en el redirect. Mitigación: try-catch del
  adapter ya evita errors, pero latencia persiste. Para asincronía
  total, swap por adapter que enqueue (Hangfire, Channel<T>).
- **Search analytics gate via Members**: si el deploy usa Users del
  backoffice para investigar, el gate no aplica — agregarse vía
  policy `IBackOfficeSecurityAccessor` queda pendiente.

**Neutras:**

- 3 commits totales (2 feat + 1 docs ADR consolidado).
- 0 GUIDs nuevos.
- 0 dependency changes.

## Implementation summary

| # | Hash | Foco |
|---|---|---|
| 88 | `198be8c` | `SearchSettings.AnalyticsAdminRolesCsv` + role gate en `/api/search/analytics` + 3 subjects brand-aware en Account/Forms |
| 89 | `6c6f04b` | `ICommentModerationNotifier` seam + `EmailCommentModerationNotifier` default + `CommentsSettings.NotifyEmailAddress` + email template Razor + hook en `CommentsController` |
| 0046 | (este) | ADR consolidado |

## Próximas direcciones

- **Ola 90+**: backoffice section custom AngularJS para moderation queue
  (Ola 78 deferred persistente). Ahora con 4 endpoints (`pending`/
  `{nodeId}/pending`/`approve`/`reject`) + notifier hooked, la UI
  consumidora finalmente tiene contrato estable para construirse.
- **Ola 91+**: typed views remaining — `_Layout`, `PageBase`,
  `Account/Login`, `Error.cshtml`. Refactor más invasivo (PageBaseResponse
  intermedio).
- **Notifier alternativos**: SlackCommentModerationNotifier sobre
  webhook + WebhookCommentModerationNotifier genérico.
- **Search analytics persistencia**: adapter Timescale o Influx
  reemplazando `InMemorySearchAnalyticsStore` (deferred desde Ola 86).
- **ProductPage typed view**: pendiente decisión schema (productImages
  como singular MediaWithCrops vs collection con DataType
  `Multiple Media Picker`).

## References

- ADR 0034 — Member self-service runtime (subjects brand-aware extends)
- ADR 0035 — Email transactional runtime (notifier consume IEmailService)
- ADR 0038 — Comments runtime end-to-end (moderation notifier closes loop)
- ADR 0010 — Branding via provider (consumido en Ola 88 + 89)
- ADR 0044 — Email templates Razor (template CommentPendingModeration sigue convención)
- ADR 0045 — Search analytics + comments moderation (negativos cerrados)
