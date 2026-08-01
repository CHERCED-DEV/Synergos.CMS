# ADR 0119 — La estadía es contenido, y su reputación no se inventa

- **Estado:** Aceptado
- **Fecha:** 2026-08-01
- **Complementa:** ADR 0118 (el inmueble es contenido), ADR 0117 (el aforo de un evento es
  contenido), ADR 0107 (motor de catálogo + `ICatalogSource`), ADR 0021 (DataType por intención
  editorial), ADR 0083 (la UI es la fuente de verdad de las claves), ADR 0002 (Application sin
  Umbraco)

## Contexto

Booking era **el último vertical con catálogo y cero superficie CMS**. No existía un DocType de
propiedad hotelera: las estadías del portal vivían sembradas en C# dentro de
`StubStayContentProvider` —Cartagena, Medellín, Bogotá y el Eje Cafetero, con geo real— y un
hotelero no tenía dónde publicar la siguiente. El adapter real estaba anotado en el propio
contrato del seam —*"CMS content / channel-manager"*— apuntando a algo que nunca se escribió.

Con esta rebanada se cierra la serie que abrieron Tienda, Eventos e Inmobiliaria: los cuatro
verticales con catálogo se sirven ya del contenido que alguien autora.

Hay una diferencia de forma con los tres anteriores, y conviene decirla de entrada:
`IStayContentProvider` **es de solo lectura**. Tiene `GetStayAsync` y nada más. No hay un
`PublishStayAsync` equivalente al del agente inmobiliario o al del organizador de eventos,
porque en este dominio no existe un segundo autor: la disponibilidad y el precio los cotiza
`IRoomAvailabilityProvider`, y el contenido lo escribe el hotelero en el backoffice. Eso
simplifica la rebanada entera, como se explica abajo.

## Decisión

### 1. `stayListing`, el DocType que faltaba

Una propiedad hotelera se autora en cinco pestañas: **Contenido** (slug, nombre, ciudad,
departamento, códigos de tipo de habitación), **Ficha** (descripción, galería, amenidades),
**Características** (habitaciones, huéspedes por habitación, tamaño promedio, check-in,
check-out), **Ubicación** (dirección, lat/lng) y **Reseñas** (calificación general, número de
reseñas, frase destacada y los cuatro ejes del desglose).

Se **reutiliza agresivamente**: `Textstring`, `Textarea`, `Numeric`, `DTTextList`,
`Multiple Image Media Picker` y `DTGeoCoordinate` cubren dieciocho de las veintitrés
propiedades. Tres enteros casi idénticos —habitaciones, huéspedes, metros— comparten el
`Numeric` de siempre en vez de nacer como tres DataTypes nuevos, que es justo la proliferación
contra la que advierte el ADR 0021.

Dos DataTypes nuevos, y ninguno gratuito:

| DataType | Editor | Por qué |
|---|---|---|
| `DTReviewScore` | Decimal `min 0 / max 10 / step 0.1` | No es "otro decimal". `DTPriceCop` y `DTGeoCoordinate` no llevan configuración porque un precio y una coordenada no tienen tope; un puntaje **sí**, y ese tope es lo que hace que el backoffice rechace el 92 de quien pensó sobre 100 antes de que llegue al servidor. El `step` fija la resolución de un decimal que el rubro usa así |
| `DTTimeOfDay` | DateTime con formato `HH:mm` | Un check-in es una hora, no una fecha. `Date Picker with time` obligaría al hotelero a elegir un día que no significa nada, y un `Textstring` reabre el problema del §2 en el eje del valor |

**No hay campo de precio**, y no es un olvido: la tarifa de una estadía no vive en su contenido
sino en `IRoomAvailabilityProvider`, que cotiza por fecha y ocupación. Es la diferencia
estructural con `propertyListing`, donde el precio es una propiedad del inmueble y su ausencia
descarta la ficha.

### 2. Las características se DERIVAN, y la hora también

`StaySpec` es un par etiqueta-valor y la tentación obvia era, otra vez, un BlockList de
"Etiqueta / Valor". Se descartó por lo mismo que en el ADR 0118 §2: habría dado *"Check-in"*,
*"check in"*, *"Check-In"* y *"Hora de entrada"* en el mismo portal, y ninguna comparación
posible entre propiedades. Los campos son tipados, el orden lo pone el proyector, y **un valor
en 0 o sin autorar no sale**: "Tamaño promedio: 0 m²" no informa, desinforma.

La novedad frente a Inmobiliaria es que aquí **también se deriva un valor**, no solo su
etiqueta. El hotelero elige una hora en un reloj y el servidor decide cómo se lee:

```
15:00 → "3:00 p. m."   ·   12:00 → "12:00 m."   ·   11:00 → "11:00 a. m."   ·   00:30 → "12:30 a. m."
```

Con texto libre habrían convivido "15:00", "3pm", "3:00 PM" y "3:00 p.m." en el mismo sitio. Y
el formato se compone a mano en vez de con `ToString("h:mm tt", es-CO)` por dos razones
concretas: **el mediodía en Colombia se escribe `12:00 m.`**, que ninguna cultura de .NET
produce; y depender de ICU haría que la cadena cambie con la versión del runtime o con
`InvariantGlobalization` activado, sin que nada falle.

### 3. Los ejes de reseñas son cuatro y fijos — el ElementType anidado se descartó

Era el único sitio de esta rebanada donde un ElementType anidado tenía sentido a primera vista:
`StayReviewCategoryScore` es una lista de (categoría, puntaje) y un BlockList la modela sin
esfuerzo. **Se decidió que no**, y la razón no es la de siempre:

> El desglose por categoría existe **para comparar propiedades entre sí**. Eso solo funciona si
> todas puntúan los mismos ejes. Con texto libre, un hotel calificaría "Limpieza" y el de al
> lado "Aseo", y la comparación moriría en silencio.

El problema de mayúsculas del ADR 0118 §2 sigue aplicando encima, pero es el secundario: aquí
lo que se pierde con la libertad no es una faceta, es el sentido del bloque entero. Cuatro
campos `DTReviewScore` —limpieza, ubicación, servicio, relación precio-calidad— con sus
etiquetas en el Dictionary; un puntaje en 0 no sale, porque un 0 en "Servicio" es un campo sin
diligenciar y no una calificación.

**Lo que se acepta:** una propiedad no puede añadir un quinto eje sin tocar el schema. Es
correcto — un eje que solo tiene un hotel no se puede comparar con nada y pintaría una barra
huérfana en la ficha.

### 4. Un puntaje sin reseñas detrás no es prueba social

`ReviewCount ≤ 0` **vacía el bloque entero**: promedio en 0, desglose vacío, frase destacada
fuera. Ningún portal del rubro muestra un 9,2 respaldado por cero opiniones, y emitirlo sería
inventar reputación. La propiedad no se pierde por eso: sale sin bloque de reseñas, que es
exactamente lo correcto para una que acaba de abrir. Si el hotelero **sí** había escrito
puntajes, se avisa; si no había escrito nada, no se dice nada — un hotel nuevo sin reseñas es lo
normal y avisarlo sería ruido en cada arranque.

Por lo mismo, **el promedio no se calcula a partir de los cuatro ejes**. La media de limpieza,
ubicación, servicio y precio-calidad no es la calificación general en ningún portal del rubro:
se pondera por volumen y por otras señales. Derivarla aquí sería inventar un número con pinta de
dato. Cuando hay desglose y el promedio va en 0, se avisa y se sirve así: la ficha muestra las
barras sin encabezado, que es feo pero cierto.

Un puntaje fuera de 0-10 **se recorta y avisa**, no descarta: un 92 es casi siempre una escala
equivocada, y 10 es la lectura correcta de esa intención.

### 5. El código de tipo de habitación es la llave con el buscador, y se vigila

`RoomTypeCodes` es lo que ata cada oferta del buscador de hotel (`RoomOffer.RoomTypeCode`) a su
propiedad, y de ahí salen el nombre, la foto, el rating y el pin del mapa de cada resultado. Por
eso los códigos se normalizan a mayúsculas y sin repetidos: un `"dlx "` con espacio es una
oferta sin nombre, sin foto y sin pin.

Una lista vacía es **legítima** y no avisa: una propiedad direct-only —sin cupo en el motor de
disponibilidad— tiene ficha y no tiene ofertas. Dos de las seis estadías sembradas ya eran así.

Lo que sí es un error es que **dos propiedades declaren el mismo código**. `GetStayAsync`
devuelve la primera que lo reclama, así que las ofertas de la segunda saldrían con el nombre, la
foto y el pin de la primera. No falla nada —el JSON es válido y la pantalla se pinta— y por eso
hay que gritarlo. Es la única regla que solo se ve mirando el catálogo entero, así que vive
aparte de la proyección de cada nodo: `FindRoomTypeCollisions`, pura y con sus pruebas, la
ejecuta la fuente después de proyectar. **No corrige**: quitarle el código a uno de los dos
escondería inventario, y elegir a cuál sería arbitrario.

### 6. Se omite poco, y solo lo que no se puede servir sin mentir

Sin slug (no hay nada que resolver en `/api/travel/stay/{id}`), sin nombre —y aquí **no** se cae
al nombre del nodo, porque *"Estadía (1)"* no solo sale en la tarjeta: es el `Title` que hereda
cada oferta del buscador— o sin ciudad (no agrupa ni se encuentra). Nada más. Todo lo demás se
degrada.

La geo incompleta o fuera de rango se sirve como **(0, 0)**, mismo contrato que el ADR 0118 §4:
se exigen las dos coordenadas y dentro de rango, porque media coordenada no es medio pin sino un
pin en el meridiano de Greenwich. La ficha aparece y se queda fuera del mapa.

> **Verificación pendiente.** En Inmobiliaria el contrato de (0, 0) se pudo comprobar en el
> cliente (`realty.ts` solo dibuja el pin si `lat !== 0 || lng !== 0`). El módulo de Booking vive
> en el repo hermano `Synergos.UI`, que no estaba disponible al escribir esto, así que la regla
> se calca de Inmobiliaria por consistencia y **queda por confirmar contra el guard del módulo**.
> Lo que sí consta en este repo es que `TravelController` ya emite `geo: null` cuando la estadía
> no resuelve, de modo que la ausencia de pin es un caso que la UI necesariamente maneja.

### 7. Las etiquetas del servidor van al Dictionary uSync; nada más

Las cinco etiquetas de características (`Stay.Spec.*`) y los cuatro nombres de eje
(`Stay.Review.*`) viajan **ya escritos** en el JSON que consume la ficha: son texto del
servidor. Por eso son claves del Dictionary uSync con es-CO y en-US.

Y **solo esas nueve**. La regla del ADR 0118 §3 se aplicó igual: los módulos Angular traen su
i18n dentro del bundle y no consumen el diccionario del CMS, así que una clave para copy del
bundle nacería huérfana. *Una cadena va al Dictionary uSync si y solo si el servidor la emite.*

### 8. Una sola capa, y decirlo es la mitad de la decisión

`CatalogStayContentProvider` sirve el contenido **sin capa durable encima**, a diferencia de
`CatalogPropertyCatalogProvider` y `CatalogEventCatalogProvider`, que fusionan el contenido con
lo que publicaron el agente o el organizador desde su consola.

**No es una omisión: es la consecuencia de que la seam sea de solo lectura.** No hay un segundo
autor que publique por fuera del backoffice, así que no hay nada que fusionar. Añadir el
`IJsonEntityStore` "por simetría" habría creado exactamente el campo que promete algo que nadie
cumple —un overlay sin escritor— y el siguiente que lo viera confiaría en él. Cuando exista un
channel-manager que empuje inventario, esa capa se añade junto con su escritor.

Lo que **sí** se calca del stub es el orden de resolución: primero `stayId` exacto, después
código de tipo de habitación, aceptando el `offerId` del buscador (`"DLX/DLX-FLEX"`) por su
segmento inicial. Si eso cambiara al mover el flag, cada oferta del buscador perdería su
identidad sin que nada fallara.

## Por qué así

### Por qué el cuarto vertical repite la forma en vez de generalizarla

El ADR 0118 dejó abierta la pregunta: *"se generaliza cuando haya un cuarto que aporte
evidencia"*. Este es el cuarto, y **la evidencia dice que no**.

Lo que se repite sigue siendo la **forma**: leer nodos del siteRoot acotado, proyectar, omitir lo
inservible, contar lo omitido en una línea. Son unas cuarenta líneas de `ResolveNodes` casi
idénticas. Lo que no se repite es nada de lo que importa: las reglas de Eventos hablan de aforo y
asientos, las de Inmobiliaria de estratos y pines, las de Booking de reputación y de códigos de
habitación compartidos. Y Booking rompió incluso la parte que parecía estable —no tiene precio,
no tiene capa durable, y tiene una regla que mira el catálogo entero y no el nodo—, que es
precisamente la señal de que una base común habría tenido que parametrizar la parte trivial y
dejar fuera la cara.

Lo que sí se extrajo, en su momento y con evidencia, fue lo pequeño y estable: `EventContentResult<T>`,
`EventContentIssue` y `CleanTextList` son de Eventos y los usan los cuatro sin ceremonia.

### Por qué las reglas volvieron a salir a una clase pura

Igual que en los ADR 0117 y 0118, y por la misma razón: `IPublishedContent.Value<T>` no se puede
simular con coste razonable, y las reglas que deciden si un hotel muestra un 9,2 respaldado por
cero opiniones no podían ser las únicas sin cobertura. `StayContentRules` es pura y tiene sus
pruebas; `UmbracoStayContentSource` solo lee y loguea.

### Por qué la fuente falla cerrada cuando falta el scope

Sin `Synergos:Catalog:Scopes:Booking` **no se sirve nada**, y se registra un `LogError`. Es la
misma decisión de los otros tres, y la razón vale doble aquí: `productPage` ya demostró que un
DocType compartido entre siteRoots sin acotar mezcla inventario en silencio. Un catálogo de
hoteles que devuelva las propiedades de otro origen es peor que uno vacío, porque el vacío se ve.

## Consecuencias

### Lo que se gana

- Booking deja de ser el vertical sin CMS: un hotelero publica una propiedad completa sin tocar
  código, y `Synergos:Catalog:Sources:Booking = cms` la sirve. Con esto **los cuatro verticales
  con catálogo se sirven de contenido**.
- Cada oferta del buscador de hotel puede heredar nombre, foto, rating y pin de una propiedad que
  alguien autoró, en vez de una sembrada en C#.
- Las etiquetas de características y los ejes de reseñas se traducen sin desplegar.
- El código de habitación duplicado —que antes habría sido un misterio de "por qué esta oferta
  sale con el nombre de otro hotel"— ahora es una línea de error en el log.

### Lo que se acepta

- **`stayListing` no tiene template ni partial de render.** Es el modelo que consume el bundle
  Angular vía la API, no una página que Umbraco enrute. Una estadía no tiene URL propia en el
  CMS; su ficha la pinta el módulo.
- **La reputación la escribe el hotelero.** Este DocType no la deriva de reseñas reales, como sí
  hace Tienda con `ICatalogSocialProof` (ADR 0114). Es contenido declarado, con las salvaguardas
  del §4 puestas justamente porque lo es. Migrarlo a UGC es una rebanada aparte, y `stayReview*`
  quedaría entonces como respaldo del arranque en frío.
- **Cuatro ejes de reseñas fijos**, sin manera de que una propiedad añada el quinto.
- **Cuatro clases paralelas** entre verticales, ahora con evidencia explícita de por qué no se
  unifican.
- El catálogo se relee entero en cada resolución, sin caché. Misma decisión consciente que el
  resto del motor: read-your-writes gratis, y una caché se añade junto con su invalidador y su
  consumidor, nunca antes. Aquí duele menos que en los otros tres: `GetStayAsync` se llama una
  vez por código de habitación distinto y no por oferta.

### Lo que hay que hacer antes de que esto se vea

1. El arquitecto corre **uSync Import** desde el backoffice (dos DataTypes, un DocType, diez
   entradas de Dictionary).
2. Debe declarar **`Synergos:Catalog:Scopes:Booking`** con el `brandKey` del siteRoot donde vive
   el portal. Sin ese scope la fuente falla cerrada y ruidosa a propósito.
3. Debe aplicar a mano las **dos líneas de registro en `SeamComposer`** que reemplazan el
   registro incondicional del stub — están en el reporte de la rebanada.
4. Queda pendiente **confirmar el guard de (0, 0)** en el módulo Angular de Booking cuando
   `Synergos.UI` esté disponible (§6).
