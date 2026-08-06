# ADR 0103 — Persistencia FileSystem + ownership por-Member para órdenes de Tienda (T1+T2, doc 25)

- **Status:** Accepted
- **Date:** 2026-07-15
- **Deciders:** Arquitecto + agente, fase de lógica de negocio (doc rector `25`, transversales T1 persistencia + T2 auth). Piloto sobre la compra de Tienda. Verificado end-to-end contra código vivo (`StubShopOrderService`, `FileSystemShopOrderStore`, `ShopCatalogController`, `DefaultMemberAccessGate`, `IMemberService`, cookie de member real de Umbraco 13) con curl + cookie jar y dos members (alice/bob), incluido reinicio del CMS.
- **Relacionados:** ADR 0098 (Healthcare PHI — patrón `IPatientRepository → IPhiStore → disco` que este ADR calca sin la carga regulatoria), ADR 0002 (Application no referencia Umbraco/AspNetCore), ADR 0083 (contract drift / host-bridge — la UI es fuente de verdad), ADR 0075 (cada seam ship con tests), ADR 0013 (sin seeders automáticos; nada de I/O en boot), ADR 0028 (Shop runtime + `IPriceFormatter` es-CO), ADR 0025 (members runtime), ADR 0034 (self-service de member: login/registro). Regla de oro: ninguna capacidad transversal se implementa dos veces (memoria `feedback_audit_vs_analytics_seams`).

---

## Context

Al cierre de la fase de acabado (ADR/doc 25) el diagnóstico fue: **cimientos
excelentes, músculo de negocio ausente**. El síntoma más crudo estaba en la
compra de Tienda: el motor transaccional `StubShopOrderService` (que compone
catálogo + reservas + pago + idempotencia) guardaba el estado de las órdenes en
un `ConcurrentDictionary` del proceso. Dos consecuencias inaceptables para un
producto de negocio:

1. **Ninguna durabilidad.** Un reinicio del CMS borraba toda venta/orden;
   `GET /api/shop/orders` salía vacío. Una orden confirmada no sobrevivía al
   proceso que la creó.
2. **Ninguna identidad real.** El historial se recuperaba por
   `GET /api/shop/orders?customer=<email>`. El email es enumerable → cualquiera
   podía leer el historial de compras de cualquier otro correo (IDOR). La orden
   no estaba atada a un login; el email tecleado en el checkout era la única
   "identidad".

La meta dura del doc 25 para el piloto: **una orden confirmada sobrevive un
reinicio, atada a un login real, y solo su dueño la ve.** Esto son dos
transversales — T1 (persistencia) y T2 (auth/ownership) — que, una vez probadas
en Tienda, deben poder heredarse por los demás verticales transaccionales
(Booking, Eventos, Propiedades) por repetición de receta, no por rediseño.

Restricciones heredadas: sin EF (la persistencia del proyecto es FileSystem-on-disk,
como el PHI); Application no referencia Umbraco/AspNetCore (ADR 0002); nada de I/O
en boot (ADR 0013); el guest checkout (comprar sin cuenta) es una decisión de
diseño existente que **no** debe romperse.

## Decision

### T1 — Persistencia durable tras un seam de storage apilado (no rewrite)

El motor **no se reescribe**. Solo su **estado** se muda del diccionario del
proceso a disco, detrás de un seam nuevo — exactamente el apilado del PHI
(`IPatientRepository → IPhiStore → disco`), traído a Tienda sin la carga
regulatoria.

- **`IShopOrderStore`** (Interfaces) — puerto de almacenamiento **opaco**: JSON
  por `orderRef` (`Write`/`Read`/`List`/`Delete`). No conoce el dominio; el
  filtrado lo hace el motor deserializando lo que `List` devuelve. Un
  `FileSystemShopOrderService` monolítico se **descarta**: obligaría a re-hostear
  la composición del motor en Web, duplicando lógica y violando ADR 0002.
- **`InMemoryShopOrderStore`** (Application) — default para tests/efímeros
  (`ConcurrentDictionary<string,string>`).
- **`FileSystemShopOrderStore`** (Web) — calca `FileSystemEncryptedPhiStore`
  **menos el cifrado** (es PII de compra, no PHI). Escritura **atómica** temp +
  `File.Move(overwrite:true)` bajo lock; 1 archivo por orden en
  `App_Data/syn-orders/{orderRef}.json` (mutación Pending→Paid sin tocar otras);
  I/O fail-safe (corrupto → se salta + log, nunca propaga); `Directory.CreateDirectory`
  lazy justo antes de escribir (nada en boot).
- **`PersistedOrder`/`PersistedOrderLine`** (Application, internos) — el
  **superset** serializado. Guarda más que el `ShopOrder` público:
  `ReservationId` por línea + `PaymentSessionId` (necesarios para que
  `ConfirmAsync` sobreviva un reinicio) + `OwnerMemberKey` (campo de T2, aditivo).
- **`ShopOrdersSettings.StorageRoot`** (`Synergos:ShopOrders`), default
  `App_Data/syn-orders/`.

`StubShopOrderService` gana un ctor aditivo `(...tracking, IShopOrderStore store,
now)`; los ctors previos delegan con `new InMemoryShopOrderStore()` → cero
call-sites rotos, tests verdes. `ConfirmAsync` sigue idempotente:
read → `if (Status == Paid) return` → capturar → confirmar reservas → escribir Paid.

### Gate fix (prerrequisito de T2, bug latente app-wide)

Verificando T2 se descubrió que `DefaultMemberAccessGate.CurrentMemberKey`
devolvía **siempre null** para todo member autenticado: asumía que el claim
`NameIdentifier` del cookie de member era el `Member.Key` (GUID), pero en Umbraco
13 ese claim lleva el **`Member.Id` (entero)** y el GUID no viaja en ningún claim.
Impacto silencioso (build verde no lo atrapa): `DefaultPhiAccessGuard` fail-closed
negaba a todo member su propio PHI; `DefaultHostBridgeContextBuilder.BuildMember`
dejaba `window.synergos.member = null` aun logueado; el enrollment 2FA se
keyeaba por null.

**Fix:** resolver el GUID del `Member.Id` vía `IMemberService.GetById(id).Key`
(cache de Umbraco), tomando `IMemberService` de `HttpContext.RequestServices` (el
gate es Singleton e `IMemberService` es Scoped → inyectarlo por ctor sería un
captive dependency). Se conserva un fast-path `Guid.TryParse` por si un setup
futuro ya emite el GUID en el claim. Es puro bug fix: null → valor correcto.

### T2 — Orden atada al Member + cierre del IDOR (guard-first)

Todo **aditivo** (campos opcionales `= null` → build-safe):

- **Contrato:** `ShopCustomer` gana `MemberKey` (Guid?); `ShopOrder` gana
  `OwnerMemberKey` (Guid?); nuevo `IShopOrderService.GetOrdersByMemberAsync(memberKey)`
  — la vía **segura** de "mis compras" (filtra por key, excluye invitados).
- **Motor:** el checkout persiste `OwnerMemberKey = customer.MemberKey`;
  `GetOrdersByMemberAsync` filtra el `List` por key, más reciente primero.
- **Controller** (`ShopCatalogController`, inyecta `IMemberAccessGate`):
  - `RequireMember()` → 401 si anónimo (molde `DashboardApiController`/
    `HealthcareApiController`, `StatusCode(403/401)` limpio, no `Forbid()` que
    redirige con auth de members).
  - `DenyIfForeignMember(order)` → 403 si un **otro** member autenticado toca una
    orden con dueño; **no** gatea al invitado que trae el `orderRef` (credencial
    bearer inadivinable `ord_{guid:N}` — el self-service de invitado no se rompe);
    admin overridea.
  - `POST checkout`: con sesión deriva name/email/`memberKey` del gate e **ignora
    el body** (anti-tampering — no se puede comprar a nombre de otro); sin sesión,
    invitado con los datos del form y `OwnerMemberKey` null.
  - `GET /orders`: guard-first + `GetOrdersByMemberAsync(actorKey)` — deja de
    confiar en `?customer=` → **cierra el IDOR enumerable**.
  - `tracking` + `return` por-orden: ownership check tras resolver la orden.

**Postura:** el guest checkout **sigue abierto**; el IDOR enumerable
(`/orders?customer=email`) se cierra duro (401/por-memberKey); el acceso cruzado
entre members se bloquea (403). Ownership **solo** por `memberKey` — un invitado
que luego se loguea no reclama sus órdenes viejas por email (evita reintroducir el
IDOR por la puerta de atrás). Esto **refina** el diseño original, que gateaba
`tracking` con `RequireMember` y habría roto el self-service de invitado.

## Consequences

**Positivas:**

- **Meta dura cumplida y verificada e2e:** una orden confirmada sobrevive un
  reinicio del CMS, atada a un login real, y solo su dueño la ve. Primer músculo
  de negocio real de la plataforma.
- **El fix del gate arregla la identidad de member app-wide:** PHI guard,
  host-bridge (`window.synergos.member`) y 2FA pasan de "roto silenciosamente" a
  funcional.
- **Fan-out barato:** el store y el patrón de ownership son genéricos. Al 2º
  consumidor transaccional (Booking/Eventos) se extrae `IShopOrderStore →
  IJsonEntityStore(resourceType,key)` (el `IPhiStore` sin cifrado) y se copia el
  guard `RequireMember`/`owner == actorKey`. Un dominio paga el diseño, el resto
  lo calca — como hizo el PHI.
- **Sin EF, sin cifrado innecesario, sin seeders, sin tocar Application↔Umbraco.**
  Escritura atómica crash-safe.

**Negativas o trade-offs:**

- **`ListAsync` O(n) por request** (deserializa todas las órdenes para filtrar).
  Aceptable a volumen demo; el camino de escala es swap a SQLite con índice
  secundario por `OwnerMemberKey`/email, **sin tocar el motor** (el seam lo aísla).
- **`CurrentMemberKey` hace un lookup a `IMemberService` por acceso** (cacheado
  por Umbraco). Se llama 2-3× por request en los flujos de Tienda; no se memoiza
  aún.
- **Órdenes de invitado no son recuperables por el dueño** tras loguearse
  (ownership solo por memberKey). Decisión consciente anti-IDOR; migración de
  datos viejos no aplica (demo).
- **Escritura single-instance** (lock local). Multi-instancia real requeriría un
  store distribuido — fuera de alcance del piloto.

**Notas de implementación:**

- `App_Data/syn-orders/` está en `.gitignore`; los JSON de runtime nunca se
  commitean. Los archivos llevan BOM UTF-8 (consistente con el PhiStore); el
  read los tolera.
- Commits: `6f520fd` (T1), `340c646` (fix del gate), `b5c1ae9` (T2). 17 tests de
  Tienda verdes (incluye 3 nuevos de ownership: owner-filter, guest-excluido,
  bind-owner).

## Alternatives considered

- **`FileSystemShopOrderService` monolítico (rewrite del motor en Web).**
  Descartado: re-hostearía la composición catálogo+reservas+pago en Web,
  duplicando lógica y violando ADR 0002. El seam apilado mantiene el motor en
  Application, ignorante del disco.
- **EF/DB para las órdenes.** Descartado: la persistencia del proyecto es
  FileSystem (patrón PHI). SQLite es el camino de escala futuro, detrás del mismo
  seam.
- **Ownership por email server-trusted** (en vez de `memberKey`). El email de la
  sesión es confiable, pero el GUID es el identificador durable/único correcto y
  desacopla el ownership de un email mutable. Se eligió `memberKey`.
- **Gatear `tracking`/`return` con `RequireMember`** (diseño original). Descartado:
  rompería el self-service de invitado (orden sin dueño, `orderRef` bearer). Se
  usa `DenyIfForeignMember` (bloquea cross-member, preserva invitado).
- **Reintentar/forzar el GUID en el claim del cookie de member.** Fuera de
  alcance (tocaría el claims factory de Umbraco); resolver del `Member.Id` vía
  `IMemberService` es local y suficiente.

## References

- doc rector `25` — Punto de estabilización + roadmap de transversales (T0…T9).
- `scratchpad/t1t2-design.md` — diseño de construcción completo (secuencia
  incremental, fan-out, riesgos).
- Código: `Synergos.CMS.Interfaces/IShopOrderStore.cs`,
  `IShopOrderService.cs`; `Synergos.CMS.Application/Services/Impl/{StubShopOrderService,
  InMemoryShopOrderStore,PersistedOrder}.cs`, `Configuration/ShopOrdersSettings.cs`;
  `Synergos.CMS.Web/Services/{FileSystemShopOrderStore,DefaultMemberAccessGate}.cs`,
  `Controllers/ShopCatalogController.cs`, `Composers/{SeamComposer,OptionsComposer}.cs`.
- Memorias: `project_business_logic_t1_t2`, `feedback_umbraco13_member_key_claim`.
