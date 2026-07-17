# ADR 0072 — Webhook telemetry: per-channel latency observability (Olas 165-166)

- **Status:** Accepted
- **Date:** 2026-04-27
- **Deciders:** Arquitecto + agente, scope-amplio batch.
- **Consolida:** 2 olas en un único ADR.

## Context

ADR 0069 introdujo per-channel resilience tuning, pero el operador no
tenía datos para decidir QUÉ canales tunear. La próxima dirección
listada fue: *"per-channel telemetry: dashboard panel que muestre
latencias observadas per FactoryName"*.

Sin métricas, el operador adivina (Teams es lento porque "lo dice
el rumor"). Con métricas, observa P95 real → ajusta `AttemptTimeoutSeconds`
en `WebhookResilience.PerChannel.X` con base evidencia.

## Decision

### Ola 165 — `IWebhookTelemetryStore` seam

```csharp
public interface IWebhookTelemetryStore
{
    void RecordOutcome(string channelName, TimeSpan elapsed,
                       int statusCode, bool isSuccess);
    IReadOnlyList<ChannelTelemetrySnapshot> GetChannelStats();
}
```

`ChannelTelemetrySnapshot` record con TotalCalls, SuccessCount,
FailureCount, P50/P95/P99 LatencyMs, LastObservedUtc.

### Ola 166 — Implementation + handler

**`InMemoryWebhookTelemetryStore`**: Ring buffer per-canal con last
1000 samples. Thread-safe via per-channel `lock`. Reads computan
percentiles via copy + sort (O(N log N), N≤1000).

**`WebhookTelemetryHandler : DelegatingHandler`**: wraps cada outgoing
request con `Stopwatch`. Constructor inject `channelName` (closure
sobre el FactoryName del HttpClient) + `IWebhookTelemetryStore`.
Captura outcome incluso ante exceptions (catch + record + rethrow).

**Wiring** en `WebhookResilienceExtensions.AddWebhookResilience()`:

```csharp
builder.AddHttpMessageHandler(sp =>
    new WebhookTelemetryHandler(channelName,
        sp.GetRequiredService<IWebhookTelemetryStore>()));
builder.AddStandardResilienceHandler();
```

Telemetry handler se registra **antes** del resilience — captura
elapsed total INCLUYENDO retries internas del resilience pipeline.
Esto es clave: lo que el operador quiere medir es la latencia
user-facing real, no per-attempt.

### Health view extendida

`AdminController.Health` resuelve `IWebhookTelemetryStore` via
`[FromServices]` y popula `ViewData["WebhookTelemetry"]` con el
snapshot. `Health.cshtml` agrega panel "Webhook telemetry":

| Canal | Total | OK | Fail | P50 | P95 | P99 | Última call |

Coloreado por fail rate: `≥20%` rojo, `≥5%` amarillo, default neutral.

## Consequences

**Positivas:**

- **Decisiones de tuning basadas en evidencia**: operador ve P95 real
  per canal y ajusta `WebhookResilience.PerChannel.X` con datos.
- **Observabilidad en el dashboard**: sin necesidad de instalar
  DataDog/Prometheus/etc. para los outgoing webhooks. Los 12 canales
  tienen métricas built-in.
- **Cero overhead crítico**: ring buffer fixed-size, lock per-channel
  granular. Single record op es O(1).
- **Truly user-facing latency**: handler antes del resilience captura
  total (incluso si hubo retries).

**Negativas:**

- **Reset on restart**: stats no persisten. Para histórico real, swap
  por adapter sobre time-series store (Influx/Prometheus/etc.) — fuera
  del scope.
- **Ring buffer 1000 samples**: para canales muy activos (>1k
  requests/min), las stats reflejan solo los últimos minutos. Aceptable
  — para SLO long-tail tracking, usar herramienta externa.
- **Percentile computation O(N log N)** por GetChannelStats call: con
  N=1000 + 12 canales = 12k ops. Trivial.
- **No persists across restart**: si el host reinicia, las decisiones
  basadas en últimas N=1000 samples se pierden. Mitigación: swap a
  persistente cuando llegue el requirement.

**Neutras:**

- 1 commit feat batch (Olas 165+166) + 1 docs ADR.
- 0 GUIDs nuevos.
- 0 NuGet packages nuevos.

## Implementation summary

| # | Foco |
|---|---|
| 165 | `IWebhookTelemetryStore` seam + `ChannelTelemetrySnapshot` record. |
| 166 | `InMemoryWebhookTelemetryStore` ring buffer impl + `WebhookTelemetryHandler` DelegatingHandler + wired en `WebhookResilienceExtensions` ANTES del resilience handler + Health view panel con fail rate coloring. |
| 0072 | (este) ADR consolidado |

## Próximas direcciones

- **Time-series store adapter**: Influx / Prometheus / DataDog para
  histórico persistente.
- **Telemetry alerts**: si fail rate > X% en últimos N minutos, notify.
  Vincula al composite notifier de cart abandonment para reuse.
- **Per-attempt vs per-call mode**: opcional swap de orden de
  handlers para capturar también per-attempt (útil para diagnose
  de retries).

## References

- ADR 0064 — Hot-reload WebhookResilience via IOptionsMonitor.
- ADR 0069 — Per-channel WebhookResilience tuning (telemetry was
  the deferred direction).
- ADR 0058 — `/admin/health` endpoint donde se renderiza el panel.
