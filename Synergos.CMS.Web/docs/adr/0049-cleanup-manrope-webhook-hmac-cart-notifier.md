# ADR 0049 — IDE0005 cleanup + Manrope font + Webhook HMAC + Cart abandonment notifier (Olas 98-102)

- **Status:** Accepted
- **Date:** 2026-04-26
- **Deciders:** Arquitecto + agente, durante batch tras Ola 96 — *"continua"*.
- **Consolida:** 5 olas en un único ADR.

## Context

Tras la batch de CSS modular (Olas 92-96) quedaron varios pendientes:

1. **Build noise**: dotnet build emitía 17+ warnings IDE0005 (using
   directives redundantes via Web SDK implicit usings o stale tras
   refactors). Aunque no bloquean compilación, ensucian el output y
   hacen difícil distinguir warnings reales.
2. **Error.cshtml runtime crash**: cuando un visitante hit 404/500,
   StatusCodePagesMiddleware re-ejecuta `/error/{code}` y la
   compilación Razor falla con CS0234 — `@Umbraco.GetDictionaryValue(...)`
   no resuelve porque la view tiene `@model X` que fuerza
   `RazorPage<X>` (sin helper Umbraco).
3. **Manrope font no cargada**: el design system Synergos.UI usa
   `'Manrope'` como font canónica pero el CMS no la cargaba —
   fallback a `Segoe UI` siempre.
4. **Webhook signature ausente**: ADR 0047 dejó como negativa "Sin
   signature/HMAC en outgoing webhook". Receptor no puede verificar
   que el payload viene del CMS.
5. **Cart abandonment scanner solo emite analytics events**: ADR 0047
   listó como próxima dirección agregar email/webhook al operador
   cuando un cart > threshold queda abandonado.

## Decision

Ejecutar 5 olas en secuencia.

### Ola 98 — IDE0005 cleanup primer round + Error.cshtml fix (1 commit `5f19ea8`)

- 13 archivos `Services/*` con usings stale o redundantes via SDK
  implicit usings (Web SDK con `<ImplicitUsings>enable</ImplicitUsings>`
  ya importa `Microsoft.AspNetCore.Http`, `Microsoft.Extensions.Logging`,
  `Microsoft.Extensions.Hosting`, `System.Net.Http.Json`).
- `Error.cshtml` agrega `@inject Umbraco.Cms.Core.Web.IUmbracoHelper Umbraco`
  para que `@Umbraco.GetDictionaryValue(...)` resuelva. Las Account
  views NO tienen el problema porque NO tienen `@model X`.

### Ola 99 — IDE0005 sweep completo (1 commit `33f697b`)

13 archivos adicionales identificados al hacer rebuild completo:
controllers (Account/Error/Robots/Sitemap), composers (SeamComposer),
notifications (MemberGatingHandler), services (Cart hosted services,
CompositeNotifiers, DefaultBlogQuery, DefaultCartService,
DefaultEmailService). Resultado: **0 warnings IDE0005 en build**.

### Ola 100 — Manrope font wire (1 commit `d085748`)

Carga Manrope desde Google Fonts via preconnect + stylesheet
display=swap. Wireado en 3 entry points:

- `_Layout.cshtml` (todas las pages con Layout).
- `Account/_AccountHead.cshtml` partial (8 Account views Layout=null).
- `Error.cshtml` (error pages Layout=null).

Pesos cargados: 300/400/500/600/700 (cubre todos los font-weight
tokens de syn-tokens.css). Si `_BrandThemeStyle` override define
otra font-family, ese gana.

### Ola 101 — WebhookSigner helper + HMAC en 2 channels existentes (1 commit `f52f771`)

**Nuevo `WebhookSigner` helper** (static, `System.Security.Cryptography`):
```csharp
public static string? ComputeHeader(string? secret, ReadOnlySpan<byte> body)
public const string SignatureHeaderName = "X-Synergos-Signature";
```

API mínima: si `secret` vacío → null (signaling "no firmar"). Si poblado
→ `"sha256={hex_lowercase}"` con HMAC-SHA256.

**Settings extends**:
- `CommentsSettings.WebhookHmacSecret` (string?, default null).
- `FormsSettings.WebhookHmacSecret` (mismo shape).

**Refactor 2 webhook channels existentes** (Comment + Form):
- Serialize payload a UTF-8 bytes una sola vez via
  `JsonSerializer.SerializeToUtf8Bytes` (eficiente: evita doble
  conversión bytes ↔ JsonContent).
- `ByteArrayContent` con `Content-Type: application/json; charset=utf-8`.
- Si `WebhookSigner.ComputeHeader(...)` no-null → agrega header
  `X-Synergos-Signature: sha256={hex}` via `TryAddWithoutValidation`.

Sin replay protection built-in en V1 — el receptor combina con
timestamp/nonce dentro del payload si lo necesita.

### Ola 102 — Cart abandonment notifier (1 commit `fbafafd`)

Tercera familia de notifiers con el pattern Composite + Channel
canónico (paralelo de comments + forms).

**Nuevo seam** `Synergos.CMS.Interfaces/ICartAbandonmentNotifier.cs`:
```csharp
Task NotifyAbandonedAsync(AbandonedCart cart, CancellationToken ct);
```
+ marker `ICartAbandonmentNotifierChannel`.

**Composite + 2 channels**:
- `CompositeCartAbandonmentNotifier`: itera channels con try-catch.
- `EmailCartAbandonmentNotifier`: renderiza nuevo template Razor
  `Views/Emails/CartAbandonment.cshtml` con
  `CartAbandonmentEmailModel` (cartId/items/subtotal/currency/
  lastActivity/minutesSinceActivity/siteName). Subject brand-aware.
- `WebhookCartAbandonmentNotifier`: POST JSON al `WebhookUrl` con
  payload + Bearer token + HMAC signature (reusa `WebhookSigner` Ola 101).

**`CartAbandonmentSettings` extends**:
- `NotifyEmailAddress`, `WebhookUrl`, `WebhookBearerToken`,
  `WebhookHmacSecret` (todos opt-in default vacío).

**Hook en `CartAbandonmentScannerHostedService`**: tras cada cart
detectado + filtrado por `MinSubtotalToReport`, llama
`_notifier.NotifyAbandonedAsync(cart, stoppingToken)` fire-and-forget.

**Composer wire**: 2 channels Singleton + composite Singleton +
HttpClient named `"cart-abandonment-webhook"`.

## Consequences

**Positivas:**

- **Build limpio**: dotnet build sin IDE0005 — output legible para
  detectar warnings reales nuevos.
- **Error.cshtml robusto**: 404/500 no rompen el render. Se ve la
  página de error custom según diseño.
- **Tipografía premium**: Manrope (font canónica del design system)
  carga ahora en todos los entry points; el sitio se ve como fue
  diseñado en lugar del fallback Segoe UI.
- **Webhooks autenticables**: integradores pueden verificar HMAC
  para descartar payloads spoofeados. Backwards-compat: si secret
  vacío, no firma — no rompe consumidores existentes.
- **3 familias de notifiers consistentes**: Comments + Forms + Cart
  todas usan el mismo pattern Composite + Channel + HMAC. Adaptador
  Slack-shaped (próxima Ola) es 1 archivo nuevo en cualquiera.
- **Cart abandonment ahora accionable**: operador recibe email/Slack
  cuando un cart > threshold queda abandonado, no solo log analytics
  que requiere consumer downstream para ver.
- **Cero schema rompedor**.
- **Cero NuGet packages nuevos** (HMACSHA256 vive en BCL).

**Negativas:**

- **Sin replay protection HMAC V1**: receptor debe combinar con
  timestamp/nonce dentro del payload si lo necesita.
- **Sin Slack-shaped formatting**: webhooks emiten flat JSON
  genérico — para Slack messages ricos (text + blocks), escribir
  adapter Slack-shaped (1 archivo nuevo). Diferido.
- **Sin retry/backoff**: si webhook devuelve 500, el canal loguea
  Warning y suelta. Para garantía de entrega, swap por adapter con
  Polly retry o queue async.
- **Manrope external font dependency**: si Google Fonts está caído,
  el fallback `Segoe UI` toma efecto. Mitigación aceptable —
  display=swap garantiza render no-bloqueante.
- **CartAbandonmentEmailModel no incluye productos**: el email
  reporta subtotal y count pero no qué items. Refinement futuro:
  extender el model para incluir top 3 SKUs.

**Neutras:**

- 5 commits feat + 1 docs ADR consolidado.
- 0 GUIDs nuevos.
- 0 dependency changes.
- 26 archivos modificados / creados totales.

## Implementation summary

| # | Hash | Foco |
|---|---|---|
| 98 | `5f19ea8` | IDE0005 primer round (13 archivos Services) + Error.cshtml @inject UmbracoHelper |
| 99 | `33f697b` | IDE0005 sweep completo (13 archivos: controllers/composers/notifications/services) |
| 100 | `d085748` | Manrope font wire en _Layout + AccountHead + Error |
| 101 | `f52f771` | WebhookSigner helper + HMAC-SHA256 en 2 channels existentes + secrets settings |
| 102 | `fbafafd` | Cart abandonment notifier (composite + email + webhook channels + email template + scanner hook) |
| 0049 | (este) | ADR consolidado |

## Próximas direcciones

- **Slack-shaped adapter**: `SlackXxxNotifier` para los 3 dominios
  (comments/forms/cart) que mapeen a `{"text": "...", "blocks": [...]}`.
- **Replay protection**: agregar timestamp + nonce al payload con
  ventana de tolerancia para que el receptor pueda detectar replays.
- **Polly retry**: agregar policy a los 3 HttpClients named (`comment-
  moderation-webhook`, `form-submission-webhook`, `cart-abandonment-webhook`)
  vía `Microsoft.Extensions.Http.Polly`. Requiere ADR aparte (NuGet
  package nuevo).
- **CartAbandonmentEmailModel extends**: agregar top SKUs + recovery
  CTA URL para que el email sea accionable.
- **Backoffice section AngularJS** para moderation queue (Ola 78
  deferred persistente).

## References

- ADR 0030 — Forms internal submission runtime
- ADR 0038 — Comments runtime end-to-end
- ADR 0043 — Cart abandonment scanner inicial (solo analytics)
- ADR 0044 — Email templates Razor (consumido por canales email)
- ADR 0046 — Brand-aware email subjects + Comments moderation notifier
- ADR 0047 — Composite + Channel notifier pattern (extendido aquí a
  cart + agregada HMAC signature)
- ADR 0048 — CSS design system aligned with Synergos.UI (Manrope era
  font canónica documentada pero no cargada hasta Ola 100)
