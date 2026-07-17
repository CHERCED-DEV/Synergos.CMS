# ADR 0058 — Health endpoint + Receiver SDK docs + Webhook test harness (Olas 131-133)

- **Status:** Accepted
- **Date:** 2026-04-26
- **Deciders:** Arquitecto + agente, batch final del scope-amplio bound a Ola 135.
- **Consolida:** 3 olas en un único ADR.

## Context

Tras Olas 128-129 (Adaptive Cards + Polly per-channel) quedaban 3
items operacionales para cerrar el scope acotado:

1. **Health endpoint** que reporte uptime/memoria/seam counts/settings
   activos para troubleshooting rápido.
2. **Receiver SDK helper docs** — markdown explicando HMAC + replay
   verification con snippets C# / Node.js / Python.
3. **Webhook test harness** — botón en /admin para disparar payload
   de test a los channels configurados, validar URL/secret/HMAC
   antes de prod.

## Decision

### Ola 131 — Health endpoint

**`AdminController.Health`** action en `GET /admin/health` con
`HealthReport` record (UptimeSeconds, ProcessMemoryMb, DotnetVersion,
HostName, PendingComments, FormsWithSubmissions, ResilienceMaxRetries,
ResilienceTimeoutTotal, CacheTtlSeconds).

Lecturas baratas: `Process.GetCurrentProcess()` + `pendingPage(1,1)` +
`ListFormKeys()` + `_adminSettings.CurrentValue.WebhookResilience.X`.

**View `Health.cshtml`** con 3 summary cards (Uptime / Memoria /
Runtime) + 2 panels de detail (Seams + counts / Settings activos).

`FormatUptime(int seconds)` helper Razor: < 1h → "Mm Ss", < 1d →
"Hh Mm", >= 1d → "Dd Hh Mm".

### Ola 132 — Receiver SDK docs

**Nuevo doc** `docs/webhooks/receiver-sdk.md` con:
- Headers convención (`Authorization` Bearer / `X-Synergos-Timestamp`
  / `X-Synergos-Signature`).
- Algoritmo de verificación step-by-step (1. read headers / 2. body
  raw / 3. concat ts.body / 4. HMACSHA256 / 5. constant-time compare
  / 6. tolerance window).
- **Snippets C# / Node.js / Python** con `FixedTimeEquals` /
  `timingSafeEqual` / `secrets.compare_digest`.
- Payloads ejemplo de los 3 events (`comment.pending-moderation` /
  `form.submitted` / `cart.abandoned`).
- Sección "Idempotencia" recomendando dedup via `(event, siteName, id)`
  tuple.
- Sección "Fallos lado receiver" explicando Polly retry behavior
  (5xx → retry, 4xx → no retry).
- Tabla de configuración multi-canal.

### Ola 133 — Webhook test harness

**`AdminController.WebhookTestHarness`** action en
`GET /admin/webhooks/test` + 3 POST actions:

- `TestCommentWebhook` → construye `Comment` fake con autor "[TEST]
  Synergos webhook test" + body explicativo + nodeId=0 + Approved=false
  → llama `ICommentModerationNotifier.NotifyPendingAsync` (composite
  itera todos los channels configurados).
- `TestFormWebhook` → `FormSubmissionRequest` fake con
  `formKey="synergos-webhook-test"` + 3 fields de prueba +
  `FormSubmissionResult.Ok("test-storage-ref")`.
- `TestCartWebhook` → `AbandonedCart` fake con `cartId="test-{guid8}"`
  + 3 items + 187.500 COP + LastActivityUtc 90 min atrás.

Cada test action emite `webhooks.test.{domain}` analytics event
para audit trail. Redirect con `msg={domain}-fired` para flash
message.

**View `WebhookTestHarness.cshtml`** con:
- Page header explicativo + warning sobre idempotencia.
- Flash message según `messageCode`.
- 3 paneles 2-col responsive (Comments / Form / Cart) cada uno con
  hint + botón "🚀 Disparar test".
- Panel info "📖 Receiver SDK" apuntando a
  `docs/webhooks/receiver-sdk.md`.

**Topbar nav extends** con 2 entries: "Webhooks" y "Health".

## Consequences

**Positivas:**

- **Health visibility inmediato**: sysadmin abre `/admin/health` y ve
  uptime + memoria + counts + settings sin SSH ni tooling.
- **Webhook integration self-service**: integradores externos pueden
  copy-paste el SDK helper docs en su lenguaje y validar HMAC con
  garantía de constant-time compare. Sin "build my own" risk de
  timing attacks.
- **Test harness reduce error rate de setup**: el moderator que
  configura un nuevo webhook puede hacer click → recibe payload →
  valida que llegó antes de mover a prod. Cierra loop de validación
  manual.
- **Composite pattern probado**: el test harness usa `ICommentModerationNotifier`
  (composite) que itera ALL channels — si alguno está roto, el log
  Warning lo identifica.
- **Topbar nav escalado**: 6 entries (Inicio / Moderación / Forms /
  Búsqueda / Webhooks / Health) — clean visual progression.
- **Cero schema rompedor**.
- **Cero NuGet packages nuevos**.

**Negativas:**

- **Health endpoint sin auth probe externo**: para load-balancer
  health check, hace falta un `/healthz` endpoint público (no
  member-gated) que retorna 200 OK rápido sin filesystem reads.
  Diferido — el `/admin/health` actual es para humanos, no LBs.
- **Test harness payload está marcado como test pero se persistirá
  según receiver**: si un receiver guarda en DB todo lo que recibe,
  habrá filas test en su DB. Mitigación: el SDK doc enseña a
  filtrar por `cartId.StartsWith("test-")` o `formKey == "synergos-
  webhook-test"`.
- **No hay status indicator del HTTP response del test**: el test
  harness redirige inmediato sin esperar el response del receiver.
  Para visibility real-time, swap por SSE o WebSocket — diferido.
- **Receiver SDK docs solo 3 lenguajes**: Go / Java / PHP / Ruby
  no incluidos. Aceptable por ahora — los 3 cubren la mayoría;
  el algoritmo es trivial de port.
- **Health no incluye DB connection probe**: si Umbraco pierde DB
  el `/admin/health` igual responde OK (el dashboard sigue
  funcionando con cache). Mitigación: agregar DB ping al report
  — diferido.

**Neutras:**

- 1 commit feat batch + 1 docs ADR consolidado.
- 0 GUIDs nuevos.
- 0 dependency changes.

## Implementation summary

| # | Foco |
|---|---|
| 131 | AdminController.Health + HealthReport record + Health.cshtml view con 3 summary cards + 2 detail panels + FormatUptime helper |
| 132 | docs/webhooks/receiver-sdk.md — guía completa con snippets C#/Node/Python + payloads + idempotencia + Polly behavior |
| 133 | AdminController.WebhookTestHarness + TestCommentWebhook + TestFormWebhook + TestCartWebhook actions + WebhookTestHarness.cshtml view + topbar 2 entries (Webhooks + Health) |
| 0058 | (este) ADR consolidado |

## Próximas direcciones (post-cap Ola 135)

- **`/healthz` público** para load-balancer health check.
- **Status indicator real-time** via SSE en test harness.
- **DB-backed comment repository** (sigue diferido).
- **Member roster admin view** (`/admin/members`).
- **i18n admin baseline** via Dictionary keys (sigue diferido).
- **2FA opt-in** para Members.
- **SEO maturity**: JSON-LD + sitemap index multi-brand.
- **Adaptive Cards 1.5+** cuando MS lo lance.

## References

- ADR 0036 — Output caching via IMemoryCache
- ADR 0050 — Slack channels + Webhook replay protection
- ADR 0051 — Admin moderation dashboard SSR
- ADR 0056 — AdminSettings + streaming + undo
- ADR 0057 — Adaptive Cards Teams + Polly per-channel
- Stripe webhook signing reference: <https://stripe.com/docs/webhooks/signatures>
- Adaptive Cards docs: <https://adaptivecards.io>
