# ADR 0127 — El CMS configura el mapa de asientos y un proveedor exógeno lo llena

- **Estado:** Aceptado — **actualizado el mismo día**: los **cuatro** ejes que la medición
  evaluó están cerrados. El contrato del componente se extendió en el repo UI y el CMS se
  adaptó después, en ese orden; ver «[Lo que se midió](#lo-que-se-midió-y-es-peor-de-lo-que-parecía)»
  y las tres secciones que la siguen.
- **Fecha:** 2026-08-01
- **Enmienda:** ADR 0126 (un elemento que otro bundle embebe no lleva DocType)
- **Complementa:** ADR 0117 (aforo y asientos de un evento como contenido), ADR 0083 (la UI es
  la fuente de verdad de las claves), ADR 0012 (contrato CDN consumido), ADR 0002 (grafo de
  capas)

## Contexto

El ADR 0126 concluyó que `elementSynSeatMap` no necesitaba DocType: el bundle de Eventos lo
embebe, le pasa la carga JSON y escucha su `seatselect`, así que dárselo permitiría al editor
soltar un mapa sin evento detrás y sin nadie escuchando el evento.

El arquitecto corrigió esa conclusión, y la corrección es correcta.

> *"El seatmap debe ser una funcionalidad que en sí pueda customizar desde la interfaz —en
> apariencia, en servicio, en forma de construcción, en categorías. El llenado debe ser por un
> proveedor exógeno, una API que emule el comportamiento de una cabina de avión. No podríamos
> estarlo llenando desde el CMS."*

## Qué acertó el 0126 y qué confundió

**Acertó** en lo que más importaba: el inventario de butacas no se autora en el CMS. Un
BlockList de asientos sería una hoja de cálculo mal puesta que se desincroniza en el primer
vuelo — el mismo argumento por el que el ADR 0117 generó los asientos en vez de autorarlos.

**Confundió** dos cosas distintas: *"el CMS no lo llena"* con *"el CMS no tiene nada que decir
al respecto"*. **Configuración no es contenido.** Qué cabina se muestra, cuántas butacas puede
elegir alguien y cómo se ve el bloque son decisiones editoriales legítimas, y no tocan una sola
butaca.

Y confundió también el test que propuso. *"¿Otro bundle embebe su tag?"* es **necesario pero no
suficiente**: una pieza puede estar embebida por `eventos` **y además** ser un bloque colocable
con su propia configuración. `pax-selector` sigue siendo el primer caso puro —no tiene sentido
fuera del wizard— y por eso sigue sin DocType.

## Decisión

### 1. La seam `ISeatMapProvider` es exógena

El inventario lo publica quien lo conoce: el sistema de la aerolínea, el del recinto, o un
adapter que los emule. El default `StubCabinSeatMapProvider` **emula una cabina real**, y lo que
emula es lo que hace que el mapa se lea como verdadero:

- **la fila 13 no existe** — media flota mundial la omite;
- **la columna I no se usa** — en un asiento impreso se confunde con un 1, y el componente
  publicado ya aplica esa regla: apartarse desalinearía el rótulo del servidor con el del
  cliente;
- **ventana / pasillo / centro se derivan de la geometría**, no del índice de columna: en un
  `2-4-2` el bloque central no tiene ninguna ventana, por evidente que parezca mirando un `3-3`;
- **una fila de salida siempre trae más espacio** — es el requisito regulatorio que la define,
  no una cortesía comercial;
- **bloqueada no es vendida** — existe y no se vende (tripulación, avería): no se libera sola y
  no la compró nadie. Confundirlas haría mentir un reporte de ocupación.

Es **determinista**: las ocupadas salen de un hash estable de (referencia, butaca). Un mapa que
cambia en cada refresco no se puede grabar para una demo y un test sobre él sería intermitente.

### 2. El CMS aporta configuración, nunca inventario

`elementSynSeatMap` tiene **seis propiedades**: qué mapa cargar, cuántas butacas se pueden
elegir y en qué moneda (Integración), más densidad, ocultar precios y ocultar leyenda (Estilo).
Ni filas, ni butacas, ni precios de butaca.

### 3. El host resuelve en el SERVIDOR

El partial SynHost pide el mapa por la seam y le entrega al componente la carga ya resuelta,
que es como el elemento publicado espera recibirla. No es solo comodidad: **mantiene fuera del
navegador las credenciales del proveedor** y evita exponer su API al público. Y no exige ningún
cambio en el bundle.

## Por qué así

### Por qué no se le dio al elemento un `apiBase` para que buscara él

Era la alternativa obvia y la que muchos sistemas eligen. Se descartó por tres razones, en este
orden: exigiría cambiar el contrato del componente (trabajo UI-first, ADR 0083); publicaría el
endpoint del proveedor al navegador junto con lo que haga falta para autenticarse; y volvería el
bloque inservible sin CDN, mientras que resolver en el servidor deja al menos la configuración
en pie.

### Por qué el DocType no expuso apariencia, servicio ni categorías de entrada

Porque **el componente no las leía**. Agregar propiedades para claves que nadie consume es
precisamente la deriva que el ADR 0083 y el gate de contratos existen para frenar: schema que se
ve completo y no hace nada. El orden correcto era enseñarle al componente primero, y eso es lo
que se hizo después (ver las tres secciones tras la medición).

Servicio y categorías, además, **no son configuración editorial**: son atributos de la cabina
que el proveedor conoce y el editor no. Su sitio es la carga, no el DocType. La apariencia es al
revés — no la sabe el proveedor, la decide quien coloca el bloque— y por eso sí terminó en el
DocType.

## Lo que se midió, y es peor de lo que parecía

Contrastando los cuatro ejes del arquitecto contra el contrato real. **Esta es la medición
original**; los dos ejes tachados se cerraron el mismo día en la sección siguiente:

| Eje | Estado | Detalle |
|---|---|---|
| ~~**Apariencia**~~ | ~~Parcial, y **nada por el componente**~~ → **cerrado** | El bundle no exponía ni un input de apariencia. Lo que funcionaba venía del host: `compDom*` aterriza en el envoltorio y el SCSS del bundle está tokenizado, así que el tema del siteRoot ya mandaba el color. Era el exterior de la caja; el interior no se configuraba |
| ~~**Servicio**~~ | ~~**Nada**~~ → **cerrado** | `ServiceClass` no tenía clave en la carga. El 787 sembrado son 43 filas en tres cabinas y se dibujaba como una sola grilla indiferenciada: un pasajero no veía dónde termina ejecutiva |
| ~~**Forma de construcción**~~ | ~~Parcial — el peor hueco~~ → **cerrado** | `aisleAfterColumns` era **un solo entero**, así que solo se podía dibujar **un** pasillo. Todo widebody salía mal: el `3-3-3` dibujaba el pasillo tras la C y **nada entre F y G**; el bloque derecho se soldaba al central. El `1-2-1` igual |
| ~~**Categorías**~~ | ~~Parcial~~ → **cerrado** | El enum `type` mezclaba dos ideas ortogonales —tres posiciones más un rasgo de confort—, así que `extra-legroom` **sobrescribía** la posición cuando ambas eran ciertas. `Features` e `IsExitRow` se descartaban |

### Lo que el contrato de la UI necesitaría, por prioridad

1. ~~**`aisleAfterColumns` → arreglo.**~~ **Hecho.**
2. ~~**`rows[].serviceClass`** más un encabezado de sección en la plantilla.~~ **Hecho.**
3. ~~**`seats[].features: string[]`**, aditivo.~~ **Hecho.**
4. ~~**Entradas de apariencia** (`density`, `showLegend`, `showPrices`).~~ **Hecho.**

## Servicio y categorías: cómo se cerraron

Se hizo **UI-first**, que es lo que el ADR 0083 obliga y lo que la medición de arriba dejaba
como trabajo del otro repo. Primero se extendió el componente publicado; después la proyección
del CMS se adaptó a lo que el componente ya sabía leer. En ningún momento el CMS emitió una
clave que el bundle no consumiera.

### Servicio — `rows[].serviceClass`

El componente agrupa filas contiguas y dibuja un encabezado **solo cuando la clase cambia**, no
en cada fila: repetir "Económica" 30 veces sería ruido. Un mapa sin clases se ve exactamente
igual que antes, porque sin la clave no hay encabezados que dibujar.

El vocabulario es **abierto** con etiquetas conocidas para las cuatro cabinas habituales
(`first` / `business` / `premium` / `economy`); lo que no está en el mapa se rotula con su
propio valor en vez de desaparecer. La proyección normaliza a minúsculas porque ahí es donde el
componente busca la etiqueta.

### Categorías — `seats[].features[]`, y `type` recupera su significado

`type` vuelve a ser **solo la posición**. Los rasgos se mudaron a un arreglo aparte, también de
vocabulario abierto, y la leyenda se deriva de lo que la carga trae.

Tres decisiones dentro de esto:

- **La compatibilidad hacia atrás se preservó en el componente, no en el CMS.** Un
  `type: "extra-legroom"` de una carga vieja se pliega solo: posición `middle` más el rasgo.
  Las cinco pruebas que ya existían del componente pasan sin tocarse, que es la evidencia de
  que ninguna carga previa se rompió.
- **`IsExitRow` es de la fila y se reparte a cada butaca**, porque el contrato lleva los rasgos
  por butaca.
- **`exit-row` NO se plegó dentro de `extra-legroom`** aunque casi siempre coincidan. Una fila
  de salida conlleva requisitos regulatorios —edad mínima, nada en el piso— que "más espacio"
  no comunica. Como casi siempre coinciden, el relleno se lo queda `extra-legroom` y la fila de
  salida se marca con **borde**: si compartieran el relleno, una taparía a la otra justo en el
  caso normal.

### Y una pérdida silenciosa que apareció al cerrarlo

El módulo `eventos` **descartaba** `serviceClass` y `features` al traducir la carga del CMS. Ese
módulo no dibuja el mapa: se lo pasa a `<synergos-seat-map>`. Lo que su parser no copie no llega
a la pantalla, y **no hay error en ninguna parte** — ni en el CMS, que emitió bien, ni en el
componente, que nunca vio la clave.

O sea que servicio y categorías funcionaban por el bloque suelto y **no** por la ruta
`CMS → eventos → seat-map`, que es la que usa una ficha de evento real. Está corregido, y el
paso a través quedó con prueba propia campo por campo.

La regla que deja: **un módulo que reenvía una carga es parte del contrato.** Extenderlo sin
tocar sus traductores deja el dato a mitad de camino sin que nada se queje.

## Forma de construcción: `aisleAfterColumns` es una lista

Era el hueco #1 de la lista de prioridades y el único que producía un plano **incorrecto** en
vez de uno incompleto.

`aisleAfterColumns` acepta ahora un arreglo de índices de columna 1-based: `[3, 6]` para un
`3-3-3`, `[1, 3]` para un `1-2-1`. El stub ya lo sabía calcular sin ayuda —son las sumas
acumuladas de los bloques de la fórmula, **menos la última**— y estaba tirando toda esa
información salvo el primer número.

Tres decisiones:

- **El número suelto sigue valiendo**, normalizado a `[n]`. `eventos` y `travel-shell` arman su
  carga con un entero y hay cargas grabadas de demos; ninguna podía romperse por admitir el
  arreglo.
- **Nunca se dibuja un pasillo después de la última butaca de la fila.** Ahí el hueco no separa
  nada y descuadra la fila respecto de las demás. Empieza a importar justo ahora: con varios
  pasillos, una fila corta —una sección de suites al frente de una cabina `3-3-3`— alcanza la
  posición del segundo pasillo justo en su último asiento.
- **Las posiciones se sanean y se ordenan** en las dos mitades. El orden no es cosmético: el
  componente las compara mientras recorre la fila de izquierda a derecha.

### Y un segundo nivel, porque una cabina no tiene una sola distribución

La primera versión puso los pasillos solo en el mapa, y eso dejaba fuera el caso que el propio
stub tenía sembrado: la ejecutiva de un 787 es `1-2-1` y su turista `3-3-3`. Con una sola
geometría, el pasillo del mapa cae **en medio del bloque central de las suites**. No es un caso
raro que valga la pena diferir: es cómo está hecho casi cualquier avión de largo radio.

`rows[].aisleAfterColumns` manda sobre el del mapa cuando la fila lo declara. Y el `787`
sembrado pasó a tener **tres** distribuciones —`1-2-1`, `2-3-2`, `3-3-3`—, así que el caso se
ejercita de verdad en vez de quedar como una capacidad sin usar.

**La distinción que sostiene todo esto es `null` contra lista vacía.** `null` —la clave
ausente— es «no digo nada, usa los del mapa», que es lo que hace toda cabina de una sola
distribución, o sea casi todas. La lista **vacía** es «esta fila no tiene ningún pasillo».
Colapsarlas dejaría sin forma de declarar una sección corrida dentro de una cabina que sí tiene
pasillos — justo el caso que este nivel viene a cubrir. Por eso el traductor de `eventos` dejó
de colapsar `[]` a `undefined`: ahí el paso a través habría borrado la diferencia antes de que
el mapa la viera.

## Apariencia: tres controles que no tocan una butaca

Era el último eje, y el que obligó a revisar una premisa de este mismo ADR. Arriba se dijo que
el DocType no expone apariencia **porque el componente no la lee**. Eso era una constatación,
no una decisión: la respuesta correcta era enseñarle a leerla, no dejar el eje sin cerrar.

El componente recibe tres entradas nuevas —`density`, `showPrices`, `showLegend`— y el bloque
tres propiedades en la pestaña **Estilo**, que ya existe por `compDom*`.

- **`density`** (`comfortable` | `compact`) no es cosmética. Una cabina de 44 filas mide más de
  mil píxeles de alto: en un móvil deja el resumen y el botón de compra fuera de la pantalla.
  `compact` **encoge** butaca, huecos y pasillo; **no quita nada**, y por eso el objetivo táctil
  se mantiene por encima del mínimo accesible. Un valor que el componente no conoce cae en
  `comfortable` en vez de dejar el mapa sin estilo.
- **`showPrices`** se apaga donde el precio no distingue nada —un recinto con una zona a precio
  único—: trescientas etiquetas idénticas no informan, tapan el mapa. Apagarlo **no esconde el
  costo**: el total sigue en el resumen y el precio sigue en el `aria-label` de cada butaca,
  porque quien navega con lector de pantalla no tiene el resumen a la vista para compensarlo.
- **`showLegend`** vale menos desde que la leyenda se deriva del contenido, pero sigue teniendo
  un caso: el mapa embebido en un paso de compra donde el visitante ya vio las convenciones.

### Por qué el CMS las autora en NEGATIVO

Las dos propiedades del bloque se llaman **«Ocultar precios»** y **«Ocultar leyenda»**, no
«Mostrar».

`Umbraco.TrueFalse` guarda `false` cuando el editor nunca tocó el interruptor, y el componente
enciende las dos por defecto. Con un «Mostrar precios», ese `false` heredado se emitiría como
`showPrices: false` y **todo bloque ya colocado —y todo bloque nuevo sin tocar— se quedaría sin
precios sin que nadie lo pidiera**. Con «Ocultar», el estado apagado —el que cada bloque tiene
por defecto— significa exactamente lo que el componente ya hace.

Se descartaron dos alternativas: un `DataType` propio con `"Default": true` no arregla el
contenido que ya existe, y distinguir «nunca autorado» de «apagado a mano» depende de cómo
Umbraco trata el `0` en `HasValue`, que es justo la clase de sutileza que no debe sostener un
default visible.

Va en la misma dirección la regla de emisión: **solo se emite el apagado**. Un bloque que no
eligió apariencia no emite ninguna de las tres claves, así que si el componente cambia su
default algún día, lo sigue en vez de quedar clavado al de hoy.

## Consecuencias

### Lo que se gana

- El editor puede colocar un mapa de asientos y decidir qué cabina muestra, sin tocar código y
  sin autorar una sola butaca.
- El inventario tiene una seam con un adapter real esperando, y el stub ya emula el
  comportamiento que ese adapter tendrá que reproducir.
- Queda medido y escrito **qué de los cuatro ejes se puede hoy y qué no**, con el orden en que
  conviene cerrarlo.
- **Servicio y categorías llegan hasta la pantalla**, por las dos rutas: el bloque suelto y la
  ficha de evento. El 787 sembrado ya se lee como tres cabinas y no como una grilla; una butaca
  de ventana con espacio extra sigue siendo de ventana; y una fila de salida se distingue de una
  con espacio extra, que es lo que un rasgo con consecuencias regulatorias necesita.
- **Las tres cabinas sembradas se dibujan bien.** Dos son de doble pasillo y salían con uno; el
  787 además tiene tres distribuciones y cada sección se dibuja con la suya.
- **El editor decide cuánto ocupa el mapa y cuánto explica**, sin tocar código y sin que ninguna
  de esas decisiones pueda alterar una butaca, un precio o una disponibilidad.

### Lo que se acepta

- **El vocabulario abierto no se valida en ninguna de las dos mitades.** Un proveedor que mande
  `xtra-legroom` verá esa cadena rotulada tal cual en la leyenda, sin error en ningún gate. Es
  el precio de no exigir un despliegue del CMS por cada rasgo nuevo, y se aceptó a sabiendas.
- **La apariencia llega hasta donde llega el componente.** Se configura cuánto ocupa el mapa y
  cuánto explica, no cómo se ve por dentro: el color de una butaca sigue viniendo del tema del
  siteRoot por el envoltorio, no de una propiedad del bloque. Cambiar eso sería exponer tokens
  al editor, que es otra decisión y de otro tamaño.
- **Las dos propiedades negadas se leen peor que las afirmadas.** «Ocultar precios» apagado
  significa que se ven, y eso obliga a una doble negación mental cada vez. Se aceptó porque la
  alternativa no era un nombre más lindo: era un default que se rompe solo.
- **Sin diccionario y sin respaldo SSR.** El servidor no emite copy: la proyección emite ids,
  enums, booleanos y números; todo el texto visible vive en el bundle. Un respaldo SSR sería lo
  primero que necesitaría claves de diccionario, y una grilla de butacas renderizada en el
  servidor duplicaría el componente en vez de degradarlo.
- **Una fila con un hueco físico** (galley, mamparo) corre las letras siguientes, porque el
  bundle rotula por índice del arreglo. Los saltos de *número de fila* sí funcionan.
- `pax-selector` **sigue sin DocType**, y esta enmienda no lo cambia: es el caso puro del
  ADR 0126 — no tiene sentido fuera del wizard que lo embebe.
