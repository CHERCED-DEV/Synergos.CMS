# ADR 0050 — Slack-shaped notifier channels + Webhook replay protection (Olas 104-105)

- **Status:** Accepted
- **Date:** 2026-04-26
- **Deciders:** Arquitecto + agente, durante batch tras Ola 102 — *"siguamos!"*.
- **Consolida:** 2 olas en un único ADR.

## Context

Tras la consolidación del pattern Composite + Channel para los 3
notifiers (Comments / Forms / Cart) y la firma HMAC de Ola 101,
quedaron 2 deferred items recurrentes en los ADRs 0047 y 0049:

1. **Slack-shaped adapter**: los webhooks emitían flat JSON genérico
   compatible con n8n/Zapier/endpoints custom, pero no con Slack
   Block Kit (mensajes ricos en canales del equipo).
2. **Sin replay protection HMAC V1**: la firma HMAC de Ola 101
   cubría solo el body. Un atacante que capturara un payload+signature
   válidos podía replayar el mismo POST indefinidamente — el receptor
   no tenía forma de detectar duplicados.

## Decision

Ejecutar 2 olas paralelas.

### Ola 104 — Slack-shaped notifier channels (1 commit `627b49a`)

**3 nuevos canales** (uno por dominio):

- `SlackCommentModerationNotifier` — emoji 💬 header + 4 fields
  (Autor / Nodo / Recibido / ID) + body section con blockquote
  truncated 500 chars + action button "Abrir cola moderación".
- `SlackFormSubmissionNotifier` — emoji 📩 header + context line
  (IP / Origen / Recibido) + section fields con primeros 8 campos
  (truncated 240 chars cada uno).
- `SlackCartAbandonmentNotifier` — emoji 🛒 header + 4 fields
  (Subtotal / Items / Inactivo / CartId) + context italic con
  recovery suggestion.

**Helper compartido `SlackWebhookSender`** (static): serialize
UTF-8 → ByteArrayContent application/json → POST → log non-success.
Sin auth header (Slack URLs ya contienen el secret); sin HMAC
(Slack no valida inbound HMAC en incoming webhooks).

**Settings nuevos** — uno por dominio:
- `CommentsSettings.SlackWebhookUrl`
- `FormsSettings.SlackWebhookUrl`
- `CartAbandonmentSettings.SlackWebhookUrl`

Independientes del `WebhookUrl` genérico — un sitio puede tener
ambos configurados; el composite los itera y los 2 disparan en
paralelo por cada evento.

**Composer wire**: 3 named HttpClients + 3 channels Singleton bajo
el mismo composite que los demás canales.

### Ola 105 — Webhook replay protection (1 commit `2c798e6`)

**Patrón canónico tipo Stripe/GitHub**:

```
header: X-Synergos-Timestamp: {iso8601_utc}
header: X-Synergos-Signature: sha256={hex_lowercase}
signed: HMACSHA256(secret, "{timestamp}.{body}")
```

El timestamp es **parte del input del HMAC**, no solo header
separado. Eso previene el ataque clásico de replay donde un atacante
reusa un body+signature válidos mutando solo el timestamp header.

**Refactor `WebhookSigner`**:
- Reemplaza `ComputeHeader(secret, body)` con
  `ComputeSignedHeaders(secret, body)` que devuelve tuple
  `(string Timestamp, string Signature)?`.
- Internal: construye `signedInput = "{ts}.{body_utf8}"` y HMAC
  sobre eso.
- Const nuevo `TimestampHeaderName = "X-Synergos-Timestamp"`.

**3 webhook notifiers actualizados** (Comment / Form / Cart) para
emitir AMBOS headers cuando el secret está poblado.

**Receptor (documentar externamente)**:

1. Lee `X-Synergos-Timestamp` y `X-Synergos-Signature`.
2. Recomputa HMAC sobre `"{ts}.{body}"` con su copia del secret.
3. Compara constant-time contra el header recibido (rechaza mismatch).
4. Verifica `|now - timestamp| < ±5min` (ventana razonable; ajustable).

## Consequences

**Positivas:**

- **Slack mensajes ricos**: equipo de operaciones recibe alertas
  formateadas con headers / fields / actions en su canal —
  vs. el flat JSON crudo que solo orchestrators saben parsear.
- **Webhooks resistentes a replay**: receptor puede rechazar
  payloads viejos. Combinado con HTTPS (que previene MITM),
  la cadena de seguridad cierra.
- **Escalable a más plataformas**: agregar Discord / Teams /
  PagerDuty / etc. es 1 archivo nuevo siguiendo el pattern
  Slack — el helper `SlackWebhookSender` puede reutilizarse o
  servir como template.
- **Multi-canal real-world**: un site puede tener configurados
  email + webhook genérico + Slack todos al mismo tiempo. Útil
  para audit trail (email) + automation (webhook custom) +
  team awareness (Slack).
- **Cero schema rompedor**.
- **Cero NuGet packages nuevos** (Slack POST con HttpClient,
  HMAC con BCL).

**Negativas:**

- **Slack URLs hardcoded en settings**: un site con multi-team
  necesitaría múltiples Slack URLs. Mitigación: los 3 dominios
  tienen `SlackWebhookUrl` separado, así que team-comments puede
  ir a #moderation y team-cart a #ecommerce. Para más channels
  por dominio, agregar `SlackWebhookUrls` array (diferido).
- **Breaking change vs Ola 101**: el HMAC ahora cubre
  `{timestamp}.{body}`, no solo body. Receivers que validaron
  contra Ola 101 deben actualizar. Aceptable porque aún no había
  receptores en producción.
- **Slack truncation arbitraria**: 500 chars body comments,
  240 chars per form field, 8 form fields max. Para preview en
  Slack es razonable; el storage tiene la submission/comment
  completa.
- **No replay protection en email/Slack channels**: solo el
  webhook genérico firma con HMAC. Email no aplica (SMTP es push).
  Slack no aplica (URL token es el "signature"). OK.
- **Sin retry/backoff en Slack channels**: igual que webhook
  genérico, log Warning y suelta. Para garantía → Polly retry
  (deferido, requiere ADR para NuGet).

**Neutras:**

- 2 commits feat + 1 docs ADR consolidado.
- 0 GUIDs nuevos.
- 0 dependency changes.

## Implementation summary

| # | Hash | Foco |
|---|---|---|
| 104 | `627b49a` | SlackWebhookSender helper + 3 Slack-shaped channels (Comments/Forms/Cart) + 3 SlackWebhookUrl settings + composer wire |
| 105 | `2c798e6` | WebhookSigner.ComputeSignedHeaders refactor + 3 webhook notifiers emiten X-Synergos-Timestamp + X-Synergos-Signature firmando "{ts}.{body}" |
| 0050 | (este) | ADR consolidado |

## Próximas direcciones

- **Discord/Teams adapters**: clonar el pattern Slack para los 2
  competitors. 6 archivos nuevos totales (3 dominios × 2 plataformas).
- **Multi-Slack channels per dominio**: settings array `SlackWebhookUrls[]`
  para team con varios canales por evento.
- **Polly retry**: agregar policy Polly a los 6 named HttpClients
  (3 webhook + 3 Slack). Requiere ADR para NuGet (`Microsoft.Extensions.Http.Resilience`).
- **Receiver SDK helper**: empaquetar verificador HMAC + replay check
  en un helper publicable (NuGet o copy-paste C# / Node / Python)
  para que integradores no tengan que reimplementar.
- **Backoffice section AngularJS** para moderation queue (Ola 78
  deferred persistente — el último gran feature pendiente).

## References

- ADR 0030 — Forms internal submission runtime
- ADR 0038 — Comments runtime end-to-end
- ADR 0043 — Cart abandonment scanner
- ADR 0047 — Composite + Channel notifier pattern
- ADR 0049 — Cleanup + Manrope + Webhook HMAC + Cart notifier
- Stripe webhook signing reference: <https://stripe.com/docs/webhooks/signatures>
