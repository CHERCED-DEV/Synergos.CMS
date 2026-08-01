# ADR 0117 — El aforo de un evento es contenido, y los asientos se generan

- **Estado:** Aceptado
- **Fecha:** 2026-08-01
- **Reemplaza / complementa:** ADR 0107 (motor de catálogo + `ICatalogSource`), ADR 0008
  (schema vía uSync), ADR 0002 (grafo de capas), ADR 0021 (DataType por intención editorial),
  ADR 0105 (`IJsonEntityStore`)

## Contexto

Desde la Ola A (ADR 0107) Eventos tiene una fuente de catálogo respaldada por contenido,
`UmbracoEventCatalogSource`, y un flag para activarla:
`Synergos:Catalog:Sources:Events = cms`.

Ese flag **no hacía nada**. El propio código lo decía:

> ⚠️ REGISTRADA PERO AÚN SIN CONSUMIRSE, y por eso el flag nace en "demo" […]
> `eventPage` solo modela el RESUMEN — cablearlo hoy daría una agenda del CMS cuyas fichas no
> se pueden comprar. Modelar tiers/aforo como contenido es la rebanada siguiente.

La razón era concreta: `IEventCatalogProvider` devuelve `EventDetail` —resumen **más**
localidades, aforo, agenda y mapa de asientos— y `eventPage` solo tenía título, fecha, ciudad,
recinto, imagen, precio "desde" y geo. Un editor podía anunciar un evento pero no ponerle nada
a la venta. Mientras tanto, el módulo Angular de Eventos ya sabía pintar tarjetas de localidad
con precio, aforo restante, beneficios en viñetas, ventana de venta y localidad recomendada, y
un mapa de asientos por zonas: la UI iba por delante del schema.

Esta es esa rebanada siguiente.

## Decisión

### 1. Las localidades, la agenda y las zonas se modelan como contenido anidado

Tres ElementTypes nuevos, consumidos por `eventPage` a través de tres BlockList:

| ElementType | Qué modela | BlockList |
|---|---|---|
| `elementEventTier` | Una localidad a la venta: código, nombre, precio, aforo, tope por compra, qué incluye, beneficios, ventana de venta, recomendada, zona | `DT.BlockList.EventTiers` |
| `elementEventSession` | Un punto de agenda: hora, qué pasa, quién | `DT.BlockList.EventSessions` |
| `elementEventZone` | Una zona con asiento numerado: código, nombre, localidad, precio, filas, butacas por fila | `DT.BlockList.EventZones` |

`eventPage` gana además descripción, organizador, "por qué asistir", el perfil del artista
(nombre, descriptor, seguidores) y el nombre del mapa, repartidos en tres pestañas nuevas —
**Ficha**, **Entradas** y **Mapa de asientos**— para que la pestaña Contenido siga siendo lo
que el editor llena primero.

Un solo DataType genérico nuevo, `DTTextList` (`Umbraco.MultipleTextstring`), cubre las dos
listas de texto repetible (beneficios y "por qué asistir") y los rótulos de fila. Precios,
aforos y contadores reusan el `Numeric` que ya existía: tres DataTypes de entero idénticos
habrían sido justo la proliferación contra la que advierte el ADR 0021.

### 2. Los asientos se GENERAN de filas × butacas; no se autoran

Una zona declara sus rótulos de fila (`A`, `B`, `C`) y cuántas butacas tiene cada una. De ahí
salen `A1…A12`, `B1…B12`. El id de cada asiento lleva el código de la zona por delante
(`platea-A1`).

### 3. Las reglas de negocio viven en una clase pura, no en el lector de Umbraco

`EventContentRules` (Web, pero sin un solo tipo de Umbraco) recibe records planos y devuelve
records de dominio. `UmbracoEventCatalogSource` solo lee contenido, llama a las reglas y emite
al log los problemas que devuelven.

### 4. El flag `cms` ya no es inerte

`CatalogEventCatalogProvider` (Application, lógica pura) implementa `IEventCatalogProvider`
sobre `ICatalogSource<EventDetail>` más una capa durable de eventos publicados por
organizadores. `UmbracoEventCatalogSource` pasa a emitir las dos caras del catálogo —el
resumen que lista el buscador y la ficha comprable— del mismo recorrido del árbol.

## Por qué así

### Por qué el aforo es contenido y no configuración

Es la pregunta que decide el resto. El aforo se parece a un parámetro de sistema, pero **cambia
por evento y lo cambia quien no toca código**: el organizador amplía la platea, abre una
segunda tanda de early-bird, cierra el palco. Si vive en `appsettings`, cada ajuste comercial
es un despliegue. Vive donde ya vive el resto de la ficha.

### Por qué los asientos se generan

Un teatro mediano son varios miles de butacas. Autorarlas una por una en el backoffice no es
un modelo de contenido: es una hoja de cálculo mal puesta, y una que se desincroniza en cuanto
el recinto cambia una fila. Declarar 40 filas de 30 butacas son dos campos.

El coste es un techo: `MaxSeatsPerEvent = 20 000`. `zoneSeatsPerRow = 10000` es un dígito de
más y son diez mil objetos por fila, serializados en cada request de la ficha. **Al pasarse, la
zona se descarta entera con un error, no se recorta:** media zona vende asientos que no
existen, y eso lo descubre el asistente en la puerta.

### Por qué todas las reglas omiten y ninguna lanza

Una ficha con una localidad mal escrita tiene que vender las otras. Lo que no puede pasar nunca
es servir algo plausible pero equivocado. De ahí las decisiones que parecen severas:

- **Código de localidad repetido → se queda la primera, error en el log.** El código es lo que
  el checkout usa para resolver precio y aforo. Con dos `vip` a distinto precio, cuál cobra
  depende del orden de un diccionario y el comprador paga lo que salga.
- **Aforo 0 → la localidad no se pone a la venta.** Una tarjeta agotada desde el minuto cero
  confunde más de lo que informa.
- **Precio negativo → se descarta.** No es un descuento: es una errata que le pagaría al
  comprador.
- **Zona apuntando a una localidad inexistente → se descarta.** Vendería asientos que el
  checkout no sabe cobrar.
- **Tope por compra recortado al aforo.** Ofrecer "hasta 10" sobre una localidad de 4 es
  prometer seis entradas que no existen.

### Por qué el modo real manda sobre el declarado

`eventMode` es un campo de texto que el editor escribe. Un evento marcado `reserved` sin zonas
servibles dejaría al asistente en una pantalla de mapa vacía, sin forma de comprar; uno marcado
`general` que sí trae mapa se vendería por cantidad, repartiendo asientos que el comprador
creía elegir. Gana lo que hay, y se avisa.

### Por qué las reglas salieron a una clase pura

No fue una preferencia de estilo: **es lo único que las hace verificables.**
`IPublishedContent.Value<T>` es un método de extensión sobre una cadena de propiedades,
fallbacks y converters; simularlo cuesta más que el código que verifica, y por eso en este repo
no había un solo test de las fuentes Umbraco-backed. Las reglas que deciden si alguien paga por
un asiento que existe no podían ser las únicas sin cobertura. Con el corte, son 29 tests.

### Por qué el organizador publica a una capa aparte y no a `eventPage`

`IEventCatalogProvider.PublishEventAsync` lo usa la cara de organizador
(`POST /api/eventos/event`). Con el catálogo servido desde contenido había cuatro salidas:

1. **Lanzar** — rompe una pantalla que hoy funciona.
2. **Tragárselo** — el organizador publica y no pasa nada.
3. **Escribir nodos de Umbraco desde Application** — imposible sin romper el ADR 0002, y crear
   contenido publicado a espaldas del editor es una decisión editorial, no técnica.
4. **Persistir en el store genérico (ADR 0105)** — sobrevive un reinicio, es visible de
   inmediato, y el editor puede promoverlo a `eventPage` cuando quiera.

Se eligió la cuarta. **Que el ascenso a contenido sea manual es deliberado**, no una tarea
pendiente: quién publica en el sitio es una decisión de gobierno editorial.

La capa del organizador **gana** sobre el contenido cuando coinciden id o slug. Publicar y no
ver el cambio es peor que la ambigüedad.

### Por qué no se repuntaron las propiedades existentes a DataTypes nuevos

`eventCategory`, `eventCity`, `eventMode`, `eventPriceFrom`, `eventLat` y `eventLng` siguen
siendo `Umbraco.TextBox` aunque `eventMode` sea un enum de dos valores y los otros sean números.
Cambiar el `<Definition>` de una propiedad que **ya tiene contenido** cambia cómo se deserializa
lo guardado: `Umbraco.DropDown.Flexible` guarda `["general"]` y encontraría un `general` a
secas. Eso no es un cambio de schema, es una migración de contenido, y necesita su propia
rebanada con un paso de conversión. Queda anotado como deuda con dueño, no como olvido.

## Consecuencias

### Lo que se gana

- El flag `Synergos:Catalog:Sources:Events = cms` **hace algo**: la agenda y las fichas salen
  del contenido del editor, con rollback de una línea a `demo` sin redespliegue.
- Un editor puede publicar un evento comprable de punta a punta sin tocar código.
- Las reglas que deciden qué se puede comprar están cubiertas por tests por primera vez.

### Lo que se acepta

- **`Remaining` arranca igual al aforo declarado.** El contenido declara *cuánto hay*, no
  *cuánto queda*: lo vendido lo sabe el ledger de reservas del motor de ticketing, que esta capa
  no ve. En una ficha servida desde el CMS, el "quedan N" y el porcentaje vendido arrancan
  llenos. Cerrarlo es una consulta de disponibilidad al motor — su propia rebanada.
- **Dos orígenes de verdad mientras el flag esté en `cms`**: contenido y capa del organizador.
  El orden entre ellas está definido y cubierto por tests, pero sigue siendo un empalme.
- **Los tres ElementTypes nuevos no tienen partial de render.** Son modelos de datos que consume
  el bundle Angular vía la API, no bloques que el editor coloque en una página.
- El techo de 20 000 asientos por evento es arbitrario. Es holgado para un recinto real y
  estrecho para una errata, que es exactamente lo que se buscaba.

### Lo que hay que hacer antes de que esto se vea

El arquitecto corre **uSync Import** desde el backoffice. Hasta entonces el schema existe como
XML pero no en la base de datos, y los eventos ya autorados no tienen dónde poner sus
localidades.
