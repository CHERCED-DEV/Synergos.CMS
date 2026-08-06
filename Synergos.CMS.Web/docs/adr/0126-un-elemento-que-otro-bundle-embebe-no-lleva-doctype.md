# ADR 0126 — Un elemento que otro bundle embebe no lleva DocType

- **Estado:** Aceptado — **enmendado por el [ADR 0127](0127-el-cms-configura-el-mapa-de-asientos-y-un-proveedor-exogeno-lo-llena.md)**
- **Enmienda:** `elementSynSeatMap` SÍ lleva DocType, para CONFIGURACIÓN (qué cabina, cuántas
  butacas, qué moneda) — nunca para inventario. El test que este ADR propone —«¿otro bundle
  embebe su tag?»— resultó **necesario pero no suficiente**: una pieza puede estar embebida
  por `eventos` y además ser un bloque colocable con su propia configuración. `pax-selector`
  sigue siendo el caso puro y sigue sin DocType. Lee el 0127 antes de aplicar la regla de abajo.
- **Fecha:** 2026-08-01
- **Complementa:** ADR 0012 (contrato CDN consumido), ADR 0015 (SynHost framework-agnóstico),
  ADR 0083 (la UI es la fuente de verdad de las claves), ADR 0117 (aforo y asientos como
  contenido), ADR 0115 (gates que fallan cerrados)

## Contexto

Cuando el validador de contratos CMS↔UI corrió por primera vez de verdad contra los dos repos
(ADR 0115 — hasta entonces salía 0 sin validar nada si no encontraba el CMS), reportó seis
desajustes. Dos de ellos se anotaron en el baseline así:

> `elementSynPaxSelector` — *"Elemento del vertical Viajes publicado al CDN sin su DocType. Un
> editor NO puede colocarlo: no existe en el backoffice."*
>
> `elementSynSeatMap` — *"Igual que el anterior, para Eventos. […] Autorar el DocType cuando se
> modelen tiers y aforo como contenido."*

Ese disparador se cumplió: el ADR 0117 modeló localidades, aforo y zonas como contenido, y la
proyección genera los asientos. Tocaba escribir los dos DocTypes.

Al ir a hacerlo, la premisa resultó falsa.

## El hallazgo

Ninguno de los dos es un bloque que un editor coloque. **Los dos los embebe otro bundle:**

```
platforms/angular/apps/elements/modules/booking-wizard/src/booking-wizard/booking-wizard.html:70
    <synergos-pax-selector …></synergos-pax-selector>

platforms/angular/apps/elements/modules/eventos/src/eventos/eventos.ts:430
    /** The seat-map payload (JSON string) for `<synergos-seat-map>`. */
    …:1075
    /** Handler for the `<synergos-seat-map>` `seatselect` CustomEvent. */
```

`booking-wizard` declara `pax-selector` como dependencia en el registry, lo pinta en su
plantilla y normaliza la ocupación que emite. `eventos` le pasa a `seat-map` una carga JSON y
escucha su evento `seatselect` para meter el asiento al carrito.

Son **piezas de composición con un contrato de eventos**, no bloques de página.

Y la cadena que faltaba ya está completa, sin DocType de por medio:

```
editor autora zonas en eventPage
  → UmbracoEventCatalogSource genera los asientos (ADR 0117)
  → EventosController emite venue.zones[].seatmap
     (con [JsonPropertyName("seatmap")] fijado al contrato)
  → el bundle eventos se lo pasa a <synergos-seat-map>
```

## Decisión

**Un elemento registrado que otro bundle embebe NO recibe DocType, y eso no es deuda.**

Los dos salen del baseline como desajustes pendientes y quedan anotados como correctos por
diseño.

## Por qué así

### Qué pasaría si les diéramos DocType

Un editor podría arrastrar un mapa de asientos a una página de contenido. Ahí:

- **No hay evento detrás.** El mapa se alimenta de `EventDetail.SeatMap`, que resuelve el
  módulo de Eventos desde su API. Suelto en una página no tiene de dónde sacar zonas ni
  butacas: o sale vacío, o el editor termina pegando JSON a mano en un TextArea —
  reintroduciendo exactamente el "autorar asientos uno por uno" que el ADR 0117 descartó.
- **Nadie escucha su evento.** `seatselect` lo maneja el componente de Eventos para poner el
  asiento en el carrito. Sin ese contenedor, el visitante elige una butaca y no pasa nada. Un
  control que responde visualmente y no hace nada es peor que un control ausente.
- Lo mismo, más corto, con `pax-selector`: elegir dos adultos y un niño solo significa algo
  dentro del wizard que después busca disponibilidad con esa ocupación.

### Entonces por qué están en el registry

Porque el registry declara **qué publica el CDN**, no **qué puede colocar un editor**. Son dos
preguntas distintas y el validador las estaba tratando como una. Un elemento se publica para
que otro bundle pueda cargarlo por su tag —que es precisamente cómo `booking-wizard` declara
`dependencies: ["pax-selector"]`—, y eso no implica que tenga que existir en el backoffice.

### Cómo se distingue, para la próxima

La señal **no es el tier**. Hay 26 composiciones `elementSyn*` y casi todas sí tienen DocType;
un acordeón o un date-picker son bloques de página legítimos. La señal es concreta y
verificable con un grep:

> ¿Aparece su tag `<synergos-*>` dentro de la plantilla o del código de **otro** bundle?

Si sí, es una pieza embebida: su contenedor le pasa los datos y escucha sus eventos, y no lleva
DocType. Si no, es un bloque de página y **sí** lo lleva — que un bundle publicado no sea
colocable es la otra mitad de este mismo desajuste, y esa sí es deuda.

## Consecuencias

### Lo que se gana

- Dos entradas del baseline dejan de ser trabajo pendiente. Aparecían en cada barrido como
  "elemento sin DocType", y el siguiente que las viera habría escrito el XML.
- Queda una regla aplicable con un grep, en vez de un criterio que hay que redescubrir.

### Lo que se acepta

- **El validador sigue reportándolos.** Compara dos listas de alias y no sabe leer plantillas
  Angular; enseñarle a distinguir un elemento embebido exigiría que el registry declarara esa
  intención — un campo `embeddedBy`, o `standalone: false`. Es la mejora obvia y **no se hizo
  aquí**: cambiar la forma del registry es una decisión del repo UI y toca su validador, su
  auditoría de contratos y sus 153 entradas. Por ahora las dos entradas se quedan en el
  baseline con `action: ninguna`, que es honesto pero no es lo ideal.
- **El baseline queda con dos clases mezcladas**: deuda real (los cuatro DocTypes sin bundle) y
  no-deuda documentada (estos dos). Se distinguen por su `reason`; separarlas de verdad depende
  del cambio de registry anterior.
- Si algún día alguien quiere un mapa de asientos suelto en una página —un plano del recinto,
  informativo— eso **sí** sería un bloque nuevo, con su DocType y su contenido propio. No sería
  este elemento: sería uno que no emite `seatselect`.
