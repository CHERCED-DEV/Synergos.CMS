# ADR 0118 — El inmueble es contenido, y sus características se derivan

- **Estado:** Aceptado
- **Fecha:** 2026-08-01
- **Complementa:** ADR 0117 (el aforo de un evento es contenido), ADR 0107 (motor de catálogo +
  `ICatalogSource`), ADR 0021 (DataType por intención editorial), ADR 0105 (`IJsonEntityStore`),
  ADR 0083 (la UI es la fuente de verdad de las claves)

## Contexto

De los nueve verticales, Inmobiliaria era el único con catálogo y **cero superficie CMS**. No
existía un DocType de inmueble: los ocho listados del portal vivían sembrados en C# dentro de
`StubPropertyCatalogProvider`, y un editor no tenía dónde publicar el noveno. El adapter real
estaba anotado en el propio contrato del seam —*"Examine sobre `propertyListing`, o una API
MLS"*— apuntando a un `propertyListing` que nunca se escribió.

Al mismo tiempo, el inventario funcional (§3.1) marcaba a Inmobiliaria como el vertical donde
**nada persiste**: visitas, leads y búsquedas guardadas vivían en diccionarios de proceso.

Esta ADR cubre la primera mitad —el inmueble como contenido—. La segunda es la aplicación
mecánica del ADR 0105 y no necesita decisión nueva.

## Decisión

### 1. `propertyListing`, el DocType que faltaba

Un inmueble se autora en cinco pestañas: **Contenido** (slug, título, operación, tipo, precio,
destacado), **Ficha** (descripción, galería, amenidades), **Características** (habitaciones,
baños, área, parqueaderos, estrato, antigüedad, piso), **Ubicación** (ciudad, barrio,
dirección, lat/lng) y **Agente** (nombre, teléfono).

Cuatro DataTypes nuevos, y ninguno gratuito:

| DataType | Editor | Por qué |
|---|---|---|
| `DTSelectPropertyOperation` | DropDown | venta \| arriendo. Es el filtro principal del portal; texto libre lo rompería con "Venta", "VENTA" y "en venta" |
| `DTSelectPropertyType` | DropDown | 7 tipos. Alimenta una faceta: cada variante tecleada es un chip basura |
| `DTPriceCop` | Decimal | **No `Umbraco.Integer`.** Un inmueble de 3 000 millones desborda `int` (tope ≈ 2 147 millones). En Eventos el entero sirve porque una boleta no llega ahí; aquí, sí |
| `DTGeoCoordinate` | Decimal | En `eventPage` la geo es `TextBox` y hay que parsearla a mano. Aquí nace tipada |

### 2. Las características se DERIVAN de campos tipados

`PropertySpec` es un par etiqueta-valor, y la tentación obvia era un BlockList de "Etiqueta /
Valor". Se descartó: habría dado libertad total y, con ella, *"Habitaciones"*, *"habitaciones"*,
*"Hab."* y *"N° de habitaciones"* en el mismo portal — y ninguna faceta posible, porque nada de
eso es un número comparable.

Los campos son tipados, el orden lo pone el proyector, y **un valor en 0 o vacío no sale**:
"Parqueaderos: 0" es ruido en un apartaestudio y "Estrato: 0" es directamente falso.

### 3. Las etiquetas de las características viven en el Dictionary uSync

`PropertySpec.Label` viaja **ya escrito** en el JSON que consume la ficha: es texto del
servidor, no del bundle. Por eso las siete etiquetas (Área, Habitaciones, Baños, Parqueaderos,
Estrato, Antigüedad, Piso) son claves `Realty.Spec.*` con es-CO e en-US.

Esto es lo contrario de lo que corresponde al resto del copy del vertical. Los nueve módulos
Angular traen su i18n dentro del bundle y **no consumen el diccionario del CMS** — se verificó
antes de escribir una sola clave, y por eso Eventos no recibió ninguna: habrían nacido
huérfanas. La regla que queda: *una cadena va al Dictionary uSync si y solo si el servidor la
emite.*

### 4. (0, 0) significa "sin pin", porque eso es lo que la UI ya declara

`PropertyListing.Lat/Lng` son `double` no anulables, así que el dominio no puede expresar "sin
ubicación". La pregunta era qué hacer con un inmueble sin coordenadas.

La respuesta estaba en el cliente: `realty.ts:447` solo dibuja el pin si
`listing.geo.lat !== 0 || listing.geo.lng !== 0`. Así que el backend emite (0, 0) y el inmueble
aparece en los resultados y se queda fuera del mapa — que es exactamente lo correcto. Se exigen
las **dos** coordenadas y dentro de rango: media coordenada no es medio pin, es un pin en el
meridiano de Greenwich, y una longitud tecleada en el campo de latitud manda el mapa al océano.

### 5. Se omite poco, y solo lo que no se puede servir sin mentir

A diferencia de Eventos, donde descartar una localidad deja las otras a la venta, **perder una
ficha es perder el activo que el portal existe para mostrar**. Solo se descarta por: sin slug
(no hay enlace), sin título (caer al nombre del nodo daría *"Inmueble (1)"* en la tarjeta), sin
ciudad (no cae en ninguna faceta y nadie lo encuentra) o precio ≤ 0 (se pinta como *Gratis* —
y es la misma regla que `PublishListingAsync` ya aplicaba al borrador del agente).

Todo lo demás se degrada: una operación desconocida cae a `venta` con aviso, un tipo vacío se
queda vacío **sin inventarse** —inventar "apartamento" haría que quien filtra por apartamento
acabe viendo un lote— y un entero negativo vale 0 sin perder la ficha.

### 6. Misma forma de dos capas que Eventos

`CatalogPropertyCatalogProvider` (Application, pura) sirve el contenido con una capa durable
encima para lo que publica el **agente** desde la consola, que gana por id o por slug. La
búsqueda se calca del stub —mismo descriptor, mismo motor, mismo pre-filtro de precio y
ubicación fuera del índice— porque un buscador que se comporte distinto según de dónde salen
los inmuebles es una regresión invisible que solo aparece al mover el flag, y en un portal
inmobiliario **el buscador es el producto**.

## Por qué así

### Por qué el tercer vertical repite la forma en vez de generalizarla

Tienda, Eventos e Inmobiliaria tienen ahora tres clases casi paralelas: `Umbraco*CatalogSource`
+ `*ContentRules` + `Catalog*CatalogProvider`. La tentación de extraer una base genérica es
real y se resistió a propósito.

Lo que se repite es la **forma** (leer nodos del siteRoot, proyectar, omitir lo inservible,
fusionar con una capa durable); lo que no se repite es **nada de lo que importa**: las reglas de
Eventos hablan de aforo y asientos, las de Inmobiliaria de estratos y pines. Una base común
tendría que parametrizar la parte trivial y dejar fuera la parte cara — la definición de una
abstracción prematura, que este repo prohíbe explícitamente. Se generaliza cuando haya un
cuarto que aporte evidencia, no antes.

### Por qué las reglas volvieron a salir a una clase pura

Igual que en el ADR 0117, y por la misma razón: `IPublishedContent.Value<T>` no se puede
simular con coste razonable, y las reglas que deciden si un inmueble de 850 millones aparece en
el portal no podían ser las únicas sin cobertura. `PropertyContentRules` es pura y tiene sus
tests; `UmbracoPropertyCatalogSource` solo lee y loguea.

### Por qué el proveedor de contenido omite y el del agente lanza

Son dos llamadores distintos. El editor de backoffice ve un log y una ficha que no aparece; el
agente ve un formulario, y un 400 con la razón se puede pintar en el campo que está mal.
"Se publicó y no aparece" es la peor de las dos.

## Consecuencias

### Lo que se gana

- Inmobiliaria deja de ser el vertical sin CMS: un editor publica un inmueble completo sin
  tocar código, y `Synergos:Catalog:Sources:Realty = cms` lo sirve.
- El precio deja de poder desbordar. Con `Umbraco.Integer` un inmueble de más de 2 147 millones
  habría dado un valor negativo o un cero, en silencio.
- Las etiquetas de las características se traducen sin desplegar.

### Lo que se acepta

- **`propertyListing` no tiene template ni partial de render.** Es el modelo que consume el
  bundle Angular vía la API, no una página que Umbraco enrute. Un inmueble no tiene URL propia
  en el CMS; su ficha la pinta el módulo.
- **Las características del inmueble publicado por el agente van vacías.** El wizard captura
  habitaciones, baños y área, que ya viajan en el resumen y pinta la tarjeta; duplicarlas con
  etiquetas inventadas en Application —que no ve el Dictionary— daría dos listas distintas para
  lo mismo.
- **Tres clases paralelas** entre verticales, a la espera de un cuarto caso que justifique la
  base común.
- El catálogo se relee entero en cada búsqueda, sin caché. Es la misma decisión consciente que
  el resto del motor: read-your-writes gratis, y una caché se añade junto con su invalidador y
  su consumidor, nunca antes.

### Lo que hay que hacer antes de que esto se vea

El arquitecto corre **uSync Import** desde el backoffice, y además debe declarar
`Synergos:Catalog:Scopes:Realty` con el `brandKey` del siteRoot donde vive el portal. Sin ese
scope la fuente falla **cerrada y ruidosa** a propósito: servir sin acotar mezclaría el
inventario de todos los siteRoots, en silencio.
