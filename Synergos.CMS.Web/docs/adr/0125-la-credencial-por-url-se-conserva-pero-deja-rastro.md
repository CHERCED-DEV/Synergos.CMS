# ADR 0125 — La credencial por URL se conserva, pero deja rastro

- **Estado:** Aceptado
- **Fecha:** 2026-08-01
- **Complementa:** ADR 0037 (rastro de auditoría), ADR 0122 (guardas de Booking), ADR 0119
  (`stayListing`), ADR 0107 (`ICatalogSource`)

## Contexto

El inventario funcional cerraba la ficha de Viajes con *"catálogos sembrados; **cancelación por
URL-capacidad**"*. Las dos mitades resultaron ser cosas muy distintas de lo que parecían.

### La cancelación

`POST /api/travel/order/{orderRef}/cancel` es anónimo. El controller **sí** tiene
`IMemberAccessGate` inyectado y lo usa para `trips` —"mis viajes" pide sesión, porque un
itinerario con fechas dice cuándo la persona no está en su casa— pero `order/{ref}` y su
cancelación no.

Eso **no es un descuido**: está fijado como correcto en `TravelControllerAuthTests`, con su
razón escrita —el ref es `trip_{Guid:N}`, 128 bits, y tenerlo *es* la autorización; es lo que
permite que quien compró como invitado vuelva a su reserva— y con un test de regresión
deliberado para que nadie lo "arregle" después.

Lo que ese archivo decía y **no** tenía detrás: *"y su cancelación"*. La mitad destructiva de
la misma credencial no tenía un solo test. Al abrirla se encontró:

- **La idempotencia ya estaba bien.** `CancelOrderAsync` retorna temprano sobre una orden ya
  `Cancelled`, sin re-cancelar reservas ni pedir un segundo reembolso. A diferencia de Booking
  (ADR 0122), aquí no había doble pago — pero tampoco había nada que lo mantuviera así.
- **No había rastro.** Una acción anónima, destructiva y que mueve dinero, sin una sola línea
  en `IAuditTrailWriter`.

### Los catálogos

Viajes tiene tres proveedores sembrados —`IRoomAvailabilityProvider`,
`IFlightAvailabilityProvider`, `ICarRentalProvider`— más el contenido de la estadía
(`IStayContentProvider`).

**El contenido ya salió del código**: `stayListing` (ADR 0119) llegó con la rebanada de Booking,
y `GET /api/travel/stay/{id}` lo consume desde el mismo seam. Viajes heredó esa mitad sin
trabajo propio.

## Decisión

1. **La credencial por URL se conserva.** No se le pone sesión a `cancel`.
2. **La cancelación deja rastro** — en Viajes (`travel.order.cancelled`, desde
   `TravelCartService`, que es quien sabe qué se liberó y cuánto se devolvió) y también en
   Booking (`booking.reservation.cancelled`), donde el ADR 0122 lo había dejado anotado como
   pendiente.
3. **Los tres proveedores de disponibilidad NO reciben rebanada CMS.** Ver abajo.

## Por qué así

### Por qué no se le pone sesión a la cancelación

Porque cerraría la compra de invitado sin cerrar nada. Quien compró sin cuenta **no tiene**
sesión que ofrecer; exigirla convierte "cancelar mi viaje" en "cree una cuenta primero", y el
atacante que ya tiene el ref sigue teniéndolo — solo que ahora también puede crearse una cuenta.
La credencial de 128 bits no se debilita por añadirle un login: se debilita cuando se filtra, y
un login no impide que se filtre.

Es además la decisión que el repo ya tomó explícitamente para la lectura, con su test de
regresión. Revertirla a medias —lectura abierta, cancelación cerrada— dejaría al invitado
viendo una reserva que no puede cancelar, que es la peor de las tres opciones.

### Entonces qué cambia, si no cambia el acceso

**La trazabilidad.** Sin rastro no había forma de responder *"¿quién canceló este viaje y
cuándo?"*, que es exactamente la pregunta que llega cuando alguien reenvía un correo de
confirmación, comparte un navegador, o una URL se filtra por una cabecera `Referer`. Auditarlo
no pide sesión y no rompe nada: solo deja constancia.

**Con una limitación que hay que decir en voz alta:** el actor registrado es **el viajero de la
orden, no quien hizo la petición**. No hay sesión que consultar. Es una consecuencia honesta del
modelo de credencial-por-URL, y queda escrita en el propio código para que nadie lea ese
registro como una identificación del solicitante. Lo que el rastro responde es *qué pasó y
cuándo*, no *quién lo pidió*.

### Por qué el rastro es best-effort y va al final

La cancelación y el reembolso ya ocurrieron cuando se escribe. Desandarlos porque no se pudo
escribir una línea de log sería el remedio peor que la enfermedad. Mismo criterio que la
transferencia de tickets de Eventos.

Es el criterio **opuesto** al del guard de PHI, que es fail-closed: si no puede auditar, niega.
La diferencia no es de gusto — allí la auditoría **precede** al acceso y puede negarlo; aquí
**sucede** a un efecto ya irreversible. Auditar antes de cancelar tampoco ayudaría: registraría
cancelaciones que después fallan.

### Por qué los tres proveedores de disponibilidad no van al CMS

Es la pregunta obvia después de cuatro verticales seguidos moviendo su catálogo a contenido, y
la respuesta aquí es **no**, por una razón de dominio y no de esfuerzo:

`IRoomAvailabilityProvider`, `IFlightAvailabilityProvider` y `ICarRentalProvider` no sirven
*catálogo*: sirven **disponibilidad y precio para un rango de fechas**. Eso cambia por minuto,
lo publica un channel manager o un GDS, y su fuente de verdad está fuera de este sistema por
definición. Un editor no autora en Umbraco cuántos asientos quedan en el vuelo de mañana; si
pudiera, el dato estaría mal desde el primer refresh.

La línea que sí se cruzó es la correcta: **el contenido de la estadía** —galería, amenities,
descripción, reputación— *sí* es editorial y *sí* lo autora un hotelero, y ya salió al CMS con
`stayListing` (ADR 0119).

La separación entre las dos cosas **ya estaba en el diseño**: `IStayContentProvider` se
introdujo precisamente para "separar el CONTENIDO de la estadía de la DISPONIBILIDAD/precio,
que sigue intacta". Esta ADR solo confirma que esa frontera es donde termina el trabajo de
contenido, no un pendiente.

## Consecuencias

### Lo que se gana

- Cancelar un viaje deja rastro consultable, en los dos verticales que lo permiten por URL.
- La cancelación de Viajes pasa de cero tests a diez, incluida la idempotencia que ya
  funcionaba y ahora está protegida.
- Queda por escrito por qué tres de los cuatro catálogos de Viajes **no** van al CMS, para que
  no vuelva a aparecer como deuda en el próximo barrido.

### Lo que se acepta

- **El rastro no identifica al solicitante.** Registra al viajero de la orden. Cerrar eso
  exigiría sesión, que es justo lo que esta ADR decide no hacer.
- **Sigue sin haber límite de tasa** en la cancelación. Quien tenga el ref puede llamarla
  repetidamente; es idempotente, así que el daño es ruido y no dinero, pero es ruido que ahora
  también se audita.
- **La política de cancelación de Viajes reembolsa el total.** `ICancellationPolicyEvaluator`
  no aplica al carrito multi-producto —evalúa por `ratePlanCode` + `checkIn`, y las líneas
  heterogéneas no cargan ni lo uno ni lo otro—, así que el MMB v1 devuelve todo. Está anotado
  en el código y sigue siendo una decisión de producto pendiente, no un defecto.
