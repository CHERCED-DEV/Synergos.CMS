# ADR 0122 — El cupo se verifica primero; la caja se abre después

- **Estado:** Aceptado
- **Fecha:** 2026-08-01
- **Complementa:** ADR 0116 (motor de pagos como seam polimórfica), ADR 0075 (gate de tests)

## Contexto

`BookingController` era el único controller del repo sin un solo test, y no era código inerte:
captura pagos y emite reembolsos. Escribir esa cobertura —53 tests, verificados matando 14
mutaciones— destapó dos defectos que mueven dinero. Ninguno de los dos rompía nada visible.

### Defecto 1 — cancelar dos veces reembolsaba dos veces

La guarda del reembolso miraba la política de cancelación y el `PaymentSessionId`, **nunca el
estado de la reserva**. Y `CancelAsync` es idempotente pero no limpia el id de sesión, así que
una segunda llamada con el mismo `reservationId` volvía a evaluar la política y a llamar
`RefundAsync` por el mismo monto.

No se duplicaba plata **solo porque el proveedor stub reembolsa únicamente sesiones
`Captured`**, y la segunda pasada encontraba `Refunded`. El PSP salvaba al controller; el
controller no se salvaba solo. Y `RefundAsync` es el **único** método mutador de
`IPaymentProvider` cuyo contrato no promete idempotencia — `CaptureAsync` y `VoidAsync` sí la
prometen explícitamente. Una pasarela real que acepte reembolsos parciales sucesivos —el caso
**normal** cuando hay penalidad, porque quedan pesos sin devolver en la sesión— paga dos veces.

Con Wompi conectado (ADR 0116), esto deja de ser hipotético.

### Defecto 2 — pagar sobre un hold vencido cobraba y después reventaba

Las guardas de `pay` cubrían `Confirmed` y `Cancelled`. Un hold vencido no es ninguna de las
dos: caía derecho a `CreateSessionAsync` + `CaptureAsync` —**dinero capturado**— y solo
entonces `ConfirmAsync` lanzaba porque el hold ya no valía. Nadie atrapaba esa excepción.

Resultado: **HTTP 500, huésped cobrado, sin reserva, y sin `Void` ni `Refund` que compensara.**
Alcanzable dejando pasar la ventana de 15 minutos, y también cuando el escáner de vencimientos
voltea el hold entre el hold y el pago — una carrera de minutos, no de milisegundos.

## Decisión

**Dos guardas, las dos antes de tocar el motor de pagos.**

1. **`cancel`**: si la reserva ya está `Cancelled`, se responde el estado actual sin llamar al
   PSP ni al motor de reservas. La misma guarda idempotente que `pay` ya tenía para
   `Confirmed`.
2. **`pay`**: si la reserva está `Expired`, o está `Held` con `ExpiresAt` ya pasado, se
   responde **409** sin abrir sesión de pago.

## Por qué así

### Por qué la segunda cancelación responde 200 y no un error

Cancelar lo ya cancelado es **el resultado que el huésped pidió**. Reintentar tras un timeout
de red no puede parecer un fallo — es exactamente el escenario que produce la segunda llamada
en la vida real. Un 409 aquí obligaría a cada cliente a distinguir "no se pudo" de "ya estaba",
que es la ambigüedad que la idempotencia existe para eliminar.

Pero el `RefundStatus` va en **null**, no en `"Refunded"`: esta pasada no movió dinero.
Afirmar un reembolso que no ocurrió aquí es la clase de dato con cara de verdad que ya costó
una vez en este mismo endpoint — la ola anterior arregló precisamente que la cifra reembolsable
fuera decorativa.

### Por qué el hold vencido responde 409 y no 400

No es una petición mal formada: el cuerpo es correcto y el recurso existe. Es un **conflicto
con el estado actual del recurso**, que es literalmente para lo que existe el 409. Un 400
mandaría al cliente a revisar lo que envió, cuando lo que tiene que hacer es volver a apartar
el cupo.

### Por qué se corta antes y no se compensa después

La alternativa era dejar que cobrara y compensar con `VoidAsync`/`RefundAsync` al fallar el
confirm. Se descartó: una compensación es un segundo viaje al PSP que también puede fallar, y
entonces hay que compensar la compensación. **El orden correcto es verificar el cupo y después
abrir la caja**, no al revés — y aquí verificarlo es leer un campo que ya está en memoria.

La compensación sigue siendo necesaria para lo que *no* se puede saber de antemano (que el
confirm falle por otra razón), y esa red ya existe en el resto del motor. Lo que este cambio
elimina es el caso que **sí** se podía prever.

### Por qué `ExpiresAt` nulo no cuenta como vencido

`ExpiresAt` es `DateTimeOffset?`. Sin fecha declarada no hay vencimiento que comprobar, y
tratarlo como vencido cerraría el cobro de toda reserva que no declare ventana. La comparación
lifted de C# ya devuelve `false` para null, así que el comportamiento sale del lenguaje — pero
está fijado por un test, porque depender de una sutileza del lenguaje sin decirlo es cómo se
rompe en la siguiente refactorización.

## Consecuencias

### Lo que se gana

- Cancelar dos veces reembolsa una. La garantía la pone el controller y no el PSP, que es
  donde tiene que estar: el contrato de `RefundAsync` no la promete.
- Nadie queda cobrado sin reserva por un hold que se venció.
- El controller pasa de 0 a 57 tests.

### Lo que se acepta y queda pendiente

Tres cosas que la cobertura sacó a la luz y que **no** se tocaron, porque son decisiones de
producto o de alcance mayor:

- **`pay` devuelve dos DTOs distintos.** Primera llamada → `PayResponse` (`paymentStatus`,
  `amountCaptured`, `paymentSessionId`). Reintento sobre una reserva `Confirmed` → `Ok(
  MapReservation(...))`, es decir `ReservationResponse`, que no tiene ninguna de esas claves.
  Una UI que lea `paymentStatus` en el reintento recibe `undefined` (territorio ADR 0083).
- **Un reembolso fallido responde 200.** El `RefundStatus` lleva el estado real del PSP y nunca
  miente, pero `refundable: true` y `penaltyFormatted` siguen presentes: una pantalla que los
  pinte e ignore `refundStatus` se lee tranquilizadora. Hoy **nada en el repo consume
  `refundStatus`** — un grep lo encuentra solo dentro del propio controller.
- **Los cinco endpoints son anónimos**, y es deliberado: el `reservationId` es la credencial
  (`resv_{Guid:N}`, 128 bits), el mismo patrón de compra-sin-cuenta que
  `TravelControllerAuthTests` ya bendice. **No es la forma del defecto de
  `/api/shop/return/{rmaId}/advance`**, que era una asimetría —un endpoint abierto entre dos
  vecinos que sí verificaban ownership—; aquí las cinco rutas son uniformemente públicas y así
  está documentado.

  El riesgo residual que sí conviene dejar por escrito: a diferencia del `order/{ref}` de
  Viajes, que es de solo lectura, el `cancel` de Booking es **destructivo y mueve dinero**,
  alcanzable por cualquiera que tenga el id, sin límite de tasa y **sin entrada en
  `IAuditTrailWriter`**. Quien vea ese id —un correo reenviado, una cabecera `Referer`, una
  línea de log, un navegador compartido— puede cancelar la estadía. Un endurecimiento barato
  que no rompe la compra sin cuenta: auditar la cancelación.
