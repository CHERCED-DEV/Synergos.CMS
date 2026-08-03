# La promoción de la máquina de sagas — y el segundo orquestador

> **Estado: construido y verificado con procesos reales.** `Synergos.Bff.Core` existe porque
> apareció `Synergos.Bff.Tienda`, y no un día antes. Continúa el
> [doc 09](09-compensacion-cruzada.md), donde la máquina vivía dentro de `Bff.Salud` con una nota
> que decía exactamente cuándo tocaba sacarla.

## 1. La regla que decidió el momento

CLAUDE.md §6 prohíbe las abstracciones prematuras. El doc 09 §6 lo dejó anotado en el propio
`Program.cs`:

> *La máquina de sagas vive acá dentro a propósito. Es plumbing que los ocho BFF van a necesitar,
> pero con UN consumidor promoverla sería la abstracción prematura que CLAUDE.md §6 prohíbe. Se
> promueve a `Synergos.Bff.Core` cuando el segundo la necesite — la misma disciplina que se aplicó
> a `JsonCollectionStore`, que esperó a tener seis.*

Con el segundo, la cuenta se invierte: **copiarla sería copiar el retroceso exponencial, la guarda
de «armada no es pendiente», el aviso una-sola-vez y las llaves deterministas** — y perder una
copia de cada decisión sutil por cada dominio nuevo. Las decisiones sutiles no sobreviven al
copiar-pegar. Eso es lo que una capa compartida existe para evitar.

## 2. Por qué el segundo es Tienda y no otro

Elegir el segundo orquestador **es** el experimento. Si hubiera sido uno con la misma forma que
Salud, «la abstracción sirve» no habría querido decir nada.

| | Salud | Tienda |
|---|---|---|
| Capacidades | 4 | **6** |
| Compartidas con Salud | — | 3 (Pricing, Payments, Notifications) |
| ¿Booking? | sí | **no** |
| Compensaciones | fijas: 2 | **variables: N+2** (una por línea) |
| Kinds | 3 | **4** |

**Que Tienda no tenga Booking es la parte que importa.** Si el segundo hubiera vuelto a apoyarse en
holds de calendario, la máquina podría haber quedado disimuladamente moldeada alrededor de un cupo
con TTL sin que nadie lo notara.

## 3. Qué se promovió y qué NO

| A `Bff.Core` | Se queda en cada BFF |
|---|---|
| `SagaStatus`, `Compensation`, `ISaga<TSelf>` | La saga concreta y sus campos |
| `SagaEngine<TSaga>` — deshacer, reintentar, rendirse, avisar | **El ORDEN de los pasos** |
| `Compensator<TSaga>` + retroceso + `MaxAttempts` | `ICompensationExecutor<TSaga>` — qué significa deshacer cada kind |
| `CompensationSweeper<TSaga>` | Los clientes de sus capacidades |
| `CompensationAlert` + `AlertOptions` | Su ruteo y sus contratos |
| `CapabilityHttp` — HTTP → `Rejection` | |
| `AddSagaMachinery<TSaga,TExecutor>` | |

**La regla de admisión es positiva y la verifica un gate**: entra lo que los ocho orquestadores
necesitan y **no nombra ningún negocio**. Un tipo llamado `Order`, `Booking` o `Payment` en
`Bff.Core` rompe el build — la misma disciplina que sostiene a `Synergos.Shared`.

### 3.1 Los dos cambios que la promoción obligó

**`Compensation.Kind` pasó de `enum` a `string`.** Un enum acá tendría que enumerar
`ReleaseBookingHold`, `ReleaseStockHold`, `CancelOrder`, `RestockItem`… es decir, el catálogo de
todo lo que los ocho dominios saben deshacer. Eso convierte a `Bff.Core` en el sitio que hay que
tocar **cada vez que nace un dominio**, que es justo el acople que la capa viene a evitar. El
dominio nombra sus kinds en constantes suyas; esta capa solo sabe *que hay algo pendiente y cuándo
reintentarlo*.

**`SagaStatus` perdió los nombres de Salud.** `AwaitingConfirmation`/`Confirmed` pasaron a
`Running`/`Completed`. Es un cambio de contrato en la respuesta HTTP de `Bff.Salud`, asumido a
sabiendas: el motor no puede saber que «lo que falta» es una confirmación médica. Cada BFF le pone
el nombre que quiera en su propia respuesta.

**El ejecutor recibe la saga del dominio, no la interfaz** (`ICompensationExecutor<TSaga>`).
Deshacer casi nunca se explica solo con un identificador —devolver existencias necesita saber
*cuántas*— y eso vive en la saga concreta. La alternativa, un campo de carga libre en
`Compensation`, sería una cadena sin tipo que cada dominio interpreta a su manera.

## 4. La medida de si valió la pena

Lo que costó el segundo orquestador, en líneas de lo que **no** se pudo compartir:

- `TiendaCompensationExecutor` — ~50 líneas: cuatro kinds y cómo se deshace cada uno.
- `PurchaseFlow` — el orden de los pasos y qué se anota como compensable.
- Sus clientes, contratos y ruteo.

Lo que vino gratis: retroceso exponencial, ocho intentos, rendirse como estado, la guarda de
«armada no es pendiente», el aviso a una persona una sola vez, el reintento manual, las llaves
deterministas, el barrido y el registro DI. **Y los 31 tests de Salud pasaron sin tocar un solo
assert** — la máquina se movió sin cambiar de comportamiento.

## 5. Lo que la corrida real enseñó, otra vez

Los tests de Tienda pasaron 16 de 16 a la primera. La corrida con ocho procesos no.

**Se cerraba el pedido antes de despachar, y eso condenaba su propia compensación.** Matando
`Api.Fulfillment` entre el cobro y el envío, dos de las cinco compensaciones quedaron colgadas —
y las capacidades lo dijeron con todas las letras:

```
inventory.hold_already_consumed → "devolver existencias es un ajuste, no una liberación"
orders.order_closed             → "el pedido está Fulfilled… una devolución no es una cancelación"
```

Es **la misma lección que el doc 09** —la compensación del pago cambia de *liberar* a *devolver* al
capturar— aplicada a dos pasos más, que se me pasaron:

1. **Consumir existencias cambia la compensación**: `ReleaseStockHold` → `RestockItem`. El flujo la
   reescribe *dentro del bucle*, apartado por apartado: si el consumo falla en la línea tres, las
   dos primeras ya están consumidas y su compensación tiene que ser la buena.
2. **Cerrar el pedido va DESPUÉS de despachar.** Cerrarlo es lo que le quita a `Api.Orders` la
   posibilidad de anularlo. La regla que sale de ahí, y que vale para los ocho dominios:
   **lo que cierra una puerta va lo más tarde posible**, cuando ya no queda nada que pueda fallar
   detrás.

Y una nota sobre los tests: los cinco que fallaron tras el arreglo fallaron **porque estaban
escritos contra el comportamiento defectuoso** — guionaban `release` donde ahora va `adjust`. Un
test que pasa no prueba que el código esté bien; prueba que el código coincide con lo que uno creía.

### 5.1 La corrida, con el arreglo

```
1. comprar        Running | total 166 600 | apartadas 3 | por deshacer 5
2. Fulfillment MUERTO
3. confirmar      503 fulfillment.unreachable   ← capturó, consumió, y no pudo despachar
4. la compra      Compensated | por deshacer 0
5. existencias    p-1 10/10 · p-2 10/10 · p-3 10/10   ← DEVUELTAS (ajuste, no liberación)
6. el pago        Captured | devolvible 0             ← la plata volvió sola
7. el pedido      Cancelled                           ← se pudo anular porque no se había cerrado
```

Cinco compensaciones de tres tipos distintos, en una sola vuelta, con una capacidad caída.

### 5.2 Y Salud, sobre el núcleo, por DI de verdad

Los tests construyen las piezas a mano; el registro nuevo (`AddSagaMachinery`) solo se ejercita
levantando el proceso. Por eso se hizo:

```
agendada    Running | total 50 000
confirmada  Completed | reserva 45357578…
retry       409 salud.nothing_stuck
```

## 6. Los gates nuevos

Tres, todos mutados para comprobar que se disparan:

- **`Bff.Core` solo referencia `Core` y `Shared`.** Una referencia a una `Api.*` metería una
  capacidad en la capa que usan los ocho; una al CMS haría de la capa media una carpeta del CMS.
- **`Bff.Core` no nombra un sustantivo del dominio.** La regla de admisión, ejecutable.
- **Un orquestador no referencia otro orquestador.** Lo común va en `Bff.Core`; si un flujo
  necesita de verdad otro dominio, se habla por HTTP como con todo lo demás.

## 7. Lo que queda abierto

- **Un consumo devuelto es un leer-sumar-escribir.** `Api.Inventory` solo ofrece `adjust` con el
  total absoluto, así que dos devoluciones simultáneas sobre el mismo ítem pueden pisarse. Con una
  instancia del orquestador no pasa; es la primera razón por la que esa capacidad necesitaría un
  ajuste relativo.
- **Si cerrar el pedido falla tras despachar, no se compensa.** La plata está cobrada, la mercancía
  salió y el envío existe: el estado del pedido es contabilidad, y deshacer un despacho no existe
  como operación. Queda un log en rojo y una fila para conciliar.
- **Siguen abiertos los tres del doc 09 §6**: no hay política de abandono, el retroceso no es
  configurable, y el arranque no comprueba que la plantilla del aviso exista.
- **Seis orquestadores más** — Viajes, Eventos, Realty, Gob, Academy, Social. El tercero ya no
  debería mover nada de `Bff.Core`; si lo mueve, es la señal de que la línea quedó mal cortada.
