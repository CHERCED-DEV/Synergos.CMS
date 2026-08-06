# ADR 0062 — /healthz public probes (Olas 142-143)

- **Status:** Accepted
- **Date:** 2026-04-27
- **Deciders:** Arquitecto + agente, scope-amplio batch.
- **Consolida:** 2 olas en un único ADR.

## Context

Diferido §11.12 listaba "/healthz público para load-balancer probe".
Diferenciado del `/admin/health` member-gated (ADR 0058) que reporta
diagnostics ricos: uptime, memoria, runtime, seam counts, settings
activos. Para LB / orchestrator probes se necesita endpoint:

- **Público** (sin auth) — el LB no se loguea como member.
- **Minimal** — solo "ok" o "warming" + uptime, sin tocar DB ni IO.
- **Diferenciado liveness vs readiness** — k8s y los LB modernos
  hacen probes distintos.

## Decision

`HealthzController` nuevo en `Synergos.CMS.Web/Controllers/`:

### Ola 142 — `/healthz` (liveness)

```csharp
[HttpGet]
[AllowAnonymous]
public IActionResult Liveness()
{
    var uptimeSeconds = (int)(DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime()).TotalSeconds;
    return Ok(new { status = "ok", uptimeSeconds });
}
```

Siempre HTTP 200 si el proceso está vivo. No toca Umbraco ni DB. Usado
por **k8s livenessProbe** para detectar cuelgues fatales (deadlock,
OOM en spiral, etc.) — k8s reinicia el pod si falla.

### Ola 143 — `/healthz/ready` (readiness)

```csharp
[HttpGet("ready")]
[AllowAnonymous]
public IActionResult Readiness()
{
    var ready = _umbracoContextAccessor.TryGetUmbracoContext(out var ctx) && ctx.Content is not null;
    var uptimeSeconds = (int)(DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime()).TotalSeconds;
    if (!ready)
    {
        return StatusCode(503, new { status = "warming", uptimeSeconds });
    }
    return Ok(new { status = "ok", uptimeSeconds });
}
```

HTTP 200 si Umbraco context + content cache listos. HTTP 503 con
`status="warming"` durante boot (los primeros segundos hasta que el
published cache se popula desde DB). Usado por **k8s readinessProbe**
y **AWS ALB target group** para sacar la instancia del pool durante
deploy/restart hasta que esté lista para tráfico real.

JSON shape común: `{ "status": "ok|warming", "uptimeSeconds": N }`.

## Consequences

**Positivas:**

- **Zero-downtime deploys reales**: k8s no envía tráfico al pod nuevo
  hasta que `/healthz/ready` retorna 200. Antes (sin el endpoint) el
  primer request post-restart pegaba el race de localization (ADR 0059)
  o un Umbraco context warming.
- **Liveness independiente**: si el published cache se corrompe o un
  hosted service deadlock, el process sigue respondiendo a `/healthz`
  pero `/healthz/ready` falla — k8s saca la instancia del pool sin
  reiniciar (se puede investigar el estado).
- **Sin auth boundary**: el LB no necesita credenciales; los endpoints
  no exponen datos sensibles (solo uptime + status enum).

**Negativas:**

- **Liveness no detecta cuelgues internos**: si Umbraco está vivo
  pero todo content cache está corrompido, `/healthz` sigue OK. Por
  diseño — liveness es coarse-grained. `/healthz/ready` es donde se
  detectaría.
- **No incluye DB ping**: por simplicidad. Si DB se cae, el published
  cache sigue sirviendo desde memory hasta que un write falla. Para
  detectar DB-down explícito, agregar `/healthz/ready/db` futuro
  (deferred).
- **uptimeSeconds vs uptime monotonic**: usa wall-clock
  `Process.StartTime` que podría saltar si se ajusta el clock.
  Para precisión clock-monotonic usar `Environment.TickCount64`
  futuro.

**Neutras:**

- 1 commit feat batch (Olas 142+143 unificadas) + 1 docs ADR.
- 0 GUIDs nuevos.
- 0 NuGet packages nuevos.

## Implementation summary

| # | Foco |
|---|---|
| 142 | `HealthzController.Liveness` `GET /healthz` HTTP 200 siempre + JSON `{status:"ok", uptimeSeconds}`. |
| 143 | `HealthzController.Readiness` `GET /healthz/ready` HTTP 200 si Umbraco context+content listos; 503 con `status="warming"` durante boot. |
| 0062 | (este) ADR consolidado |

## Próximas direcciones

- **`/healthz/ready/db`**: DB ping explícito si llega requirement.
- **`Environment.TickCount64`** para uptime clock-monotonic.
- **Customizable disabled probe** via Settings flag — para entornos
  donde los probes públicos no se desean exponer.

## References

- ADR 0058 — `/admin/health` member-gated diagnostics endpoint
  (diferenciado).
- [Kubernetes Liveness, Readiness, Startup probes](https://kubernetes.io/docs/concepts/configuration/liveness-readiness-startup-probes/)
- ADR 0059 — bug Umbraco localization race (donde el readiness probe
  habría evitado servir el primer request antes de que el provider
  termine de cargar).
