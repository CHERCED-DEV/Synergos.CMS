# ADR 0105 — `IJsonEntityStore`: un único seam de persistencia JSON durable (consolidación T1/T3/Booking)

- **Status:** Accepted
- **Date:** 2026-07-15
- **Deciders:** Arquitecto + agente, fase de lógica de negocio (doc rector `25`). Refactor PURO ejecutado de forma AISLADA tras haber probado el patrón cuatro veces. Verificado contra código vivo + datos reales en disco escritos por las implementaciones anteriores.
- **Relacionados:** ADR 0103 (T1+T2 — introdujo `IShopOrderStore` y anotó "extraer el genérico al 2º consumidor"), ADR 0104 (T3 — lo repitió para pagos/reservas y volvió a anotar la deuda: "3 stores idénticos justifican extraer `IJsonEntityStore`"), ADR 0002 (Application sin Umbraco — el adapter FileSystem vive en Web), ADR 0075 (tests por seam), ADR 0013 (sin I/O en boot), ADR 0098 (`IPhiStore` cifrado — familia SEPARADA, no se colapsa aquí). Regla de oro doc 25: ninguna capacidad transversal se implementa dos veces.

---

## Context

La fase de negocio probó el mismo patrón de persistencia cuatro veces:

| Ola | Seam dedicado | Impl FileSystem | Consumidor |
|---|---|---|---|
| T1 | `IShopOrderStore` | `FileSystemShopOrderStore` | `StubShopOrderService` |
| T3 | `IPaymentSessionStore` | `FileSystemPaymentSessionStore` | `StubPaymentProvider` |
| T3 | `IReservationStore` | `FileSystemReservationStore` | `StubReservationService` |
| Booking | `ITravelOrderStore` | `FileSystemTravelOrderStore` | `TravelCartService` |

Los cuatro seams eran **idénticos en forma** (write/read/list/delete de JSON por clave) y
las cuatro impls FileSystem eran **la misma escritura atómica copiada** (temp +
`File.Move`, `Sanitize`, fail-safe, lazy). Cuatro POCOs de settings clonados. Eso es
exactamente lo que la regla de oro del doc 25 prohíbe: **una capacidad transversal
implementada cuatro veces**. ADR 0103 y ADR 0104 anotaron la deuda explícitamente y la
difirieron — 0103 para no re-tocar T1, 0104 para no arriesgar el código de pagos recién
verificado.

El momento de saldarla es **ahora y aislado**: el patrón ya está probado por cuatro
consumidores (no es abstracción prematura — la prohibición §6 de CLAUDE.md se cumple con
creces), y hacerlo como refactor puro (sin mezclar features) permite exigir el listón que
un refactor merece: **no debe cambiar NADA**.

## Decision

**`IJsonEntityStore` es el ÚNICO seam de persistencia JSON durable del proyecto.** Todo
estado durable de dominio se guarda a través de él, keyed por `(resourceType, key)`:

```csharp
public interface IJsonEntityStore
{
    Task WriteAsync(string resourceType, string key, string json, CancellationToken ct = default);
    Task<string?> ReadAsync(string resourceType, string key, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListAsync(string resourceType, CancellationToken ct = default);
    Task<bool> DeleteAsync(string resourceType, string key, CancellationToken ct = default);
}
```

- **`FileSystemJsonEntityStore`** (Web) es la **única** implementación durable: escritura
  atómica temp+`File.Move` bajo lock, `Directory.CreateDirectory` lazy (ADR 0013), saneo
  anti path-traversal, lectura fail-safe. **`InMemoryJsonEntityStore`** (Application) es el
  default de tests — una sola instancia sirve a todas las familias (la clave compuesta las
  aísla), lo que simplifica los tests de durabilidad multi-motor.
- **`resourceType` es el discriminador de familia** y define el subdirectorio:
  `{StorageRoot}/syn-{resourceType}/{key}.json`. Cada motor lo declara en una const
  (`"orders"`, `"payments"`, `"reservations"`, `"travel-orders"`). El filtrado sigue
  haciéndolo el motor deserializando sobre `ListAsync` — el store sigue tonto.
- **Un solo POCO de settings** (`JsonEntityStoreSettings.StorageRoot`, default
  `App_Data/`) reemplaza a los cuatro.
- **Se ELIMINAN** los 4 seams dedicados, sus 8 impls y sus 4 settings.

**Preservación de rutas (requisito duro del refactor):** el prefijo `syn-` + el
`resourceType` reproducen EXACTO los directorios que ya usaban los stores dedicados
(`syn-orders`, `syn-payments`, `syn-reservations`, `syn-travel-orders`). Los datos ya
escritos **no se huerfanizan**. Un test dedicado fija esta propiedad.

### Qué NO se colapsa

- **`IPaymentEventStore`** (ledger de idempotencia del webhook) se mantiene aparte: su
  primitiva es **create-exclusiva atómica** (`FileMode.CreateNew`), no un upsert.
  Colapsarlo en el genérico reintroduciría el TOCTOU que existe para evitar.
- **`IPhiStore`** (Healthcare, ADR 0098) se mantiene aparte: cifra con `IDataProtector`.
  El genérico es para PII de compra, no PHI.

## Consequences

**Positivas:**

- **Una sola implementación de escritura atómica** en todo el proyecto: un bug de
  durabilidad se arregla una vez y beneficia a los cuatro dominios. Regla de oro saldada.
- **El fan-out a los verticales restantes** (Eventos/Educación/Gobierno) ya no crea un
  seam por dominio: inyectar `IJsonEntityStore` + declarar un `resourceType`.
- **−502 líneas netas** (+421/−923, 32 archivos) — la duplicación cuantificada.
- **Un solo knob de config** para el operador; el swap a SQLite (índices secundarios) se
  hace en un solo lugar, invisible para los cuatro motores.
- Tests del genérico fijan dos propiedades que antes nadie verificaba: **aislamiento por
  `resourceType`** y el **mapeo exacto de ruta**.

**Negativas o trade-offs:**

- **`resourceType` es un string mágico** por motor. Mitigado con una const documentada en
  cada uno; un enum acoplaría el store al dominio (rompería su opacidad). Cambiarlo
  re-ubica los datos — el xmldoc lo advierte.
- **Un solo Singleton sirve a todos los dominios**: un fallo de I/O afecta a todos por
  igual (antes también, misma clase de fallo en 4 copias — no es regresión).
- El genérico **no puede** expresar primitivas distintas (create-exclusivo, cifrado); por
  eso `IPaymentEventStore`/`IPhiStore` quedan fuera. La familia de seams de storage no es
  "uno para todo", sino "uno por primitiva".

**Notas de implementación:**

- Refactor PURO verificado como tal: suite **695/704** (los 9 rojos son los pre-existentes
  de formato es-CO/ICU del entorno, ajenos); **datos pre-existentes escritos por los stores
  DEDICADOS siguen resolviendo por API** tras el refactor (orden de Tienda SYN-07B402AB
  Paid; orden de viaje SYN-70666362 Confirmed) — la prueba de no-huerfanización;
  flujos nuevos e2e OK; **restart-gap sigue cerrado** (checkout → matar CMS → reiniciar →
  confirm = 200 Paid, SYN-A98BE146); mismos 4 directorios, ninguno nuevo.
- Commit: `5a759ac`.

## Alternatives considered

- **Dejar los 4 stores dedicados** (tipado fuerte por dominio, sin string mágico).
  Descartado: cuatro copias de la misma escritura atómica es exactamente la violación que
  la regla de oro nombra; el "tipado fuerte" no aportaba nada — los seams eran idénticos.
- **Añadir un 5º store dedicado para Booking** y diferir otra vez. Descartado: la deuda ya
  se había diferido dos veces (0103, 0104) y crecía con cada vertical.
- **Colapsar también `IPaymentEventStore`.** Descartado: su primitiva create-exclusiva es
  la que evita el TOCTOU de la idempotencia del webhook; un upsert genérico la rompería.
- **Migrar todo a SQLite en este refactor.** Descartado: mezclaría un cambio de motor de
  persistencia con la consolidación del seam. El seam es precisamente lo que hará ese swap
  barato después.
- **Hacerlo mezclado con el fan-out de Booking.** Descartado: habría arriesgado el código
  de pagos recién verificado dentro de un commit de feature. Aislarlo permitió exigir
  "no debe cambiar nada" y verificarlo contra datos reales.

## References

- ADR 0103 §Consequences y ADR 0104 §Consequences — donde se anotó esta deuda.
- Código: `Synergos.CMS.Interfaces/IJsonEntityStore.cs`;
  `Synergos.CMS.Application/Services/Impl/InMemoryJsonEntityStore.cs`,
  `Configuration/JsonEntityStoreSettings.cs`;
  `Synergos.CMS.Web/Services/FileSystemJsonEntityStore.cs`;
  `Synergos.CMS.Tests/Services/FileSystemJsonEntityStoreTests.cs`.
- Memoria: `feedback_json_entity_store_canonical`.
