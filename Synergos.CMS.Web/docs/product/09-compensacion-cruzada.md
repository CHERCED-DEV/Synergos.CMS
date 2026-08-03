# La compensación cruzada — cómo se deshace lo que ya se hizo

> **Estado: construido y verificado con procesos reales.** `Synergos.Bff.Salud` es el primer
> orquestador; el flujo *agendar una cita con copago* cruza cuatro capacidades y es el caso
> canónico. Cierra la pregunta que el [doc 07 §7](07-diseno-atomico-capacidades.md) dejó abierta.

## 1. El problema, en una frase

Pagar una cita toca **Payments** y **Booking**. Se cobra, y después hay que confirmar el cupo. Si
lo segundo falla, hay **plata cobrada sin cita** — y no hay transacción que abarque a los dos,
porque cada capacidad es dueña de su almacén y nadie más lo lee.

## 2. Las cuatro decisiones que lo resuelven

### 2.1 Dos fases: autorizar es barato, capturar cuesta

Agendar **aparta** el cupo y **autoriza** el cobro —reserva cupo en el medio de pago sin mover
plata—. Confirmar **captura** y confirma.

No es cortesía con la interfaz: es lo que hace que el caso más común de fallo —el paciente se
arrepiente, la ventana se vence, el medio de pago rechaza— **no cueste una devolución**. Es el
hold de `Api.Booking` aplicado un nivel más arriba, y por eso `Api.Payments` separa autorizar de
capturar desde el principio.

### 2.2 El orden dentro de confirmar: cobrar y después confirmar

Es el único sitio donde puede doler, y la elección es deliberada:

| Orden | Si falla el segundo paso | ¿Se deshace solo? |
|---|---|---|
| confirmar cupo → capturar | cita confirmada que nadie pagó | **no** — hay que llamar al paciente |
| **capturar → confirmar cupo** | plata cobrada sin cita | **sí** — se devuelve |

Se elige el fallo que **el sistema puede reparar sin una persona**.

### 2.3 La llave de idempotencia deriva de la saga

`{sagaId}:hold`, `{sagaId}:authorize`, `{sagaId}:capture`, `{sagaId}:confirm`. Deterministas.

**Es lo que hace recuperable todo lo demás:** con llaves deterministas, *reintentar un paso ES la
recuperación* — la capacidad reconoce la llave y devuelve lo que ya hizo en vez de hacerlo otra
vez. Sin eso, tras una caída entre «cobré» y «lo anoté» no habría manera de averiguar si el cobro
salió, porque las capacidades **no exponen «búscame por llave»** y no van a exponerlo: sería una
puerta para enumerar operaciones ajenas.

### 2.4 La compensación es un DATO, no una función

Una compensación tiene que poder ejecutarse *después* —desde otro proceso, tras un reinicio,
horas más tarde—, y un `Action` en memoria no sobrevive a nada de eso. Se guarda `{kind, targetId,
reason, attempts, nextAttempt}` en la bitácora de la saga, y un barrido de fondo la reintenta.

**Y se anota en el momento en que existe lo que hay que deshacer**, no después. Si la
autorización se anotara tras confirmar, una caída en medio dejaría plata reservada en la tarjeta
del paciente sin nada que la libere.

## 3. Lo que se descubrió construyéndolo

**La compensación del pago CAMBIA al capturar.** Mientras está autorizado, deshacer es *liberar*;
capturado, es *devolver*. Si no se reescribiera, `Api.Payments` rechazaría cada intento con
`already_captured` y la compensación quedaría colgada para siempre — fallando por una razón que
no tiene nada que ver con el mundo real.

**Una devolución ya hecha se da por cumplida.** El compensador consulta el saldo devolvible antes
de devolver: si es cero, la compensación cumplió. Cubre el reintento donde la devolución sí había
salido pero no llegó a anotarse — y ese caso la llave sola no lo cubre, porque un barrido de otra
vida del proceso podría traer otra llave.

## 4. Cuando la compensación también falla

Es el caso que de verdad importa, porque la causa habitual de que un paso falle es que la
capacidad está caída — **y entonces compensar en línea falla también**.

- **Retroceso exponencial**, 1 → 2 → 4 … minutos con techo de 60. Martillear una capacidad caída
  no la levanta; solo alarga la caída.
- **Barrido de fondo** cada minuto, y cada compensación decide sola si le llegó el turno. Barre
  también al arrancar: un proceso que estuvo caído una hora despierta con una hora de atraso.
- **Tras 8 intentos se rinde a gritos** y la saga pasa a `CompensationFailed`, visible en
  `GET /v1/compensations` y en el log con nivel *error*. **Nunca se marca como hecha.** Una
  compensación que se da por buena sin ejecutarse es plata cobrada sin servicio, y nadie se
  entera.

## 5. Verificación con procesos reales

Cuatro capacidades y el BFF, cinco procesos. **Se mató `Api.Booking` entre el cobro y la
confirmación** — el instante exacto que un test no puede elegir:

```
1. agendada       AwaitingConfirmation, total 50 000
2. Booking MUERTO (health: sin respuesta)
3. confirmar      503 booking.unreachable   ← capturó, y no pudo confirmar
4. pago           Captured | devuelto 50 000 | devoluble 0
                  ← LA PLATA VOLVIÓ SOLA, con Booking caído
5. cita           Compensating | pendientes 1   ← falta soltar el cupo
6. (vuelve Booking; el barrido reintenta con su retroceso)
   → "Compensación ReleaseBookingHold completada en el intento 4"
7. cita           Compensated | pendientes 0
8. el cupo        otro paciente pudo apartarlo
```

Las dos compensaciones se comportaron distinto **y así tenía que ser**: la devolución salió al
primer intento porque Payments estaba vivo; soltar el cupo tardó cuatro porque Booking no lo
estaba. Cada una avanza a su ritmo, y ninguna espera a la otra.

## 6. Lo que queda abierto

- **La máquina de sagas vive dentro de `Synergos.Bff.Salud`.** Es plumbing que los ocho BFF van a
  necesitar, pero con un consumidor promoverla sería la abstracción prematura que CLAUDE.md §6
  prohíbe. Se promueve a `Synergos.Bff.Core` cuando el segundo la necesite — la misma disciplina
  que esperó a que `JsonCollectionStore` tuviera seis.
- **La ventana irreducible.** Entre «la capacidad ejecutó» y «el BFF lo anotó» hay un instante en
  el que una caída pierde el rastro. Las llaves deterministas lo hacen sobrevivible —repetir el
  paso devuelve lo mismo— pero *sobrevivible* no es *imposible*, y conviene no fingir lo
  contrario.
- **`CompensationFailed` no tiene quién lo atienda.** Hoy es una fila en `/v1/compensations` y un
  log en rojo. Falta el aviso a una persona, y eso es `Api.Notifications` — un renglón, cuando se
  decida a quién.
