# ADR 0047 — Composite + Channel notifier pattern for comments moderation and form submissions (Olas 90-91)

- **Status:** Accepted
- **Date:** 2026-04-26
- **Deciders:** Arquitecto + agente, durante batch tras Ola 89 — *"continuemos"*.
- **Consolida:** 2 olas en un único ADR.

## Context

Tras Ola 89 (`ICommentModerationNotifier`) y revisando el código de
`FormSubmissionsController`, se identifican 2 problemas paralelos:

1. **Comments notifier monocanal** — `EmailCommentModerationNotifier`
   era la única opción. Sites que prefieren Slack/Discord/Teams/webhook
   custom tenían que reescribir el adapter completo o aceptar email.
   ADR 0046 *Próximas direcciones* lo dejó como deferred.
2. **Forms notifier inline en el controller** — `FormSubmissionsController.SendNotificationAsync`
   tenía 40 líneas de email setup hardcoded en el controller. No
   reutilizable, no swappable, y blockeaba agregar canales adicionales
   (webhook, Slack) sin amontonar más DI en el controller.

## Decision

Adoptar el pattern Composite + Channel para AMBOS notifiers:

- **`I*Notifier`** es la fachada que el controller inyecta.
- **`I*NotifierChannel`** es marker que extiende la fachada — cada
  canal implementa el shape completo y puede ser usado solo o vía
  composite.
- **`Composite*Notifier`** consume `IEnumerable<I*NotifierChannel>` y
  forwardea con try-catch por canal.

Cada canal es responsable de su propio short-circuit: si su settings
están vacíos, retorna inmediatamente sin error. Eso permite registrar
todos los canales by default sin condicional en el composer.

### Ola 90 — Comments composite + webhook channel (1 commit `1c97859`)

- **Refactor `ICommentModerationNotifier`**: agrega marker
  `ICommentModerationNotifierChannel : ICommentModerationNotifier`.
- **`EmailCommentModerationNotifier`** ahora implementa `Channel`.
- **NUEVO `WebhookCommentModerationNotifier`**: POST JSON al
  `CommentsSettings.WebhookUrl`. Bearer token opcional. HttpClient via
  `IHttpClientFactory` (named `"comment-moderation-webhook"`) — evita
  captura singleton del HandlerLifetime.
- **NUEVO `CompositeCommentModerationNotifier`**: itera todos los
  channels, try-catch por canal con log Warning incluyendo
  `Channel.GetType().Name`.
- **`CommentsSettings`** extends:
  - `WebhookUrl` (string?, default null)
  - `WebhookBearerToken` (string?, default null)

### Ola 91 — Forms notifier seam + composite + webhook channel (1 commit `78f7019`)

- **NUEVO `IFormSubmissionNotifier`** + marker `IFormSubmissionNotifierChannel`
  en `Synergos.CMS.Interfaces/`.
- **NUEVO `CompositeFormSubmissionNotifier`** — paralelo del de comments.
- **NUEVO `EmailFormSubmissionNotifier`** — extrae la lógica antes
  inline en `FormSubmissionsController.SendNotificationAsync`. Mantiene
  branding via `IBrandingProvider` y subject brand-aware.
- **NUEVO `WebhookFormSubmissionNotifier`** — POST JSON al
  `FormsSettings.WebhookUrl`. Mismo pattern HttpClientFactory.
- **`FormsSettings`** extends:
  - `WebhookUrl` (string?, default null)
  - `WebhookBearerToken` (string?, default null)
- **Refactor `FormSubmissionsController`**: pierde 3 deps directos
  (`IEmailService` / `IEmailTemplateRenderer` / `IBrandingProvider`)
  y el método privado de 40 líneas. Solo conoce
  `IFormSubmissionNotifier`.

### Composer wire (ambas olas)

```csharp
// Comments
services.AddSingleton<ICommentModerationNotifierChannel, EmailCommentModerationNotifier>();
services.AddHttpClient(WebhookCommentModerationNotifier.FactoryName);
services.AddSingleton<ICommentModerationNotifierChannel, WebhookCommentModerationNotifier>();
services.AddSingleton<ICommentModerationNotifier, CompositeCommentModerationNotifier>();

// Forms
services.AddSingleton<IFormSubmissionNotifierChannel, EmailFormSubmissionNotifier>();
services.AddHttpClient(WebhookFormSubmissionNotifier.FactoryName);
services.AddSingleton<IFormSubmissionNotifierChannel, WebhookFormSubmissionNotifier>();
services.AddSingleton<IFormSubmissionNotifier, CompositeFormSubmissionNotifier>();
```

## Consequences

**Positivas:**

- **Extensibilidad sin tocar core**: agregar `SlackXxxNotifier`,
  `DiscordXxxNotifier`, `QueueXxxNotifier` es 1 archivo nuevo + 1
  línea en composer. Ningún consumidor cambia.
- **Multi-canal simultáneo**: site puede tener `NotifyEmailAddress`
  Y `WebhookUrl` poblados — ambos disparan en paralelo. Útil para
  mantener email como audit trail mientras Slack es el canal primario.
- **FormSubmissionsController slim**: pasó de 8 deps a 6, perdió 40
  líneas privadas, más fácil de testear y leer.
- **Payload JSON estable**: webhooks emiten flat JSON con `event`/
  `siteName`/datos del evento. Compatible con Slack incoming
  webhooks (texto en `text` field — futuro adapter Slack-shaped),
  Discord, Teams, n8n, Zapier o endpoints custom.
- **HttpClient bien gestionado**: `IHttpClientFactory` named
  garantiza handler rotation default 2min — evita socket exhaustion.
- **Cero schema rompedor**.
- **Cero NuGet packages nuevos**.

**Negativas:**

- **Sin retry/backoff por canal**: si webhook devuelve 500, el canal
  loguea Warning y suelta. Para garantía de entrega, swap por adapter
  con Polly retry, o un canal `QueueXxxNotifier` que enqueue a
  Hangfire/Channel<T> con worker dedicado.
- **Sin orden garantizado entre canales**: el composite itera
  IEnumerable, sin control de orden cross-canal. Para casos donde
  el orden importa (ej. Slack antes que email), implementar custom
  composite ordenado.
- **Webhook payload genérico**: no es Slack-shaped (`{"text": "..."}`)
  ni Discord-shaped. Funcional para n8n / Zapier / endpoints custom;
  para Slack mensaje rico, escribir adapter Slack-shaped.
- **Sin signature/HMAC en outgoing webhook**: el receptor no puede
  verificar que el payload viene del CMS. Bearer token via
  `WebhookBearerToken` mitiga; HMAC sobre body queda diferido.
- **Sin pattern equivalente en cart/account/search**: si esos módulos
  necesitan multi-canal en el futuro, replicar el pattern (no
  generalizar a `INotifier<T>` aún — Rule of Three).

**Neutras:**

- 3 commits totales (2 feat + 1 docs ADR consolidado).
- 0 GUIDs nuevos.
- 0 dependency changes.

## Implementation summary

| # | Hash | Foco |
|---|---|---|
| 90 | `1c97859` | Composite/Channel pattern para Comments + WebhookCommentModerationNotifier + CommentsSettings.WebhookUrl/BearerToken |
| 91 | `78f7019` | Mismo pattern para Forms + IFormSubmissionNotifier seam + Email/Webhook channels + composite + FormsSettings.WebhookUrl/BearerToken + slim FormSubmissionsController |
| 0047 | (este) | ADR consolidado |

## Próximas direcciones

- **Slack-shaped adapter**: `SlackCommentModerationNotifier` /
  `SlackFormSubmissionNotifier` que mapeen al schema
  `{"text": "...", "blocks": [...]}` para mensajes ricos.
- **Webhook signature**: agregar header
  `X-Synergos-Signature: sha256={hex}` con HMAC del body usando
  un secret compartido. Permite al receiver verificar autenticidad
  y descartar replay attacks.
- **Retry/backoff via Polly**: agregar policy a los HttpClients
  named (`comment-moderation-webhook` / `form-submission-webhook`).
- **Cart abandonment notifier**: el `CartAbandonmentScannerHostedService`
  (Ola 81) actualmente solo emite `IAnalyticsTracker`. Aplicar el
  mismo Composite + Channel pattern para email + webhook al operador
  cuando un carrito > threshold queda abandonado.
- **Backoffice section AngularJS** para moderation queue (Ola 78
  deferred). Ahora con notifiers wireados, los moderators no dependen
  de polling.

## References

- ADR 0030 — Forms internal submission runtime (lógica de email
  ahora extracted a seam)
- ADR 0038 — Comments runtime end-to-end (notifier extends)
- ADR 0044 — Email templates Razor (consumido por canales email)
- ADR 0046 — Brand-aware email subjects + comments moderation
  notifier (negativa "monocanal" cerrada)
- ADR 0010 — Branding via provider (consumido en webhook payloads)
