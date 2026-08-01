# Qué le pide cada vertical al motor de pagos

Fuente: lectura directa de los 8 consumidores + `PaymentWebhookController` +
`PaymentWebhookVerifier` + `IIdempotencyLedger` + ADR 0104 + ADR 0106. Todo lo
que sigue cita fichero:línea del código leído en esta sesión. Donde no pude
verificar algo directamente lo marco "no verificado".

## Tabla maestra

| Vertical | Métodos IPaymentProvider usados (orden) | Captura o autorización | Qué mete en Metadata | Compensa fallo | Reembolso | Hold con vencimiento | sessionId durable |
|---|---|---|---|---|---|---|---|
| Tienda (`StubShopOrderService`) | CreateSession → Capture (en Confirm) | Captura inmediata en Confirm, no autoriza-y-diferir explícito | `null` — no usa Metadata | No: si `ConfirmAsync` revienta después de `CaptureAsync` (p.ej. un hold vencido en `_reservations.ConfirmAsync`), el dinero ya se cobró y la excepción burbujea sin revertir la captura ni el stock (comentario propio lo admite) | Sí, delegado a `StubReturnService` (total o parcial vía RMA por línea) | Sí, vía `IReservationService.HoldItemAsync` (hold de stock por línea) | Sí — `PersistedOrder` en `IJsonEntityStore` (`App_Data/syn-orders/`) |
| Viajes (`TravelCartService`) | CreateSession → Capture (en Confirm) → Refund (en Cancel) | Captura inmediata | `null` | Parcial: si falla la captura no confirma nada (lanza antes de tocar reservas); pero si el capture pasa y una reserva individual falla al confirmar, el carrito queda `"Partial"` — no revierte el cobro ni compensa las reservas ya confirmadas | Sí, pero SOLO total (`RefundAsync(sessionId, null, ...)`) — el comentario dice explícitamente que la política parcial por línea no aplica aquí | Sí, un hold por ítem heterogéneo (`HoldItemAsync`) | Sí — `CartOrder` en `IJsonEntityStore` (`App_Data/syn-travel-orders/`) |
| Eventos (`StubEventTicketingService`) | CreateSession → Capture (en Confirm) | Captura inmediata | `null` | No: si `ConfirmAsync` falla capturando aborta antes de tocar reservas, pero si falla confirmando una unidad a mitad de loop deja unidades ya confirmadas y otras no, sin revertir el cobro | No implementado — `IEventTicketingService` no tiene método de reembolso; solo hay transferencia de ticket | Sí, un hold por asiento/cupo (`HoldItemAsync`) | Sí — `PersistedEventOrder` en `IJsonEntityStore` (`App_Data/syn-event-orders/`) |
| Educación (`StubEnrollmentService`) | (rama gratis: ninguno) / CreateSession → Capture (en Confirm) | Captura inmediata; rama gratis evita el PSP por completo | `{"courseId": ...}` — el único consumidor que usa Metadata, y solo para llevar el id del curso | No: si `CaptureAsync` no captura, lanza y la matrícula queda `PendingPayment` para siempre — no hay hold de cupo que liberar (Educación no reserva "asientos") | No implementado — no hay reembolso de matrícula en el seam | No — no hay reserva de cupo, solo el estado `PendingPayment` de la matrícula misma | Sí — `PersistedEnrollment.PaymentSessionId` en `IJsonEntityStore` (`App_Data/syn-enrollments/`) |
| Salud (`StubClinicalSchedulingService`) | CreateSession → Capture (inline, sin esperar acción del cliente) | Captura inmediata, secuencial dentro de `BookAsync` (no hay paso de confirmación async) | `null` | No: si `CaptureAsync` no captura, el código sigue igual y arma la cita con `Status = "pending"` — la reserva ya quedó `Confirmed` vía `_reservations.ConfirmAsync` incondicionalmente, pase lo que pase con el pago | No implementado | Sí, vía `HoldItemAsync`, pero el propio motor no espera nada entre Hold→Pay→Confirm (todo en una llamada síncrona) | **No** — estado en `ConcurrentDictionary` del proceso; el único de los 8 sin `IJsonEntityStore` |
| Gobierno (`StubApplicationService`) | (si `Fee == 0`: ninguno) / CreateSession (sin Capture ni Confirm) | **Ninguna de las dos** — abre la sesión y nunca la captura ni la consulta | `null` | N/A — el expediente se radica igual con `Status=Radicado` exista o no exista pago exitoso; el `paymentSessionId` se devuelve al llamador pero nada en el motor vuelve a tocarlo | No implementado | No — no hay reserva de recurso, es un trámite | Sí — `paymentSessionId` no persiste en `CaseState` en absoluto (ni siquiera se guarda el campo); solo se retorna una vez en `RadicarResult` |
| Devoluciones (`StubReturnService`) | RefundAsync (único método usado) | N/A — consumidor puro de reembolso, no abre sesiones | N/A | No aplica (es la ruta de compensación de Tienda) | Sí, parcial por línea (`rma.RefundAmount` = `line.LineTotal`) o se podría pedir total según cuántas líneas se devuelvan | No | N/A — lee el `PaymentSessionId` ya persistido por `StubShopOrderService`; el propio RMA vive en `ConcurrentDictionary` (no durable) |
| Booking hoteles (`BookingController`) | CreateSession → Capture (en `/pay`) | Captura inmediata, en un solo request HTTP | `null` | Parcial: si `Capture` no llega a `Captured` NO confirma la reserva (correcto), pero tampoco libera el hold ni informa al motor de reservas — el hold sigue vivo hasta que expire solo | Vía `ICancellationPolicyEvaluator` + `/cancel`, pero ese endpoint llama solo a `_reservations.CancelAsync` — **no** llama `IPaymentProvider.RefundAsync** en ningún punto visible del controller | Sí, vía `IReservationService.HoldAsync` (hold nativo hotel, no `HoldItemAsync`) | Reserva sí (vía `IReservationService`/`FileSystemJsonEntityStore`, según ADR 0104); el `PaymentSessionId` en sí solo se guarda dentro del `Reservation` record, no en un store propio del controller |

## Ficha por vertical (fichero:línea)

### Tienda — `Synergos.CMS.Application/Services/Impl/StubShopOrderService.cs`
- Orden de llamadas: `CreateSessionAsync` en checkout (líneas 210-219) → `CaptureAsync` en `ConfirmAsync` (línea 258).
- `Metadata: null` (línea 218).
- Fallo de captura: lanza `InvalidOperationException` (líneas 259-263) sin revertir el `IReservationService.HoldItemAsync` ya hecho en checkout (línea 187) — el hold de stock queda vivo hasta que expire solo.
- Reembolso: no lo maneja este servicio; lo hace `StubReturnService` sobre el mismo `PaymentSessionId` (línea 224 lo persiste en `PersistedOrder`).
- Hold: sí, un `HoldItemAsync` por línea (línea 187-196), usando `TravelProductType.Hotel` como discriminador neutro (comentario línea 184-186).
- sessionId durable: `PersistedOrder.PaymentSessionId` vía `IJsonEntityStore` (línea 224, 234).
- Confirmación asíncrona/tardía: `ConfirmAsync` es idempotente (línea 248-255) y es el mismo método que llama tanto el retorno del cliente como `PaymentWebhookController` (comentario líneas 300-303) — soporta razonablemente el caso PSE de confirmación tardía por webhook.

### Viajes — `Synergos.CMS.Application/Services/Impl/TravelCartService.cs`
- Orden: `CreateSessionAsync` en checkout (líneas 175-184) → `CaptureAsync` en `ConfirmAsync` (línea 209) → `RefundAsync` en `CancelOrderAsync` (línea 361).
- `Metadata: null` (línea 183).
- Fallo de captura: lanza antes de tocar reservas (líneas 210-214) — no compensa los holds ya hechos.
- Confirmación parcial: si una reserva individual no confirma, el estado agregado queda `"Partial"` (línea 236) sin revertir el cobro ya capturado — dinero cobrado por ítems que no se confirmaron.
- Reembolso: solo total, explícitamente documentado como decisión de producto (comentario líneas 352-358): "el MMB v1 reembolsa total (política demo)".
- Hold: uno por ítem heterogéneo, `HoldItemAsync` (línea 153).
- sessionId durable: `CartOrder.PaymentSessionId` vía `IJsonEntityStore` (línea 189-191).

### Eventos — `Synergos.CMS.Application/Services/Impl/StubEventTicketingService.cs`
- Orden: `CreateSessionAsync` (líneas 264-273) → `CaptureAsync` en `ConfirmAsync` (línea 309).
- `Metadata: null` (línea 272).
- Fallo de captura: lanza antes de confirmar reservas (líneas 310-314).
- Sin reembolso: `IEventTicketingService` no expone ningún método de reembolso/cancelación de compra — solo `TransferTicketAsync` (línea 417). Un evento cancelado o una devolución de entradas **no tiene ruta en el motor**.
- Hold: uno por unidad de ticket/asiento (línea 240-249).
- sessionId durable: `PersistedEventOrder.PaymentSessionId` (línea 279, 787).
- El QR/ticket firmado (`ITicketSigner`, línea 70, 595) es un concepto de negocio propio de Eventos sin ningún análogo en `PaymentSessionRequest`.

### Educación — `Synergos.CMS.Application/Services/Impl/StubEnrollmentService.cs`
- Rama gratis: ningún método de `IPaymentProvider` (líneas 149-164).
- Rama de pago: `CreateSessionAsync` (líneas 168-187) → `CaptureAsync` en `ConfirmAsync` (línea 222).
- `Metadata: {"courseId": course.Id}` (líneas 183-186) — **el único de los 8 consumidores que usa `Metadata`**, y solo para llevar un id que el motor genérico ya podría resolver por `OrderReference` si el mapeo curso↔orden viviera en el propio dominio.
- Fallo de captura: lanza (líneas 223-227); la matrícula queda en `PendingPayment` indefinidamente — no hay hold de cupo de curso que liberar (Educación no modela cupo/aforo, a diferencia de Eventos).
- Sin reembolso: no existe ruta de "dar de baja + reembolsar matrícula".
- No hay hold con vencimiento: el "cupo" de un curso no se reserva; solo el pago queda pendiente sin límite de tiempo (a diferencia de Tienda/Viajes/Eventos que sí usan `IReservationService`).
- sessionId durable: `PersistedEnrollment.PaymentSessionId` (línea 191, 589).

### Salud — `Synergos.CMS.Application/Services/Impl/StubClinicalSchedulingService.cs`
- Orden: `HoldItemAsync` (línea 105) → `CreateSessionAsync` (línea 117) → `CaptureAsync` (línea 125) → `ConfirmAsync` de la reserva (línea 128) — **todo en una sola llamada síncrona sin paso de redirect/3DS/webhook intermedio**.
- `Metadata`: no se pasa (el `PaymentSessionRequest` en líneas 118-124 no setea `Metadata` — usa el default `null`).
- Fallo de captura: **no se compensa nada** — la reserva se confirma igual (línea 128, incondicional) y la cita se crea con `Status = "pending"` en vez de `"booked"` (líneas 129-131) si el pago no capturó, pero la cita YA EXISTE y el slot YA ESTÁ tomado. Es el peor manejo de fallo de los 8: el copago fallido no bloquea la reserva del turno.
- Sin reembolso.
- **No usa `IJsonEntityStore`**: el estado vive en `ConcurrentDictionary<string, ClinicalAppointment>` (línea 39) — confirmado también en el wiring (`SeamComposer.cs:727`, `services.AddSingleton<IClinicalSchedulingService, StubClinicalSchedulingService>()` sin pasar ningún store). Es el único de los 8 verticales que **no sobrevive un reinicio del CMS**, ni la cita ni el `PaymentSessionId`.

### Gobierno — `Synergos.CMS.Application/Services/Impl/StubApplicationService.cs`
- Orden: solo `CreateSessionAsync` cuando la tasa aplica (líneas 174-189) — **nunca llama `CaptureAsync` ni `GetStatusAsync` ni `RefundAsync`**. El pago se abre y se abandona; el `paymentSessionId` se devuelve en `RadicarResult` (línea 248) pero el expediente se radica con `Status = CaseStatus.Radicado` (línea 212) sin condicionarlo al resultado del pago.
- `Metadata`: no se pasa (default `null`, líneas 174-188).
- No hay compensación posible porque no hay captura que revertir.
- Sin reembolso.
- Sin hold (no hay recurso reservable).
- El `PaymentSessionId` **no se persiste** en `CaseState` (líneas 807-823 no tienen ese campo) — no hay forma de, más adelante, capturar o consultar esa sesión desde el propio expediente. Este es el consumidor más desacoplado del ciclo de vida real de un pago: básicamente solo usa el seam para "generar un link de cobro" sin comprometerse a nada después.

### Devoluciones — `Synergos.CMS.Application/Services/Impl/StubReturnService.cs`
- Único método usado: `RefundAsync` (línea 177), llamado solo al llegar a `ShopReturnStatus.Refunded` (línea 168).
- Reembolso parcial por línea: `rma.RefundAmount = line.LineTotal` (línea 122) — es decir, hoy siempre reembolsa el total de la línea devuelta, nunca un monto arbitrario menor, aunque el seam sí soporta un `amount` explícito.
- Fallo de reembolso: lanza `InvalidOperationException` (líneas 178-182) y dado que la transición de estado ocurre DESPUÉS de esa llamada (líneas 185-191), un reembolso fallido no deja el RMA en un estado inconsistente — pero tampoco reintenta ni notifica.
- Estado del propio RMA: `ConcurrentDictionary` (línea 43) — **no durable**, a diferencia de la orden de la que depende (`StubShopOrderService`, que sí persiste). Un reinicio del CMS pierde las solicitudes de devolución en curso aunque la orden pagada sobreviva.

### Booking hoteles — `Synergos.CMS.Web/Controllers/BookingController.cs`
- Orden: `HoldAsync` (línea 148, vía `/hold`) → `CreateSessionAsync` (línea 207) → `CaptureAsync` (línea 225) → `ConfirmAsync` de la reserva (línea 245), todo dentro del handler `/pay`.
- `Metadata: null` (línea 222).
- Fallo de captura: correctamente NO confirma la reserva (líneas 227-243) y devuelve el motivo de fallo al cliente — el mejor manejo de "no confirmar en fallo" de los 8, pero **no libera el hold ni lo cancela**: el cupo queda `Held` hasta que el scanner de expiración (`HoldExpirationScannerHostedService`, mencionado en `SeamComposer.cs:220`) lo expire solo.
- `/cancel` (línea 261-289) llama `_cancellationPolicy.Evaluate` + `_reservations.CancelAsync`, pero **no llama `IPaymentProvider.RefundAsync` en ningún punto del método** — la política de cancelación calcula una penalidad/monto reembolsable (`outcome.Refundable`, `outcome.PenaltyAmount`, línea 285-286) que se devuelve como dato informativo al cliente, pero el controller no ejecuta el reembolso real. Esto es un gap concreto: Booking calcula "cuánto se debería reembolsar" sin ejecutar el reembolso.
- Hold: `HoldAsync` nativo del vertical Hoteles (forma propia `ReservationRequest`, distinta de `TravelItemReservationRequest` que usan Viajes/Tienda/Eventos/Salud).
- sessionId: vive dentro de `Reservation.PaymentSessionId` (seam `IReservationService`), no en un store propio del controller.

## El receptor de webhooks — firma, replay, enrutamiento, idempotencia

`Synergos.CMS.Web/Controllers/PaymentWebhookController.cs`, `Synergos.CMS.Web/Services/PaymentWebhookVerifier.cs`, `Synergos.CMS.Interfaces/IIdempotencyLedger.cs`.

- **Firma**: HMAC-SHA256 sobre `"{timestamp}.{body-raw-bytes}"` (`PaymentWebhookVerifier.cs:74-80`), comparación constant-time con `CryptographicOperations.FixedTimeEquals` (línea 83) — resistente a timing attack. El body se lee como bytes RAW antes de deserializar (`PaymentWebhookController.cs:69-74`) porque el HMAC es sobre bytes exactos.
- **Replay**: ventana ±5 min sobre el timestamp firmado (`PaymentWebhookVerifier.cs:23, 94-97`) — el timestamp forma parte del input firmado, así que no se puede reusar body+firma con un timestamp nuevo sin romper el HMAC.
- **Fail-closed en producción**: si `provider != "stub"` la firma es obligatoria; sin `WebhookSecret` configurado devuelve `MisconfiguredSecret` → 500, nunca acepta a ciegas (`PaymentWebhookController.cs:79-96`, `PaymentWebhookVerifier.cs:55-60`). Para `provider == "stub"` la firma es opcional (demo).
- **Enrutamiento**: **no hay enrutamiento real** — el controller solo conoce `IShopOrderService` (`PaymentWebhookController.cs:40, 143`), inyectado por constructor. `KnownProviders` solo tiene `"stub"` (línea 36). Es decir, hoy el webhook confirma **exclusivamente órdenes de Tienda**; Viajes/Eventos/Educación/Salud/Gobierno/Booking no tienen ningún camino de confirmación asíncrona por webhook aunque su `ConfirmAsync` sea igual de idempotente. El propio ADR 0104 lo documenta como decisión explícita ("Despacho Tienda-específico por ahora", línea 72 del ADR) y lo justifica en "Alternatives considered" (rechaza un `IPaymentReconciler` domain-neutral por prematuro, líneas 135-138 del ADR).
- **Anti-tampering**: liga `payload.SessionId == order.PaymentSessionId` (líneas 121-126) y re-consulta `GetStatusAsync` sobre la sesión de LA ORDEN, nunca confía el `Status` que trae el payload (líneas 130-136).
- **Idempotencia**: `IIdempotencyLedger.TryClaimAsync(scope: "payment-events", key: "{provider}-{eventId}")` (línea 146), implementado con `FileMode.CreateNew` atómico anti-TOCTOU (`FileSystemIdempotencyLedger`, según ADR 0104 líneas 46-47 y 139-140). **Se marca DESPUÉS de confirmar** (mark-after-confirm, línea 146 tras la línea 143) — corrección deliberada de un bug encontrado en revisión adversarial (ADR 0104 §"Endurecimiento", punto 1): marcar antes hubiera dejado la orden "cobrada-sin-confirmar" ante un fallo transitorio.
- Sí, el flujo es idempotente end-to-end: `ConfirmAsync` idempotente + ledger atómico + anti-tampering ligado a la orden.

## Conceptos de negocio distintos forzados en el mismo `PaymentSessionRequest`

Todos verificados por el uso real de cada consumidor sobre el mismo record (`Synergos.CMS.Interfaces/IPaymentProvider.cs:33-40`):

1. **Un pedido cerrable (Tienda)** — captura única sobre un carrito ya armado, con reembolso parcial post-venta gestionado por un flujo de negocio completamente distinto (RMA con máquina de estados legal).
2. **Un itinerario compuesto multi-proveedor (Viajes)** — N reservas heterogéneas bajo una sola sesión, donde el resultado puede ser "parcialmente confirmado" (`"Partial"`, `TravelCartService.cs:236`) — un estado que no existe en `PaymentStatus` ni en ningún otro vertical, y que el modelo genérico no tiene forma de expresar (no hay "reembolso parcial automático por ítem no confirmado").
3. **Un derecho de admisión transferible con credencial anti-fraude (Eventos)** — el "producto" no es lo que se paga sino un token firmado (`ITicketSigner`) que vive completamente fuera de `PaymentSessionRequest`/`PaymentSession`; el pago es solo el gate de entrada al problema real (emisión/rotación/verificación de QR).
4. **Una suscripción/matrícula con dos ramas de negocio (gratis vs. paga) donde solo una toca el motor** (Educación) — el "pedido" ni siquiera existe en la rama gratis; el `PaymentSessionRequest` es opcional a nivel de dominio, algo que ningún otro vertical modela así.
5. **Un copago clínico atado a un turno médico con reserva-y-cobro atómicos en un solo request** (Salud) — no hay noción de "reserva sin pago exitoso" en este dominio: el motor genérico permite exactamente ese estado inconsistente (turno `"pending"` con slot ya ocupado) porque nada en `IPaymentProvider`/`IReservationService` fuerza a los dos a tener éxito juntos.
6. **Una tasa administrativa gubernamental opcional y desconectada del ciclo de vida del pago** (Gobierno) — el expediente es la fuente de verdad, no la sesión de pago; el trámite se radica exista o no exista pago, y el sistema literalmente no vuelve a mirar la sesión que abrió. Este es el caso más forzado: usa `IPaymentProvider` solo como "generador de link de cobro", no como motor transaccional.
7. **Una habitación de hotel con política de cancelación monetaria explícita que el motor no ejecuta** (Booking) — el único vertical con una función pura de "cuánto se debe reembolsar" (`ICancellationPolicyEvaluator`) que vive completamente desconectada de `IPaymentProvider.RefundAsync`.

El `PaymentSessionRequest.Metadata` — el único lugar reservado para que un dominio exprese algo propio — solo lo usa 1 de 8 consumidores (Educación, `courseId`), lo que confirma que el resto ya renuncia a intentar decirle algo al motor sobre su propio dominio: la información de negocio vive por completo fuera de la sesión de pago, en el registro propio de cada vertical (`PersistedOrder`, `CartOrder`, `PersistedEventOrder`, `PersistedEnrollment`, `ClinicalAppointment`, `CaseState`).

## Lo que la seam actual NO expresa (con el caso concreto que lo demuestra)

1. **No hay noción de "captura parcial" ni "confirmación parcial del carrito".** `TravelCartService` necesita reembolsar solo las líneas que NO se confirmaron cuando el resultado es `"Partial"` (`TravelCartService.cs:236`), pero `PaymentOutcome`/`RefundAsync` solo puede reembolsar por sesión completa o un monto arbitrario sin saber a qué línea corresponde — no hay forma de decir "de estos $X capturados, devuelve el 30% correspondiente al ítem que no se pudo confirmar".
2. **No hay compensación automática ligada al fallo de captura.** Ningún consumidor (excepto Booking, parcialmente) revierte el `HoldItemAsync`/`HoldAsync` cuando `CaptureAsync` no captura. El seam no ofrece un "CancelSessionAsync" que dispare un callback de compensación — cada consumidor tendría que orquestarlo a mano, y hoy ninguno lo hace completo.
3. **No hay forma de expresar "pago opcional/condicional al dominio" de forma segura.** Gobierno abre una sesión y nunca la vuelve a mirar (`StubApplicationService.cs:174-189`); nada en el contrato obliga o ayuda a atar el resultado del pago al estado del expediente. El motor no distingue entre "abrí una sesión que me importa" y "abrí una sesión de la que me desentendí".
4. **No hay reserva/hold en el propio `IPaymentProvider`.** El "hold" vive en un seam completamente distinto (`IReservationService`), y el copago de Salud (`StubClinicalSchedulingService.cs:104-131`) demuestra el problema: nada fuerza que el hold de recurso y el hold de fondos avancen o retrocedan juntos — se puede confirmar la reserva sin que el pago haya capturado.
5. **No hay concepto de reembolso parcial por línea de forma nativa.** Solo `StubReturnService` lo simula calculando el monto en el propio dominio (`StubReturnService.cs:122`) y pasándolo como `amount` a `RefundAsync` — el seam soporta el parámetro pero ningún otro consumidor lo usa así, y Viajes documenta explícitamente que NO lo hace por decisión de producto, no porque el seam se lo impida ni se lo facilite.
6. **`Metadata` es de facto `IReadOnlyDictionary<string,string>` sin ningún contrato ni validación** — el 87% de los consumidores (7/8) ni lo usa. No hay un lugar tipado para que cada vertical declare "esto es lo mío" (courseId, eventId, radicado, doctorId, orderKind) de forma consistente.
7. **No hay "reason code" ni "decline code" estructurado.** `PaymentOutcome.FailureReason` es un string libre (`IPaymentProvider.cs:59`) — cada consumidor lo re-envuelve en su propia excepción con su propio mensaje (p.ej. `StubShopOrderService.cs:261-263`), sin ningún vocabulario común que un dashboard o un flujo de reintento pudiera consumir de forma genérica.
8. **No hay `Async`/callback de estado post-3DS distinto del webhook Tienda-only.** `SimulateRequiresAction` (`StubPaymentProvider.cs:85-90`) modela el redirect, pero solo Tienda tiene un receptor de confirmación async — los otros 7 consumidores, si el PSP real devolviera `RequiresAction`, no tienen ningún mecanismo para enterarse de la resolución salvo que el usuario vuelva y llame `ConfirmAsync` manualmente.
9. **El "OrderReference" es ambiguo entre "identificador de negocio" e "identificador de hold".** Salud usa el `hold.Id` de la reserva como `OrderReference` (`StubClinicalSchedulingService.cs:119`), mientras el resto usa un identificador de orden propio (`ord_`, `trip_`, `evord_`, `enr_`) — no hay convención impuesta por el seam, cada consumidor decide.

## Riesgos si el PSP confirma tarde o por webhook (caso PSE)

1. **7 de 8 verticales no tienen ningún receptor de webhook.** `KnownProviders` en `PaymentWebhookController.cs:36` solo contiene `"stub"` y el despacho está cableado a `IShopOrderService` (línea 40). Si PSE confirma un pago de Viajes/Eventos/Educación/Gobierno/Salud/Booking horas después, **no existe ningún endpoint que lo reciba** — el único camino de confirmación es que el usuario vuelva y dispare `ConfirmAsync` manualmente, que en el caso de PSE (confirmación bancaria asíncrona real) puede no ocurrir nunca si el usuario cierra la pestaña.
2. **Salud confirma la reserva de forma incondicional antes de saber si el pago tardío llegará.** `StubClinicalSchedulingService.cs:128` llama `ConfirmAsync` de la reserva sin condicionarlo al resultado de `CaptureAsync` — con PSE (que típicamente no captura al instante), el turno queda agendado como `"pending"` con el slot ya bloqueado indefinidamente, sin ningún reintento ni expiración visible en este servicio (no usa `IJsonEntityStore` ni el `HoldExpirationScannerHostedService`).
3. **Gobierno pierde la sesión de pago apenas la abre.** Si PSE tarda o confirma por webhook, no hay ningún sitio en `StubApplicationService` que vuelva a consultar `GetStatusAsync` — el `paymentSessionId` ni siquiera se persiste en `CaseState` (confirmado: el record no tiene ese campo, líneas 807-823). Un trámite con tasa pagada tarde vía PSE queda con el mismo estado que uno impagado; no hay diferencia observable en el expediente.
4. **Booking calcula bien el "no confirmar sin captura" pero deja el hold vivo esperando un timeout genérico**, no un evento de confirmación tardía — si PSE confirma 30 minutos después y el hold ya expiró (`HoldExpirationScannerHostedService`, ~1-2 min según su doc), la habitación ya se liberó y se vendió a otro huésped: el dinero de PSE llegaría capturado sobre una reserva que ya no existe, sin ruta de reembolso automático visible en el controller.
5. **El anti-tampering del webhook (ligar `payload.SessionId == order.PaymentSessionId`) es sólido para Tienda**, pero como no se generaliza a los otros verticales, extenderlo naïvemente (agregar un `switch` por provider/vertical dentro del mismo controller) violaría el principio 5/7 del proyecto (branding/dominio vía provider, no `if` hardcodeado) — el rediseño necesita, como mínimo, un seam de "resolución de orden por referencia" domain-neutral antes de poder generalizar el webhook a los 7 verticales restantes (el propio ADR 0104 ya lo anticipa y lo descarta por prematuro, líneas 135-138).
6. **El caso Viajes agrava el riesgo de PSE con su estado `"Partial"`:** si PSE confirma un pago que cubre el total pero mientras tanto una de las N reservas del itinerario expiró, el motor no tiene ningún mecanismo de reconciliación — el comprador pagó el total y solo una parte de su viaje quedó confirmada, sin trigger de reembolso automático del resto.

## Insumos para el rediseño (lista priorizada)

1. **Separar "informar al pago" de "atar el pago al dominio"**: el rediseño necesita un evento/callback domain-neutral post-confirmación (autorizado/capturado/fallido) que cada vertical pueda suscribir, en vez de que solo Tienda tenga webhook. Esto es lo más urgente dado el riesgo de PSE (§ arriba) y ya está anticipado como deuda en el propio ADR 0104 ("Deuda de verticales", líneas 106-108) y en la decisión explícita de no construir `IPaymentReconciler` todavía (líneas 135-138 del ADR).
2. **Modelar el "hold de fondos" y el "hold de recurso" como una sola transacción compensable**, o al menos como un patrón obligatorio de Saga/compensación — hoy cada consumidor decide a mano si compensa (ninguno lo hace completo; Booking es el que más se acerca) y Salud demuestra el peor caso (confirma incondicionalmente).
3. **Agregar un tipo de reembolso "por línea" de primera clase**, no un `decimal? amount` suelto — Viajes y Tienda ya necesitan expresar "reembolsa la parte de X, no el total", y hoy lo hacen fuera del seam (StubReturnService calcula el monto por su cuenta).
4. **Reemplazar `Metadata: IReadOnlyDictionary<string,string>?` por un modelo tipado y extensible por vertical** (algo tipo genérico polimórfico calcando `NotificationEvent`/`NotificationTypes` de T4 — ADR 0106, líneas 44-48) — hoy 7/8 consumidores no lo usan porque no hay dónde colgar información propia del dominio de forma segura.
5. **Generalizar el enrutamiento del webhook** con un seam de "resolución de orden por `OrderReference`" domain-neutral (algo como `IPaymentSubjectResolver` o extender `ICheckoutRecorder`), que permita a `PaymentWebhookController` despachar a cualquier vertical sin un `if`/`switch` hardcodeado — necesario antes de dar por completo el fan-out que el ADR 0104 dejó pendiente.
6. **Definir explícitamente el ciclo de vida "pago abandonado/huérfano"** (Gobierno, y potencialmente cualquier vertical con pago opcional): el seam necesita una forma de expirar/cancelar sesiones que nadie volvió a capturar, hoy invisible.
7. **Persistir `PaymentSessionId` de forma consistente en TODOS los agregados de dominio** — Gobierno no lo hace en absoluto (`CaseState` no tiene el campo) y Salud no persiste nada del pago fuera del `ConcurrentDictionary` del proceso; esto es un requisito previo a cualquier reconciliación futura.
8. **Estandarizar el manejo de fallo de captura** (compensar vs. dejar pendiente vs. abortar) como parte del contrato del motor, no como una decisión libre de cada Stub — hoy hay 4 comportamientos distintos entre 8 consumidores para la misma situación ("CaptureAsync no capturó").
9. **Decidir si `IPaymentProvider` necesita un modo autorización-diferida real** (`Authorized` sin `Capture` inmediato) — hoy todos los 8 consumidores "capturan en el mismo paso lógico que autorizan" salvo Gobierno (que ni siquiera captura); si un PSP real (Wompi/PayU) impone captura diferida por reglas de negocio (p.ej. anti-fraude), el motor necesita que el consumidor pueda esperar sin romper su propio modelo de estado.

## Notas de verificación

- No se encontró ningún consumidor que use `PaymentSession.ClientSecret` (campo pensado para flujos inline tipo Stripe Elements) — no verificado si algún front-end lo consume; fuera del alcance de este análisis de servidor.
- No se verificó el comportamiento de `HoldExpirationScannerHostedService` en detalle (solo su mención en comentarios de `SeamComposer.cs`) — no abierto en esta sesión.
- No se verificó `StubReservationService` completo (solo su interfaz `IReservationService`) — el análisis de holds se basa en el contrato y en cómo lo invoca cada consumidor, no en la implementación del stub.
