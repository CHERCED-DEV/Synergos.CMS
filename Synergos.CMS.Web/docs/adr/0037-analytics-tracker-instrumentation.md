# ADR 0037 — Analytics tracker + instrumentación de 4 módulos transaccionales (Ola 67)

- **Status:** Accepted
- **Date:** 2026-04-25
- **Deciders:** Arquitecto + agente, durante Ola 67
- **Related:** ADR 0009 (extension seams), ADR 0028 (cart events
  candidatos), ADR 0030 (form events candidatos), ADR 0031 (search
  events candidatos), ADR 0034 (account events candidatos)

## Context

Tras Olas 60-65 (Forms internal + Search + SEO + Member self-service +
Email), todos los módulos transaccionales del CMS tenían tracking
parcial vía `ILogger` directo, pero sin convención compartida ni
schema de eventos consistente. Resultado:

- Operador no podía hacer "funnel del carrito" o "trending searches"
  sin parsear logs ad-hoc por cada controller.
- No había seam intercambiable para swap a Mixpanel/Segment/dashboard
  custom.
- Eventos importantes (form honeypot triggered, search con cero
  resultados, cart abandono) se mezclaban con logs de infra y se
  perdían entre el ruido.

## Decision

Introducir un seam `IAnalyticsTracker` simple + impl default sobre
`ILogger`, e instrumentar los 4 controllers transaccionales con un
naming convention estable.

### Seam (Ola 67.1)

**`Synergos.CMS.Interfaces/IAnalyticsTracker.cs`**:

```csharp
void Track(string eventName, IReadOnlyDictionary<string, object?>? properties = null);
```

Sync, fire-and-forget — el caller no espera ni maneja errores. Para
providers async (HTTP send), wrap en `Task.Run` o queue interno del
adapter.

**`LoggerAnalyticsTracker`** (default, Ola 67.1): emite cada evento
como `LogInformation` con scope `"Analytics"` y properties
serializadas como `key=value`. El operador agrega vía cualquier sink
estándar (Serilog console + sink Elastic/Loki/Application Insights).

Wire: `services.AddSingleton<IAnalyticsTracker, LoggerAnalyticsTracker>()`.

### Instrumentation (Ola 67.2)

11 evento slugs cubren los 4 módulos. **Naming convention**:
`module.action[-status]` en kebab-case dotted. Operadores filtran por
prefijo (ej. `Event: cart.*` para funnel completo del carrito).

| Módulo | Eventos |
|---|---|
| Search | `search.executed` (query, resultCount, docTypeFilter, elapsedMs), `search.no-results` (query) |
| Forms | `form.honeypot-triggered` (formKey), `form.rate-limited` (formKey), `form.submit-failed` (formKey, errorCode), `form.submitted` (formKey, fieldCount) |
| Shop | `cart.item-added` (sku, variantSku, quantity, cartItemCount), `cart.quantity-updated`, `cart.item-removed`, `cart.cleared` |
| Account | `account.login` + `account.login-failed` (errorCode), `account.registered` + `account.register-failed`, `account.logout` |

`Track` se invoca después de la operación principal — nunca antes
(eventos solo cuando hay confirmación de éxito/fracaso). PRG flow
preservado.

## Consequences

**Positivas:**

- **Funnels visibles**: filtros tipo `cart.*` o `account.*` agrupan
  el journey completo del visitante. "Cuántos `cart.item-added` →
  cuántos `cart.quantity-updated` → cuántos abandonaron" es trivial
  desde un dashboard que indexa los logs.
- **Top no-result queries**: filter `search.no-results` ordenado por
  count revela los queries que merecen contenido nuevo o sinónimos.
  Input directo para el equipo editorial.
- **Honeypot/rate-limit visibility**: `form.honeypot-triggered` y
  `form.rate-limited` antes solo logueaban como Info anónimo. Ahora
  son eventos auditables — el operador puede setear alerts si el
  rate sube.
- **Seam swap**: a Mixpanel se cambia el binding sin tocar los 4
  controllers. Naming convention persiste — el dashboard no se
  reconfigura.
- **Properties consistentes**: `errorCode` siempre del mismo set de
  slugs estables (definidos en `MemberAuthResult.ErrorCode`,
  `FormSubmissionResult.ErrorCode`). Dashboards pueden agrupar.

**Negativas:**

- **Sin user identity en los eventos**: `account.login` no incluye
  el memberKey/email del que se loguea. Decisión deliberada — los
  logs no deberían contener PII por default. Si se necesita
  user-attributed analytics, agregar parámetro opcional al `Track`
  + adapter que respete config de PII redaction.
- **Sin sampling**: bajo load alto, `search.executed` puede ser muy
  ruidoso. Agregar `IAnalyticsSampler` decorator si justifica.
  Diferido.
- **Sync write puede bloquear bajo sink lento**: si el `ILogger`
  sink (Elastic, AI) tiene backpressure, el `Track` espera. Mejorable
  con `IBackgroundEventQueue` + `IHostedService` consumidor.
  Diferido — `ILogger` es buffered por default en la mayoría de
  configuraciones.
- **String-typed event names**: typos no se detectan en compile time.
  Para hardening, generar `static class AnalyticsEvents` con
  consts. KISS no lo agregamos en el primer pase.
- **Cart abandono no tracked explícitamente**: requiere session
  tracking + timeout (visitante con `cart.item-added` que no llega
  a checkout en X horas). Diferido — necesita session storage que
  no tenemos.

**Neutras:**

- 1 seam + 1 default impl + 11 instrumentación points en 4 controllers.
  Cero schema editorial nuevo.
- `LoggerAnalyticsTracker` usa `LogInformation` (no Debug/Trace) —
  los eventos sobreviven el log level default de Production. Trade-off
  aceptable porque los eventos son señales operacionales, no debug
  spam.

## Alternatives considered

- **Acoplar consumidores directamente a `ILogger` con scope manual**.
  Se pierde la posibilidad de swap a Mixpanel y se mezcla con logs
  de infra. Rechazado.
- **Adoptar OpenTelemetry desde el primer pase**. Más estándar pero
  agrega NuGet (~5 paquetes) y requiere config explícita de
  exporters. Diferido — `IAnalyticsTracker` es interface-compatible
  con OTel (un futuro `OpenTelemetryAnalyticsTracker` adapter).
- **Adoptar SDKs de Mixpanel/Segment directo**. Premature, costo
  recurrente, vendor lock. Adapter en el seam permite cuando
  justifique.
- **Async API en el seam**. La mayoría de usos son fire-and-forget
  hot-path. Sync es simpler para el caller; los providers que
  necesitan async manejan internamente.
- **Generar consts de event names**. KISS — strings funcionan;
  consts agregan ceremonia sin valor para 11 eventos.

## Implementation summary (Ola 67, 3 commits)

| Commit | Hash | Foco |
|---|---|---|
| `feat(ola-67.1)` | `36d0ad4` | `IAnalyticsTracker` seam + `LoggerAnalyticsTracker` default + wire (Singleton) |
| `feat(ola-67.2)` | `674a23a` | Wire en 4 controllers — 11 evento slugs (search, form, cart, account) |
| `docs(ola-67.3)` | (este) | ADR 0037 + index README |

## References

- ADR 0009 — Extension seams (`IAnalyticsTracker` sigue el patrón)
- ADR 0028 — Shop runtime (cart events candidatos)
- ADR 0030 — Forms internal (form events candidatos)
- ADR 0031 — Search infrastructure (search events candidatos)
- ADR 0034 — Member self-service (account events candidatos)
