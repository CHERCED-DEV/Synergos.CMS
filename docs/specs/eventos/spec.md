---
vertical: eventos
sustantivo: Event
alias:       [Ticket]      # el eje 3 usa otro sustantivo; sin declararlo el oráculo no lo ve
epica: 6
oraculo: si            # este spec se mide contra el disco: es el piloto 0
ejes:
  catalogo:    { doctype: eventpage, fuente: UmbracoEventCatalogSource, interruptor: "Synergos:Catalog:Sources:Events" }
  transaccion: { forma: Bff, interruptor: "Synergos:Eventos:Mode", seam: IEventTicketingService, orquestador: Synergos.Bff.Eventos }
  artefacto:   { que: "la entrada con su QR, su portador y su check-in", sustantivo: Ticket, sella: si, sello: ITicketSigner }   # sella: la lee un portero que no es nuestro (doc 12 §3.1)
preguntas:
  deshacer:      si          # aforo apartado + cobro autorizado → orquestador
  recurso_ajeno: si          # el aforo lo lleva Api.Inventory
  quien_cobra:   Bff.Eventos
reusa:
  capacidades: [Api.Inventory, Api.Pricing, Api.Payments]
  elementos:   [eventos, countdown-clock]
crea:
  doctypes:    [eventpage, elementeventsession, elementeventtier, elementeventzone, elementsyneventos]
  seams:       [IEventCatalogProvider, IEventTicketingService, IEventManagementService, ITicketSigner]
  capacidades: []
  artefacto:   [EventTicketIssuer, EventTicketLedger, EventPurchaseNotification]
  composer:    SeamComposer.EventsPropertiesGov   # agrupa tres verticales; el molde no lo predice (#155)
ui:
  app: eventos
rechazos:                                 # leídos de Bff.Eventos/Domain/, no inventados
  - "eventos.no_lines · comprar sin entradas · Invalid · NO transitorio"
  - "eventos.bad_tier · una línea sin localidad · Invalid · NO transitorio"
  - "eventos.bad_quantity · cantidad fuera de rango · Invalid · NO transitorio"
  - "eventos.seat_is_one · una butaca nominada con cantidad > 1 · Invalid · NO transitorio"
  - "eventos.duplicate_seat · la misma butaca dos veces en la compra · Invalid · NO transitorio"
  - "eventos.too_many_lines · más líneas de las que el flujo admite · Invalid · NO transitorio"
  - "eventos.not_confirmable · confirmar una compra que ya no está viva · Conflict · NO transitorio"
  - "eventos.purchase_not_found · la compra no existe · NO transitorio"
---

# Eventos — el spec del vertical que ya existe

> **Este spec es el PILOTO 0 y no es un plan de trabajo.** Eventos está construido y cableado
> (HU #35, rebanadas 2 y 2b). Se escribió **contra el disco**, hacia atrás, para medir una sola
> cosa: **si el molde del doc 12 da para generar un vertical**. Lo que el plan derivado pierda es
> un paso que el doc 12 no escribió, y eso vale más que el porcentaje.
>
> Se mide con `node tools/spec-valida.mjs --oraculo=eventos`.

## Qué problema del negocio resuelve

Vender entradas para un evento con aforo real: el comprador elige localidad y cantidad, el sistema
aparta cupo, cobra, y **sólo entonces** emite las entradas con su QR. Si el cobro falla a mitad, el
aforo vuelve al pozo solo y no se emite ninguna entrada.

Y del otro lado: el organizador publica el evento y sus localidades desde el backoffice —sin
despliegue— y escanea en la puerta.

## Los tres ejes, y por qué se cablean por separado

**Eje 1 · el catálogo.** `eventpage` con sus localidades (`elementeventzone`), sus tarifas
(`elementeventtier`) y sus sesiones (`elementeventsession`). Lo sirve `UmbracoEventCatalogSource`
por `CatalogEventCatalogProvider`, y el rollback es `Synergos:Catalog:Sources:Events = demo` sin
redespliegue. **Cablearlo a `Api.Catalog` sería un retroceso** (doc 12 §3): el dato ya tiene dueño
y meter una ida a la red le quita al organizador la superficie donde publica.

**Eje 2 · la transacción.** Es el único que cruza. `Synergos:Eventos:Mode` con `Stub` de default,
y con `Bff` el cliente `HttpEventTicketingService` habla **sólo** con `Synergos.Bff.Eventos`.

**Eje 3 · el artefacto.** La entrada se queda de este lado, y las dos razones son consecuencias:
el firmante vive acá (`ITicketSigner` / `HmacTicketSigner` con la llave del servidor) y **la prueba
tiene que poder verse con el otro árbol caído** — con `Bff.Eventos` abajo se sigue viendo «mis
entradas», se sigue transfiriendo y se sigue escaneando en la puerta.

## Las tres preguntas, por separado

| pregunta | respuesta | qué decide |
|---|---|---|
| ¿hay algo que **deshacer**? | **sí** — el aforo apartado y el cobro autorizado | hace falta **orquestador** |
| ¿el recurso lo lleva **alguien más**? | **sí** — el aforo es un pozo contable de `Api.Inventory` | hay que **cablearlo** |
| ¿**quién tiene la plata**? | **`Bff.Eventos`** con `Mode=Bff` | a quién se le pide el movimiento |

**La primera es la que elige la forma.** Apartar aforo y cobrar son dos pasos que pueden fallar a
la mitad, así que hay algo que deshacer y va **orquestada**: un interruptor por FLUJO, no por
capacidad (doc 12 §4.1). Mirar «cuántos pasos compone» habría dado la misma respuesta por
casualidad, y es el error que costó cuatro olas — se contestan las tres por separado.

**Y la granularidad del aforo se resolvió en el identificador del sujeto, no en la capacidad:**
butaca nominada y cupo general son el MISMO pozo contable, así que `evento/localidad` o
`evento/localidad/butaca` con existencia 1. Si `Api.Inventory` tuviera que distinguir, dejaría de
servirle a la tienda al día siguiente.

## Lo que reusa, y por qué está antes que lo que crea

Tres capacidades —`Api.Inventory`, `Api.Pricing`, `Api.Payments`— y **ninguna hizo falta crear**:
es la diferencia entre «agnóstica» y «agnóstica hasta el segundo caso». Del otro árbol reusa el
elemento `eventos` y su dependencia `countdown-clock`, de los 132 publicados.

`reusa:` va antes que `crea:` porque el daño de lo contrario está medido: `CLAUDE.md` §2 decía
«UNA capacidad conectada» cuando eran nueve, y un agente que lo leyera proponía de cero lo que ya
existe.

## Cómo sabemos que quedó bien

Está construido, así que el criterio no es «funciona»: es **qué encontró el oráculo**. Los dos
números y sus dos listas salen de correr el validador, y **cada línea de las listas es un
hallazgo con su issue** (doc 13 §9).

Del lado del producto, lo que ya está verificado con los cuatro procesos vivos (HU #35): matando
`Api.Payments` a mitad de la confirmación, **el aforo vuelve al pozo solo y no se emite ninguna
entrada**.

## Los rechazos son DISEÑO, y salieron del disco

«En este repo las reglas de rechazo SON el diseño» (plantilla de Evolutivo), así que la cabecera
los lleva con su código y su columna transitorio — `Rejection.IsTransient` decide si algo se
reintenta o se grita una vez.

**Los ocho se leyeron de `Synergos.Bff.Eventos/Domain/`, no se escribieron.** El primer borrador
de este spec inventó tres (`eventos.aforo_agotado`, `eventos.entrada_ya_transferida`,
`eventos.checkin_duplicado`) que suenan bien y **no existen**: es la fabricación de
`feedback_a_fabrication_can_be_a_derivation`, cometida dentro del propio artefacto que existe para
cruzarse contra el disco. Lo que la cazó no fue releer: fue un `grep` de los literales.

**Y lo que el spec NO declara todavía son los del eje 3** —lo que la puerta rechaza al escanear un
QR ya usado— porque ese eje vive en el CMS y no construye `Rejection`. Es el mismo hallazgo H1: el
eje 3 no tiene sub-spec, así que tampoco tiene dónde declarar lo que rechaza.

## Lo que este spec NO dice

- **No lleva los ficheros.** Si la cabecera enumerara sus rutas, el plan sería igual a la cabecera
  y la cobertura no mediría nada — la tautología de `feedback_contract_shape_needs_its_own_test`.
  El plan se deriva de `vertical`, `sustantivo` y las formas de los ejes.
- **No propone trabajo.** Eventos no se toca en esta HU. Lo que sale de acá son hallazgos.
