# ADR 0080 — Webhook telemetry alerts via threshold breach (Olas 195-196)

- **Status:** Accepted
- **Date:** 2026-04-27
- **Deciders:** Arquitecto + agente.

## Context

ADR 0072 introdujo `IWebhookTelemetryStore` con stats per-canal
(Total/Success/Failure + P50/P95/P99). Próxima dirección listada:
*"telemetry alerts: si fail rate > X% en últimos N min → notify"*.

## Decision

### Ola 195 — `WebhookTelemetryAlertSettings`

POCO con sección `Synergos:Admin:WebhookTelemetryAlerts`:
- `Enabled` (default false, opt-in).
- `CheckIntervalMinutes` (default 5).
- `FailRateThreshold` (0-1, default 0.20 = 20%).
- `MinimumSampleSize` (default 100).
- `CooldownMinutes` (default 60, evita spam).
- `AlertEmail` (vacío = no-op).

### Ola 196 — `WebhookTelemetryAlertHostedService`

`BackgroundService` que cada `CheckIntervalMinutes` polléa
`GetChannelStats()` y para cada canal:
- Skip si `TotalCalls < MinimumSampleSize`.
- Calcula `failRate = FailureCount / TotalCalls`.
- Skip si `< FailRateThreshold`.
- Cooldown via `ConcurrentDictionary<channelName, lastAlertUtc>`;
  skip si dentro del window.
- Email HTML al `AlertEmail` con table de metrics + acciones
  sugeridas + cooldown info.

Diseño simple: usa `IEmailService` directo en lugar de un
composite notifier pattern. Si llega Rule of Three (Slack +
Discord + Teams para alerts también), refactor a composite.

## Consequences

**Positivas:**

- **Operacional**: operador recibe email cuando un canal degrada.
  No tiene que monitorear `/admin/health` periódicamente.
- **Cooldown built-in**: máx 1 alert/hora per canal — no spam.
- **MinimumSampleSize**: evita stats irrelevantes ("100% fail
  rate sobre 1 sample" = 1 fail).
- **Opt-in**: default disabled. No surprise emails.

**Negativas:**

- **Solo email**: no Slack/Teams/Discord aún. Refactor cuando
  Rule of Three.
- **In-memory cooldown**: reset on restart. Si reinicia 10 veces
  por algo, podrían salir 10 alerts. Aceptable — restart es
  evento raro.
- **No alert al recover**: cuando fail rate baja por sí solo,
  no se notifica. Future: track rec_overy events para "alerts
  resolved" emails.

## Implementation summary

| # | Foco |
|---|---|
| 195 | `WebhookTelemetryAlertSettings` POCO + `OptionsComposer.Configure`. |
| 196 | `WebhookTelemetryAlertHostedService` BackgroundService + email HTML body + cooldown dict + `SeamComposer.AddHostedService`. |
| 0080 | (este) ADR consolidado |

## Próximas direcciones

- **Composite notifier para alerts** cuando llegue Rule of Three.
- **Recovery event**: notify cuando fail rate baja debajo del
  threshold tras alert previo.
- **Per-channel custom thresholds**: dict similar a PerChannel del
  resilience.
- **Escalation**: si fail rate sigue alto > N hours, escalate a
  pager/SMS.

## References

- ADR 0035 — IEmailService (used directly).
- ADR 0072 — IWebhookTelemetryStore (data source).
- `feedback_audit_vs_analytics_seams` (memoria — alerts son
  business events, no audit).
