# ADR 0070 — Resilience config validation + audit retention sweep (Olas 161-162)

- **Status:** Accepted
- **Date:** 2026-04-27
- **Deciders:** Arquitecto + agente, scope-amplio batch.
- **Consolida:** 2 olas en un único ADR.

## Context

Tras cap-160 cerrado (ADRs 0066-0069) con `WebhookResilienceSettings.PerChannel`
y `IAuditTrailWriter` file-based, 2 próximas direcciones explícitas
quedaban pendientes:

- **PerChannel validation**: typos en keys del dict no fallaban —
  silently no aplicaban el override. ADR 0069 listó esto como
  "deferred via `IValidateOptions`".
- **Audit retention**: `App_Data/syn-audit/{yyyy-MM-dd}.jsonl` crecía
  indefinidamente. ADR 0067 listó "retention automático via hosted
  service" como deferred.

## Decision

### Ola 161 — `WebhookResilienceSettingsValidator`

```csharp
public sealed class WebhookResilienceSettingsValidator
    : IValidateOptions<WebhookResilienceSettings>
{
    private static readonly IReadOnlySet<string> KnownFactoryNames = ...;
    public ValidateOptionsResult Validate(string? name, WebhookResilienceSettings options)
    {
        foreach (var key in options.PerChannel.Keys)
        {
            if (!KnownFactoryNames.Contains(key))
            {
                _logger.LogWarning("PerChannel key '{Key}' does not match...", key);
            }
        }
        return ValidateOptionsResult.Success;
    }
}
```

Set estático con los 12 FactoryNames conocidos (3 dominios × 4
plataformas). Validate retorna Success siempre — un typo no es fatal,
solo loguea warning para que el operador lo vea en startup logs.

Validación se invoca por DI options pipeline en el primer
`IOptions.Get` y también cada vez que `IOptionsMonitor.OnChange`
dispara (hot-reload propaga warning sobre typos nuevos).

### Ola 162 — `AuditRetentionHostedService`

```csharp
public sealed class AuditRetentionHostedService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            Sweep();
            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }
}
```

`Sweep` itera `App_Data/syn-audit/*.jsonl`, parsea filename como
`yyyy-MM-dd`, borra si más viejo que cutoff (`AuditRetentionDays`
de `AdminSettings`).

Default `AuditRetentionDays = 90`. Setting `0` desactiva el sweep
completamente (operador gestiona retention manual).

Idempotent — solo borra archivos cuyo nombre matchea el formato
esperado. Files editados a mano o con nombres atípicos se ignoran.

Wired en `SeamComposer` con `AddHostedService<...>()`.

## Consequences

**Positivas:**

- **Discoverabilidad de typos PerChannel**: warning visible en startup
  logs evita que el operador descubre meses después que su override
  no estaba aplicando.
- **Retention automatizada**: archivos JSONL no crecen indefinidamente.
  90 días default cubre la mayoría de forensic review windows.
- **Sweeps al boot + cada 24h**: si el process corre 24/7, el sweep
  diario cubre. Si reinicia frecuente, el boot sweep aún limpia.
- **Cero data loss accidental**: si se setea retention=0 (default-safe
  variant), el sweep no toca nada.

**Negativas:**

- **Validate solo warns, no falla**: si el operador prefiere strict-mode
  (fail boot si typo detectado), agregar setting futuro
  `WebhookResilienceStrict=true`.
- **Retention granularity día**: archivos < 1 día no se purgan
  parcialmente. Aceptable.
- **Sweep síncrono**: si hay > 10k archivos, el sweep bloquea ~segundos.
  Para scale, swap por adapter sobre indice DB.

**Neutras:**

- 1 commit feat batch (Olas 161+162) + 1 docs ADR.
- 0 GUIDs nuevos.
- 0 NuGet packages nuevos.

## Implementation summary

| # | Foco |
|---|---|
| 161 | `WebhookResilienceSettingsValidator` con set estático de 12 FactoryNames + warning per key inválida. |
| 162 | `AdminSettings.AuditRetentionDays` + `AuditRetentionHostedService` sweep cada 24h en `SeamComposer.AddHostedService`. |
| 0070 | (este) ADR consolidado |

## Próximas direcciones

- **Strict mode**: setting `WebhookResilienceStrict=true` que falla
  boot si typo detected. Útil para CI/CD validation pipelines.
- **Audit retention cold-storage**: en lugar de `File.Delete`, mover
  archivos viejos a `App_Data/syn-audit-archive/{yyyy}/`.

## References

- ADR 0067 — IAuditTrailWriter (deferred retention listado).
- ADR 0069 — Per-channel resilience tuning (deferred validation listado).
