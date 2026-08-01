# Dominio: Verticales de agenda y aprendizaje

## Resumen ejecutivo

El vertical **Eventos** es, con diferencia, el más maduro de los tres: motor de compra
completo (catálogo → checkout → confirm → e-ticket QR **firmado** → check-in verificado),
persistencia real vía `IJsonEntityStore`, notificaciones, tracking, transferencia de
ticket auditada y guardas de rol server-trusted en 5 de 9 endpoints. Tiene además la
única rebanada T5 (Ola A) de este dominio — `eventPage` + `UmbracoEventCatalogSource` —
pero el flag `Synergos:Catalog:Sources:Events` es **INERTE de verdad**: está registrado
pero nada lo inyecta (`StubEventCatalogProvider` no toma `ICatalogSource<EventSummary>`
en su constructor), y aunque lo inyectara, `eventPage` solo modela el resumen (12 props),
no tiers ni seat-map, así que una ficha "cms" no tendría qué vender. El propio código lo
documenta en el composer.

**Educación** es un LMS completo en superficie (catálogo, matrícula con pago/gratis,
progreso, certificado, panel de instructor) pero 100% DEMO: sin DocType, sin flag, sin
adapter CMS. Tiene una seam huérfana real: `ICertificateService.VerifyAsync` (verificación
pública del certificado) está implementada pero **ningún controller la expone** — el
`VerifyUrl` que el propio certificado promete (`/academy/verify/{certId}`) es un enlace
muerto, y el índice de certificados emitidos vive en un diccionario en memoria (no
sobrevive un reinicio, aunque tuviera consumidor).

**Booking/Reservas** resultó ser dos subsistemas distintos con el mismo vocabulario:
(a) `BookingController` es el motor de **reservas de habitación estilo hotel** (Hoteles),
100% DEMO sin ninguna rebanada CMS ni tests de controller; y (b) `IAppointmentScheduler`
("citas") es en realidad el agendamiento **clínico** de Healthcare (ADR 0098), consumido
solo por `HealthcareApiController`, con persistencia real sobre `IPhiStore` y lock
anti-overbooking — no tiene nada que ver con `/api/booking`. Se documentan ambos porque
el encargo los listó, pero son dos productos sin relación funcional.

## Vertical: Eventos

### Catálogo / agenda (búsqueda + ficha)

- **Madurez**: DEMO (motor) con una rebanada T5 registrada pero **inerte**.
- **Seams**: `IEventCatalogProvider` (`Synergos.CMS.Interfaces/IEventCatalogProvider.cs`).
- **Implementación**: `StubEventCatalogProvider`
  (`Synergos.CMS.Application/Services/Impl/StubEventCatalogProvider.cs:23-439`) — catálogo
  sembrado en memoria (4 eventos: festival general, sinfónico reserved con seat-map,
  conferencia, teatro infantil), `ICatalogIndex<EventSummary>` para búsqueda/orden.
  `PublishEventAsync` (línea 98) agrega eventos nuevos a un `ConcurrentDictionary` **en
  memoria del proceso** — no usa `IJsonEntityStore`, así que un evento publicado por un
  organizador se pierde en un reinicio.
- **Rebanada T5 (Ola A)**: `UmbracoEventCatalogSource`
  (`Synergos.CMS.Web/Services/Catalog/UmbracoEventCatalogSource.cs`) — proyecta
  `eventPage` → `EventSummary` con guardas fail-closed (omite sin slug/título/fecha,
  valida precio SOLO-DÍGITOS, ancla `eventStart` a UTC-5 Bogotá, valida geo en rango).
  Código real y correcto, **pero sin consumidor**: registrado como
  `ICatalogSource<EventSummary>` en `SeamComposer.cs:808-815`, y ningún servicio del
  proceso pide esa interfaz por DI — a diferencia de Tienda, donde
  `StubProductCatalogProvider` SÍ inyecta `ICatalogSource<CatalogProduct>`
  (`SeamComposer.cs:315`), `IEventCatalogProvider` sigue siendo siempre
  `StubEventCatalogProvider` (`SeamComposer.cs:792`), que lee su propio diccionario
  interno.
- **Persistencia**: seed en memoria (`StubEventCatalogProvider`); `eventPage` si el
  editor lo autora (Umbraco Content, nunca leído en runtime salvo por el source huérfano).
- **Superficie HTTP**: `GET /api/eventos/events?q&category&city&sort` (público),
  `GET /api/eventos/event/{id}` (público) — `EventosController.cs:145-233`.
- **Schema CMS**: `eventPage` (`uSync/v9/ContentTypes/eventpage.config`) — 11 props en
  tab Contenido: `eventSlug`, `eventTitle`, `eventCategory` (TextBox, no Dropdown —
  desviación consciente de ADR 0021, documentada en la propia `<Description>`),
  `eventCity`, `eventVenue`, `eventStart`, `eventImage` (MediaPicker3), `eventPriceFrom`
  (TextBox, SOLO DÍGITOS), `eventMode` (TextBox: general|reserved), `eventLat`/`eventLng`.
  **No modela tiers ni seat-map** — por eso el flag no puede cerrar el círculo. Sin
  template (dato de catálogo, no página navegable).
- **UI/CDN**: `elementSynEventos` / `<synergos-eventos>` — módulo Angular real en
  `synergos.ui/platforms/angular/apps/elements/modules/eventos/` con cliente HTTP contra
  los 9 endpoints reales (`eventos-api.client.ts`) y degradación a mock solo si el
  backend falla. Manifest en `dist/manifests/eventos/{angular,react,svelte,vanilla}`,
  pero **sin bundle real** (`entryScript: main.js` no existe en `dist/`) — el
  `StubBundleRegistryClient` de CMS (ADR 0012, bloqueado externamente) sigue devolviendo
  null y el partial emite un placeholder HTML comment.
- **Flags**: `Synergos:Catalog:Sources:Events` = `demo` (`appsettings.Development.json:107`).
  **Voltear a `cms` NO cambia nada hoy** — verificado por código, no por comentario: nadie
  resuelve `ICatalogSource<EventSummary>`. Es exactamente el caso pedido por el encargo.
  `Synergos:Catalog:Scopes:Events` = `eventos` (brandKey) — sí lo usa
  `UmbracoEventCatalogSource`, pero solo se ejecuta si algo la invoca, cosa que no ocurre.
- **Tests**: `StubEventCatalogProviderTests.cs` (9 tests). Sin tests de
  `UmbracoEventCatalogSource` (no se encontró `UmbracoEventCatalogSourceTests.cs`).
- **Huecos**:
  - `Synergos.CMS.Web/Composers/SeamComposer.cs:792` — `IEventCatalogProvider` fijo a
    `StubEventCatalogProvider`, ignora cualquier fuente CMS.
  - `Synergos.CMS.Web/Composers/SeamComposer.cs:808-815` — `ICatalogSource<EventSummary>`
    registrado pero sin ningún inyector; comparar con `SeamComposer.cs:304-320` (Shop) que
    sí lo consume.
  - `Synergos.CMS.Application/Services/Impl/StubEventCatalogProvider.cs:31` — catálogo
    (incluye lo publicado por organizadores) en `ConcurrentDictionary` **no durable**.

### Checkout → pago → e-ticket QR firmado

- **Madurez**: **VIVO** (motor transaccional real, persistencia real, firma criptográfica
  real). Es la capacidad más terminada de todo el dominio.
- **Seams**: `IEventTicketingService`, `ITicketSigner`
  (`Synergos.CMS.Interfaces/IEventTicketingService.cs`, `ITicketSigner.cs`).
- **Implementación**: `StubEventTicketingService`
  (`Synergos.CMS.Application/Services/Impl/StubEventTicketingService.cs`, 794 líneas) —
  `CheckoutAsync` (línea 144) resuelve tier/precio/aforo REAL del catálogo (anti-tampering),
  aparta cada unidad vía `IReservationService.HoldItemAsync`, abre UNA sesión de pago;
  `ConfirmAsync` (línea 289) captura el pago, confirma las reservas, emite tickets con QR
  **firmado** (`_signer.Sign`, línea 725), avanza el `IOrderTrackingService`
  (paid→confirmed→attended) y notifica al comprador (best-effort). Idempotente por
  `orderRef`. `TransferTicketAsync` (línea 417) rota el QR (bump de `QrVersion`) y audita
  vía `IAuditTrailWriter`.
- **Firma del QR**: `HmacTicketSigner`
  (`Synergos.CMS.Application/Services/Impl/HmacTicketSigner.cs`) — HMAC-SHA256, formato
  `SYN-TKT-{eventId}-{ticketId}-v{qrVersion}.{hmac-hex}`, comparación en tiempo constante
  (`CryptographicOperations.FixedTimeEquals`). Llave resuelta por
  `TicketSigningKeyProvider` (`Synergos.CMS.Web/Services/TicketSigningKeyProvider.cs`):
  `Synergos:Events:TicketSigningSecret` si está configurado, o generada una vez y
  guardada CIFRADA (`IDataProtector`) en `IJsonEntityStore` bajo `ticket-signing-v1` — así
  el QR sobrevive un reinicio (ADR 0110 documenta que esto se verificó reiniciando el CMS
  en vivo). Registrado como `LazyTicketSigner` en `SeamComposer.cs:687`.
- **Check-in verificado**: `MarkCheckedInDetailedAsync`
  (`StubEventTicketingService.cs:591-641`) — **solo admite un token con firma válida**
  (`_signer.Verify`); rechaza el `ticketId` suelto, un token manipulado, y un token de
  `QrVersion` anterior (anti-reventa tras transferencia). Antes de ADR 0110 esto comparaba
  contra el id plano y **nunca miraba el QR** — el bug real que motivó el ADR.
- **Persistencia**: `IJsonEntityStore` sobre `App_Data/syn-event-orders/` — la orden, sus
  unidades (tier/asiento/holder/`QrVersion`/`CheckedIn`) y el `PaymentSessionId`
  sobreviven un reinicio (T1/ADR 0105).
- **Superficie HTTP**: `POST /api/eventos/checkout` (público, sin auth — compra como
  invitado), `POST /api/eventos/confirm` (público), `GET /api/eventos/tickets` (🔒
  member — email server-trusted, sin `?holder=`), `POST /api/eventos/ticket/{id}/transfer`
  (🔒 member + ownership check) — todo en `EventosController.cs:238-420`.
- **Schema CMS**: ninguno (los tiers/precio salen del catálogo en memoria, no de
  `eventPage`).
- **UI/CDN**: mismo `elementSynEventos`; el flujo de checkout/wallet vive en el mismo
  módulo Angular.
- **Flags**: ninguno propio; depende de `Synergos:Events:TicketSigningSecret` (opcional).
- **Tests**: `StubEventTicketingServiceTests.cs` (14), `HmacTicketSignerTests.cs` (12),
  `TicketSigningKeyProviderTests.cs` (6), más los de auth en
  `EventosControllerTests.cs` (12, ver abajo).
- **Huecos**:
  - Ninguno grave. `checkout`/`confirm` siguen sin auth de member — es una decisión
    documentada (compra como invitado), no un olvido.

### Manage (dashboard organizador) + check-in operativo

- **Madurez**: VIVO.
- **Seams**: `IEventManagementService` (`Synergos.CMS.Interfaces/IEventManagementService.cs`).
- **Implementación**: `StubEventManagementService`
  (`Synergos.CMS.Application/Services/Impl/StubEventManagementService.cs`) — NO duplica
  estado: `GetManageAsync` (línea 34) compone aforo del catálogo + tickets confirmados del
  `StubEventTicketingService` concreto (DIP); `CheckInAsync` delega a
  `MarkCheckedInDetailedAsync`; `CreateEventAsync` (línea 66) valida el borrador y publica
  vía `IEventCatalogProvider.PublishEventAsync` (por eso un evento creado por un
  organizador tampoco sobrevive un reinicio — ver hueco arriba).
- **Superficie HTTP**: `GET /api/eventos/manage/{eventId}` (🔒 `organizador,admin`),
  `POST /api/eventos/checkin` (🔒 `organizador,admin` — desde T9/ADR 0110 exige además
  token firmado, no solo rol), `POST /api/eventos/event` (🔒 `organizador,admin`) —
  `EventosController.cs:98-109, 305-463`.
- **Nota sobre ADR 0110**: el ADR (fecha 2026-07-18) dice textualmente *"El resto de
  `EventosController` sigue sin auth (`checkout`, `confirm`, `manage/{eventId}`,
  `event`)"*. **Esto ya no es cierto para `manage`/`event`**: el código actual SÍ exige
  `RequireOrganizer()` en ambos (confirmado leyendo `EventosController.cs` línea por
  línea) y hay tests explícitos (`Manage_Anonymous_Returns401_AndSkipsSeam`,
  `CreateEvent_Anonymous_Returns401`) que lo verifican. El ADR quedó desactualizado por
  un commit posterior (T2-Eventos), documentado solo en el código/tests, no en un ADR
  nuevo — vale la pena que el arquitecto lo sepa si audita ADRs contra código.
- **Tests**: `StubEventManagementServiceTests.cs` (7) + los 12 de `EventosControllerTests.cs`
  (cubren 401/403/rol para manage, checkin, createEvent, tickets, transfer; y que
  `Events` es público).
- **Huecos**: sin realtime más allá del check-in (`RealtimeController.EventosCheckinPrefix`,
  best-effort, no verificado en detalle aquí).

## Vertical: Educación

### Catálogo de cursos (búsqueda + PDP-curso)

- **Madurez**: DEMO. Sin flag, sin DocType, sin adapter CMS — a diferencia de Eventos, ni
  siquiera hay una rebanada T5 inerte; el composer no menciona ningún
  `ICatalogSource<CourseSummary>`.
- **Seams**: `ICourseCatalogProvider` (`Synergos.CMS.Interfaces/ICourseCatalogProvider.cs`).
- **Implementación**: `StubCourseCatalogProvider`
  (`Synergos.CMS.Application/Services/Impl/StubCourseCatalogProvider.cs`, 423 líneas) —
  catálogo sembrado (`AcademyDemoSeed.cs`, 249 líneas: varias categorías × cursos ×
  módulos × lecciones). Polimorfismo real con Blogs: cada lección se siembra en
  `IContentStream` con `Kind=lesson` (DIP, no instancia el módulo Blogs). `PublishCourseAsync`
  agrega a `_published` (`ConcurrentDictionary<string, AcademyDemoSeed.SeedCourse>`,
  línea ~50) — **en memoria, no durable**, mismo patrón (y mismo hueco) que
  `StubEventCatalogProvider.PublishEventAsync`.
- **Persistencia**: seed en memoria + `IContentStream` (que sí es durable para el
  contenido de las lecciones, pero el catálogo de cursos en sí no).
- **Superficie HTTP**: `GET /api/academy/courses`, `GET /api/academy/course/{id}`
  (públicos) — `AcademyController.cs:96-177`.
- **Schema CMS**: **ninguno**. No existe `coursePage` en `uSync/v9/ContentTypes/` (se
  verificó con `find` — solo aparecen `elementsynacademy.config` para el bloque CDN-host,
  cero DocTypes de dominio de curso/lección/matrícula).
- **UI/CDN**: `elementSynAcademy` / `<synergos-academy>`, módulo Angular real
  (`academy-api.client.ts`, `academy-fulfillment.strategy.ts`). Manifest sin bundle real
  (mismo estado que Eventos, bloqueo CDN transversal).
- **Flags**: ninguno.
- **Tests**: `StubCourseCatalogProviderTests.cs` (11).
- **Huecos**:
  - `Synergos.CMS.Application/Services/Impl/StubCourseCatalogProvider.cs` (campo
    `_published`) — cursos publicados por instructores no sobreviven reinicio.
  - No hay adapter CMS ni flag: si el arquitecto espera un `Synergos:Catalog:Sources:Education`
    análogo a Eventos, **no existe todavía**, ni siquiera como registro inerte.

### Matrícula (enroll → pagar → confirmar) + progreso

- **Madurez**: VIVO (motor transaccional + persistencia real).
- **Seams**: `IEnrollmentService`, `IEnrollmentMetrics`
  (`Synergos.CMS.Interfaces/IEnrollmentService.cs`).
- **Implementación**: `StubEnrollmentService`
  (`Synergos.CMS.Application/Services/Impl/StubEnrollmentService.cs`, 603 líneas) —
  `EnrollAsync` resuelve precio real del catálogo; curso de pago abre sesión
  `IPaymentProvider`, curso gratis activa matrícula de inmediato. `ConfirmAsync` idempotente.
  `MarkLessonAsync`/`GetProgressAsync` llevan progreso por (alumno, curso). Alimenta un
  `IOrderTrackingService` propio (`AcademyPipeline`, no comparte el de Tienda). Expone
  `IEnrollmentMetrics.GetCourseStatsAsync` que el catálogo COMPONE (property injection
  post-construcción, `SeamComposer.cs:458-460`) para el panel de instructor.
- **Persistencia**: `IJsonEntityStore` — resourceTypes `enrollments` y `course-progress`
  (`App_Data/syn-enrollments/`, `App_Data/syn-course-progress/`). Sobrevive reinicio.
- **Superficie HTTP**: `POST /api/academy/enroll` (🔒 member vía `RequireStudent()`),
  `POST /api/academy/confirm`, `GET/POST /api/academy/progress` (🔒 student server-trusted)
  — `AcademyController.cs:178-326`.
- **Schema CMS**: ninguno (igual que el catálogo).
- **Flags**: ninguno.
- **Tests**: `StubEnrollmentServiceTests.cs` (15), `AcademyOla5SmokeTests.cs` (9),
  `AcademyAndTravelAuthTests.cs` (11 — cubre auth de Academy Y Travel en el mismo archivo).
- **Huecos**: ninguno grave identificado; es la capacidad más sólida de Educación.

### Certificado verificable

- **Madurez**: **SÓLO SEAM** para la mitad pública del contrato (verificación); DEMO para
  la mitad privada (obtener el propio certificado).
- **Seams**: `ICertificateService` (`Synergos.CMS.Interfaces/ICertificateService.cs`) —
  `GetAsync` (privado, del alumno) + `VerifyAsync` (público, por id de credencial).
- **Implementación**: `StubCertificateService`
  (`Synergos.CMS.Application/Services/Impl/StubCertificateService.cs`, 120 líneas).
  `GetAsync` (línea 47) emite el certificado solo al 100% de progreso, con id determinista
  (FNV-1a de curso+alumno) y lo registra en `_issued`
  (`ConcurrentDictionary<string, IssuedCertificate>`, línea 32 — **en memoria, no
  `IJsonEntityStore`**). `VerifyAsync` (línea 82) re-verifica el progreso actual, no un
  sello mudo — pero solo puede encontrar algo si `GetAsync` ya lo registró en ESTA
  instancia del proceso.
- **El hueco real**: `VerifyAsync` está implementado, registrado en DI
  (`SeamComposer.cs:466-469`), tiene comentario de diseño ("verificación PÚBLICA... no
  requiere identificar al solicitante") — pero **ningún controller lo invoca**. Se
  verificó con `grep -rn "VerifyAsync\(" --include=*.cs` sobre todo el repo Web: el único
  llamador de `ICertificateService` es `AcademyController`, y su único endpoint de
  certificado es `GET /api/academy/certificate?course=` (línea 327), que llama `GetAsync`
  (privado, requiere sesión), nunca `VerifyAsync`.
  - Consecuencia visible: `VerifyUrl` = `/academy/verify/{certId}`
    (`StubCertificateService.cs:75` y también hardcodeado de forma independiente en
    `StubEnrollmentService.cs:327` — dos sitios que arman la misma URL) **no resuelve a
    ninguna ruta**: `grep -rn "academy/verify"` en todo el repo solo encuentra esas dos
    literales de construcción de string, ni un controller ni una vista lo sirven. Un
    empleador que reciba ese link (el caso de uso explícito en el comentario de
    `AcademyController.cs:334-338`) obtiene 404.
- **Persistencia**: en memoria — un reinicio del CMS vacía `_issued`, así que incluso si
  se agregara el endpoint de verificación hoy, un certificado emitido antes del reinicio
  dejaría de verificar (aunque el alumno siga al 100%, porque el índice por id se perdió,
  no el progreso — `GetAsync` lo re-emitiría con el mismo id determinista, pero
  `VerifyAsync` solo mira `_issued`).
- **Superficie HTTP**: `GET /api/academy/certificate?course=` (🔒 student) — SIN endpoint
  de verificación pública.
- **Tests**: cubiertos indirectamente por `AcademyOla5SmokeTests.cs` y
  `AcademyAndTravelAuthTests.cs` (no hay archivo dedicado a `ICertificateService`).
- **Huecos**:
  - `Synergos.CMS.Web/Controllers/AcademyController.cs` — falta un
    `GET /api/academy/verify/{certificateId}` (o equivalente) que llame `VerifyAsync`.
  - `Synergos.CMS.Application/Services/Impl/StubCertificateService.cs:32` — `_issued` no
    durable.

### Panel de instructor (autoría + métricas)

- **Madurez**: DEMO.
- **Seams**: `ICourseCatalogProvider.GetForInstructorAsync` / `PublishCourseAsync`,
  `IEnrollmentMetrics`.
- **Implementación**: compone `StubCourseCatalogProvider` + `StubEnrollmentService` (ver
  arriba). Rol `instructor` sembrado solo por tooling dev
  (`Synergos.CMS.Web/Services/DevMemberRoleSeeder.cs:43`, gated por
  `Synergos:DevSeed:Enabled`) — sin ese seeder, nadie puede demostrar el panel en un
  ambiente limpio.
- **Superficie HTTP**: `GET /api/academy/instructor/courses` (🔒 `instructor,admin`),
  `POST /api/academy/course` (🔒 `instructor,admin`) — `AcademyController.cs:356-...`.
- **Huecos**: cursos publicados no durables (ya señalado arriba).

## Vertical: Booking/Reservas

> **Aclaración de alcance, verificada por código.** El encargo agrupa bajo "Booking" tres
> seams (`IAppointmentScheduler`, `ICancellationPolicyEvaluator`, `IRoomAvailabilityProvider`)
> que en el código **no son la misma cosa**: `ICancellationPolicyEvaluator` y
> `IRoomAvailabilityProvider` alimentan `BookingController` (`/api/booking`), que las
> propias XML-doc del código rotulan explícitamente "vertical Hoteles". `IAppointmentScheduler`
> en cambio es el agendamiento CLÍNICO de Healthcare (ADR 0098: `AppointmentRequest` lleva
> `PatientKey`/`DoctorKey`, persiste sobre `IPhiStore`), consumido únicamente por
> `HealthcareApiController` — cero relación con `/api/booking` o con el `BookingWizard`
> de la UI. Se documentan ambos por separado abajo porque el encargo los citó, pero son
> productos distintos que comparten vocabulario ("cita"/"reserva"), no motor.

### Disponibilidad + hold + pago + cancelación (Hoteles, `/api/booking`)

- **Madurez**: DEMO. Sin ninguna rebanada CMS (a diferencia de Eventos, ni siquiera hay un
  registro inerte de `ICatalogSource<RoomOffer>` — no existe tal tipo en el composer).
- **Seams**: `IRoomAvailabilityProvider`, `IReservationService`, `ICancellationPolicyEvaluator`
  (`Synergos.CMS.Interfaces/`).
- **Implementación**:
  - `StubRoomAvailabilityProvider`
    (`Synergos.CMS.Application/Services/Impl/StubRoomAvailabilityProvider.cs`) — 4 room
    types × 2 rate plans (refundable/non-refundable) sembrados como constante estática,
    precio = tarifa base × noches. Sin persistencia, sin estado — pura función.
  - `StubReservationService`
    (`Synergos.CMS.Application/Services/Impl/StubReservationService.cs`) — `HoldAsync`/
    `HoldItemAsync`/`ConfirmAsync`/`CancelAsync`/`ExpireStaleHoldsAsync`, con hold window
    de 15 min (`DefaultHoldWindow`). **Sí es durable**: `IJsonEntityStore` sobre
    `App_Data/syn-reservations/` (T3/ADR 0104) — sobrevive reinicio, a diferencia del
    catálogo de habitaciones (que no tiene estado que perder). También lo reutilizan
    Eventos (`HoldItemAsync`) y Travel.
  - `StubCancellationPolicyEvaluator`
    (`Synergos.CMS.Application/Services/Impl/StubCancellationPolicyEvaluator.cs`) — regla
    determinista por convención de nombre (`-NREF` = no reembolsable; refundable = 0
    penalidad si se cancela con >1 día de antelación, si no una "penalidad de 1 noche"
    **simbólica y fija** de 220.000 COP, no proporcional al total real de la reserva).
- **Background job**: `HoldExpirationScannerHostedService`
  (`Synergos.CMS.Web/Services/HoldExpirationScannerHostedService.cs`) — corre cada 2 min
  (delay inicial 45s), llama `ExpireStaleHoldsAsync`, libera holds vencidos. Real y activo.
- **Persistencia**: reservas durables (`IJsonEntityStore`); catálogo de habitaciones NO
  (constante en código, ni CMS ni seed mutable).
- **Superficie HTTP**: `POST /api/booking/search`, `/hold`, `/pay`, `/cancel`,
  `GET /api/booking/{reservationId}` — **todos sin auth-gate** (decisión documentada: "el
  huésped no necesita login para buscar/apartar/pagar") — `BookingController.cs` completo.
- **Schema CMS**: ninguno propio. Nota de `CatalogSettings.cs` (comentario en el código):
  `productPage` lo comparten Tienda/Booking/Propiedades bajo distintos `brandKey`
  (`ecommerce`/`meridian`/`propiedades`) para OTRO catálogo (`CatalogProduct`, servicios
  tipo spa) — no para `RoomOffer`. No hay overlap real de datos para esta capacidad.
- **UI/CDN**: `elementSynBookingWizard` / `<synergos-booking-wizard>` — módulo Angular
  real (`booking-wizard.ts`) que llama `/api/booking/{search,hold}` de verdad (`apiBase`
  default `/api/booking`, confirmado en código). Mismo estado de bundle sin construir que
  el resto (manifest sí, `main.js` no).
- **Flags**: ninguno.
- **Tests**: `StubReservationServiceTests.cs` (8), `StubRoomAvailabilityProviderTests.cs`
  (4), `StubCancellationPolicyEvaluatorTests.cs` (4). **Cero tests de `BookingController`**
  — se buscó con `find Synergos.CMS.Tests -iname "*Booking*"` y no devolvió nada. Comparar
  con `EventosControllerTests.cs` (12 tests) o `AcademyAndTravelAuthTests.cs` (11): el
  controller HTTP de Booking es el único de los tres verticales sin cobertura de
  integración/auth propia.
- **Huecos**:
  - `Synergos.CMS.Tests/` — no existe `BookingControllerTests.cs`.
  - `Synergos.CMS.Application/Services/Impl/StubCancellationPolicyEvaluator.cs:29`
    (`OneNightPenalty = 220_000m`) — penalidad fija sin relación con el precio real de la
    reserva cancelada (el stub no recibe el total; el comentario lo reconoce como
    limitación deliberada del stub).
  - Sin rebanada T5: sería el siguiente candidato natural si el arquitecto quiere repetir
    el patrón Eventos/Shop en Hoteles.

### Agenda de citas clínicas (Healthcare — mal catalogada como "Booking" por el encargo)

- **Madurez**: VIVO.
- **Seams**: `IAppointmentScheduler` (`Synergos.CMS.Interfaces/IAppointmentScheduler.cs`).
- **Implementación**: `LockingAppointmentScheduler`
  (`Synergos.CMS.Web/Services/LockingAppointmentScheduler.cs`) — lock async
  por-doctor (`SemaphoreSlim`) para que el read-check-write anti-overbooking sea atómico;
  la decisión de conflicto es lógica pura en
  `Synergos.CMS.Application.Services.AppointmentSchedulingService`. Persiste sobre
  `IPhiStore` (cifrado, `FileSystemEncryptedPhiStore`).
- **Persistencia**: real — `IPhiStore` (resourceType `appointments`), cifrado at-rest.
- **Superficie HTTP**: consumido exclusivamente por `HealthcareApiController` (no se
  detalla aquí: fuera del alcance explícito del encargo, que pidió ignorar
  `HealthcareSettings`, y este seam SÍ depende de `HealthcareSettings.MaxOverbookingMinutes`
  — `SeamComposer.cs:692-695`).
- **Flags**: `HealthcareSettings.MaxOverbookingMinutes` (fuera de alcance).
- **Tests**: `AppointmentSchedulingServiceTests.cs` (7), `LockingAppointmentSchedulerTests.cs` (7).
- **Huecos**: single-instance (lock in-process, documentado como límite conocido D1 —
  no es un hueco nuevo, es una decisión de ADR 0098).

## Flujos end-to-end que HOY funcionan

1. **Eventos — comprar → ticket firmado → check-in**: `GET /api/eventos/events` →
   `GET /api/eventos/event/{id}` → `POST /api/eventos/checkout` → `POST /api/eventos/confirm`
   (emite e-ticket con QR HMAC-firmado, persistido) → `POST /api/eventos/checkin` (🔒
   organizador, verifica firma + `QrVersion`) → estado `used`. Verificado leyendo
   `StubEventTicketingService.cs` de punta a punta; **cierra completo**, incluida la
   supervivencia a un reinicio del CMS (la orden y la llave de firma son durables).
2. **Eventos — transferir ticket**: dueño transfiere → QR rota (`QrVersion++`) → el QR
   viejo deja de verificar → se audita (`IAuditTrailWriter`). Cierra completo.
3. **Educación — matricularse (curso de pago) → pagar → progresar → certificado**:
   `POST /api/academy/enroll` → `POST /api/academy/confirm` → `POST /api/academy/progress`
   (repetido hasta 100%) → `GET /api/academy/certificate?course=` devuelve el certificado
   del ALUMNO LOGUEADO. Cierra completo — **pero solo para el propio alumno**, nunca para
   un tercero (ver flujo roto abajo).
4. **Booking (Hoteles) — buscar → apartar → pagar → confirmar/cancelar**:
   `POST /api/booking/search` → `/hold` (Held, expira en 15 min) → `/pay` (captura +
   confirma) o `/cancel` (penalidad según política). Cierra completo, con expiración
   automática de holds abandonados vía el hosted service.

## Flujos que NO cierran y dónde se cortan exactamente

1. **Editor publica un evento en el CMS → aparece en la agenda comprable.**
   Se corta en `Synergos.CMS.Web/Composers/SeamComposer.cs:792` (y confirmado por la
   ausencia de cualquier inyección de `ICatalogSource<EventSummary>` en todo el repo salvo
   su propio registro en la línea 808): `IEventCatalogProvider` es siempre
   `StubEventCatalogProvider`, así que `eventPage` nunca llega a `SearchAsync`/`GetEventAsync`
   sin importar el valor de `Synergos:Catalog:Sources:Events`. Aunque se arreglara la
   inyección, se cortaría de nuevo en `Synergos.CMS.Web/uSync/v9/ContentTypes/eventpage.config`
   (sin props de tiers/aforo/seat-map): la ficha resolvería `GetEventAsync` a `null` porque
   ese método sigue viviendo en el stub, no en el source CMS.

2. **Un tercero (empleador) verifica un certificado de Educación por su URL pública.**
   Se corta en dos puntos:
   - `Synergos.CMS.Web/Controllers/AcademyController.cs` — no existe ninguna acción que
     llame `ICertificateService.VerifyAsync`; la única ruta de certificado
     (`GET /api/academy/certificate`) exige sesión del propio alumno.
   - `Synergos.CMS.Application/Services/Impl/StubCertificateService.cs:75` — el
     `VerifyUrl` que se construye (`/academy/verify/{certId}`) no corresponde a ninguna
     ruta registrada (verificado con `grep -rn "academy/verify"` en todo el repo: solo
     aparece como string literal en dos sitios de construcción, nunca como atributo de
     ruta ni vista).

3. **Un evento u organizador/instructor "creado en caliente" sobrevive un reinicio.**
   Se corta en `StubEventCatalogProvider.cs:31` (`Catalog` = `ConcurrentDictionary`
   estático, sin `IJsonEntityStore`) y en `StubCourseCatalogProvider.cs` (`_published`,
   mismo patrón) — a diferencia de sus contrapartes transaccionales (`StubEventTicketingService`,
   `StubEnrollmentService`), que sí persisten. Comprar una entrada de un evento recién
   creado sobrevive; el evento mismo, no.

4. **Hoteles — flujo de reservas con cobertura de pruebas de integración.**
   No existe corte funcional (el flujo cierra), pero sí un corte de verificación:
   `Synergos.CMS.Tests/` no tiene ningún archivo para `BookingController`, así que un
   cambio en el mapeo de DTOs, en los 400/404 de validación, o en la idempotencia del
   `/pay` no tiene un test de integración que lo agarre — solo los tests de las seams
   subyacentes (que no cubren el controller en sí).

## Tabla de artefactos

| DocType | Seam | Impl | Endpoint | Elemento UI | Madurez |
|---|---|---|---|---|---|
| `eventPage` (uSync) | `IEventCatalogProvider` | `StubEventCatalogProvider` (+ `UmbracoEventCatalogSource` sin consumidor) | `GET /api/eventos/events`, `GET /api/eventos/event/{id}` | `elementSynEventos` / `synergos-eventos` | DEMO (rebanada CMS registrada, **inerte**) |
| — | `IEventTicketingService` + `ITicketSigner` | `StubEventTicketingService` + `HmacTicketSigner`/`LazyTicketSigner` | `POST /checkout`, `/confirm`, `GET /tickets` 🔒, `POST /ticket/{id}/transfer` 🔒 | `elementSynEventos` | VIVO |
| — | `IEventManagementService` | `StubEventManagementService` | `GET /manage/{eventId}` 🔒org, `POST /checkin` 🔒org, `POST /event` 🔒org | `elementSynEventos` (consola) | VIVO |
| — (ninguno) | `ICourseCatalogProvider` | `StubCourseCatalogProvider` + `AcademyDemoSeed` | `GET /api/academy/courses`, `/course/{id}` | `elementSynAcademy` / `synergos-academy` | DEMO |
| — | `IEnrollmentService` / `IEnrollmentMetrics` | `StubEnrollmentService` | `POST /enroll`, `/confirm`, `GET/POST /progress` 🔒 | `elementSynAcademy` | VIVO |
| — | `ICertificateService` | `StubCertificateService` | `GET /certificate` 🔒 (solo `GetAsync`; `VerifyAsync` sin ruta) | — | SÓLO SEAM (mitad pública) / DEMO (mitad privada) |
| — (ninguno) | `IRoomAvailabilityProvider` | `StubRoomAvailabilityProvider` | `POST /api/booking/search` | `elementSynBookingWizard` / `synergos-booking-wizard` | DEMO |
| — | `IReservationService` | `StubReservationService` + `HoldExpirationScannerHostedService` | `POST /hold`, `/pay`, `/cancel`, `GET /{id}` | `elementSynBookingWizard` | VIVO (persistencia) / DEMO (motor stub) |
| — | `ICancellationPolicyEvaluator` | `StubCancellationPolicyEvaluator` | (compone `/search` y `/cancel`) | `elementSynBookingWizard` | DEMO |
| — (Healthcare, fuera de alcance) | `IAppointmentScheduler` | `LockingAppointmentScheduler` | `HealthcareApiController` (no detallado) | — (`elementEhr` probable, no verificado) | VIVO |
| `elementsynacademy`, `elementsynbookingwizard`, `elementsyneventos` (uSync, bloques CDN-host) | `ISynHostEmitter` / `IBundleRegistryClient` | `DefaultSynHostEmitter` + `StubBundleRegistryClient` | (renderizado SSR, no API) | los 5 elementos (`academy`, `booking-wizard`, `calendar`, `eventos`, `seat-map`) tienen manifest + módulo Angular real, **sin bundle publicado** | DEMO/placeholder (bloqueo CDN transversal, ADR 0012) |
