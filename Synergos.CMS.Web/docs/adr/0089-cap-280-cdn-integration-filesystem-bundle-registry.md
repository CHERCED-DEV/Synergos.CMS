# ADR 0089 — Cap-280: CDN integration end-to-end via FileSystem bundle registry (Olas 281-289)

- **Status:** Accepted
- **Date:** 2026-04-29
- **Deciders:** Arquitecto + agente.

## Context

Cap-280 cierra el último bloqueo de la integración CDN para los 71
`elementSyn*` del catálogo (ADR 0015). Hasta cap-270 el `IBundleRegistryClient`
seam existía con un único adapter `StubBundleRegistryClient` que retornaba
siempre `null`, dejando al `DefaultSynHostEmitter` emitiendo placeholders
HTML comment. La CDN remota seguía bloqueada externamente (ver §9 de
`CLAUDE.md` + `docs/umbraco/cdn-contract.md`).

Pero el operador tiene una **CDN local funcional** en `C:\LOCAL_CDN`
con la shape canónica completa (49 elementos, registry.json + per-element
manifest.json + main.js per framework/version). El bloqueo no era
arquitectural — era de transporte. La pregunta era: **¿podemos consumir
la CDN local automáticamente sin scripts manuales ni rename?**

### Constraint del operador

> "el Umbraco debería poder leer la CDN, por eso la CDN es un endpoint
> diferente, no entiendo. hagamos una mejor práctica pero una funcional
> en automático sin correr comandos, solo que sea umbraco leyendo la
> CDN y rescatando estáticos."

KISS, automático, idempotente. Nada de pipelines. Nada de scripts de
rename. El CMS lee la CDN tal como está.

### Auditoría del estado pre-Cap-280

✅ **Saludable**:
- `IBundleRegistryClient` seam definido en `Synergos.CMS.Interfaces`.
- `BundleDescriptor` record back-compat-friendly (campos opcionales).
- `DefaultSynHostEmitter` ya maneja `descriptor is null` con comment
  fallback.
- 71 `elementSyn*` registrados en schema, todos con `<synergos-{block}>`
  custom element tag (ADR 0015).
- `C:\LOCAL_CDN\synergos\registry.json` con shape correcta y 49
  elementos catalogados.

⚠️ **Gaps**:
1. Sin **adapter FileSystem** — solo el Stub.
2. Sin **endpoint static-files** que sirva los bundles bajo un
   `/cdn-bundles/` predecible.
3. Sin **SRI integrity** automático — el manifest no siempre lo trae.
4. Sin **hot-reload** — cambio del registry requería restart.
5. Sin **health probe** — el operador no sabía si el adapter cargó OK.
6. Sin **señal frontend** cuando el bundle no resuelve — los elementos
   quedaban silenciosos sin opción de styling.

## Decision

### Batch A — Olas 281-282 — Local CDN static files endpoint

**`LocalCdnSettings`** POCO (sección `Synergos:LocalCdn`):
- `Enabled` (default `false`).
- `LocalPath` — directorio físico (ej. `C:\LOCAL_CDN`).
- `RoutePath` (default `/cdn-bundles`) — URL prefix bajo el cual se
  sirve.
- `CacheControlMaxAgeSeconds` (default `31536000` — 1 año).

**`Program.cs`** wirea `app.UseStaticFiles(new StaticFileOptions { ... })`
condicionado a `Enabled` + `Directory.Exists(LocalPath)`. NO-OP silent
si el directory no existe (e.g. CI builds, dev sin CDN local).

**Smart cache control** distinto por path:
- Pointers **mutables** (`/latest/`, `/v0/`, `/v1/`, ...) →
  `public, no-cache, must-revalidate`. Browser revalida con server
  cada request, devuelve 304 si no cambió.
- Pointers **inmutables** (`/1.0.5/`, `/0.1.0/`, semver exacto) →
  `public, max-age=31536000, immutable`.

Resuelve el bug "1 year cached version inmovible" cuando el CDN team
publica una versión nueva bajo un pointer mutable.

**`Access-Control-Allow-Origin: *`** habilitado para que SRI
crossorigin functione.

### Batch B — Olas 283-286 — FileSystemBundleRegistryClient

**`BundleRegistrySettings`** POCO (sección `Synergos:BundleRegistry`):
- `Mode` (default `Stub`) — `Stub` | `FileSystem` | `Http`.
- `LocalPath` — root absoluto de la CDN (FileSystem mode).
- `BundlesNamespace` (default `synergos`) — subdirectorio bajo
  `LocalPath` que contiene `registry.json`.
- `RegistryFileName` (default `registry.json`).
- `PublicBaseUrl` (default `/cdn-bundles`) — URL base servida al
  browser, debe matchear `LocalCdnSettings.RoutePath`.
- `DefaultFramework` (default `angular`) — coincide con memoria
  `feedback_framework_agnostic_integration`.
- `DefaultSlot` (default `latest`) — pointer mutable por defecto.
- `StripFolderPrefix` (default `true`) — strip de `synergos-` cuando
  se resuelve folder. La CDN local tiene folders cortos
  (`column/`, no `synergos-column/`); el tag DOM mantiene el prefix
  (`<synergos-column>`).
- `ComputeIntegrityIfMissing` (default `true`) — SRI lazy computado
  on-the-fly cuando el manifest no lo trae.
- `HotReloadEnabled` (default `true`) — `FileSystemWatcher` activo.
- `HotReloadDebounceMilliseconds` (default `500`).

**`FileSystemBundleRegistryClient`** (Synergos.CMS.Web):
- Lee `{LocalPath}/{BundlesNamespace}/{RegistryFileName}` al boot.
- Cada elemento del registry expone: `name`, `alias`, `tag`, `tier`,
  `implementations: { framework: { slot: version, ... }, ... }`.
- **Triple lookup** en `TryResolveAsync(string)`: ByTag (`synergos-column`),
  ByAlias (`elementStructColumn`), ByName (`column`). Permite que los
  callers usen la forma natural — el SynHost emitter pasa
  `synergos-{kebab}`, el CMS schema usa el alias del elementType.
- **Atomic snapshot swap** via `volatile RegistrySnapshot? _snapshot`.
  Lock-free reads con writes de un solo struct reference.
- **Lazy SRI compute** via `SHA384.HashData(bytes)` cacheado por path
  con invalidación por mtime. No bloquea el boot — primer resolve
  paga la lectura, sucesivos hit caché.
- **`FileSystemWatcher`** sobre `BundlesNamespace` con debounce timer.
  Cualquier cambio (registry, manifest, main.js) re-snapshot completo
  500ms tras el último evento.
- Cache de manifests + integrity en `ConcurrentDictionary` thread-safe.
- `IDisposable` para cleanup del watcher + timer.

**`BundleRegistryWarmupHostedService`** (IHostedService):
- Fuerza la construcción del `IBundleRegistryClient` singleton al
  boot via `serviceProvider.GetRequiredService<IBundleRegistryClient>()`.
- Sin esto, el adapter quedaba lazy hasta el primer SynHost render y
  los logs del registry loading + watcher setup no aparecían.
- Loguea el tipo concreto del adapter (visibilidad ops):
  `BundleRegistry warmup OK: adapter=FileSystemBundleRegistryClient`.

**`SeamComposer.cs`** registración condicional:
```csharp
var mode = builder.Config["Synergos:BundleRegistry:Mode"] ?? "Stub";
if (string.Equals(mode, "FileSystem", StringComparison.OrdinalIgnoreCase))
    services.AddSingleton<IBundleRegistryClient, FileSystemBundleRegistryClient>();
else
    services.AddSingleton<IBundleRegistryClient, StubBundleRegistryClient>();
```

`Http` mode deferred — requiere el contrato CDN team-side bloqueado.

**`DefaultSynHostEmitter` extendido** para emitir SRI integrity cuando
el descriptor lo trae:
```html
<script src="..." integrity="sha384-..." crossorigin="anonymous"
        type="module" defer></script>
```

**`BundleDescriptor`** record extendido con campos opcionales
(back-compat — sin breaking change para el StubBundleRegistryClient
existente):
- `Tag`, `Alias`, `Tier`, `Integrity`, `Framework`.

**10 unit tests** en `FileSystemBundleRegistryClientTests` (temp dir
real, no mocks de FS):
- Triple lookup happy path por Tag/Alias/Name.
- Unknown tag returns null.
- Registry missing → graceful degradation.
- Manifest missing for one element → otros siguen funcionando.
- Framework fallback cuando `DefaultFramework` no disponible.
- Manifest con integrity pre-computado pasa through.
- `ComputeIntegrityIfMissing=false` → `Integrity=null`.
- `Mode=Stub` no-op boot (no lee FS).
- `HotReloadEnabled=false` para evitar timer races en xUnit parallel.

### Batch C — Olas 287-288 — Health probe

**`BundleRegistryProbe`** implementa `ISchemaHealthProbe` (ADR 0009).
Visible vía `/_health` que ya existe (HealthController).

Verdict por Mode:
- **Stub**: healthy con mensaje `"stub mode active (no CDN configured)"`.
  El operador sabe que el CDN no está activo pero NO es un fallo —
  Stub es un default legítimo.
- **FileSystem**: intenta resolver el probe tag canónico
  `synergos-column` (es el primer primitive del catalog Synergos).
  Si retorna `BundleDescriptor` → healthy con info accionable
  (framework, version, integrity present/missing). Si null → unhealthy
  con guidance: `"Verify registry.json + manifest.json + main.js exist
  at {LocalPath}/{BundlesNamespace}/."`. Si lanza → unhealthy con el
  message del exception.
- **Unknown mode**: unhealthy con el valor literal del setting (catch
  typos del operador).

Wire en `SeamComposer`:
```csharp
services.AddSingleton<ISchemaHealthProbe, BundleRegistryProbe>();
```

**7 unit tests** en `BundleRegistryProbeTests` cubriendo cada branch
del verdict + el caso edge `Mode=null` que cae a Stub.

### Batch D — Ola 289 — Graceful frontend marker

**`DefaultSynHostEmitter`** ahora marca el custom element con
`data-synergos-cdn-offline="true"` cuando el descriptor es null:

```html
<!-- bundle no resuelto → element offline-marked -->
<synergos-badge data-synergos-cdn-offline="true" config='...'></synergos-badge>
```

CSS del host puede stylar (ocultar, dim, mostrar fallback) sin que el
server tome decisión visual. Cuando el bundle hidrate post-CDN-recovery,
el component decide si remueve el atributo. Pasamos UX runtime al
bundle, mantenemos el server framework-agnostic.

**2 tests** en `DefaultSynHostEmitterTests`:
- Test existente `EmitAsync_NullRegistryResolution_*` extendido con
  assert del atributo presente.
- Nuevo `EmitAsync_ResolvedRegistry_DoesNotEmitOfflineAttribute` para
  verificar que NO se emite en happy path.

## Consequences

**Positivas:**

- **Integración CDN 100% automática**: 0 scripts de rename, 0 manual
  steps. El CMS lee la CDN tal como está. Cumple el constraint del
  operador.
- **49 elementos resuelven en runtime**: el log `FileSystemBundleRegistryClient:
  registry loaded with 49 elements` confirma el catálogo CDN local.
- **Hot-reload sin restart**: arquitecto publica nueva versión, el
  watcher detecta, el client re-snapshota, próximo SynHost render usa
  la versión nueva. 500ms debounce evita thrashing por bursts del CDN
  team.
- **SRI defense-in-depth**: el browser rechaza ejecutar el bundle si
  hash no matchea (CDN compromised, MITM, cache poisoning). Lazy
  compute evita el read+hash overhead al boot — solo paga cuando se
  resuelve.
- **Health visibility**: `/_health` ahora reporta el estado del CDN
  adapter. En CI o producción, monitoring detecta degradación
  inmediatamente.
- **Graceful frontend**: el host no aparece muerto cuando la CDN está
  caída — emite un placeholder con marker styleable.
- **Pattern reutilizable**: cuando llegue `HttpBundleRegistryClient`,
  reusa el snapshot pattern + integrity cache + triple lookup. Solo
  cambia el origen de la lectura (HttpClient + cache TTL en lugar de
  FileSystemWatcher).
- **0 dependencies nuevas**: 0 NuGets, 0 npm. Mantiene KISS / FOSS-only.

**Negativas:**

- **`FileSystemWatcher` Windows-only-tested**: el debounce + atomic
  swap pattern es portable, pero el test runner está en Windows. En
  Linux, el watcher tiene quirks (inotify limits, Docker volume binds
  con host mounts no propagan eventos). Si llega container deploy,
  validar el comportamiento por env. `HotReloadEnabled=false` es la
  mitigation segura.
- **`DefaultFramework` global**: hoy un solo framework default
  configurable. Si llega multi-framework concurrente (e.g. blog en
  React, shop en Angular), el caller no tiene forma de override por
  alias. Extension futura: setting `FrameworkOverrides` Dictionary
  alias→framework.
- **`/_health` no expone el count del registry**: el probe healthy no
  reporta cuántos elementos cargaron. Si llega corruption parcial,
  pasa silencioso. Mejora futura: extender `SchemaHealthResult` con
  un `Dictionary<string,object?> Details` opcional.
- **Probe tag hardcoded a `synergos-column`**: el primer primitive
  Synergos. Si una CDN custom no lo tiene, el probe reporta unhealthy
  spuriously. Mitigation: setting futuro `BundleRegistrySettings.ProbeTag`
  override.

**Neutras:**

- 8 commits feat/fix/test/feat + 1 ADR + 1 current-state.
- 0 NuGet packages nuevos.
- 0 npm packages nuevos.
- 0 GUIDs nuevos, 0 schema rompedor.
- Tests: 205 → 224 (**+19**: 10 FileSystemBundleRegistryClient + 7
  BundleRegistryProbe + 2 DefaultSynHostEmitter).
- Static files endpoint nuevo: `GET /cdn-bundles/**`.

## Implementation summary

| # | Foco | Commit |
|---|---|---|
| 281-282 | Local CDN static files + smart cache + CORS + ASCII migration script | `4adc043`, `1302c72`, `5c736ff`, `e677a28`, `21b097e`, `254459d` |
| 283-285 | FileSystemBundleRegistryClient + warmup hosted service | `9c684fe`, `66ca4c3` |
| 286 | FileSystemBundleRegistryClient unit tests (10) | `07981f2` |
| 287-288 | BundleRegistryProbe + wire + tests (7) | `5152d97` |
| 289 | Graceful frontend marker `data-synergos-cdn-offline` + tests (2) | `36644db` |
| 290-291 | (este) ADR + current-state §11.25 |

## Próximas direcciones

Items que podrían atacarse en caps futuros:

- **`HttpBundleRegistryClient`** cuando el CDN team publique el
  registry endpoint remoto (`docs/umbraco/cdn-contract.md` lista los
  5 puntos requeridos). Reusa el snapshot pattern + integrity cache.
  Solo cambia origen lectura (HttpClient + cache TTL).
- **`BundleRegistrySettings.ProbeTag`** override para CDNs custom que
  no exponen `synergos-column`.
- **`SchemaHealthResult.Details`** extension para que probes reporten
  metadata estructurada (count del registry, last reload timestamp,
  watcher status).
- **`FrameworkOverrides`** Dictionary alias→framework si llega
  requirement multi-framework concurrente.
- **Linux watcher validation** cuando llegue container deploy.

Items emergentes del Cap-280 NO atacados:

- **`elementSyn*` placeholder behavior**: hoy emite HTML comment +
  custom element con marker offline. Podría agregar variant que emita
  un fallback visible (e.g. card grey con icon "loading") para que
  el editor vea algo en preview. Diferido — no es bloqueador
  funcional.
- **Multi-CDN failover**: si la CDN local y la remota coexisten,
  ¿cuál gana? Fuera de scope hasta que llegue el HTTP adapter.

## References

- ADR 0009 — Extension seams mandatory.
- ADR 0012 — CDN contract consumed (define el `IBundleRegistryClient` seam).
- ADR 0015 — SynHost framework-agnostic integration.
- ADR 0089 (este).
- `docs/umbraco/cdn-contract.md` — los 5 puntos bloqueantes para CDN
  remota.
- `feedback_cdn_contract_consumed` (memoria).
- `feedback_synhost_naming_convention` (memoria).
- `feedback_framework_agnostic_integration` (memoria).
- [Subresource Integrity (SRI) MDN](https://developer.mozilla.org/en-US/docs/Web/Security/Subresource_Integrity).
- [FileSystemWatcher .NET docs](https://learn.microsoft.com/dotnet/api/system.io.filesystemwatcher).
