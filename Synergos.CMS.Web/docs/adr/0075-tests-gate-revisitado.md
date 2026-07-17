# ADR 0075 — Tests gate revisitado: 83 → 111 tests (Olas 173-176)

- **Status:** Accepted (supersedes implicit gate documented in `feedback_tests_after_full_migration` memory + CLAUDE.md principle #9).
- **Date:** 2026-04-27
- **Deciders:** Arquitecto + agente, durante exploratory question
  *"qué nos está haciendo falta"*.

## Context

`CLAUDE.md` principle #9 estableció: *"Tests al final de la
migración, no en paralelo. Primero migrar, después cubrir."* La
memoria `feedback_tests_after_full_migration` reforzó: *"No proponer
tests como ola mientras la migración del Epic Fail 2 no esté al 100%."*

Pero §11.3 del current-state ya documenta *"Schema migrado (100% del
inventario)"* desde caps anteriores. Discovery de Ola 173: el Tests
project **nunca estuvo realmente vacío** — ya tiene 83 tests pasando
desde antes (Architecture, Configuration, Controllers, DTO, Middlewares,
Notifications, Proxies, Services). El gate se relajó silenciosamente
en la práctica.

Reconciliar la documentación con la realidad: lift formal del gate.

## Decision

### Ola 173 — verify status quo

`dotnet test`: 83 tests pasando, 0 failures, 1 segundo runtime.

### Olas 174-176 — coverage para los seams nuevos

3 archivos de test nuevos cubriendo los seams introducidos en caps
150-170:

#### `InMemoryWebhookTelemetryStoreTests` (6 tests)

- Empty store retorna lista vacía.
- Single call popula stats.
- Multi-channel isolation.
- Percentile computation con 100 samples (P50=50ms, P95=95ms, P99=99ms).
- Ring buffer overflow con 1500 samples (buffer keep last 1000).
- Ordering by channel name alfabético.

#### `WebhookResilienceSettingsValidatorTests` (3 + 12 InlineData = 15)

- Empty PerChannel → Success.
- Known FactoryName → Success.
- Unknown FactoryName → still Success (typo solo loguea warning, no
  falla).
- Theory con los 12 known FactoryNames cada uno verificado.

#### `FileSystemAuditTrailWriterTests` (8 tests)

- Persists event correctly.
- Idempotent on same Id (no duplicate writes).
- GetRecent ordering desc.
- Actor email filter.
- Action contains filter.
- Empty directory → empty result.
- GetByDateRange windowing.
- StubHostEnvironment con temp directory per test (IDisposable
  cleanup).

### Cleanup IDE0005 colateral

5 archivos del Web project tenían `using` directives redundantes
gracias a Web SDK implicit usings. Limpiados en mismo commit.

## Consequences

**Positivas:**

- **Reality-check**: la documentación reflecte el estado real.
  Decisión explícita > regla implícita olvidada.
- **Coverage para nuevos seams**: 28 tests nuevos cubriendo
  IAuditTrailWriter + IWebhookTelemetryStore + WebhookResilienceSettings
  validation.
- **Build clean**: 0 IDE0005 + 0 CS + 0 CA + 0 RZ.
- **Pattern para future olas**: cada nuevo seam debe llegar con tests
  asociados (no más "deferred").

**Negativas:**

- **CLAUDE.md desactualizado** mientras este ADR no se aplique al doc.
  Update incluido en el cap-190 cierre.
- **Cobertura selectiva**: muchos seams legacy (ICommentRepository,
  IFormSubmissionHandler, IMemberRosterReader/Writer) aún sin tests.
  Tracked como deferred.

**Neutras:**

- 1 commit test + 1 docs ADR.
- 0 GUIDs nuevos.
- 0 NuGet packages nuevos.

## Implementation summary

| # | Foco |
|---|---|
| 173 | `dotnet test`: 83 → status quo confirmed. |
| 174-176 | 3 test classes nuevas: 6 + 15 + 8 = 29 tests. |
| 0075 | (este) ADR consolidado |

## Próximas direcciones

- **Tests para legacy seams**: ICommentRepository, IFormSubmissionHandler,
  IMemberRosterReader/Writer, IMemberAuthService, IBlogQuery,
  IShopQuery, ISearchQuery — coverage incremental.
- **Integration tests**: end-to-end flows (login → moderation,
  comment submit → notifier composite).
- **CI pipeline**: gate merges en `dotnet test`. Hoy es manual.

## References

- CLAUDE.md principle #9 (a actualizar — "Tests al final" → "tests
  por seam, ship con coverage").
- `feedback_tests_after_full_migration` memory (a actualizar — gate
  lifted).
- ADR 0007 — xUnit framework decision (re-validated).
