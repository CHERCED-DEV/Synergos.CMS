# ADR 0123 — El trámite es contenido, y el portal no lo esconde

- **Estado:** Aceptado
- **Fecha:** 2026-08-01
- **Complementa:** ADR 0119 (la estadía es contenido), ADR 0118 (el inmueble es contenido),
  ADR 0117 (el aforo de un evento es contenido), ADR 0107 (motor de catálogo +
  `ICatalogSource`), ADR 0021 (DataType por intención editorial), ADR 0083 (la UI es la fuente
  de verdad de las claves), ADR 0002 (Application sin Umbraco)

## Contexto

Gobierno era uno de los dos últimos verticales con catálogo y **cero superficie CMS**. No existía
un DocType de trámite: los siete del portal vivían sembrados en C# dentro de
`StubTramiteCatalogProvider` —cédula, licencia, residencia, mercantil, salud, pasaporte,
subsidio— y un funcionario de una entidad pública no tenía dónde publicar el octavo. El adapter
real estaba anotado en el propio contrato del seam —*"SUIT / catálogo de la entidad vía Content
Delivery API"*— apuntando a algo que nunca se escribió.

Es la quinta rebanada de la misma serie, pero el dominio no es el mismo, y conviene decirlo de
entrada: **los requisitos y la tasa de un trámite son, en sentido literal, regulación**. Un
precio de venta lo pone quien vende; una tasa la pone una norma, y el portal no la inventa: la
recoge. Eso cambia qué se puede omitir y qué hay que gritar, y es de donde sale casi todo lo que
esta ADR decide distinto a las cuatro anteriores.

Como en Booking, `ITramiteCatalogProvider` **es de solo lectura** —`SearchAsync` y `GetAsync`, y
nada más—, con la consecuencia del §7.

## Decisión

### 1. `tramitePage`, el DocType que faltaba

Un trámite se autora en cuatro pestañas: **Contenido** (slug, nombre, resumen, categoría,
entidad, canal), **Ficha** (descripción, quién puede, documentos requeridos, normativa), **Tasa y
tiempos** (gratuito, tasa, días estimados, requiere cita) y **Formulario** (las secciones de
radicación).

Se **reutiliza agresivamente**: `Textstring`, `Textarea`, `Numeric`, `Truefalse`, `DTTextList` y
`DTPriceCop` cubren doce de las quince propiedades del DocType y once de las trece de sus dos
ElementTypes. La categoría, en particular, **no** recibe una lista desplegable propia y se queda
como `Textstring` con la misma advertencia que ya lleva `eventCategory` —*"escríbela igual en
todos"*—: la taxonomía de un portal de trámites es la de **esa** entidad, y embutir
"Identidad / Vehículos / Salud" en el schema sería hornear el organigrama de un municipio dentro
del producto.

**Dos DataTypes de valor nuevos**, que es el mismo presupuesto de los ADR 0118 y 0119, y ninguno
gratuito:

| DataType | Editor | Por qué |
|---|---|---|
| `DTSelectTramiteFieldType` | DropDown, 10 items | Es el único caso donde un texto libre no degrada: **el renderer del formulario elige el control con un `switch` sobre esta cadena**. Un `"Texto"` en vez de `"text"` no pinta un campo feo — no pinta ningún campo, y la pregunta desaparece del trámite sin que nada falle. `elementFormField` (el bloque de marketing) tiene este mismo campo como `Textstring`, y es exactamente el bug que aquí no se repite |
| `DTSelectTramiteChannel` | DropDown, 3 items | `presencial \| en-linea \| mixto` es vocabulario del **producto**, no de la entidad — al revés que la categoría. Y es el único enum de esta rebanada cuyo valor errado **mueve a una persona por la ciudad**: con texto libre convivirían "En línea", "en linea" y "Virtual", y el badge de la ficha caería a su valor por defecto en los tres casos |

A eso se suman **dos BlockList estructurales** —`DT.BlockList.TramiteSections` y
`DT.BlockList.TramiteFields`—, que no son DataTypes "de intención" sino la forma que este repo ya
tiene de declarar qué bloques admite una colección: son 18 los `DT.BlockList.*` que existen, uno
por colección, y su configuración **es** su razón de ser.

### 2. El formulario SÍ se autora como bloques anidados — y es la excepción, no el olvido

Los ADR 0118 §2 y 0119 §2/§3 rechazaron el BlockList de "Etiqueta / Valor" tres veces seguidas.
Aquí se hace lo contrario, y la diferencia no es de gusto:

> Allá la libertad mataba la **comparación entre fichas** —"Habitaciones" contra "Hab.", "Limpieza"
> contra "Aseo"—. Aquí la **variación es el producto**: un formulario de renovación de cédula y uno
> de subsidio de vivienda no comparten ni una pregunta, y el patrón GOV.UK de "una tarea por
> sección" existe precisamente para que cada trámite varíe sin tocar el módulo Angular.

Nadie compara el campo `sisben` de un trámite con el `nit` de otro; no hay faceta que proteger.
Lo que sí se cierra es el **tipo** de cada pregunta (§1), porque eso no lo declara la entidad
sino lo que el renderer sabe pintar.

Se descartó reusar `elementFormField` —el bloque de formulario de las páginas de marketing—: no
tiene opciones ni patrón, su `fieldName` significa otra cosa (la clave del POST HTTP, no la del
expediente), y extenderlo habría cambiado un bloque que otras páginas ya usan para acomodar un
vertical.

### 3. Los pasos se DERIVAN de la máquina de estados, y esa es la decisión de schema más fuerte

`TramiteStep` es un trío (id, título, detalle) y los siete trámites sembrados comparten los mismos
tres. La opción obvia era un BlockList de "Paso / Detalle". Se descartó por una razón más dura que
la de los pares etiqueta-valor:

> Los pasos **no describen el trámite: describen el expediente**. Y el expediente recorre siempre
> los mismos estados —`radicado → en-revisión → resuelto/rechazado`— porque los fija
> `ICaseWorkflowService`, no el editor.

Si cada entidad escribiera los suyos, la lista de "qué pasa" de la ficha y la `Timeline` que el
ciudadano ve dentro de su propio expediente contarían dos historias distintas del mismo
procedimiento, y ninguna capa fallaría al hacerlo. En un portal de Estado eso no es un problema de
copy: es que el ciudadano no sabe cuál de las dos es el procedimiento real, y el que lo sabe es
quien está al otro lado de la ventanilla.

Los ids se calcan del stub (`radicar`, `revision`, `resolucion`) para que mover el flag a `cms` no
cambie lo que la pantalla ya recibía.

**Lo que se acepta:** un trámite con un paso propio —"agende su cita", "presente el examen
médico"— no puede añadirlo. Es correcto: si ese paso existiera de verdad como estado del
expediente, tendría que estar en la máquina de estados, no en una lista de texto que nadie hace
cumplir. `RequiresAppointment` ya viaja como su propio dato y la ficha lo pinta aparte.

### 4. Una tasa en 0 significa "gratis" solo cuando alguien lo afirmó

Es la pregunta que este dominio hace y los otros cuatro no. En Inmobiliaria un precio ≤ 0 descarta
siempre (ADR 0118 §5) porque **todo** inmueble tiene precio y un "$0" es basura. En Gobierno la
gratuidad es lo normal y muchas veces la **manda la norma**: tres de los siete trámites sembrados
son gratuitos, y descartar todo trámite en 0 borraría justo los que el Estado más quiere que la
gente use —afiliación al régimen subsidiado, certificado de residencia, subsidio de vivienda—.

Y a la vez, un editor que simplemente no diligenció el campo también deja 0, y ahí el portal
estaría anunciando una gratuidad que ninguna norma concedió. Las dos lecturas son ciertas y el
número no las distingue.

**La ambigüedad se resuelve en el schema, no con una moneda al aire.** El DocType lleva una casilla
`tramiteIsFree` aparte, y la regla es:

| Casilla | Tasa | Resultado |
|---|---|---|
| marcada | 0 | Gratis. Sin una sola queja: es el caso normal |
| marcada | > 0 | **Se sirve COBRANDO**, y se registra un ERROR |
| sin marcar | > 0 | Se cobra. Caso normal |
| sin marcar | ≤ 0 | **Se OMITE**, con ERROR |

La casilla existe para volver **intencional** el 0; sin ella, el 0 es un campo sin diligenciar.

La fila que hay que justificar es la segunda, porque las dos declaraciones se contradicen y
cualquiera de las dos era defendible. Gana el número, y el criterio es **cuál de las dos lecturas
no puede dejar el viaje perdido**: a quien se le anuncia $65.500 y resulta exento no le pasa nada
—no paga y ya—; a quien se le anuncia gratis y hay que pagar en caja, se le acabó el día, y si
viajó desde un corregimiento, la semana. La casilla vuelve intencional un cero; no tapa un número
que el editor escribió.

### 5. El portal nunca ESCONDE un trámite que existe

Es la tesis del vertical, y de ella sale que Gobierno **descarte menos que ninguno de los otros
cuatro**: solo sin slug (no hay nada que resolver), sin nombre —y aquí **no** se cae al nombre del
nodo, porque *"Trámite (1)"* no solo sale en la tarjeta: es el `ServiceName` que hereda cada
expediente radicado y que el funcionario ve en su bandeja para siempre— y la tasa ambigua del §4.

Nada más. En particular:

- **Sin categoría se sirve**, al revés que un inmueble sin ciudad (ADR 0118 §5). La diferencia es
  medible: el motor ya salta los valores en blanco al armar facetas, así que un trámite sin
  categoría no ensucia ningún chip, y **sigue apareciendo en el listado completo y en la búsqueda
  por texto**. Queda fuera de un filtro, no del portal.
- **Sin entidad se sirve**, con aviso.
- **Sin documentos requeridos se sirve**, y se registra como **ERROR** — el único degradado que se
  loguea al nivel de los que omiten. Ver §6.
- **Sin formulario se sirve**, también como ERROR: que el trámite exista, con su nombre, su
  entidad, su tasa y su norma, es información pública a la que el ciudadano tiene derecho aunque
  la radicación en línea no esté lista. Es ERROR porque además desarma la validación de campos
  obligatorios del servidor: sin campos declarados, cualquier radicación pasa.

El principio que ordena todo esto: **el portal no esconde un trámite que existe, y tampoco hace
una afirmación que la entidad no hizo.** Omitir por falta de tasa cae del segundo lado; omitir por
falta de requisitos habría caído del primero.

### 6. Los requisitos vacíos se sirven, y se gritan

Es el otro lado de la moneda del §4, y la respuesta es la contraria por una razón concreta:

> Una tasa ausente **se convierte en una afirmación**: el portal dice "$0". Una lista de requisitos
> ausente **no dice nada**: la ficha muestra un bloque vacío. Y lo que la lista recoge no es una
> decisión del portal sino un recordatorio de lo que la norma **ya** exige.

Esconder el trámite no libera al ciudadano de tener que hacerlo. Peor: quien no lo encuentra
concluye que no existe, mientras la obligación legal sigue en pie y el plazo corriendo. Entre "sale
incompleto" y "no sale", en un vertical donde la ausencia de información no suspende el deber, la
primera es estrictamente mejor.

Lo que sí cambia es el volumen: es el **único degradado registrado como ERROR**, porque es la
omisión que hace perder un día de trabajo en una ventanilla y es la primera que un editor olvida.

> **Verificación pendiente.** Este razonamiento asume que la ficha pinta el bloque de requisitos
> **vacío** y no una frase del tipo *"No requiere documentos"* — que sí sería la afirmación que
> esta ADR quiere evitar. El módulo Angular de Gobierno vive en el repo hermano `Synergos.UI`, que
> no estaba disponible al escribir esto. Lo que sí consta en este repo es que `GovController` emite
> las listas tal cual, sin sustituir la vacía por copy. Queda por confirmar contra el guard del
> módulo; si resultara que la UI escribe esa frase, el sitio del arreglo es la UI (ADR 0083), no
> esta regla.

### 7. Reglas del formulario: nada puede hacer imposible radicar

Dentro del formulario, el criterio es el mismo llevado al detalle. Una pregunta se omite solo
cuando no se puede formular ni guardar:

- **Sin identificador** → se omite: la respuesta se guardaría bajo una clave vacía y el funcionario
  no sabría qué contestó el ciudadano.
- **Sin etiqueta** → se omite: no hay nada que preguntar.
- **Identificador repetido en TODO el formulario** → se omite la segunda, con **ERROR**. Es la
  regla que solo se ve mirando el formulario entero y no la sección: `CaseDetail.FormData` es un
  diccionario por id, así que la segunda respuesta pisaría a la primera **en silencio** y el
  expediente quedaría con una sola. No se corrige renombrando: inventar `cedula2` le cambiaría el
  significado al dato.
- **Tipo desconocido** → cae a texto libre. Un campo invisible es una pregunta que no se puede
  responder; una mal tipada, sí.
- **Lista o botones sin opciones** → también cae a texto libre. Y aquí está el caso que fija el
  criterio: si esa pregunta además era **obligatoria**, servirla como lista vacía haría el trámite
  **imposible de radicar**. Ningún error de autoría puede llegar a bloquear un procedimiento al que
  alguien tiene derecho.
- **Sección sin título** → conserva sus preguntas y pierde el encabezado. Quitar la sección entera
  le quitaría a la entidad los datos que necesita para resolver, y eso es peor que un grupo sin
  título.

Las opciones se autoran una por línea con **valor igual a etiqueta**, que es lo que el propio seed
ya emite en sus siete listas. Inventar una sintaxis `valor|etiqueta` dentro de un campo de texto
habría metido un mini-lenguaje sin documentar justo donde menos se puede fallar.

### 8. Una sola capa, sin overlay durable

`CatalogTramiteCatalogProvider` sirve el contenido **sin capa durable encima**, igual que
`CatalogStayContentProvider` (ADR 0119 §8) y a diferencia de Inmobiliaria y Eventos.

**No es una omisión: es la consecuencia de que la seam sea de solo lectura.** No hay un segundo
autor que publique trámites por fuera del backoffice, así que no hay nada que fusionar. Añadir el
`IJsonEntityStore` "por simetría" habría creado el campo que promete algo que nadie cumple —un
overlay sin escritor—, y el siguiente que lo viera confiaría en él. Lo que sí es durable en
Gobierno son los **expedientes** (`gov-cases`), que son de otro agregado y ya tienen su capa.

La búsqueda se calca del stub —mismo descriptor con nombre 5 / entidad 3 / categoría 2 / resumen 1,
misma faceta única, mismo `Take` explícito porque la seam promete TODAS las coincidencias y el tope
del motor (24) las truncaría en silencio—. Un portal en el que buscar "Registraduría" deje de
funcionar al mover un flag de configuración no falla: simplemente deja de encontrar.

### 9. Al Dictionary uSync van seis cadenas, y solo seis

Los títulos y detalles de los tres pasos (`Gov.Step.Submit.*`, `Gov.Step.Review.*`,
`Gov.Step.Resolution.*`) viajan **ya escritos** en el JSON que consume la ficha: los emite el
servidor. Por eso son claves del Dictionary con es-CO y en-US.

Y **solo esas seis**. La regla del ADR 0118 §3 se aplicó igual: los módulos Angular traen su i18n
dentro del bundle y no consumen el diccionario del CMS, así que una clave para copy del bundle
nacería huérfana. *Una cadena va al Dictionary uSync si y solo si el servidor la emite.* Nótese
que Gobierno no tiene el equivalente de `PropertySpec.Label` o `StaySpec.Label` —no hay
características derivadas—, así que estas seis son todo lo que había.

## Por qué así

### Por qué el quinto vertical repite la forma en vez de generalizarla

El ADR 0118 dejó la pregunta abierta y el 0119 la respondió con evidencia. Este es el quinto y la
evidencia se refuerza: lo que se repite sigue siendo la **forma** —leer nodos del siteRoot acotado,
proyectar, omitir lo inservible, contar lo omitido en una línea—, unas cuarenta líneas de
`ResolveNodes` casi idénticas. Lo que no se repite es nada de lo que importa.

Y Gobierno lo demuestra más que ninguno: es el primero que **deriva un bloque entero del dominio en
vez de leerlo** (§3), el primero cuya estructura autorada es **recursiva** (secciones → preguntas),
y el primero donde la pregunta central no es "¿qué se puede servir?" sino "¿qué tiene derecho a ver
el ciudadano?". Una base común habría tenido que parametrizar la parte trivial y dejar fuera la
cara — otra vez.

Lo que sí se reusa, y sin ceremonia, es lo pequeño y estable que salió de Eventos:
`EventContentResult<T>`, `EventContentIssue` y `CleanTextList`, que ya usan los cinco.

### Por qué las reglas volvieron a salir a una clase pura

Igual que en los ADR 0117, 0118 y 0119, y por la misma razón: `IPublishedContent.Value<T>` no se
puede simular con coste razonable, y las reglas que deciden si el portal le anuncia a un ciudadano
que un trámite es gratuito no podían ser las únicas sin cobertura. `TramiteContentRules` es pura y
tiene sus pruebas; `UmbracoTramiteCatalogSource` solo lee y loguea.

### Por qué la fuente falla cerrada cuando falta el scope

Sin `Synergos:Catalog:Scopes:Gov` **no se sirve nada**, y se registra un `LogError`. Es la misma
decisión de los otros cuatro, y aquí es donde más se nota: servir sin acotar mezclaría los trámites
de todos los siteRoots —los de **otra alcaldía**, con otras tasas, otros requisitos y otra
normativa— en silencio. Un catálogo vacío se ve; un catálogo que le cobra a un ciudadano la tarifa
de otro municipio, no.

## Consecuencias

### Lo que se gana

- Gobierno deja de ser el vertical sin CMS: un editor de la entidad publica un trámite completo
  —con su formulario de radicación— sin tocar código, y `Synergos:Catalog:Sources:Gov = cms` lo
  sirve. Queda **un solo vertical con catálogo sembrado en C#**.
- El formulario de radicación deja de necesitar un despliegue. Añadir una pregunta a un trámite era
  hasta hoy una edición de `StubTramiteCatalogProvider.Seed()`.
- Una tasa en cero ya no es ambigua: o alguien declaró la gratuidad, o el trámite no se publica.
- El identificador de pregunta duplicado —que antes habría sido un misterio de "por qué el
  expediente solo tiene una de las dos cédulas"— ahora es una línea de error en el log.
- Los textos de los tres pasos se traducen sin desplegar.

### Lo que se acepta

- **`tramitePage` no tiene template ni partial de render.** Es el modelo que consume el bundle
  Angular vía la API, no una página que Umbraco enrute. Un trámite no tiene URL propia en el CMS;
  su ficha la pinta el módulo.
- **Los pasos son tres y fijos**, sin manera de que una entidad añada el suyo (§3).
- **`TramiteSummary.Channel` se autora y hoy nadie lo emite.** `GovController.ToServiceDto` no lo
  incluye en el DTO. Se modela igual porque el record del dominio lo declara y el stub lo puebla en
  los siete trámites; exponerlo es un cambio de `GovController`, fuera del alcance de esta rebanada.
- **Cinco clases paralelas** entre verticales, con la evidencia acumulada de por qué no se unifican.
- El catálogo se relee entero en cada búsqueda y en cada ficha, sin caché. Misma decisión consciente
  que el resto del motor: read-your-writes gratis, y una caché se añade junto con su invalidador y
  su consumidor, nunca antes.

### Lo que hay que hacer antes de que esto se vea

1. El arquitecto corre **uSync Import** desde el backoffice: cuatro DataTypes, un DocType, dos
   ElementTypes y siete entradas de Dictionary (`Gov` + las seis de los pasos).
2. Debe declarar **`Synergos:Catalog:Scopes:Gov`** con el `brandKey` del siteRoot donde vive el
   portal. Sin ese scope la fuente falla cerrada y ruidosa a propósito.
3. Debe aplicar a mano las **dos líneas de registro en `SeamComposer`** que reemplazan el registro
   incondicional del stub — están en el reporte de la rebanada.
4. Queda pendiente **confirmar cómo pinta la UI una lista de requisitos vacía** (§6) cuando
   `Synergos.UI` esté disponible.
