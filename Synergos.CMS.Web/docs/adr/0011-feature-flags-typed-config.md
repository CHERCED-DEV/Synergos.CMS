# ADR 0011 — Feature flags vía typed config, sin servicio externo

- **Status:** Accepted
- **Date:** 2026-04-18
- **Deciders:** Project owner
- **Source:** promoted from `refactor-docs/adr-drafts/0011-feature-flags-typed-config.md`

## Context

El producto necesita feature gates para rutas y lógica opt-in. La
trampa clásica es adoptar un SaaS de flags (LaunchDarkly, ConfigCat,
Azure App Configuration) antes de tener la primera bandera real.

## Decision

- Las feature flags del core viven en `appsettings.*.json`, bajo el
  prefijo `Synergos:FeatureFlags`, bound a `FeatureFlagsSettings`
  en `Synergos.CMS.Application/Configuration/`.
- El contrato de consulta es el seam `IFeatureGate`:

```csharp
public interface IFeatureGate
{
    bool IsEnabled(string gate);
}
```

- La implementación por defecto es `AppsettingsFeatureGate` en
  `Synergos.CMS.Application/Services/Impl/`, que consulta
  `IOptions<FeatureFlagsSettings>`.
- Los nombres de flags son **constantes tipadas** en
  `Synergos.CMS.Application/Dto/Constants/FeatureGateKeys.cs` —
  no strings sueltos.
- El `FeatureGateMiddleware` sólo se implementa **cuando exista al
  menos una flag real con consumer probado** (Ola 9 del plan de
  migración). No se despliega middleware sin consumidor.

## Consequences

**Positive**

- Cero runtime dependencies adicionales.
- Tests inyectan un `IOptions<FeatureFlagsSettings>` sintético.
- Cambio de flag = deploy estándar; no hay consola externa.

**Negative**

- No hay rollout gradual, targeting por usuario, ni A/B.
- Cambio de flag requiere redeploy (o reload de config si se habilita
  `IOptionsSnapshot`).

## Escalado futuro

Si el producto demanda rollout gradual o targeting avanzado, se
escribe un ADR sucesor que introduzca un provider adicional. Ese
provider **se registra como `IFeatureGate` alternativo** sin tocar
consumidores. Por eso el seam queda desde el día 1.

## Alternatives considered

- **Adoptar SaaS desde el inicio** — rechazado por YAGNI.
- **Flags en tabla DB custom** — rechazado: migraciones sin beneficio
  frente a appsettings.
