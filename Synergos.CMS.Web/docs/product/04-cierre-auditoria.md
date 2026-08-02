# Cierre de la auditoría de arquitectura (F5)

> Boleta final, backlog priorizado por **riesgo real** (no por olor ni por tamaño), y el
> CODEOWNERS completo como apéndice. Documentos hermanos:
> [01 plan](01-plan-auditoria-arquitectura.md) · [02 boleta SOLID](02-boleta-solid.md) ·
> [03 mapa de segregación](03-mapa-segregacion.md).

## Qué se hizo en esta ola de auditoría

Las cinco fases del plan corrieron de un tirón. Lo que arrojó, en una tabla:

| Fase | Resultado |
|---|---|
| F0 Línea base | Medida: capas limpias (0 usings prohibidos), 21/37 controllers sin test, hotspots identificados |
| F1 Gate de capas | `LayerRuleTests` (4) — el ADR 0002 pasa de costumbre a invariante ejecutable |
| F2 Boleta SOLID | 7 hotspots + 3 controles calificados con evidencia `archivo:línea` |
| F3 Mapa segregación | 14 verticales mapeados; núcleo compartido identificado; Booking≡Travel |
| F4 Refinar | **2 defectos de seguridad cerrados** + código muerto borrado + pared del demo testeada |
| F5 Cierre | este documento |

**Los dos hallazgos que justificaron toda la auditoría** fueron defectos de seguridad vivos que
ningún test cubría, ambos corregidos con pruebas que fijan la propiedad:

1. **IDOR en Shop** (wishlist + mensajería): identidad tomada del cliente. Cerrado —
   `RequireMemberEmail()`, 9 tests. El propio archivo lo declaraba cerrado en `Orders` y lo tenía
   abierto al lado.
2. **Downcast 2FA** (`AccountController:545`): otra impl dejaba al member sin recovery codes en
   silencio. Cerrado — los códigos viajan en el resultado, 2 tests.

## Backlog priorizado por riesgo real

El orden es por **daño si no se hace**, no por esfuerzo ni por lo feo que se ve.

### Alto — toca seguridad, datos o corrección
1. ~~**AdminController: 29 actions con auth repetida a mano.**~~ **HECHO.**
   `RequireRolesAttribute` a nivel de clase; los 29 checks manuales removidos. El default pasa
   de abierto a cerrado. Verificado end-to-end: anónimo contra las 8 rutas reales de admin →
   las 8 deniegan. 9 tests, incluido el que clava que el `[AllowAnonymous]` de la CLASE no
   desactiva el filtro (el bug que casi se cuela: habría abierto las 29 con build verde).
2. **Antiforgery en los POST destructivos de admin** (`gdpr-erase`, `members/{key}/delete`,
   `bulk-*`). Hoy no lo llevan por decisión documentada, pero ahora que la auth es declarativa
   es el siguiente eslabón del mismo flujo. **Nuevo, sale de #1.**
3. **La semántica 401 vs 403 del dashboard.** Un autenticado SIN rol recibe hoy redirección al
   login (bucle: ya inició sesión). Se preservó a propósito al refactorizar #1 —no mezclar
   estructura con semántica— pero queda como corrección pendiente.

### Medio — deuda que crece con cada equipo nuevo
3. ~~**`SeamComposer.cs` → partials por vertical.**~~ **HECHO.** Partido en 10 clases
   parciales por dominio; el archivo principal quedó en 59 LOC (orquestador). Orden de
   registro preservado exacto (por eso los notificadores composite siguen registrándose al
   final). Verificado con huella de las 483 líneas + el gate de reconstrucción arrancando el
   contenedor DI completo. El corte PERFECTO por vertical (sin dominios repartidos en dos
   archivos por el intercalado del original) queda pendiente — necesita reordenar registros,
   seguro solo con un gate de snapshot de registros.
4. **Extraer derivación de copy UI a los `*ContentRules` que ya existen.** Realty
   (`BuildSubtitle`/`BuildBadges` → `PropertyContentRules`), Eventos (`DeriveStatus`/`Badges` →
   `EventContentRules`). Mecánico, les da los tests que hoy no tienen.
5. **`ICommentRepository` (12 métodos) → split lectura/escritura/moderación**, siguiendo el
   precedente `IMemberRosterReader`/`Writer` del propio repo.
6. **El `switch` de selección de PSP en `SeamComposer:245`** → `AddPaymentEngine(...)`. Es
   política viviendo en el composer; agregar un proveedor lo toca (OCP).

### Bajo — higiene, sin riesgo
7. **Migración a carpetas por vertical** (`Services/Impl/Eventos/`, …). Prerequisito del
   CODEOWNERS robusto y de los handler sets. Grande y mecánico.
8. **`BlogsController`: `sync-over-async` en bucle** (`L759`) → recibir conteos ya resueltos al
   extraer `ComputeTrending` (cae junto con #4).
9. **`IPaymentRouting.cs`: renombrar** al tipo que contiene (`PaymentRoutingRule`).
10. **`IDashboardReadModel`**: abstracción prematura documentada. Decisión del arquitecto —
    dejar o colapsar contra su único consumidor.

### Explícitamente NO en el backlog
- **Reescribir `DevContentFiller` (4581 LOC).** Tooling tras flag; feo pero sin riesgo. No vale
  el costo mientras haya cualquier item de riesgo Alto abierto.
- **`EhrController`.** Es demo `[DevSeedOnly]` con su pared testeada. Convertirlo a producción
  (auth por rol + portal por pertenencia) es **decisión de producto**, no refactor — el propio
  archivo lo documenta.

## Estado de gates al cierre

| Gate | Estado |
|---|---|
| `dotnet test` | **1608 passing** (0 fallos) |
| `usync-audit` | 0 errores, 0 warnings |
| `usync-rebuild` (ADR 0128) | 880/880 ítems, DB derivable |
| `check-css-parity` | 0 orphans |
| `LayerRuleTests` (F1) | 4/4 — capas limpias vigiladas |

## La lectura honesta de la boleta

**La arquitectura de capas es sólida y ahora está vigilada.** El ADR 0002 se cumple de verdad, y
`LayerRuleTests` lo mantiene así. El patrón seam+stub hizo que 118 archivos de tests de services
fueran baratos de escribir.

**La deuda se concentra donde el código quedó Umbraco-dependiente** — los controllers. No es
casualidad: la arquitectura hizo fácil testear lo puro y dejó lo acoplado sin red. Los dos
agujeros de seguridad vivían exactamente ahí, en superficie HTTP que ningún test tocaba. La
mitigación estructural es el fix #1 del backlog (auth declarativa), que además abre esos
controllers a tests.

**"Grande" resultó un mal predictor.** De los 7 controllers grandes, 3 no violaban SRP (grandes
por DTOs anidados). El peor riesgo —el IDOR— estaba en un endpoint corto al lado de uno correcto.
Por eso la boleta califica por evidencia, no por LOC.

---

## Apéndice — CODEOWNERS (borrador, NO activado)

Activar cuando existan los equipos y (idealmente) tras la migración a carpetas del backlog #7.
Primer subconjunto seguro de activar hoy: solo las líneas de `@synergos/core` sobre
`SeamComposer.cs` y `uSync/`.

```gitignore
# La ÚLTIMA coincidencia gana. @core es catch-all; los verticales lo sobrescriben.
*                                                   @synergos/core

# Plataforma / build / schema
/Synergos.CMS.Web/Program.cs                        @synergos/core
/Synergos.CMS.Web/Composers/                        @synergos/core
/Synergos.CMS.Web/Composers/SeamComposer.cs         @synergos/core
/Synergos.CMS.Web/uSync/                            @synergos/core @synergos/schema
/Synergos.CMS.Tests/Architecture/                   @synergos/core

# Pagos (7 verticales dependen)
/Synergos.CMS.Interfaces/IPayment*.cs               @synergos/core @synergos/pagos
/Synergos.CMS.Web/Controllers/PaymentWebhookController.cs @synergos/pagos @synergos/core

# Verticales (patrón por nombre; frágil hasta la migración a carpetas)
/Synergos.CMS.Web/Controllers/EventosController.cs   @synergos/eventos
/Synergos.CMS.Web/Controllers/RealtyController.cs     @synergos/realty
/Synergos.CMS.Web/Controllers/EhrController.cs        @synergos/salud @synergos/security
/Synergos.CMS.Web/Controllers/HealthcareApiController.cs @synergos/salud @synergos/security
/Synergos.CMS.Web/Controllers/GovController.cs        @synergos/gov
/Synergos.CMS.Web/Controllers/BookingController.cs    @synergos/booking
/Synergos.CMS.Web/Controllers/TravelController.cs     @synergos/viajes
/Synergos.CMS.Web/Controllers/{Blogs,BlogTag,BlogRss,Comments,CommentsModeration}Controller.cs @synergos/social
/Synergos.CMS.Web/Controllers/Shop*.cs               @synergos/shop
/Synergos.CMS.Web/Controllers/AcademyController.cs    @synergos/academy
/Synergos.CMS.Web/Controllers/SearchController.cs     @synergos/search
/Synergos.CMS.Web/Controllers/{Admin,DashboardApi}Controller.cs @synergos/admin
/Synergos.CMS.Web/Controllers/{Account,Member}Controller.cs @synergos/membership
```

> El CODEOWNERS por-archivo completo (Interfaces + Application + Services + Tests de cada
> vertical) lo produjo el mapa de F3; se integra aquí cuando la migración a carpetas lo vuelva
> mantenible. Por patrón de nombre plano, hoy sería un documento de cientos de líneas frágil a
> cada archivo nuevo mal nombrado — de ahí la recomendación de carpetas primero.
