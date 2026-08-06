# ADR 0030 — Forms internal submission runtime (Ola 60)

- **Status:** Accepted
- **Date:** 2026-04-25
- **Deciders:** Arquitecto + agente, durante Ola 60
- **Extiende:** ADR 0018 (Forms dual-path) — cierra Path A interno
- **Related:** ADR 0009 (extension seams), ADR 0005 (composers), ADR 0013
  (no automatic seeders)

## Context

ADR 0018 definió un modelo dual-path para Forms:

- **Path A — Custom SSR**: `<form action="{formEndpoint}">` apuntando
  a un endpoint externo (Formspree, Netlify, Mailchimp).
- **Path B — Bridge iframe**: `<iframe src="{embedUrl}">` para Typeform/
  Jotform/Google Forms.

ADR 0018 § "Re-evaluación" anticipó que cuando se necesite "auth +
server-side validation" del lado del CMS, se evaluaría
`IFormSubmissionHandler` seam.

Tras Olas 56-59 (cierres de Blog/Shop/Flow runtime), Forms quedó como
el último módulo agente-able sin runtime backend propio. El editor que
quiere un formulario interno (sin depender de un servicio externo)
debía:

1. Pegar un endpoint externo (no siempre disponible o gratuito).
2. Manejar el storage/ruteo del submission ahí.
3. Sin auditoría central, sin honeypot, sin rate-limit.

Necesitábamos un path interno: el CMS recibe el POST, valida, persiste
y redirige con feedback al editor — sin obligar al sitio a depender de
un servicio externo desde el día uno.

## Decision

Cerrar Path A con runtime backend interno respetando ADR 0018:

### Schema (Ola 60.1)

`elementFormContainer`:
- `formEndpoint`: pasa de `Mandatory=true` a `Mandatory=false`.
  Renombrado UI a "Form Endpoint (external)".
- **`formInternalKey` (NUEVO)**: `Umbraco.TextBox`, slug regex
  `^[a-z][a-z0-9-]*$`, `Variations=Nothing`, opcional. GUID
  `e4564dc8-95f2-4820-b4de-4afe1eeb3d3f`.
- Render-time defensa: si ambos vacíos → no se renderiza.

Editor flow:
- Quiere endpoint externo (Formspree etc.) → llena `formEndpoint`,
  deja `formInternalKey` vacío.
- Quiere persistencia interna → llena `formInternalKey` con un slug
  (ej. "contacto", "newsletter"). `formEndpoint` opcional.
- Si llena ambos → `formInternalKey` gana.

### Application + Interfaces (Ola 60.2)

**`Synergos.CMS.Interfaces/IFormSubmissionHandler.cs`**:

```csharp
Task<FormSubmissionResult> SubmitAsync(
    FormSubmissionRequest request, CancellationToken ct);
```

Records:
- `FormSubmissionRequest(FormKey, Fields, ClientIp, UserAgent,
  Referrer, ReceivedAtUtc)` — diccionario `Fields` insensitive a case,
  honeypot ya filtrado por el controller.
- `FormSubmissionResult(Success, ErrorCode?, StorageReference?)` con
  factories `Ok(reference)` / `Fail(errorCode)`.

**`Synergos.CMS.Application/Configuration/FormsSettings.cs`** POCO:
- `StorageRoot` = `"App_Data/syn-form-submissions/"` (default fuera de
  `wwwroot`)
- `MaxFieldLengthChars` = 5000
- `MaxFieldsPerSubmission` = 50
- `HoneypotFieldName` = `"syn_hp"`
- `MaxSubmissionsPerHourPerIp` = 10
- `SuccessQueryParam` = `"submitted"`
- `ErrorQueryParam` = `"form-error"`

Bind via `OptionsComposer` desde sección `Synergos:Forms`.

### Web — defaults (Olas 60.3 + 60.4 + 60.5)

**`FileSystemFormSubmissionHandler`**: persiste cada submission como
JSON individual a
`{ContentRoot}/{FormsSettings.StorageRoot}/{formKey}/{yyyyMMdd_HHmmss}_{guid}.json`.
SanitizeForPath neutraliza caracteres inválidos del `formKey`. Catchea
`IOException` + `UnauthorizedAccessException` y devuelve
`Fail("storage-failed")` con log `Error`. Singleton.

**`InMemoryFormRateLimiter`**: sliding-window in-memory de 1 hora por
par `(ClientIp, FormKey)`. Singleton.

**`FormSubmissionsController`** (`[Route("api/forms")]`,
`[ApiController]`, `[AllowAnonymous]`,
`[Consumes("application/x-www-form-urlencoded")]`):

```
POST /api/forms/{formKey}/submit
```

Pipeline:

1. Valida `formKey` contra slug regex (mismo que el schema property).
2. Honeypot check: si llega valor en `HoneypotFieldName`, log `Info`
   y redirect "success-fake" al `Referer` con `?{Success}=1` —
   no leak de detección al atacante.
3. Rate limit: 429 si supera el cap por hora.
4. Field extraction: skip honeypot, skip names vacíos, cap longitud
   por `MaxFieldLengthChars`, trim.
5. Reject si `> MaxFieldsPerSubmission`.
6. Llama `IFormSubmissionHandler.SubmitAsync`.
7. Redirect 302 al `Referer`:
   - Success → `?{SuccessQueryParam}=1`
   - Failure → `?{ErrorQueryParam}={ErrorCode}`

Sin `[ValidateAntiForgeryToken]` — los forms son SSR plain HTML
editor-defined; emitir tokens en cada renderer + manejar caching es
más costoso que el beneficio. Honeypot + rate limit es la defensa
documentada.

### Renderer (Ola 60.6)

`Container.cshtml` recibe inyectados `IOptions<FormsSettings>` y
`IHttpContextAccessor`:

- Action pick: `formInternalKey` → `/api/forms/{key}/submit`; sino
  `formEndpoint`.
- Cuando `isInternal`:
  - Hidden input honeypot con name `FormsSettings.HoneypotFieldName`,
    posicionado off-screen (style inline para defensa-en-capas si el
    design-system CSS no carga), `aria-hidden="true"`, `tabindex="-1"`,
    `autocomplete="off"`.
  - Lee `Request.Query[SuccessQueryParam]` y
    `Request.Query[ErrorQueryParam]`. Si presente, emite
    `<div class="syn-form__feedback syn-form__feedback--{kind}">` con
    `role="status"`/`"alert"` + `aria-live="polite"`. Mensajes via
    dictionary keys `Form.Messages.Success` y `Form.Messages.Error`
    (ya existían en Ola 23).

`data-form-internal="true|false"` en el `<section>` para que
design-system JS pueda decidir si interceptar el submit (XHR + toast)
o dejar el POST nativo del browser.

## Consequences

**Positivas:**

- **Forms 100% editable end-to-end sin servicio externo**: el editor
  configura un slug y empieza a recibir submissions persistidas como
  JSON en disco, listas para procesar offline (importar a CRM, enviar
  por email batch, auditar manualmente).
- **A11y mantenida**: el div feedback usa `role="status"`/`"alert"`
  + `aria-live="polite"`. Honeypot con `aria-hidden="true"` no
  interfiere con screen readers.
- **Anti-spam baseline**: honeypot + rate-limit cubren el 90% de
  bots de formulario sin pedirle al usuario CAPTCHA. Fail-silent del
  honeypot evita que el bot aprenda a saltarlo.
- **Defensiva en storage**: Path bajo `App_Data/` por default — fuera
  de `wwwroot`, no servido por static-files. SanitizeForPath neutraliza
  el `formKey` antes de usarlo en el filesystem. Cap de longitud +
  número de campos.
- **Seam preservada**: `IFormSubmissionHandler` permite swappear
  `FileSystemFormSubmissionHandler` por adapter sobre queue
  (Service Bus, RabbitMQ), webhook (Slack, custom backend) o email
  (SendGrid) sin tocar el controller ni el renderer.
- **Backward compatible**: `formEndpoint` sigue funcionando idéntico
  para sitios que ya lo usan; solo cambió de mandatory a opcional. El
  renderer detecta automáticamente cuál usar.

**Negativas:**

- **Sin checkout de submission HTML**: el editor que quiera ver las
  submissions desde el backoffice de Umbraco no tiene UI — debe abrir
  los archivos JSON manualmente o instrumentar un script externo.
  Diferido a futura ola si la frecuencia justifica un trees + section
  custom de Umbraco backoffice.
- **In-memory rate limit no se comparte multi-instancia**: bajo load
  balancer con N instancias, un atacante puede repartir N×cap
  submissions por hora. Mitigación: agregar un `IDistributedRateLimiter`
  adapter sobre Redis cuando aplique. Documentado en
  `InMemoryFormRateLimiter` XML doc.
- **Sin CAPTCHA**: el honeypot detiene bots simples; bots avanzados
  (con browser headless) pueden saltarlo. Para sitios con valor alto
  agregar `elementFormCaptcha` (HCaptcha/Turnstile/reCAPTCHA) en
  futura ola.
- **Sin CSRF**: forma deliberada en este pase. Forms son
  editor-defined SSR plain HTML; emitir tokens antiforgery en cada
  renderer + manejar caching/CDN es más costoso que el beneficio.
  La combinación honeypot + rate limit es la defensa estándar para
  formularios públicos editor-defined.
- **`InMemoryFormRateLimiter` puede crecer linealmente**: el
  diccionario `(IP, FormKey) → List<DateTime>` se limpia solo dentro
  del lock. Si muchas IPs únicas, el dict crece hasta el restart.
  Para sitios de tráfico alto, swap por adapter con TTL keys.

**Neutras:**

- 1 GUID nuevo (formInternalKey property). Verificación cuádruple OK.
- 0 Templates uSync nuevos — los blocks de Forms no son DocTypes
  navegables, son element types embebidos en pages.
- 0 Dictionary keys nuevas — `Form.Messages.Success` y
  `Form.Messages.Error` ya existían desde Ola 23.

## Alternatives considered

- **Adoptar Umbraco.Forms**. Diferido (decisión de ADR 0018 reiterada).
  El paquete es grande, opinionated, y su upgrade-path acopla a su
  versión. Custom seam es más simple para el 90% de use-cases internos.
- **Persistir submissions en SQLite/DB Umbraco**. Descartado por
  scope. El filesystem es trivial de auditar (ls, cat) y de exportar
  (sync rsync). DB añade migraciones, índices, retention policies que
  no aportan al MVP. Si se necesita query/agregación, swap del handler.
- **Antiforgery token obligatorio**. Diferido. Documentado en
  Consequences negativas. Si un sitio necesita hardening adicional,
  agregar `[ValidateAntiForgeryToken]` + `@Html.AntiForgeryToken()`
  en Container es cambio local.
- **Email notification automática post-submit**. Descartado. Mezcla
  responsabilidades (storage vs delivery) y obliga a wirear SMTP
  settings. Sigue la regla de ADR 0009: una seam por preocupación.
  Para email, agregar `IFormNotificationDispatcher` separado en futura
  ola.
- **Webhook delivery automática post-submit**. Mismo argumento que
  email — separar responsabilidades. Adapter alterno del
  `IFormSubmissionHandler` puede hacer delivery síncrona, o nuevo
  seam dispatch async puede coexistir.

## Implementation summary (Ola 60, 7 commits)

| Commit | Hash | Foco |
|---|---|---|
| `feat(ola-60.1)` | `d2fb101` | Schema `elementformcontainer`: `formEndpoint` mandatory→false + new `formInternalKey` prop (GUID `e4564dc8`) |
| `feat(ola-60.2)` | `27c3283` | `IFormSubmissionHandler` seam + DTOs + `FormsSettings` POCO |
| `feat(ola-60.3)` | `5b46ad4` | `FileSystemFormSubmissionHandler` + `InMemoryFormRateLimiter` |
| `feat(ola-60.4)` | `a78b256` | `FormSubmissionsController` POST `/api/forms/{key}/submit` |
| `feat(ola-60.5)` | `e6adc02` | Wire `FormsSettings` + handler + rate limiter en composers |
| `feat(ola-60.6)` | `2451cbb` | `Container.cshtml` — action pick + honeypot + success/error feedback |
| `docs(ola-60.7)` | (este) | ADR 0030 + index README + current-state §11 |

## References

- ADR 0018 — Forms dual-path (extendido — Path A interno cerrado)
- ADR 0009 — Extension seams (`IFormSubmissionHandler` sigue el patrón)
- ADR 0005 — Composers centralizados (wire en `OptionsComposer` +
  `SeamComposer`)
- ADR 0028 — Shop runtime (referente de patrón Settings POCO + cookie/
  HMAC; aquí no se firma porque forma POST es one-shot)
- `refactor-docs/migration/05-legacy-refinement-inventory.md` — Forms
  como módulo agente-able último pendiente del backlog editable
