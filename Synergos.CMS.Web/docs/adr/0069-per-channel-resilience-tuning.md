# ADR 0069 — Per-channel WebhookResilience tuning via FactoryName dictionary (Olas 157-158)

- **Status:** Accepted
- **Date:** 2026-04-27
- **Deciders:** Arquitecto + agente, scope-amplio batch.
- **Consolida:** 2 olas en un único ADR.

## Context

ADR 0064 introdujo hot-reload de `WebhookResilienceSettings` via
`IOptionsMonitor` para los 12 named HttpClients de notifier channels.
Pero los 4 settings (MaxRetryAttempts, AttemptTimeoutSeconds,
TotalRequestTimeoutSeconds, RetryBaseDelayMs) eran **compartidos
entre todos los canales**. La próxima dirección de ADR 0064 listaba:

> **Per-channel tuning** via dictionary indexed by FactoryName (Rule
> of Three — espera 3+ canales con requirements distintos).

El "Rule of Three" se cumplió en este sprint: el operador observó
distintos perfiles de latencia en producción real:
- **Teams** receivers son lentos (~5-15s typical) — bump
  AttemptTimeoutSeconds.
- **Discord** webhooks son rápidos pero tienen rate limits
  agresivos — bump RetryBaseDelayMs.
- **Slack** corre estable en defaults.

Forzar al operador a configurar el knob global a "lo peor de los 3"
es subóptimo (timeouts excesivos para Slack, retry delays innecesarios
para Teams).

## Decision

### Ola 157 — WebhookResilienceSettings.PerChannel

```csharp
public sealed class WebhookResilienceSettings
{
    public int MaxRetryAttempts { get; init; } = 3;
    public int AttemptTimeoutSeconds { get; init; } = 10;
    public int TotalRequestTimeoutSeconds { get; init; } = 30;
    public int RetryBaseDelayMs { get; init; } = 2000;

    // Olas 157-158 — overrides per-canal indexados por FactoryName.
    public Dictionary<string, WebhookResilienceChannelOverride> PerChannel { get; init; } = new();
}

public sealed class WebhookResilienceChannelOverride
{
    public int? MaxRetryAttempts { get; init; }
    public int? AttemptTimeoutSeconds { get; init; }
    public int? TotalRequestTimeoutSeconds { get; init; }
    public int? RetryBaseDelayMs { get; init; }
}
```

Top-level fields actúan como defaults; per-channel overrides solo en
los campos no-null. Permite tuning quirúrgico:

```jsonc
{
  "Synergos": {
    "Admin": {
      "WebhookResilience": {
        "MaxRetryAttempts": 3,
        "AttemptTimeoutSeconds": 10,
        "TotalRequestTimeoutSeconds": 30,
        "RetryBaseDelayMs": 2000,
        "PerChannel": {
          "comment-moderation-teams": { "AttemptTimeoutSeconds": 30 },
          "form-submission-teams": { "AttemptTimeoutSeconds": 30 },
          "cart-abandonment-teams": { "AttemptTimeoutSeconds": 30 },
          "comment-moderation-discord": { "RetryBaseDelayMs": 5000 },
          "form-submission-discord": { "RetryBaseDelayMs": 5000 },
          "cart-abandonment-discord": { "RetryBaseDelayMs": 5000 }
        }
      }
    }
  }
}
```

### Ola 158 — WebhookResilienceExtensions.AddWebhookResilience()

Capturar `builder.Name` (el FactoryName del HttpClient) en el closure
y consultar `PerChannel[name]` en runtime via `IOptionsMonitor`:

```csharp
public static IHttpClientBuilder AddWebhookResilience(this IHttpClientBuilder builder)
{
    var pipeline = builder.AddStandardResilienceHandler();
    var channelName = builder.Name;
    pipeline.Services
        .AddOptions<HttpStandardResilienceOptions>(pipeline.PipelineName)
        .Configure<IOptionsMonitor<WebhookResilienceSettings>>((opts, monitor) =>
        {
            var s = monitor.CurrentValue;
            s.PerChannel.TryGetValue(channelName, out var ovr);
            opts.Retry.MaxRetryAttempts = ovr?.MaxRetryAttempts ?? s.MaxRetryAttempts;
            opts.Retry.Delay = TimeSpan.FromMilliseconds(ovr?.RetryBaseDelayMs ?? s.RetryBaseDelayMs);
            opts.AttemptTimeout.Timeout = TimeSpan.FromSeconds(ovr?.AttemptTimeoutSeconds ?? s.AttemptTimeoutSeconds);
            opts.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(ovr?.TotalRequestTimeoutSeconds ?? s.TotalRequestTimeoutSeconds);
        });
    return builder;
}
```

Mantiene la propiedad de hot-reload de ADR 0064 — cuando settings
cambian (incluyendo el dict PerChannel), el siguiente handler rotation
construye una pipeline con el override correcto.

## Consequences

**Positivas:**

- **Tuning quirúrgico**: cada canal puede tener perfil distinto
  basado en observación real. Slack/Webhook genérico se mantienen en
  defaults agresivos; Teams/Discord pueden subir timeouts donde sirve.
- **Backward compatible**: si `PerChannel` es vacío (default), todos
  los canales heredan los defaults top-level — comportamiento idéntico
  al previo. Migración gradual sin big-bang.
- **Hot-reloadable**: el dict PerChannel respeta el mismo
  `IOptionsMonitor` flow de ADR 0064. El operador modifica
  appsettings.json sin restart.
- **Discoverable via FactoryName**: cada notifier expone su factory
  name como `static readonly string FactoryName` (e.g.,
  `WebhookCommentModerationNotifier.FactoryName = "comment-moderation-webhook"`).
  El operador puede grep el código para encontrar las keys válidas.

**Negativas:**

- **Discoverabilidad inicial**: si el operador escribe wrong key en
  PerChannel, el override silently no aplica. Mitigación: dev-time
  validation en un `IValidateOptions<WebhookResilienceSettings>` que
  warn si una key no matchea ningún FactoryName conocido (deferred).
- **Settings shape más complejo**: el JSON se vuelve más verboso. Trade
  vs precisión.

**Neutras:**

- 1 commit feat batch (Olas 157+158) + 1 docs ADR.
- 0 GUIDs nuevos.
- 0 NuGet packages nuevos.
- 0 schema rompedor.

## Implementation summary

| # | Foco |
|---|---|
| 157 | `WebhookResilienceSettings.PerChannel` dict + `WebhookResilienceChannelOverride` POCO con todos los campos nullable. |
| 158 | `WebhookResilienceExtensions.AddWebhookResilience` captura `builder.Name` y consulta override en runtime via IOptionsMonitor. |
| 0069 | (este) ADR consolidado |

## Próximas direcciones

- **`IValidateOptions<WebhookResilienceSettings>`** — warn al boot si
  un key en PerChannel no matchea ningún FactoryName registrado.
- **Per-channel telemetry**: dashboard panel que muestre latencias
  observadas per FactoryName, ayudando al operador a decidir qué
  canales necesitan tuning.

## References

- ADR 0052 — Polly retry initial introduction (Microsoft.Extensions.Http.Resilience).
- ADR 0057 — `WebhookResilienceSettings` POCO original (settings
  globales, read-once).
- ADR 0064 — Hot-reload via IOptionsMonitor (donde se introduce
  `WebhookResilienceExtensions` y se documenta el "Rule of Three"
  como deferred).
