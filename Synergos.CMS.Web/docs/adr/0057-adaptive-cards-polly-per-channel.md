# ADR 0057 — Adaptive Cards Teams + Polly per-channel config (Olas 128-129)

- **Status:** Accepted
- **Date:** 2026-04-26
- **Deciders:** Arquitecto + agente, scope-amplio batch.
- **Consolida:** 2 olas en un único ADR.

## Context

Tras Olas 124-126 (admin polish + AdminSettings) quedaban 2 deferred
items concretos del ADR 0052 + el Próximas direcciones del ADR 0056:

1. **Adaptive Cards adapter** para Teams — replace MessageCard format
   antes de que MS lo deprecate.
2. **Polly per-channel config tuning** — extraer max retries / timeout
   / circuit breaker a settings configurable runtime.

## Decision

### Ola 128 — Adaptive Cards en los 3 Teams notifiers

**Refactor in-place** de los 3 `TeamsXxxNotifier` para emitir
Adaptive Card 1.4 schema en lugar de MessageCard:

```json
{
  "type": "message",
  "attachments": [{
    "contentType": "application/vnd.microsoft.card.adaptive",
    "content": {
      "type": "AdaptiveCard",
      "schema": "http://adaptivecards.io/schemas/adaptive-card.json",
      "version": "1.4",
      "body": [
        { "type": "TextBlock", "text": "...", "weight": "Bolder" },
        { "type": "FactSet", "facts": [...] }
      ]
    }
  }]
}
```

Cada notifier:
- `TeamsCommentModerationNotifier`: TextBlock title + subtle subtitle
  + body truncated 800 chars + FactSet (ID, Recibido).
- `TeamsFormSubmissionNotifier`: TextBlock title + subtitle con IP +
  referrer + timestamp + FactSet de los primeros 12 fields.
- `TeamsCartAbandonmentNotifier`: TextBlock title con `color="Warning"`
  + subtitle + body recovery suggestion + FactSet 4 stats.

`themeColor` MessageCard removido — los Adaptive Cards usan
semantic colors (`color="Warning"`) interpretados por el host theme.

Los const `BrandIndigoHex` / `WarningAmberHex` removidos (unused).

### Ola 129 — Polly per-channel config

**`AdminSettings.WebhookResilience`** nueva property con
`WebhookResilienceSettings` POCO:
- `MaxRetryAttempts` (default 3)
- `AttemptTimeoutSeconds` (default 10)
- `TotalRequestTimeoutSeconds` (default 30)
- `RetryBaseDelayMs` (default 2000)

**`SeamComposer.Compose`** lee settings en composition time via
`builder.Config.GetSection("Synergos:Admin:WebhookResilience").Get<...>()`,
construye un `Action<HttpStandardResilienceOptions>` local
`ConfigureWebhookResilience(opts)` que:
- Setea `opts.Retry.MaxRetryAttempts`
- Setea `opts.Retry.Delay`
- Setea `opts.AttemptTimeout.Timeout`
- Setea `opts.TotalRequestTimeout.Timeout`

Reemplazo masivo: los 12 `.AddStandardResilienceHandler()` →
`.AddStandardResilienceHandler(ConfigureWebhookResilience)`. Aplicado
via Edit replace_all.

Trade-off: settings se leen al boot, no hot-reload. Para tuning de
retries/timeouts es aceptable (no es un escenario que cambie a 1Hz).

## Consequences

**Positivas:**

- **Adaptive Cards future-proof**: cuando MS deprecate MessageCard,
  el código sigue funcionando. Los receivers nuevos verán mejor UX
  (Adaptive Cards tienen mejor rendering en Teams desktop/mobile).
- **Polly tuneable runtime**: sysadmin ajusta retries/timeouts en
  `appsettings.json` sin recompilar. Útil para tuning después de
  observar telemetría real (e.g., webhook receiver lento → subir
  AttemptTimeout).
- **Configuración centralizada**: las 12 channels comparten
  `WebhookResilience` settings. Para tuning per-canal individual,
  swap por dictionary indexed by FactoryName (deferred Rule of Three).
- **Cero schema rompedor** — los adaptive cards son un payload diff,
  no afectan persistencia.
- **Cero NuGet packages nuevos**.

**Negativas:**

- **Adaptive Cards 1.4 require Teams desktop modern**: usuarios con
  Teams legacy ven fallback degradado. Aceptable — Teams desktop se
  actualiza forzado por Microsoft.
- **Settings al boot sin hot-reload**: cambios en `appsettings`
  requieren restart de la app. Mitigación: un futuro
  `IOptionsMonitor<WebhookResilienceSettings>` con suscripción y
  rebuild del pipeline — diferido (overkill para esta cadencia).
- **`color="Warning"` solo en cart**: comments y forms usan el
  default text color. Los themes Teams pueden manejarlo, pero hay
  menos consistencia con el branding indigo del CMS. Aceptable —
  Adaptive Cards son interpretadas por el host.
- **Test manual requerido**: el primer cambio de format payload no
  tiene tests automatizados. Mitigación futura: snapshot tests
  contra fixtures JSON.

**Neutras:**

- 1 commit feat batch (Olas 128+129 unificadas) + 1 docs ADR.
- 0 GUIDs nuevos.
- 0 dependency changes.

## Implementation summary

| # | Foco |
|---|---|
| 128 | TeamsCommentModerationNotifier + TeamsFormSubmissionNotifier + TeamsCartAbandonmentNotifier refactor de MessageCard a Adaptive Cards 1.4 schema. Const themeColor removidos. |
| 129 | AdminSettings.WebhookResilience + WebhookResilienceSettings POCO (4 keys). SeamComposer agrega `ConfigureWebhookResilience` local action y aplica a los 12 named HttpClients via `AddStandardResilienceHandler(action)`. |
| 0057 | (este) ADR consolidado |

## Próximas direcciones

- **Snapshot tests** contra Adaptive Card payloads (Ola futura).
- **IOptionsMonitor con rebuild de resilience pipeline** para
  hot-reload de settings.
- **Per-channel resilience tuning** via dictionary indexed by
  FactoryName.
- **Health endpoint /admin/health** que reporta status de los
  channels + resilience metrics (próxima Ola 131).

## References

- ADR 0047 — Composite + Channel notifier pattern
- ADR 0050 — Slack channels + Webhook replay protection
- ADR 0052 — Admin extensions + Discord/Teams + Polly (deferred
  items cerrados aquí)
- ADR 0056 — AdminSettings + streaming + undo (mismo pattern de
  settings runtime)
- Adaptive Cards docs: <https://adaptivecards.io>
