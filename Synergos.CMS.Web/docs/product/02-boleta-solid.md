# Boleta SOLID — Synergos.CMS (auditoría F2)

> Aplicación de la rúbrica del [plan de auditoría](01-plan-auditoria-arquitectura.md) a los
> hotspots + controles. Cada veredicto cita `archivo:línea`. Sin promedios: la nota que importa
> es la del peor archivo, no la media. Lo que se marcó como defecto accionable **ya se corrigió
> en F4** (ver la última columna); lo demás es backlog priorizado en [F5](04-cierre-auditoria.md).

## Veredicto por controller (SRP)

| Controller | LOC | Ctor | Veredicto | Evidencia en una frase | Estado |
|---|---|---|---|---|---|
| **EhrController** | 1024 | 11 | **VIOLA** | genera guías clínicas de tamizaje desde `GetHashCode()` (`L583-602`) y expone 9 lecturas de PHI direccionadas por parámetro del caller sin comprobar identidad — pero es demo `[DevSeedOnly]`, no producción | F4: pared testeada |
| **ShopCatalogController** | 1216 | 10 | **VIOLA** | la política de identidad difiere entre endpoints vecinos — `Orders` (`L561`) exige sesión, la wishlist de al lado aceptaba el email de quien preguntara | **F4: IDOR cerrado** |
| **AdminController** | 1144 | 12 | **VIOLA** | dos serializadores CSV a mano escribiendo bytes en `Response.Body` (`L239-267`, `L877-896`) conviven con roster de members y flujo GDPR | backlog |
| **BlogsController** | 988 | 9 | CUMPLE CON COSTO | auth impecable (ownership real en `Thread` `L370`), pero ~120 líneas de scoring/métricas en Web + un `sync-over-async` en bucle (`L759`) | backlog |
| **EventosController** | 854 | 7 | CUMPLE CON COSTO | grande por DTOs+mappers (`L628-854` son records); el cuerpo real son ~460 LOC bien delegadas; derivaciones puras deberían ir a `EventContentRules.cs` que está al lado | backlog |
| **GovController** | 810 | 10→8 | CUMPLE CON COSTO | el patrón sano aplicado a un dominio grande; único cargo objetivo: 2 deps inyectadas y nunca leídas | **F4: deps muertas removidas** |
| **RealtyController** | 754 | 8 | CUMPLE CON COSTO | 13 endpoints delegados, guards uniformes; 35 LOC de copy UI que pertenecen a `PropertyContentRules.cs` (ya existe con la firma exacta) | backlog |

### Controles — el patrón sano SÍ existe en el repo

| Controller | LOC | Veredicto | Por qué es el molde |
|---|---|---|---|
| **HealthcareApiController** | 202 | **CUMPLE** | mismo dominio PHI que Ehr, 1/5 del tamaño, **12/12 endpoints gatean** con `IPhiAccessGuard` guard-first; `Me` resuelve la identidad de la sesión, nunca de un parámetro (`L67-90`) |
| **DashboardApiController** | 131 | **CUMPLE** | `Deny()` en los 7 endpoints, con auditoría; es el molde que Shop/Gov/Eventos citan por nombre |
| **BlogTagController** | 51 | **CUMPLE** | 1 endpoint, normaliza y delega |

## Los principios, transversal

### SRP — sistémico en un eje, individual en otro
Tres controllers grandes (Eventos, Realty, Gov) **no violan SRP en lo esencial**: son grandes
porque los DTOs de contrato viven como tipos anidados, no porque mezclen razones de cambio.
Grande ≠ VIOLA. Lo que **sí** se repite como plantilla:
1. El controller como **fachada de dominio completo** en vez de por recurso → ctors de 8-12
   seams que arrastran contextos ajenos (mensajería + wishlist dentro de Tienda, GDPR + CSV
   dentro de Admin, facturación + In Basket dentro de EHR).
2. **Derivación de copy/vocabulario UI dejada en el controller** aunque el repo ya inventó y
   probó el destino (`Web/Services/Catalog/*ContentRules.cs`, 1790 LOC + 1573 de tests). En el
   mismo commit conviven `PropertyContentRules.BuildSpecs` y `RealtyController.BuildSubtitle`.
3. **Autorización por convención repetida a mano** en vez de por filtro declarativo. Funciona
   mientras alguien se acuerde; falla en silencio cuando no — y ya falló (el IDOR de Shop).

### OCP — mayormente bien
Agregar un vertical **no toca un registro/enum central** — no hay `switch (vertical)` ni lista
maestra (`SeamComposer.cs`, verificado). El único punto de edición inevitable es un bloque en
`SeamComposer` (candidato a partials, F3). `CatalogSettings` por diccionario (`SeamComposer.cs:1319`)
es OCP bien hecho. **Excepción real**: el `switch` de selección de PSP en el bloque de pagos
(`SeamComposer.cs:245`) es política, no wiring — agregar un proveedor lo toca. Backlog.

### LSP — un defecto grave, ya cerrado
`AccountController:545` hacía `_twoFactor is UmbracoMemberTwoFactorService impl` para sacar los
recovery codes por un canal lateral: otra implementación dejaba al member enrolado y **sin
códigos, sin error**. **F4 lo cerró** devolviéndolos en el resultado del método. Los stubs, en
cambio, respetan sus contratos: `StubBundleRegistryClient` retorna null-siempre y eso *es* el
contrato documentado — LSP correcto.

### ISP — un caso claro para backlog
`ICommentRepository` (12 métodos): ningún consumidor usa más de 7/12; la partición es limpia
en lectura pública (2) / escritura pública (2) / moderación (8). El repo ya hizo exactamente
este split en `IMemberRosterReader`/`Writer` y lo documentó como "ISP-clean"
(`SeamComposer.cs:696`). `ICommentRepository` es la excepción pendiente. Backlog.

`IDashboardReadModel` es abstracción prematura (1 impl, 1 consumidor que la usa 6/6, y su doc
cita un segundo consumidor que no existe). No es defecto —es cohesiva— pero contradice la regla
anti-abstracción de `CLAUDE.md`. Se anota, no se remueve: quitar una interfaz tiene más radio
que el beneficio, y es decisión del arquitecto.

### DIP — sano en lo estructural
**Cero `new` de impls** en `Web/Services` y `Controllers` (verificado sobre 129 clases): todo
el `new` de implementaciones está confinado a `SeamComposer`, que es donde corresponde. Tres
fugas de tipo concreto detectadas; la única con impacto de seguridad (`AccountController:545`)
se cerró en F4. Las otras dos (`RealtimeController` → `SseRealtimeHub`,
`DashboardSnapshotFlushHostedService` → `InMemoryMetricsProjectionStore`) son ISP-al-revés
(la interfaz no expone el método que el consumidor necesita); aceptable bajo la regla
anti-abstracción, anotado en backlog.

## Correcciones a la propia línea base (la regla aplica contra mí)

- **"27 de 37 controllers sin test" estaba inflado.** La primera medición buscó
  `<X>ControllerTests.cs`; el conteo real por referencia de clase es **21 de 37**. BlogsController
  y AcademyController SÍ tienen cobertura con otros nombres de suite.
- **EhrController no era "el hotspot PHI sin test".** Es demo `[DevSeedOnly]`; el PHI real
  (`HealthcareApiController`) sí está testeado. El riesgo real era la pared sin tests — cerrado en F4.
- **"IPaymentRouting.cs interfaz muerta" era falso positivo** (mío, repitiendo al auditor). El
  archivo solo tiene `PaymentRoutingRule`, que se usa. Restaurado.
