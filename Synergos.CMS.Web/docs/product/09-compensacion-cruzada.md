# La compensación cruzada — cómo se deshace lo que ya se hizo

> **Estado: construido y verificado con procesos reales.** `Synergos.Bff.Salud` es el primer
> orquestador; el flujo *agendar una cita con copago* cruza **cinco** capacidades —Consent,
> Booking, Pricing, Payments y Notifications— y es el caso canónico. Cierra la pregunta que el
> [doc 07 §7](07-diseno-atomico-capacidades.md) dejó abierta, y con el aviso a una persona (§4.1)
> cierra también la última que quedaba abierta acá.

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

**ARMADA no es PENDIENTE, y confundirlas cancelaba todas las citas.** Es el defecto más caro que
salió de aquí, y lo destapó un proceso real en seis segundos. Una cita sana esperando confirmación
lleva sus compensaciones anotadas —el cupo que soltar, el cobro que liberar— porque se arman en el
instante en que existe lo que habría que deshacer. La selección del barrido era «toda saga con
alguna compensación pendiente», y eso describe a **todas** las citas sanas. Resultado medido:

```
startedAtUtc 16:39:33  →  doneAtUtc 16:39:39
```

Seis segundos después de agendar, el barrido soltó el cupo y liberó el cobro de una cita que no
tenía ningún problema. Ningún test lo vio porque todos llamaban a `CompensateAsync` directo y
ninguno ejercitaba la *selección*. Hoy `AppointmentSaga.IsUnwinding` es la guarda —solo
`Compensating` y `CompensationFailed` son trabajo— y la misma regla filtra la vista de operación,
que si no listaría todas las citas del día como si fueran problemas. **Una compensación solo es
trabajo cuando algo YA falló.**

**«Se rinde a los ocho intentos» tampoco era verdad.** Agotados los intentos, la compensación
seguía pendiente y sin `NextAttemptUtc`, así que el barrido la reintentaba *cada minuto para
siempre*, gritando el mismo error y tapando en el log los que sí se podían atender. Rendirse tiene
que ser un **estado** (`Compensation.IsStuck`), no una línea de log.

## 4. Cuando la compensación también falla

Es el caso que de verdad importa, porque la causa habitual de que un paso falle es que la
capacidad está caída — **y entonces compensar en línea falla también**.

- **Retroceso exponencial**, 1 → 2 → 4 … minutos con techo de 60. Martillear una capacidad caída
  no la levanta; solo alarga la caída.
- **Barrido de fondo** cada minuto, y cada compensación decide sola si le llegó el turno. Barre
  también al arrancar: un proceso que estuvo caído una hora despierta con una hora de atraso.
- **Tras 8 intentos se rinde de verdad** —`IsStuck`— y el barrido la deja quieta. La saga pasa a
  `CompensationFailed`, sigue visible en `GET /v1/compensations` y **nunca se marca como hecha**:
  una compensación que se da por buena sin ejecutarse es plata cobrada sin servicio.

### 4.1 Y entonces se le avisa a una persona

Es lo que cierra el lazo. Un log en rojo solo sirve si alguien está mirando ese minuto.

| Decisión | Por qué |
|---|---|
| El destinatario se **configura** (`Salud:Alerts:{ToKind,ToId,Address}`) | Cablear una dirección es la primera grieta de un BFF que hay que desplegar en otra clínica con otra guardia. Sin configuración **no se inventa nadie**: se grita una vez cuál es la clave que falta. |
| Se manda la **clave de plantilla**, no el texto | Es la línea que mantiene `Api.Notifications` agnóstica. El texto lo escribe el dominio y vive del otro lado. |
| El aviso **no lleva datos del paciente** | Sale por correo o SMS a una guardia operativa. El identificador de la cita basta para entrar al sistema y mirar. |
| Llave `{sagaId}:alert:{n}` | Un reintento tras un timeout no manda un segundo correo idéntico; un reintento **pedido a mano** sí manda uno nuevo. |
| Fallo **transitorio** reintenta, fallo **no transitorio** se grita una vez | Notifications caída es una capacidad caída como cualquier otra. Que falte la plantilla o la guardia no lo arregla reintentar — lo arregla una persona, y repetirlo cada minuto taparía en el log lo que importa. |

**Y una puerta de vuelta:** `POST /v1/appointments/{id}/retry` rearma los intentos de lo rendido y
el aviso. Sin ella, «se rinde» sería «se abandona», y arreglar una devolución colgada exigiría
tocarla a mano en la capacidad, por fuera del rastro de la saga.

> **Antes de desplegar** hay que autorar en `Api.Notifications` la plantilla configurada en
> `Salud:Alerts:TemplateKey` usando **solo** los marcadores `{saga}`, `{origen}`, `{desde}` y
> `{pendientes}`. Un quinto hace que el envío se rechace con
> `notifications.missing_placeholder` — está verificado abajo. No hay seeder que la cree:
> CLAUDE.md §0.4 los prohíbe.
>
> *(Al promover la máquina a `Bff.Core` —[doc 10](10-promocion-bff-core.md)— `{cita}` pasó a
> `{saga}` y se sumó `{origen}`: una misma guardia puede atender Salud y Tienda con la misma
> dirección, y necesita saber de qué sistema viene el aviso.)*

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

### 5.1 La segunda corrida: la que destapó el defecto de la selección

Al añadir el aviso se repitió el ejercicio con seis procesos, y esta vez lo que falló fue el
propio barrido. Antes del arreglo, una cita sana:

```
agendada     16:39:33  AwaitingConfirmation
(nadie hace nada)
compensada   16:39:39  ← el barrido soltó el cupo y liberó el cobro. SEIS SEGUNDOS.
```

Después del arreglo, la misma cita sana con el barrido pasando dos veces:

```
agendada     16:46:51  AwaitingConfirmation | 2 compensaciones ARMADAS
(100 s, dos vueltas del barrido)
16:48:41     AwaitingConfirmation | /v1/compensations: 0 filas
             compensaciones ejecutadas por el barrido: 0
confirmar →  Confirmed | reserva b81cc9f3…
```

Y el contrapeso, para que el filtro no se pasara de largo — matando `Api.Booking` otra vez entre
el cobro y la confirmación:

```
1. agendada    AwaitingConfirmation, total 50 000
2. Booking MUERTO
3. confirmar   503 booking.unreachable        ← capturó, y no pudo confirmar
4. cita        Compensating | pendientes 1
5. pago        Captured | devolvible 0        ← la plata volvió sola, con Booking caído
6. la cita sana de antes: Confirmed, intacta  ← el filtro no se llevó por delante lo bueno
7. (vuelve Booking, 16:49:27)
   16:49  "ReleaseBookingHold falló (Connection refused); reintento 1"
   16:51  "ReleaseBookingHold completada en el intento 2"
8. cita        Compensated | 0 filas en /v1/compensations
```

### 5.2 El contrato de marcadores, contra un `Api.Notifications` de verdad

Es lo que un handler guionado **no** puede comprobar, porque no corre `NotificationRules.Fill`:

```
plantilla salud.compensacion.colgada con {cita} {desde} {pendientes}   201
el cuerpo exacto que arma CompensationAlert                           201  Sent
misma llave saga-1:alert:0  → MISMA entrega (46e99fea…)               201  ← no hay segundo correo
llave saga-1:alert:1        → entrega NUEVA (b2ec894b…)               201  ← el reintento manual sí avisa
plantilla con un cuarto marcador {paciente}                           400  notifications.missing_placeholder
entregas totales a la guardia: 2                                           ← ni una de más
```

La última fila es la que le da sentido al test que fija los tres marcadores: un cuarto **rompe el
aviso**, y rompería justo el día que hay que avisar.

## 6. Lo que queda abierto

- ~~**La máquina de sagas vive dentro de `Synergos.Bff.Salud`.**~~ **Cerrado.** Apareció el
  segundo consumidor —`Synergos.Bff.Tienda`— y se promovió a `Synergos.Bff.Core`. Ver el
  [doc 10](10-promocion-bff-core.md), que además cuenta los dos cambios que la promoción obligó
  y el defecto de ordenación que destapó.
- **La ventana irreducible.** Entre «la capacidad ejecutó» y «el BFF lo anotó» hay un instante en
  el que una caída pierde el rastro. Las llaves deterministas lo hacen sobrevivible —repetir el
  paso devuelve lo mismo— pero *sobrevivible* no es *imposible*, y conviene no fingir lo
  contrario.
- **Nadie recoge una cita abandonada.** Si el paciente agenda y nunca confirma, la saga se queda
  en `AwaitingConfirmation` para siempre. No es urgente —el hold de Booking vence solo por TTL y
  la autorización de Payments también— pero la saga queda de testigo de algo que ya no existe.
  Falta una política de abandono, y con ella la pregunta de a las cuántas horas.
- **El retroceso no es configurable.** Ocho intentos con techo de 60 minutos son ~3 horas hasta
  declarar algo colgado. Para una devolución puede estar bien; para un cupo de una clínica puede
  ser lento. Es una perilla de despliegue razonable, pero se decide con números de operación
  reales, no antes — y añadirla solo para que un test tarde menos sería la razón equivocada. Es
  también por lo que el camino de rendirse está verificado por tests y mutación, **no en vivo**:
  esperarlo con procesos reales cuesta tres horas de reloj.
- **El aviso confía en que alguien autoró la plantilla.** Si falta, el fallo se grita una vez y la
  guardia no se entera. Un arranque que compruebe la plantilla contra `Api.Notifications` lo
  convertiría en un error de despliegue en vez de uno de madrugada.
