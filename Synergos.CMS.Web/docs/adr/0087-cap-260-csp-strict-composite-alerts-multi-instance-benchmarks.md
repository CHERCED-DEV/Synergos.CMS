# ADR 0087 — Cap-260: CSP-strict + composite alert notifiers + multi-instance encryption + benchmarks (Olas 251-262)

- **Status:** Accepted
- **Date:** 2026-04-28
- **Deciders:** Arquitecto + agente.

## Context

Cap-250 cerró 4 deferred items concretos pero quedaron 10 más en
§11.22, agrupables como "hardening + scale prep". Cap-260 ataca los
4 más maduros y de mayor user-value:

1. **CSP-strict mode** — el bridge inline `<script>window.synergos =
   {...}</script>` requería `'unsafe-inline'` en `script-src`. Sites
   con CSP estricto no podían adoptar el bridge sin relajar policy.
2. **Composite notifier para alerts** — el scanner de telemetría
   solo notificaba via email. Comments / Forms / Cart ya tenían 4
   canales (email + slack + discord + teams + webhook); alerts
   quedaba afuera del pattern (Rule of Three trigger).
3. **2FA multi-instance encryption-at-rest** — los 2FA secrets
   cifrados con `IDataProtectionProvider` per-instance NO se
   descifran en pods adyacentes. Bloqueador de scale-out horizontal.
4. **Performance benchmarks** — sin baseline para detectar
   regresiones futuras (e.g. accidental switch del HMAC algorithm
   o crecimiento descontrolado del bridge payload).

## Decision

### Batch A — Olas 251-253 — CSP-strict mode

**`HostBridgeSettings.CspStrictMode`** (default `false`) flag que
flipea el comportamiento del partial `_SynergosBridge.cshtml`:

- `false` (default, back-compat): `<script>window.synergos = {...}</script>`
  inline (require `'unsafe-inline'`).
- `true`: `<script src="/synergos-bridge.js"></script>` — CSP-friendly
  bajo `script-src 'self'`.

**`SynergosBridgeController`** sirve el endpoint:

- `GET /synergos-bridge.js` → `application/javascript`.
- Body: `window.synergos = {...}; window.synergos.i18n.t = function(){...};`
- `Cache-Control: private, no-store` — el payload contiene member /
  theme / page context que varía. Compartirlo entre users sería un
  privacy leak.
- Failure path: si `IHostBridgeContextBuilder.Build()` lanza, sirve
  shape mínimo (mismo fallback del partial inline) — UI no rompe.

**Trade-off**: 1 RTT extra por page load (eliminada por HTTP cache
del navegador via cookies/sesión iguales a la página). Eliminamos la
necesidad de relajar CSP. Pago aceptable.

**Tests (5)**:
- HappyPath_ReturnsJsContentType.
- HappyPath_BodyAssignsWindowSynergos (verifies `window.synergos = ` +
  version + culture + i18n key + helper t()).
- HappyPath_SetsCacheControlPrivateNoStore.
- BuilderThrows_ReturnsFallbackPayload.
- SerializedKeysPreserveExoticCharacters (escapes Unicode `"`
  para comillas internas — `JsonSerializer.Default` safe encoder).

### Batch B — Olas 254-256 — Composite IAlertNotifier

**Cuarta familia de composite notifiers** paralela a Comments / Forms
/ Cart. El `WebhookTelemetryAlertScanner` ahora inyecta
`IAlertNotifier` (composite) en lugar de `IEmailService` directo.

**Seam** (`Synergos.CMS.Interfaces/IAlertNotifier.cs`):

- `IAlertNotifier.NotifyAlertAsync(WebhookAlertEvent, ct)` y
  `NotifyRecoveryAsync(WebhookRecoveryEvent, ct)` separados —
  cada canal puede formatear distinto (rojo alert / verde recovery).
- `IAlertNotifierChannel` marker per-canal individual.
- `WebhookAlertEvent` record (channelName, failRate, threshold,
  totalCalls, success/failure, p50/p95/p99 latency, lastObserved,
  cooldownMinutes).
- `WebhookRecoveryEvent` record (channelName, current/prior failRate,
  threshold, alertingDuration, firstFiredUtc, lastFiredUtc, calls).

**Channels** (`Synergos.CMS.Web/Services/`):

- `CompositeAlertNotifier` — itera `IEnumerable<IAlertNotifierChannel>`
  con try-catch per-canal (canal roto no afecta otros).
- `EmailAlertNotifier` — HTML body inline (tablas con métricas);
  copy movido del scanner viejo.
- `SlackAlertNotifier` — Block Kit con header + section + context;
  🚨 alert / ✅ recovery emojis.
- `DiscordAlertNotifier` — embeds con color rojo (`0xdc2626`) alert /
  verde (`0x22c55e`) recovery + fields P50/P95/P99 inline.
- `TeamsAlertNotifier` — Adaptive Card 1.4 con color `Attention`
  alert / `Good` recovery + FactSet métricas.
- `WebhookAlertNotifier` — raw JSON POST con Bearer auth +
  HMAC-SHA256 signature opcional via `WebhookSigner` shared helper.

`WebhookTelemetryAlertSettings` extiende con `WebhookUrl` +
`WebhookBearerToken` + `WebhookHmacSecret` + `SlackWebhookUrl` +
`DiscordWebhookUrl` + `TeamsWebhookUrl`. Cada canal opt-in por URL
configurada — vacío = no-op silencioso.

**Tests (4)**: `CompositeAlert_DispatchesAlert/Recovery/BrokenChannel/
EmptyChannels` — paralelo a los tests existentes de Comments/Forms/Cart.

**Refactor del scanner**: 10 tests existentes refactorizados para
asertar contra `IAlertNotifier.NotifyAlertAsync` /
`NotifyRecoveryAsync` con `Arg.Is<WebhookAlertEvent>(...)` /
`<WebhookRecoveryEvent>(...)` shape verification.

### Batch C — Olas 257-258 — Data Protection multi-instance

**`DataProtectionSettings`** POCO (sección `Synergos:DataProtection`):

- `KeyringPath` (default vacío) — path absoluto a directorio
  compartido. Vacío = no override (preserva default ASP.NET Core
  per-instance).
- `ApplicationName` (default `"Synergos.CMS"`) — discriminator.
- `KeyLifetimeDays` (default 90) — auto-rotation window.

**`DataProtectionComposer`**:

- `[ComposeAfter(typeof(OptionsComposer))]` para garantizar binding.
- Lee config directo (no via DI — `AddDataProtection` corre durante
  boot, antes de que el provider exista).
- Si `KeyringPath` vacío → NO-OP (back-compat).
- Si poblado → `services.AddDataProtection().PersistKeysToFileSystem(
  new DirectoryInfo(path)).SetApplicationName(name)
  .SetDefaultKeyLifetime(TimeSpan.FromDays(days))`.
- `Directory.CreateDirectory(path)` tolerante para containers donde
  el bind mount monta vacío.

**Tests (4)**: `DataProtectionSettingsTests` — defaults + init-only
override. Composer wiring se verifica implícitamente por los tests
existentes de `FileSystemMemberTwoFactorStoreTests` que usan
`UseEphemeralDataProtectionProvider`.

**Decisión**: FileSystem-based shared keyring es la opción más
común para deployments containerized (bind mount o volume
compartido). Redis y Azure Blob Storage requieren paquetes extra +
identity setup — documentados como next-step pero no shipped.

### Batch D — Olas 259-260 — BenchmarkDotNet harness

**Nuevo proyecto** `Synergos.CMS.Benchmarks` (Exe, net8.0):

- `Program.cs`: `BenchmarkSwitcher.FromAssembly(...).Run(args)` —
  soporta `--filter` glob para subset.
- `WebhookSignerBenchmarks` (4 cases): 200B / 2KB / 20KB / null secret
  no-op. Baseline para HMAC-SHA256 signing path (corre en 4 sites
  outbound).
- `BridgeContextSerializerBenchmarks` (4 cases): anon / 50-key
  typical / 369-key full catalog / utf8 bytes path. Baseline para
  serialización per-request del bridge.

**Decisiones**:
- `BenchmarkDotNet` 0.15.8 (verificado nuget.org).
- `[ShortRunJob]` + `[MemoryDiagnoser]` por default para iteration
  rápida — el operador puede swap a `[SimpleJob]` para datos más
  estables.
- `NoWarn CA1707` en csproj — BenchmarkDotNet usa underscores
  convencionalmente en method names (legible en reports).
- Output a `BenchmarkDotNet.Artifacts/` (gitignored local + global).
- README con run instructions + listing actual + targets candidatos.

**Targets candidatos deferred**: `RecoveryCodesHelper` PBKDF2,
`FileSystemAuditTrailWriter` JSONL append, `FileSystemMemberTwoFactorStore`
encrypt+write, `IBundleRegistryClient` lookup.

### Olas 261-262 — Cierre

Este ADR + actualización current-state §11.23 + memory.

## Consequences

**Positivas:**

- **CSP-strict adoptable**: sites con CSP estricto pueden flipear
  un flag y ya no necesitan `'unsafe-inline'`. Mejora postura security
  sin breaking change.
- **Alerts a 5 canales**: el operador puede notificar alerts a
  cualquier combo de email + slack + discord + teams + webhook. Cada
  canal configurable independientemente. Pattern reutilizado del que
  Comments/Forms/Cart ya validaron en producción (ADRs 0047/0049/0050/
  0052/0057).
- **Multi-instance scale unblocker**: el principal técnico-bloqueador
  de scale-out horizontal (2FA secrets per-instance) está resuelto
  con un toggle de config. No requiere code change, solo bind mount
  en deployment.
- **Regresiones detectables**: 2 benchmark suites baseline que el
  team puede correr antes de cap futuros para detectar si performance
  empeoró. Pattern para extender.
- **§11.22 deferred items cerrados** (4/10): cap-260 deja la lista
  con 6 items, todos requiriendo decisión de scope (DB target,
  time-series backend, etc.).

**Negativas:**

- **CSP-strict 1 RTT extra**: el endpoint `/synergos-bridge.js` se
  fetcha separado del HTML. Latencia mínima en mismo session/cookies
  pero medible en LH/WPT. Si el site tiene SLO estricto de TTFB,
  preferir mantener inline + relax CSP (default false está justo por
  esto).
- **Alert composite cost**: ahora cada alert dispara 5 channel calls
  (la mayoría no-op por URL vacía). Overhead negligible, pero el
  scanner trace ahora tiene 5 spans en lugar de 1. Acceptable.
- **DataProtection FileSystem requiere shared volume**: Redis/Azure KV
  son better fit para K8s sin shared FS. Documentado como deferred.
- **NU1608 warning de BenchmarkDotNet**: transitive version mismatch
  con Microsoft.CodeAnalysis.Common (4.10 requirement vs 4.14 resolved).
  Non-fatal — BenchmarkDotNet 0.15.8 funciona; warning queda hasta
  que el upstream actualice su pin.

**Neutras:**

- 4 commits feat + 1 commit ADR + 1 commit current-state.
- 1 NuGet package nuevo: `BenchmarkDotNet` 0.15.8 (benchmarks-project
  only — no leak a runtime ni tests).
- 0 GUIDs nuevos, 0 schema rompedor.
- Nuevo proyecto en la solution: `Synergos.CMS.Benchmarks`.
- Tests: 181 → 194 (+13: 5 controller + 4 composite alert + 4
  data protection settings).

## Implementation summary

| # | Foco | Commit |
|---|---|---|
| 251-253 | CSP-strict /synergos-bridge.js endpoint + tests | `b72c51e` |
| 254-256 | Composite IAlertNotifier + 5 channels + scanner refactor | `ced1493` |
| 257-258 | DataProtection multi-instance shared keyring | `0b5400c` |
| 259-260 | BenchmarkDotNet 0.15.8 + 2 baseline suites | `8ce5f4b` |
| 261-262 | (este) ADR + current-state §11.23 |

## Próximas direcciones (Cap-270 candidatos)

Items §11.22 que quedan (6 → cap-270):

- **DB-backed comment repository** — multi-instance scale.
- **DB-backed audit trail** — multi-instance LB.
- **DB-backed 2FA challenge cache** — TTL state cross-instance.
- **Soft-delete undo cross-restart** — depende de DB-backed comments.
- **Time-series store adapter** webhook telemetry — Postgres
  TimescaleDB? in-memory mejorado?
- **Snapshot tests** payloads (Verify.NET decisión).

Cap-270 requiere **2 decisiones de arquitectura del operador**:
- DB target: la Umbraco SQLite DB existente vs separate SQL Server vs
  Postgres con EF Core.
- Time-series backend: Postgres TimescaleDB vs InfluxDB vs Improved
  in-memory (ring buffer persistido a JSONL).

Sin esas decisiones, Cap-270 queda bloqueado en planning. Items
adicionales también deferred:

- **Bundle registry contract tests** (cuando llegue
  HttpBundleRegistryClient real).
- **Validation contract tests contra archivos source** (CMS
  `syn-tokens.css` + Dictionary XMLs).
- **Composite notifier para Cart abandoment recovery emails** (si
  llega ese flow).
- **CI integration de BenchmarkDotNet** — comparativa automática
  en PRs (deferred — cost-benefit analysis pendiente).

## References

- ADR 0080 — Webhook telemetry alerts (base de Batch B).
- ADR 0083 — CMS↔UI alignment via contracts (base de Batch A).
- ADR 0084 — Cap-230 (introduce IDataProtectionProvider en Batch C
  pre-multi-instance).
- ADR 0085 — Cap-240 (extrae scanner del hosted service en Batch C
  pre-refactor a composite).
- ADR 0086 — Cap-250 (deferred items origen de cap-260).
- `docs/contracts/host-bridge.md` — sección Security: CSP
  compatibility (Batch A spec source).
- [ASP.NET Core Data Protection — multi-instance](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview).
- [BenchmarkDotNet docs](https://benchmarkdotnet.org/).
