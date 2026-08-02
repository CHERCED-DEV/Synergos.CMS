# Mapa de segregación por vertical (auditoría F3)

> Qué archivos pertenecen a cada vertical, en cada capa. Es el **prerequisito** de los handler
> sets de uSync, del CODEOWNERS y de cualquier "circuito de aplicativo" propio que el arquitecto
> quiera separar a futuro. Conteos verificados: **110** Interfaces · **80** Application/Impl ·
> **37** Controllers · **131** Web/Services · **161** Tests.

## El mapa

| Vertical | Interfaces | Application | Controllers | Web/Services | Tests |
|---|---|---|---|---|---|
| Eventos | 5 | 5 | `EventosController` | 4 | 9 |
| Realty | 5 | 6 | `RealtyController` | 2 | 9 |
| Salud (EHR/PHI) | 17 | 12 | `EhrController`, `HealthcareApiController` | 8 | 19 |
| Gov/Trámites | 6 | 9 | `GovController` | 3 | 9 |
| Booking | 2 † | 4 † | `BookingController` | 2 † | 5 |
| Viajes | 5 (+2†) | 4 (+4†) | `TravelController` | 2 (+2†) | 7 |
| Social/Blogs | 7 | 6 | `Blogs`, `BlogTag`, `BlogRss`, `Comments`, `CommentsModeration`, `NewsSitemap` | 10 | 7 |
| Shop | 10 | 5 | `Shop`, `ShopCatalog` | 17 | 12 |
| Academy | 4 | 5 | `AcademyController` | 1 | 6 |
| Search | 2 | 0 | `SearchController` | 3 | 1 |
| Admin/Dashboard | 7 | 0 | `Admin`, `DashboardApi` | 19 | 12 |
| Membership | 4 | 0 | `Account`, `Member` | 9 | 6 |
| Forms | 4 | 0 | `FormSubmissions` | 10 | 2 |
| Flow | 0 | 0 | `Flow` | 1 | 0 |
| **COMPARTIDO (@core)** | 32 | 26 | 13 (webhook, realtime, bridge, health, sitemaps, page bases, dev) | 40 | 51 |

† = archivo **co-propiedad Booking/Travel** (mismo archivo físico sirve a los dos).

## Infraestructura compartida (3+ verticales) — el "núcleo" real

Lo que **no** puede pertenecer a un equipo de vertical porque lo consumen muchos:

| Seam | # verticales |
|---|---|
| `IMemberAccessGate` | **14** (todos) |
| `IJsonEntityStore` | 10 |
| `IAuditTrailWriter` | 9 |
| `IAnalyticsTracker` | 8 |
| `IPaymentProvider` / `IPriceFormatter` | 7 c/u |
| `IReservationService` / `ICatalogSource` | 6-7 |
| `IMessagingService` / `IOrderTrackingService` / `ITransactionalNotifier` / `IEmailService` / `IRetentionPolicy` | 4-5 c/u |
| Plataforma SSR (`ISynHostEmitter`, `IBundleRegistryClient`, `ICompositionReader`, …) | 14 |
| **`SeamComposer.cs`** | 14 — único punto de wiring, punto de colisión de merges |

## Los 3 verticales más enredados

1. **Booking ⟷ Travel — fusión de hecho.** Booking **no tiene ni un archivo exclusivo** en
   Interfaces ni Application: `IRoomAvailabilityProvider`, `ICancellationPolicyEvaluator`,
   `IStayContentProvider` y sus stubs los comparten los dos controllers. Antes de separarlos en
   "circuitos" hay que decidir si son un vertical o dos.
2. **Shop ⟷ Gov ⟷ Academy ⟷ Eventos ⟷ Travel — el motor de pedido/pago.** `PersistedOrder` lo
   leen `StubShopOrderService` **y** `StubApplicationService` (un trámite gubernamental se
   persiste como un pedido). `NotificationEmission` + `BestEffort` los comparten los seis stubs
   transaccionales. Ese motor es un candidato a "circuito propio" antes que cualquier vertical.
3. **Salud ⟷ Eventos/Booking/Realty — agenda + reserva + pago.** `EhrController` inyecta
   `IReservationService` y `IPaymentProvider`, los mismos de Eventos/Booking. Un solo
   `IMessagingService` sirve la in-basket clínica, la correspondencia Gov y los DMs sociales —
   tres dominios regulatorios distintos sobre un stub.

## Consecuencia para la estrategia de "circuitos propios"

El deseo del arquitecto —separar responsabilidades en aplicativos propios— **choca con la
realidad medida**: los verticales no están aislados, comparten un motor transaccional y un
puñado de seams de plataforma. La secuencia correcta que este mapa habilita:

1. **Primero extraer el núcleo compartido** (motor de pago/pedido, plataforma SSR, member gate)
   como su primer "circuito" — es lo que 5+ verticales necesitan y lo que hoy los acopla.
2. **Después** cada vertical puede salir apoyándose en ese núcleo, empezando por los menos
   enredados (Eventos, Realty, Gov ya tienen archivos casi exclusivos).
3. Booking/Travel se resuelven como decisión de producto (¿uno o dos?), no de código.

Partir un vertical enredado antes de extraer el núcleo reintroduce el acople por otra vía — el
mismo error de forma que el principio 8 evita con el multi-tenant.

## Prerequisito físico: carpetas por vertical

Hoy los verticales se distinguen por **convención de nombre de archivo** dentro de carpetas
planas (`Controllers/`, `Services/Impl/`). Para que el CODEOWNERS de abajo sea robusto —y para
que los handler sets de uSync y cualquier extracción futura sean mecánicos— el paso previo es
mover a subcarpetas (`Services/Impl/Eventos/`, `Web/Services/Salud/`, …). Es una migración
grande y puramente mecánica; va al backlog de F5, no aquí.

## Borrador de CODEOWNERS — NO activado

El CODEOWNERS completo por patrón de archivo vive en
[`04-cierre-auditoria.md`](04-cierre-auditoria.md) como apéndice. **No se activó como
`.github/CODEOWNERS`** a propósito: los equipos `@synergos/eventos`, `@synergos/salud`, etc. aún
no existen, y activarlo haría que cada PR pidiera revisión de equipos fantasma —bloqueando o
ensuciando el flujo—. Se activa cuando los equipos existan y (idealmente) tras la migración a
carpetas; hasta entonces es un plano, no una regla viva. Mientras tanto, `@synergos/core` con
revisión sobre `SeamComposer.cs` y `uSync/` sería el primer subconjunto seguro de activar.

## Huérfanos verificados

- **Directorios con solo `.gitkeep`** (`Web/Models`, `Web/Resolvers`, `Web/ValueConverters`,
  `Web/Config`, `Application/Extensions`): scaffolding intencional, no muerto. Se dejan.
- `SeatMapProjection.cs` / `ISeatMapProvider`: su consumidor real es un partial Razor + el
  proveedor exógeno (ADR 0127), no otro `.cs`. No es huérfano; es la forma esperada.
- `IPaymentRouting.cs`: nombre no coincide con su contenido (solo tiene `PaymentRoutingRule`, que
  se usa). Renombrar es churn; anotado, no tocado.
