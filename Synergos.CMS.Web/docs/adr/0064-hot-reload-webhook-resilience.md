# ADR 0064 — Hot-reload WebhookResilience via IOptionsMonitor (Olas 146-147)

- **Status:** Accepted
- **Date:** 2026-04-27
- **Deciders:** Arquitecto + agente, scope-amplio batch.
- **Consolida:** 2 olas en un único ADR.

## Context

ADR 0057 introdujo `WebhookResilienceSettings` (4 keys: MaxRetryAttempts
+ AttemptTimeoutSeconds + TotalRequestTimeoutSeconds + RetryBaseDelayMs)
con un trade-off explícito: settings se leen al boot, **no hot-reload**.
Diferido §11.12 listaba "Hot-reload de WebhookResilience settings via
IOptionsMonitor + pipeline rebuild" como item dentro del cap acotado
a Ola 150.

El bug que motivó esto: si un sysadmin observa telemetría que muestra
webhooks de receivers lentos, el knob (subir AttemptTimeout) requería
restart del proceso para tomar efecto. Restart de un host CMS produce
~30s de downtime (warming del published cache, ADRs 0059 + 0062) — fricción
operacional para tuning rutinario.

## Decision

### Diseño hot-reload sin pipeline rebuild manual

Patrón:

1. `WebhookResilienceSettings` registrado via
   `services.AddOptions<...>().Bind(config.GetSection(...))` —
   automáticamente registra `IConfigurationChangeTokenSource`. Cuando
   `appsettings.json` cambia, `IOptionsMonitor<WebhookResilienceSettings>`
   dispara `OnChange`.
2. Resilience handler usa
   `Configure<IOptionsMonitor<WebhookResilienceSettings>>` (no
   `Action<HttpStandardResilienceOptions>` plain) — este overload
   registra `ConfigureNamedOptions<HttpStandardResilienceOptions,
   IOptionsMonitor<WebhookResilienceSettings>>` que re-ejecuta el
   callback cada vez que el dependency cambia.
3. `HttpClientFactory` rotates handlers con default lifetime 2min.
   Cuando el siguiente handler rotation ocurre, la pipeline se
   construye con los valores frescos.

**Latencia**: cambio de settings → next handler rotation (~2min).
Aceptable — tuning de retries/timeouts no es escenario hot-path donde
2min sea unacceptable.

### WebhookResilienceExtensions helper

```csharp
internal static class WebhookResilienceExtensions
{
    public static IHttpClientBuilder AddWebhookResilience(this IHttpClientBuilder builder)
    {
        var pipeline = builder.AddStandardResilienceHandler();
        pipeline.Services
            .AddOptions<HttpStandardResilienceOptions>(pipeline.PipelineName)
            .Configure<IOptionsMonitor<WebhookResilienceSettings>>((opts, monitor) =>
            {
                var s = monitor.CurrentValue;
                opts.Retry.MaxRetryAttempts = s.MaxRetryAttempts;
                opts.Retry.Delay = TimeSpan.FromMilliseconds(s.RetryBaseDelayMs);
                opts.AttemptTimeout.Timeout = TimeSpan.FromSeconds(s.AttemptTimeoutSeconds);
                opts.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(s.TotalRequestTimeoutSeconds);
            });
        return builder;
    }
}
```

`SeamComposer` antes:

```csharp
services.AddHttpClient(X.FactoryName).AddStandardResilienceHandler(ConfigureWebhookResilience);
```

`SeamComposer` ahora (12 named clients):

```csharp
services.AddHttpClient(X.FactoryName).AddWebhookResilience();
```

Bind anterior `var resilienceSettings = builder.Config.GetSection(...).Get<>()`
removido — ahora bind via `services.AddOptions<WebhookResilienceSettings>().Bind(...)`.

## Consequences

**Positivas:**

- **Tuning sin restart**: sysadmin edita `appsettings.json`, espera
  ~2min, los nuevos settings entran al pipeline. Útil cuando se
  observa telemetría real (e.g., webhook receiver lento → subir
  AttemptTimeout). Bajo carga normal el pipeline reload es transparente.
- **Misma surface API**: el helper `.AddWebhookResilience()` mantiene
  el contrato existente — los 12 call sites son refactor mecánico.
- **Cero overhead runtime**: `Configure<TDep>` es una factory que se
  invoca cuando se construye el handler, no por request.

**Negativas:**

- **Latencia ~2min**: si el LB consume `appsettings.json` reload pero
  los handlers de pipeline están aún cached, requests durante la
  ventana usan los valores viejos. Aceptable — no hay race ni
  inconsistencia, solo eventual consistency.
- **No hot-reload "instant"**: si llega requirement de tuning
  sub-second, esto no es suficiente. Pattern futuro: emitir un
  `IOptionsChangeTokenSource` con un short-lived token que force
  rotation, o usar un custom handler que lee `IOptionsMonitor` per
  request (mucho más complejo).
- **Settings shared all 12 channels**: el helper no permite per-channel
  tuning. Pattern Rule of Three: cuando un canal específico necesite
  tuning distinto, swap por dictionary indexed by FactoryName.

**Neutras:**

- 1 commit feat batch (Olas 146+147 unificadas) + 1 docs ADR.
- 0 GUIDs nuevos.
- 0 NuGet packages nuevos.
- 0 schema rompedor.

## Implementation summary

| # | Foco |
|---|---|
| 146 | `WebhookResilienceSettings` registered via `AddOptions().Bind()` con auto change-token. |
| 147 | `WebhookResilienceExtensions.AddWebhookResilience()` helper + 12 call sites refactored en `SeamComposer`. Removed `ConfigureWebhookResilience` local action y resilienceSettings cached. |
| 0064 | (este) ADR consolidado |

## Próximas direcciones

- **Per-channel tuning** via dictionary indexed by FactoryName (Rule
  of Three — espera 3+ canales con requirements distintos).
- **Instant hot-reload** si llega requirement de < 5s response time
  para settings change — patrón complejo (custom handler reading
  monitor per request).
- **Telemetry-driven auto-tune**: monitor latencia real per channel
  y ajustar settings dinámicamente. Overkill para hoy.

## References

- ADR 0052 — Polly retry via Microsoft.Extensions.Http.Resilience
  (donde se introduce el package + 12 named clients).
- ADR 0057 — WebhookResilienceSettings introduced (read-once trade-off
  documentado, ahora superseded por este ADR para hot-reload).
- [Microsoft.Extensions.Http.Resilience docs](https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience)
- [`IOptionsMonitor<T>` change tokens](https://learn.microsoft.com/en-us/dotnet/core/extensions/options#ioptionsmonitor)
