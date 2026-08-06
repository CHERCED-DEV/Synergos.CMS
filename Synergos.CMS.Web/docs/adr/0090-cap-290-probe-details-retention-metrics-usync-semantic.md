# ADR 0090 — Cap-290: Probe details + retention metrics + uSync semantic check (Olas 292-295)

- **Status:** Accepted
- **Date:** 2026-04-29
- **Deciders:** Arquitecto + agente.

## Context

Cap-290 ataca tres items low-effort que cap-280 dejó en el listado de
diferidos §11.25 / §11.24, alineados con el constraint operativo
KISS / 0-deps / 1-dev:

1. `BundleRegistrySettings.ProbeTag` override — el probe del cap-280
   tiene `synergos-column` hardcoded; CDNs custom necesitan override.
2. `SchemaHealthResult.Details` extension — los probes hoy reportan
   solo `Name + IsHealthy + Message`. Ops dashboards quieren
   metadata estructurada (mode, version, framework, integrity, ...).
3. Retention metrics — el `RetentionSweepHostedService` solo loguea;
   no hay forma de medir liveness ni dashboardear runs.
4. uSync `Definition` GUID cross-check — Definition rota = property
   silenciosamente sin editor en backoffice. Audit harness lo
   detectaba como missing-composition-ref pero NO como missing-
   datatype-definition.

Sin nuevas dependencies. Cero NuGet, cero npm.

## Decision

### Batch A — Olas 292-293 — Probe details + ProbeTag override

**`SchemaHealthResult` extendido** (Synergos.CMS.Interfaces) con campo
opcional `Details: IReadOnlyDictionary<string, object?>?`:

```csharp
public sealed record SchemaHealthResult(
    string Name,
    bool IsHealthy,
    string? Message = null,
    IReadOnlyDictionary<string, object?>? Details = null);
```

Backward compatible — `Details` al final del record con default null,
los call sites existentes (`SchemaVersionProbe`, `UsyncFolderProbe`,
`HealthControllerTests`) compilan sin cambios.

**`BundleRegistrySettings.ProbeTag`** (Synergos.CMS.Application):
- Default `"synergos-column"` (primer primitive del catalog Synergos).
- Override útil para CDNs custom que no exponen ese tag.

**`BundleRegistryProbe`** ahora:
- Lee `s.ProbeTag` con fallback al default si vacío.
- Emite `Details` con shape distinto por verdict:
  - **Stub**: `{ mode: "Stub" }`.
  - **FileSystem healthy**: `{ mode, probeTag, framework, version,
    integrity: "present"|"missing", resolved: true }`.
  - **FileSystem unhealthy (null descriptor)**: `{ mode, probeTag,
    localPath, bundlesNamespace, resolved: false }`.
  - **FileSystem unhealthy (exception)**: `{ mode, probeTag,
    exception: typeName }`.
  - **Unknown mode**: `{ mode: <literal> }`.

**`HealthController`** proyecta `details = r.Details` en el JSON
response del `/_health` endpoint. Cuando el probe no provee Details,
el field aparece como `null` (acceptable — clients que no consumen
metadata simplemente lo ignoran).

**3 tests** en `BundleRegistryProbeTests`:
- `StubMode_*` extendido con assert `Details["mode"]="Stub"`.
- `FileSystemMode_DescriptorResolved_*` extendido con asserts del
  shape completo.
- `FileSystemMode_ProbeTagOverride_UsesConfiguredTag` (nuevo) verifica
  que el probe llama `TryResolveAsync` con el ProbeTag configurado.

### Batch B — Ola 294 — Retention metrics via IAnalyticsTracker

**Decisión de seam**: el deferred §11.24 sugería emit a
`IWebhookTelemetryStore`, pero ese seam tiene shape específico para
outgoing webhook calls (`channelName + statusCode + elapsed`). El
shape correcto para retention metrics es fire-and-forget event
tracking — `IAnalyticsTracker` (ADR 0037) ya lo provee.

**`RetentionSweepHostedService`** ahora inyecta `IAnalyticsTracker` y
emite eventos estructurados por policy invocation:

- **`"retention.swept"`** en success — properties: `policy`, `purged`,
  `durationMs`, `success: true`. Emitido **siempre** (incluso
  `purged=0`) para visibilidad de liveness — el operador confirma que
  el sweep está corriendo aún cuando no hay nada que purgar.
- **`"retention.failed"`** en exception — properties: `policy`,
  `exception` (type name), `message`, `durationMs`, `success: false`.
  Excluye `OperationCanceledException` que se propaga al
  `BackgroundService` para shutdown limpio.

**`RunSinglePolicyAsync`** extraído como método `internal` para
testabilidad (`InternalsVisibleTo Tests` ya cableado desde caps
anteriores). El infinite loop del `ExecuteAsync` no es testable
directamente.

**4 tests** en `RetentionSweepHostedServiceTests`:
- success emite `retention.swept` con purged > 0.
- zero-purged emite `retention.swept` igual (liveness).
- exception emite `retention.failed` y NO emite `retention.swept`.
- `OperationCanceledException` propaga sin emit metric.

El sink default `LoggerAnalyticsTracker` emite log estructurado
consumible por Serilog/AI/Elastic. Operador con dashboard de eventos
puede swappear por `MixpanelAnalyticsTracker` /
`SegmentAnalyticsTracker` sin tocar el service (ADR 0037).

### Batch C — Ola 295 — uSync `Definition` GUID cross-check

**`tools/usync-audit.mjs`** check #6 (vanilla Node, 0 npm deps):

Cada `<Definition>{guid}</Definition>` en `<GenericProperty>` de un
ContentType debe matchear el `<DataType Key="{guid}">` de un DataType
file. Si la GUID referenciada no existe en `uSync/v9/DataTypes/`, es
un error.

Implementación: build set de `dataTypeKeys` lowercase escaneando los
DataType files una vez, luego iterar ContentType files y matchear
cada `<Definition>` contra el set. Errors → exit 1 (CI gating ya
existente desde cap-270 Batch C).

**Verificación contra estado actual**: 223 ContentTypes cross-check
contra 104 DataTypes. **0 errors / 0 warnings**. La GitHub Action
existente (`.github/workflows/usync-audit.yml`) gateéa cualquier
regresión en PRs futuros.

### Olas 296-297 — Cierre

Este ADR + actualización current-state §11.26.

## Consequences

**Positivas:**

- **Probes ricos**: ops dashboards pueden parsear `details` para
  alerting estructurado (ej. alert si `bundle_registry.details.resolved
  = false`), sin tener que hacer regex sobre Message.
- **CDN customizable**: el operador puede setear `ProbeTag` para CDNs
  que no exponen `synergos-column`. La memoria
  `feedback_cdn_contract_consumed` queda satisfecha.
- **Retention liveness**: el operador ahora sabe que las 4 policies
  corren cada 24h aún cuando no hay nada que purgar. Si una policy
  deja de aparecer en el log de eventos, pasa silenciosamente —
  ahora el dashboard lo flag.
- **Schema fail-fast**: `Definition` GUID rota se atrapa en CI antes
  de que el editor descubra que un campo no carga. Antes solo se
  detectaba en QA manual del backoffice.
- **0 dependencies nuevas**: ni NuGets, ni npm, ni servicios externos.

**Negativas:**

- **`SchemaHealthResult.Details` es un dict heterogéneo**: keys/values
  no tienen schema. Si los probes empiezan a emitir shapes
  divergentes para el mismo verdict, el dashboard se complica.
  Mitigation: documentar el shape per-probe (ya hay XmlDoc en
  `BundleRegistryProbe`); si crece más, considerar typed records
  per-probe via discriminated union.
- **`retention.swept` con purged=0 emite SIEMPRE**: si el operador no
  filtra, el log se inunda con "swept 0 items". El `LoggerAnalyticsTracker`
  default emite a `ILogger.LogInformation` — el sink filtra según
  log level del operador. Mitigation: setting futuro
  `RetentionSettings.EmitZeroPurgedEvents` (default true).
- **Definition GUID check no detecta DataType orphans**: un DataType
  definido pero nunca referenciado pasa silencioso. Es deliberado —
  algunos DataTypes son scaffolding (memoria
  `feedback_reserved_compositions_marker` aplica también a DataTypes).
  Mitigation futura: mismo marker convention si se necesita.

**Neutras:**

- 4 commits feat/feat/feat + 1 ADR + 1 current-state.
- 0 NuGet packages nuevos.
- 0 npm packages nuevos.
- 0 GUIDs nuevos, 0 schema rompedor.
- Tests: 224 → 232 (**+8**: 1 ProbeTag override + 4 retention metrics
  + 3 probe Details enrichment del existing).
- 1 audit check nuevo (#6 Definition GUID).

## Implementation summary

| # | Foco | Commit |
|---|---|---|
| 292-293 | Probe Details + ProbeTag override + HealthController projection + 3 tests | `8ee3b86` |
| 294 | Retention metrics via IAnalyticsTracker + 4 tests | `adc2199` |
| 295 | uSync audit check #6 — Definition GUID cross-check | `e471365` |
| 296-297 | (este) ADR + current-state §11.26 |

## Próximas direcciones

Items que podrían atacarse en caps futuros:

- **`HttpBundleRegistryClient`** sigue bloqueado externamente (CDN
  team debe publicar los 5 puntos del `docs/umbraco/cdn-contract.md`).
- **Typed `SchemaHealthResult.Details`** per-probe via discriminated
  union si el dict heterogéneo dificulta consumo. Hoy 1 probe lo
  emite — diferido hasta tener 3+ probes con shapes estables.
- **`FrameworkOverrides`** Dictionary alias→framework si llega
  requirement multi-framework concurrente (Cap-280 deferred).
- **DataType orphan detection** en audit harness con marker convention.
- **uSync export hygiene check** (mojibake doble PowerShell trap,
  memoria `feedback_powershell_utf8_bulk_edits`).
- **uSync filename casing inconsistente** — cosmetic only, diferido.

Items §11.24 vigentes (operador KISS):

- DB-backed comment repository / audit trail / 2FA challenge cache —
  out of scope hasta scale real.
- Time-series store adapter webhook telemetry — idem.
- Snapshot tests fixture library — diferido hasta tests >300.

## References

- ADR 0009 — Extension seams mandatory.
- ADR 0037 — `IAnalyticsTracker` contract.
- ADR 0070 — Audit retention sweep (origen del pattern).
- ADR 0088 — Cap-270 (origen de `IRetentionPolicy` + audit harness).
- ADR 0089 — Cap-280 (origen de `BundleRegistryProbe`).
- `tools/usync-audit.mjs` — script source.
- `feedback_cdn_contract_consumed` (memoria).
