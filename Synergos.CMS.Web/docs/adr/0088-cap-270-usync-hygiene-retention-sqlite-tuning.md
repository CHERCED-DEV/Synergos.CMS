# ADR 0088 — Cap-270: uSync hygiene + retention sweep + audit harness + SQLite tuning (Olas 271-281)

- **Status:** Accepted
- **Date:** 2026-04-29
- **Deciders:** Arquitecto + agente.

## Context

Cap-260 cerró 4 deferred items concretos pero quedaron 6 más que
requerían **decisión de arquitectura del operador**: DB-backed
multi-instance (DB target + time-series backend). El operador
explicitó la decisión correcta para 1-dev / KISS / FOSS-only:

> "necesito que synergos siga siendo lo más free posible. no me
> conviene usar cosas extras. debe ser simple, en este momento no
> tendrá sino un solo desarrollador. entonces creo que en este
> momento no deberíamos atacar esta parte. podemos más bien refinar
> el uSync y mirar todo lo que esté relacionado a cómo hoy podemos
> mejorar la BD."

**Cap-270 redefinido**: NO multi-instance. En cambio, "**foundation
hygiene**" — refinar lo que ya existe sin agregar dependencies
(NuGets nuevos = 0, services externos = 0, npm deps = 0).

El cap arrancó con una **auditoría sistemática real** del estado
actual antes de planificar:

### Findings de auditoría (post-Cap-260)

✅ **Saludable**:
- 0 GUID collisions en root elements de ContentTypes/DataTypes/Dictionary.
- WAL mode SQLite activo (db-wal file presente).
- Dictionary alias canónico es PascalCase dentro del XML
  (`Admin.Action.Delete`); filename lowercase es solo convención
  uSync filesystem-safe — no drift contra contract spec.
- Composition references resueltas (los "missing" eran DocTypes
  settings/cfg* legítimos referenciados como composition para
  inheritance pattern).

⚠️ **Problemas reales**:
1. **2 iconos inválidos** en schema (no en stock 627):
   `icon-cogwheel` en `siteconfiguration.config`, `icon-document-line`
   en `cfgFooterNote.config`.
2. **4 compositions sin consumers** — inicialmente flagged como
   orphans, pero al inspeccionar las descripciones aparecen markers
   explícitos `[Bloqueado externamente - ...]` o `[Disponible — sin
   consumers actuales]`. Son **scaffolding planificado**, NO dead
   code: `compBehaviorFeatureFlag`, `compContentCollection`,
   `compContentEmbed`, `compContentMetadata`.
3. **CLAUDE.md doc stale**: dict count 369 (real: 430), ContentTypes
   ~200 (real: 223), DataTypes 37 (real: 104).
4. **WAL file 4MB sobre DB 4MB** — sin checkpoint reciente. WAL
   debería ser pequeño en uso normal.
5. **Sin retention sweep** en 3 stores filesystem (comments rejected,
   form-submissions, search-analytics). Solo audit tenía (ADR 0070).
6. **Sin uSync audit harness automático** — los 5 hallazgos arriba
   podrían haberse atrapado en CI.

## Decision

### Batch A — Olas 271-272 — Schema fixes inmediatos

**Iconos reemplazados** por stock válidos:
- `siteconfiguration.config`: `icon-cogwheel color-grey` →
  `icon-settings-alt-2 color-grey` (semánticamente más cercano
  a "configuración del sitio").
- `cfgFooterNote.config`: `icon-document-line color-grey` →
  `icon-document-dashed-line color-grey` (match exacto del intent
  "aviso/nota").

**4 compositions sin consumers NO eliminadas**. Las descripciones
ya documentan intent con markers explícitos. Decisión: registrar
la convención para que el audit harness (Batch C) las reconozca y
NO flag como orphans.

**CLAUDE.md doc bumped** (counts + sección "Dónde está la verdad"
agregando entry para reserved compositions marker).

**Memoria nueva** del agente: `feedback_reserved_compositions_marker.md`
documenta la convención + lista los 4 casos vigentes.

### Batch B — Olas 273-275 — IRetentionPolicy generalizado

**Refactor del pattern existente** (ADR 0070 audit retention) a
seam reutilizable + sweep service que itera N policies:

`IRetentionPolicy` (Synergos.CMS.Interfaces):
- `string Name { get; }` — friendly identifier para logs.
- `Task<int> SweepAsync(CancellationToken)` — retorna count purgado.
  0 = disabled o nada que purgar (idempotent).

`RetentionSettings` (Synergos.CMS.Application.Configuration):
- `CommentsRejectedRetentionDays` — default 30 (rejected only).
- `FormSubmissionsRetentionDays` — default 365.
- `SearchAnalyticsRetentionDays` — default 90.
- `0 = nunca purgar`.

`AdminSettings.AuditRetentionDays` sigue siendo source-of-truth para
audit (back-compat ADR 0070).

`RetentionSweepHostedService`:
- Itera `IEnumerable<IRetentionPolicy>` cada 24h con try-catch
  per-policy (canal roto no afecta otros).
- Initial delay 60s para no contender boot.
- Reemplaza al antiguo `AuditRetentionHostedService` (deletado).

**4 policies** en `Synergos.CMS.Web/Services/Retention/`:
- `AuditRetentionPolicy` — refactor del Sweep() inline.
- `CommentsRetentionPolicy` — read-modify-write JSON file, filtra
  `Approved=false + CreatedAtUtc < cutoff`. Comments approved nunca
  se purgan auto.
- `FormSubmissionsRetentionPolicy` — parse filename prefix
  `yyyyMMdd_HHmmss`, fallback a `LastWriteTimeUtc`.
- `SearchAnalyticsRetentionPolicy` — misma shape que audit.

Cart abandonment sin policy: `InMemoryCartAbandonmentTracker` es
in-memory por diseño. Si llega un FileSystem tracker futuro, agregar
ahí + `CartsRetentionDays`.

**8 tests** en `RetentionPolicyTests` (temp ContentRoot).

### Batch C — Olas 276-277 — uSync audit harness + CI gating

**`tools/usync-audit.mjs`** (Node vanilla, sin npm deps):

5 checks:
1. GUID collisions del root element only (`<ContentType|DataType|
   Dictionary Key="...">` primer match per-file). **Refs nested
   como `<Structure><ContentType Key="..."/>` son allowed-content
   references, no definiciones** — false positive evitado.
2. Composition orphans: alias=`comp*` definidos pero sin consumers
   Y sin marker `[Bloqueado externamente -]` o `[Disponible —]`
   al inicio del CDATA Description.
3. Missing composition refs: alias referenciado en `<Composition>`
   sin definición correspondiente.
4. Iconos inválidos: `<Icon>{name}</Icon>` con name no en
   `tools/umbraco13-icons-stock.txt` (627 stock copy versionada en
   el repo).
5. Dictionary alias PascalCase: regex
   `/^[A-Z][a-zA-Z0-9]+(\.[a-zA-Z0-9]+)*$/` — primer segmento
   PascalCase obligatorio (sección), resto alfanum case-insensitive
   para enum values (`Blog.PostType.article`).

Errors → exit 1 (CI fail). Warnings → exit 0 (visible, no
blocking).

**`.github/workflows/usync-audit.yml`** triggers en PRs/push que tocan
`Synergos.CMS.Web/uSync/v9/**` o el script audit. Node 20, sin npm
install (script no tiene deps).

Verificado contra estado post-Batch A: **✓ 0 errors / 0 warnings**.
Inyección de icono inválido genera el error esperado.

### Batch D — Olas 278-279 — SQLite maintenance pragmas

**`SqliteMaintenanceHostedService`** corre cada 24h:
- `PRAGMA wal_checkpoint(TRUNCATE)` — colapsa WAL back into main DB.
- `PRAGMA optimize` — actualiza statistics para query planner.

Auto-detect via `ConnectionStrings:umbracoDbDSN_ProviderName =
Microsoft.Data.Sqlite`. Si SQL Server (futuro), NO-OP silent.

Resuelve el placeholder `|DataDirectory|` Umbraco-specific por
`{ContentRoot}/umbraco/Data` (convention 13 default).

**`SqliteMaintenanceSettings`**:
- `Enabled` (default `true`).
- `IntervalHours` (default `24`).
- `InitialDelaySeconds` (default `120` — espera Umbraco boot).

`Microsoft.Data.Sqlite` ya transitive via Umbraco — **0 NuGet nuevo**.

**3 tests** con temp `.db` real (no in-memory para que WAL pueda
inicializarse).

`docs/hardening/backup-and-recovery.md` updated:
- Warning sobre backup inconsistency si solo se copia `.db` sin
  checkpoint del WAL.
- Sección nueva documentando el maintenance service + 3 settings.

### Olas 280-281 — Cierre

Este ADR + actualización current-state §11.24.

## Consequences

**Positivas:**

- **0 dependencies nuevas**: ni NuGets, ni npm, ni servicios externos.
  Mantiene KISS / FOSS-only / 1-dev constraint.
- **Schema healthy verificado**: audit harness corre clean post-fixes.
  Cualquier futuro driftering (icon, GUID collision, orphan) se
  detecta en PR, no en producción.
- **Stores no crecen sin límite**: 4 retention policies activas con
  defaults razonables. Comments approved siguen preservados (content
  editorial); solo rejected viejos se purgan.
- **WAL bajo control**: checkpoint cada 24h evita el growth ilimitado
  observado (4MB WAL sobre 4MB DB).
- **Pattern reutilizable**: `IRetentionPolicy` se extiende fácil
  cuando llegue otro store (e.g. webhook telemetry persistence si
  cap-280 ataca eso).
- **Reserved compositions marker formalizado**: el agente futuro NO
  va a proponer delete sobre scaffolding planeado — la memoria +
  audit harness allowlist lo previenen.

**Negativas:**

- **uSync audit no full validation**: el script chequea structure
  (GUIDs, refs, icons, naming), NO chequea semántica
  (e.g. ¿el DataType referenciado tiene la Definition correcta para
  el GenericProperty Type?). Una expansión futura podría
  cross-check Definition GUIDs contra DataType GUIDs.
- **Retention sweeps no tienen métricas**: el log dice "purged N items"
  pero no hay dashboard ni alert si la sweep se descalabra (e.g.
  borra 1000× lo normal). Si llega el caso, exponer counts vía
  `IWebhookTelemetryStore` o similar.
- **SqliteMaintenance Windows-specific path resolution**: el reemplazo
  de `|DataDirectory|` asume Umbraco 13 default layout
  (`{ContentRoot}/umbraco/Data`). Si el operador override esa convention,
  el service no resuelve. Mitigation: log Information cuando no resuelve.

**Neutras:**

- 4 commits feat/test/ci/feat + 1 ADR + 1 current-state.
- 0 NuGet packages nuevos.
- 0 npm packages nuevos.
- 0 GUIDs nuevos, 0 schema rompedor.
- Tests: 194 → 205 (+11: 8 retention + 3 sqlite maintenance).
- 1 archivo deleted (`AuditRetentionHostedService.cs` — replaced).
- 1 archivo nuevo en repo: `tools/umbraco13-icons-stock.txt`
  (627 lines, antes vivía solo en memoria del agente).

## Implementation summary

| # | Foco | Commit |
|---|---|---|
| 271-272 | Iconos fixed + reserved compositions marker + CLAUDE.md doc bump | `db6d43b` |
| 273-275 | IRetentionPolicy seam + 4 policies + sweep service + tests | `db85095` |
| 276-277 | usync-audit.mjs + GitHub Action + icon stock copy | `268cb65` |
| 278-279 | SqliteMaintenanceHostedService + maintenance doc | `00bb96d` |
| 280-281 | (este) ADR + current-state §11.24 |

## Próximas direcciones

Items §11.22 que no se atacaron en cap-270 (operador prefiere KISS):

- **DB-backed multi-instance** (comments / audit / 2FA challenge cache):
  out of scope hasta que llegue requirement real de scale. Cuando
  llegue, este ADR queda complementario — el retention sweep +
  audit harness valen igual sobre cualquier backend.
- **Time-series store adapter** webhook telemetry: idem out of scope.
- **Snapshot tests fixture library** (Verify.NET): considerar si
  el counter de tests crece a >300 y los string-matching asserts
  empiezan a doler. Hoy 205, manejable.

Items que el cap-270 audit podría extender en caps futuros:

- **uSync semantic checks**: cross-check `<Definition>{guid}</Definition>`
  en GenericProperties contra DataType `<Key>` reales.
- **Retention metrics**: emit purge counts a IWebhookTelemetryStore
  para visibilidad ops.
- **uSync export hygiene check**: detectar XMLs con encoding extraño
  (mojibake doble — memoria `feedback_powershell_utf8_bulk_edits`).

Items emergentes del audit que NO se atacaron:

- **uSync filename casing inconsistente**: `siteconfigsettings.config`
  vs `cfgAlert.config` (lowercase vs camelCase). Solo cosmetic,
  uSync no se queja. Diferido — fix requiere uSync re-import.

## References

- ADR 0008 — uSync hybrid source-of-truth (base del schema).
- ADR 0070 — Audit retention sweep (pattern generalizado en Batch B).
- ADR 0084 — Cap-230 (introduce IDataProtectionProvider — relevant
  para WAL backup hygiene).
- ADR 0086 — Cap-250 (introduce GitHub Action contract-tests pattern
  reusado en Batch C).
- ADR 0087 — Cap-260 (decisiones DB target deferred → triggered
  redirect a hygiene en Cap-270).
- `tools/usync-audit.mjs` — script source.
- `tools/umbraco13-icons-stock.txt` — stock library reference (627).
- Memoria `feedback_reserved_compositions_marker.md`.
- [SQLite WAL mode docs](https://www.sqlite.org/wal.html).
- [SQLite PRAGMA optimize docs](https://www.sqlite.org/pragma.html#pragma_optimize).
