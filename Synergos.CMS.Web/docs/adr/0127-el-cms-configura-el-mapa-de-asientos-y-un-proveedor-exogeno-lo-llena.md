# ADR 0127 — El CMS configura el mapa de asientos y un proveedor exógeno lo llena

- **Estado:** Aceptado — **actualizado el mismo día**: los tres huecos que la medición encontró
  (*servicio*, *categorías* y *forma de construcción*) están cerrados. El contrato del
  componente se extendió en el repo UI y la proyección se adaptó; ver
  «[Lo que se midió](#lo-que-se-midió-y-es-peor-de-lo-que-parecía)» y las dos secciones que la
  siguen. Queda abierto solo *apariencia*, que es el que menos duele.
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

`elementSynSeatMap` tiene **tres propiedades**: qué mapa cargar, cuántas butacas se pueden
elegir y en qué moneda. Ni filas, ni butacas, ni precios.

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

### Por qué el DocType no expone apariencia, servicio ni categorías

Porque **el componente no las lee como entradas del editor**. Sus entradas son exactamente
cuatro —`config`, `seatmap`, `currency`, `maxSelectable`— y agregar propiedades para claves que
nadie consume es precisamente la deriva que el ADR 0083 y el gate de contratos existen para
frenar: schema que se ve completo y no hace nada.

Servicio y categorías **no son configuración editorial**: son atributos de la cabina que el
proveedor conoce y el editor no. Su sitio es la carga, no el DocType — y ahí es donde se
resolvieron (ver abajo).

## Lo que se midió, y es peor de lo que parecía

Contrastando los cuatro ejes del arquitecto contra el contrato real. **Esta es la medición
original**; los dos ejes tachados se cerraron el mismo día en la sección siguiente:

| Eje | Estado | Detalle |
|---|---|---|
| **Apariencia** | Parcial, y **nada por el componente** | El bundle no expone ni un input de apariencia. Lo que funciona viene del host: `compDom*` aterriza en el envoltorio y el SCSS del bundle está tokenizado, así que el tema del siteRoot ya manda el color. Es el exterior de la caja; el interior no se configura |
| ~~**Servicio**~~ | ~~**Nada**~~ → **cerrado** | `ServiceClass` no tenía clave en la carga. El 787 sembrado son 43 filas en tres cabinas y se dibujaba como una sola grilla indiferenciada: un pasajero no veía dónde termina ejecutiva |
| ~~**Forma de construcción**~~ | ~~Parcial — el peor hueco~~ → **cerrado** | `aisleAfterColumns` era **un solo entero**, así que solo se podía dibujar **un** pasillo. Todo widebody salía mal: el `3-3-3` dibujaba el pasillo tras la C y **nada entre F y G**; el bloque derecho se soldaba al central. El `1-2-1` igual |
| ~~**Categorías**~~ | ~~Parcial~~ → **cerrado** | El enum `type` mezclaba dos ideas ortogonales —tres posiciones más un rasgo de confort—, así que `extra-legroom` **sobrescribía** la posición cuando ambas eran ciertas. `Features` e `IsExitRow` se descartaban |

### Lo que el contrato de la UI necesitaría, por prioridad

1. ~~**`aisleAfterColumns` → arreglo.**~~ **Hecho.**
2. ~~**`rows[].serviceClass`** más un encabezado de sección en la plantilla.~~ **Hecho.**
3. ~~**`seats[].features: string[]`**, aditivo.~~ **Hecho.**
4. **Entradas de apariencia** (`density`, `showLegend`, `showPrices`). Solo vale la pena después
   de lo anterior. **Sigue pendiente** — y `showLegend` perdió parte de su motivo: la leyenda
   ahora se deriva del contenido, así que un mapa sin rasgos ya no dibuja ninguna.

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
- **Las tres cabinas sembradas se dibujan bien.** Dos son de doble pasillo y hasta ahora salían
  con uno.

### Lo que se acepta

- **Una cabina con dos fórmulas sigue sin poder expresarse.** Los pasillos son del MAPA, no de
  la fila: un avión cuya ejecutiva es `1-2-1` y su turista `3-3-3` declara una sola geometría y
  una de las dos secciones sale corrida. Se aceptó por dos razones: el proveedor tampoco lo
  modela —una `CabinSpec` tiene una fórmula— y la supresión del pasillo final evita que la fila
  corta quede con un hueco colgando. Cerrarlo sería `rows[].aisleAfterColumns` con respaldo al
  del mapa, y toca las dos mitades otra vez.
- **El vocabulario abierto no se valida en ninguna de las dos mitades.** Un proveedor que mande
  `xtra-legroom` verá esa cadena rotulada tal cual en la leyenda, sin error en ningún gate. Es
  el precio de no exigir un despliegue del CMS por cada rasgo nuevo, y se aceptó a sabiendas.
- **La apariencia sigue sin configurarse desde el bloque.** Es el único de los cuatro ejes que
  queda, y el que menos duele: el tema del siteRoot ya manda el color por el envoltorio.
  `density` y `showPrices` serían lo siguiente; `showLegend` perdió parte de su motivo, porque
  la leyenda ahora se deriva del contenido y un mapa sin rasgos ya no dibuja ninguna.
- **Sin diccionario y sin respaldo SSR.** El servidor no emite copy: la proyección emite ids,
  enums, booleanos y números; todo el texto visible vive en el bundle. Un respaldo SSR sería lo
  primero que necesitaría claves de diccionario, y una grilla de butacas renderizada en el
  servidor duplicaría el componente en vez de degradarlo.
- **Una fila con un hueco físico** (galley, mamparo) corre las letras siguientes, porque el
  bundle rotula por índice del arreglo. Los saltos de *número de fila* sí funcionan.
- `pax-selector` **sigue sin DocType**, y esta enmienda no lo cambia: es el caso puro del
  ADR 0126 — no tiene sentido fuera del wizard que lo embebe.
