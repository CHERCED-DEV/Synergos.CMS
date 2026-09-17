# 12 — El molde de un vertical

> **Estado: medido contra el disco y gateado.** Lo vigila
> `Synergos.CMS.Tests/Architecture/MoldeDelVerticalTests.cs`.
>
> Continúa el [doc 08 §4](08-despiece-apis.md), que escribió el molde de una **capacidad**. Éste
> escribe el de un **vertical**, que es la unidad en la que de verdad se agrega producto.

## 1. Por qué hacía falta, y por qué no bastaba con el doc 08

El molde de una capacidad está escrito y tiene gate: cuatro carpetas, llave compartida,
`/health`, todo bajo `/v1/`, sin `MapPut`/`MapPatch`, ruteo solo en `Endpoints/`. Un agente nuevo
puede escribir **la capacidad veintiuno** sin preguntarle a nadie, y el build le dice si se
equivocó.

No existía el equivalente para un vertical. Añadir **el octavo** —Social es el que falta— era
trabajo a medida: había que leerse los siete que hay y adivinar qué partes eran esenciales y
cuáles fueron accidentes de quien las escribió. Eso es exactamente lo que hace que el siguiente
no se parezca a los anteriores.

Y no es un problema de estilo. Los cuatro errores más caros del árbol de servicios
—`CLAUDE.md` §11 los documenta uno por uno— **no fueron de código: fueron de clasificación**. Se
miró *cuántos pasos compone* un flujo y se concluyó «orquestador», cuatro veces seguidas, con
cuatro stubs distintos. El molde tiene que hacer que ese error sea difícil de cometer, no
advertir contra él.

## 2. Primero medir

La lección más repetida de este repo es que **una lista sacada de la cabeza en vez de medida
contra el fichero congela un error**. Así que antes de escribir una línea de molde, los siete
verticales construidos, contra el disco:

| Vertical | DocType que lo sostiene | Controller(s) | Catálogo | Interruptor(es) | Valor cableado | Destino | Cliente `Http*` | Gate | Claves G-6 |
|---|---|---|---|---|---|---|---|---|---:|
| **Tienda** | `productpage`, `productcategorypage` | `ShopCatalogController`, `ShopController` (1 612 L) | `Catalog:Sources:Shop` | `Synergos:Tienda:Mode` | `Bff` | `Bff.Tienda` | `HttpShopOrderService` | `ShopWiringTests` | 64 |
| **Salud** | `professionalPage` | `EhrController`, `HealthcareApiController` (1 587 L) | `Catalog:Sources:Salud` | `Synergos:Salud:Mode` | `Bff` | `Bff.Salud` | `HttpClinicalSchedulingService` | `SaludWiringTests` | 96 |
| **Realty** | `propertylisting` | `RealtyController` (1 032 L) | `Catalog:Sources:Realty` | `Synergos:Realty:Mode` | `Api` | `Api.Booking` | `HttpVisitSchedulingService` | `RealtyWiringTests` | 69 |
| **Gobierno** | `tramitepage` | `GovController` (1 065 L) | `Catalog:Sources:Gov` | `Gob:Mode` · `Gob:Notifications:Mode` · `Gob:Payments:Mode` | `Api` ×3 | `Api.Workflow` · `Api.Messaging` · `Api.Payments` | `HttpCaseWorkflowService` · `HttpGovActNotificationService` · `HttpPaymentProvider` | `GobWiringTests`, `GovNotificationWiringTests`, `PaymentsWiringTests` | 65 |
| **Eventos** | `eventpage` | `EventosController` (1 021 L) | `Catalog:Sources:Events` | `Synergos:Eventos:Mode` | `Bff` | `Bff.Eventos` | `HttpEventTicketingService` | `EventosWiringTests` | 68 |
| **Viajes** | `staylisting` | `BookingController`, `TravelController` (1 087 L) | `Catalog:Sources:Booking` | `Synergos:Viajes:Mode` | `Bff` | `Bff.Viajes` | `HttpHotelBookingService`, `HttpTravelCartEngine` | `ViajesWiringTests` | 17 |
| **Educación** | `coursepage` + `elementCourseModule`/`Lesson` | `AcademyController` (1 193 L) | `Catalog:Sources:Academy` | `Synergos:Academy:Mode` | `Api` | `Api.Signing` | `HttpCertificateIdSigner` | `AcademyWiringTests` | 60 |
| *(Social)* | `postpage`, `postcategorypage`, `authorpage` | `BlogsController` (1 377 L) | ninguno | **ninguno** | — | — | — | — | 60 |

Y los seis interruptores que **no** son de un vertical, porque los consumen varios:
`Synergos:Payments:Mode`, `Synergos:Identity:Mode`, `Synergos:Audit:Mode`,
`Synergos:Tracking:Mode`, `Synergos:SearchAnalytics:Mode`, `Synergos:BundleRegistry:Mode`.

**Trece de los quince puntos de cableado del repo siguen el mismo molde, y los dos que no son
anteriores a él** (§7).

## 3. Un vertical son TRES ejes, y se cablean por separado

Es lo primero que salió de medir, y no se ve leyendo una implementación: **«cablear un vertical»
nunca quiere decir mover el vertical entero**. Son tres decisiones independientes, y confundirlas
es lo que produce los retrocesos.

### Eje 1 — el CATÁLOGO: lo que se muestra. **No sale del CMS.**

Lo autora un editor en el backoffice y lo sirve el árbol de contenido: un DocType, un
`Umbraco<X>CatalogSource : ICatalogSource<T>`, sus `<X>ContentRules`, y un interruptor
`Synergos:Catalog:Sources:<X>` que vale `demo` (el seed) o `cms` (lo autorado). El rollback es esa
línea, sin redespliegue.

**Cablear esto a `Api.Catalog` sería un retroceso, y es el error caro de la épica** (doc 11,
familia B): el dato ya tiene dueño, así que meter una llamada HTTP en medio cambia una lectura en
proceso por una ida a la red **y** le quita al editor la superficie donde publica. Hay gate
—`El_catalogo_de_un_vertical_NO_sale_a_la_red`— y mide lo inequívoco: un `HttpClient` dentro de
un `Umbraco*Source`.

**Los siete verticales construidos tienen este eje.** Salud fue el último y era un hallazgo de
haber medido, no una omisión del molde: se cerró en el #118 (§7.2). El octavo —Social— no lo tiene
porque no tiene ninguno de los tres: no está construido.

### Eje 2 — la TRANSACCIÓN: lo que se mueve y no se deshace solo. **Es lo que cruza.**

Un interruptor `Synergos:<X>:Mode`, una implementación en proceso por defecto, un cliente `Http*`
cuando se enciende. Es el eje que tiene **dos formas**, y §4 es enteramente sobre cómo se elige
entre ellas.

### Eje 3 — el ARTEFACTO: lo que queda como prueba. **Se queda en el CMS.**

La entrada de Eventos con su QR, su portador y su check-in (`EventTicketLedger`); el diploma de
Educación; el expediente de Gobierno con su radicado y su bandeja; el RMA de Tienda; la agenda de
visitas de Realty (`VisitAgenda`, derivada acá). Los siete lo tienen.

Dos razones, y las dos son consecuencias y no gustos. Una: **el firmante vive de este lado**, así
que partir el artefacto obligaría a mover la custodia de la llave con él. Dos: **la prueba tiene
que poder verse con el otro árbol caído** — con `Bff.Eventos` abajo se sigue viendo «mis
entradas», se sigue transfiriendo y se sigue escaneando en la puerta.

> **La regla de reparto que se derivó de esto, y sirve para el octavo:** una lectura que solo
> MUESTRA se queda de este lado; una lectura que DECIDE sale a preguntar. El timeline de un
> pedido (#46) se pinta desde el almacén local porque muestra lo que ya pasó; el proceso de un
> expediente (#44) se lee de la capacidad porque decidir con un proceso que quizá ya no es el
> vigente es otra cosa.

## 4. Las dos formas del eje transaccional, y la pregunta que elige

No hay un molde único, y fingir que lo hay habría sido peor que no escribirlo. **Son dos**, y
entre ellas elige la **primera de las tres preguntas** del repo:

| | **Orquestada** | **Directa** |
|---|---|---|
| la elige | **hay algo que deshacer** si un paso falla | **no hay nada que deshacer** |
| destino | `Synergos.Bff.<X>` | `Synergos.Api.<Y>`, una por capacidad |
| valor cableado | `Bff` | `Api` |
| interruptores | **uno por FLUJO** | **uno por CAPACIDAD** |
| hoy | Tienda, Salud, Eventos, Viajes | Realty (1), Educación (1), Gobierno (3) |

**Las tres preguntas se hacen por separado.** Fundirlas es exactamente el error que se cometió
cuatro veces:

| Pregunta | Qué decide |
|---|---|
| ¿hay algo que **deshacer** si un paso falla? | si hace falta un **orquestador** |
| ¿el recurso lo lleva **alguien más**? | si hace falta **cablearlo** — no a qué nivel |
| ¿**quién tiene la plata**? | a **quién** se le pide el movimiento |

La segunda es la que salvó a `StubVisitSchedulingService`: que no necesite orquestador no quiere
decir que no haya que cablearlo, quiere decir que va directo a la capacidad. La tercera es la que
destapó el defecto #57: el RMA le pedía el reembolso al proveedor local con el identificador de
la saga, que ese proveedor no conocía, y **el caso no llegaba nunca a reembolsado sin que nada
fallara**.

### 4.1 Por qué el número de interruptores no es simetría

Es el hallazgo que más paga de haber medido, porque nadie lo escribió nunca y los siete lo
cumplen: **en la forma directa hay un interruptor por capacidad; en la orquestada, uno por
flujo.**

La razón es que en la forma directa **las capacidades son independientes**. Notificar un acto es
`Api.Messaging` y decidir un expediente es `Api.Workflow`; juntarlas bajo un interruptor obligaría
a levantar las dos para probar una, y a apagar las dos para apagar una. Por eso Gobierno tiene
tres y no uno.

En la forma orquestada **no lo son**, porque el **orden entre los pasos es precisamente lo que el
orquestador aporta**. Viajes tiene dos clientes `Http*` —la reserva de hotel y el carrito
multi-producto— bajo **un** interruptor, y está bien: los dos hablan con el mismo orquestador y
el mismo flujo.

**Y de ahí sale el gate que hace difícil el error de las cuatro veces.** La huella en el árbol de
contestar mal la primera pregunta es siempre la misma y sí se puede leer del disco: **dos
capacidades colgando de un solo interruptor directo**. Si cuelgan del mismo interruptor es que
alguien las considera un flujo — y un flujo con dos pasos que pueden fallar a la mitad tiene algo
que deshacer, o sea es un orquestador. `En_modo_Api_un_interruptor_gobierna_UNA_capacidad` rompe
el build y el mensaje recita las tres preguntas.

La salida no es siempre «hacelo `Bff`». Si de verdad no hay nada que deshacer, son dos capacidades
independientes y llevan **dos interruptores**, como Gobierno.

## 5. El orden en que se escribe un vertical

Ocho pasos. Cada uno toma **una** decisión, y cada decisión tiene quien la comprueba.

### 5.1 El objeto central se puede autorar — o se dice por qué no

Un DocType con su ficha, y si el objeto se *consume* además de mirarse, lo que haga falta para
consumirlo. Educación lo aprendió caro: `coursePage` existía con sus 16 campos **y sin temario**,
así que un curso autorado no se podía cursar; hizo falta `elementCourseModule` /
`elementCourseLesson` (#100) para que la fuente de contenido sirviera de algo.

> **Y sembrar se hace en el CATÁLOGO, nunca en la FUENTE.** `ICatalogSource.GetAllAsync` se llama
> en **cada búsqueda** —el catálogo no cachea, a propósito, para que el read-your-writes salga
> gratis—, así que sembrar ahí hace crecer el feed un ítem por lección **y por búsqueda**. La
> clave de siembra lleva la **huella del texto** (o editar una lección no se ve nunca) y el
> mapping es **durable** (o cada arranque duplica el feed entero).

### 5.2 La fuente de contenido y su interruptor

`Umbraco<X>CatalogSource` + `<X>ContentRules` + `Synergos:Catalog:Sources:<X>`, con `demo` de
default. Gates: `El_catalogo_de_un_vertical_NO_sale_a_la_red` (no salir a la red) y
`Cada_vertical_tiene_su_EJE_1` (tenerlo, y no sólo tenerlo bien).

### 5.3 El seam, en `Synergos.CMS.Interfaces`

Un `I<X>Service` con la operación del negocio, y **la implementación en proceso como default**.
Es el camino del clon limpio: un repo recién bajado tiene que levantar y vender sin levantar seis
servicios.

> **El seam se corta por atomicidad, no por comodidad.** `IReservationService` fusiona «cupo de un
> pozo contable» (`Api.Inventory`) con «una ventana sobre un recurso» (`Api.Booking`), y por eso
> su `Reservation` lleva `RoomTypeCode` y `GuestName`, que ninguna capacidad puede guardar. Un
> seam mal cortado no se nota hasta que hay que cablearlo.
>
> **Y se corta pensando en la RED aunque todavía no la haya.** `IPaymentProvider` nació síncrona
> porque sus dos proveedores escribían en un log; la primera pasarela real obligó a subirla entera
> y el atajo —`.Result` dentro del proveedor— habría vaciado el pool de hilos. Lo mismo con la
> **granularidad**: `IClinicalSchedulingService` solo sabía listar por fecha, así que la ficha del
> paciente barría −30/+60 días **llamando una vez por día**, y el default en memoria lo escondía
> porque 91 filtros de LINQ no cuestan nada. La pregunta que lo caza: *¿el llamador está barriendo
> una dimensión porque el seam solo sabe contestar por la otra?*

### 5.4 El POCO de configuración, con los cuatro campos

`Synergos.CMS.Application/Configuration/<X>Settings.cs` con `Mode`, `BaseUrl`, `ApiKey` y
`TimeoutSeconds`, más los `Kind` del vertical (`ListingKind`, `PatientKind`, `BuyerKind`…) que la
capacidad guarda y devuelve sin ramificar.

`TimeoutSeconds` no es cosmético: **comprar cruza seis servicios y no es auxiliar**, así que
cortar pronto no evita el problema —un timeout no dice «no se cobró», dice «no sé»— solo lo hace
más probable. Por eso Tienda espera 30 s y el buscador 5.

### 5.5 El interruptor, y ENLAZAR la sección

```csharp
services.Configure<XSettings>(builder.Config.GetSection("Synergos:X"));

if (string.Equals(builder.Config["Synergos:X:Mode"], "Bff", StringComparison.OrdinalIgnoreCase))
{
    // cliente nombrado: BaseAddress, Timeout, llave compartida
    //   .AddHttpMessageHandler<CorrelationForwardingHandler>()
    services.AddSingleton<IXService, HttpXService>();
}
else
{
    services.AddSingleton<IXService, StubXService>();
}
```

**El `Configure<>` es el paso que más se ha olvidado: cuatro verticales lo arrastraron** —Tienda
(#24), Salud (#25), Viajes (#36) y las notificaciones de Gobierno (#62)—. Sin él el cliente
recibe un `<X>Settings` recién construido y **todo lo que no viaja por el `HttpClient` se queda en
su default en silencio**: configurarlo no hace nada y nadie sabe por qué. Que se haya olvidado
cuatro veces es lo que lo convierte en esencial del molde y no en un descuido.

Gates: `Cada_punto_de_cableado_ENLAZA_su_seccion` · `El_default_NUNCA_es_el_valor_cableado` ·
`El_vocabulario_del_molde_es_Api_o_Bff`.

### 5.6 El cliente `Http*`, en `Synergos.CMS.Web/Services/`

Lo que los siete comparten, medido:

- **La llave compartida.** Un cliente sin llave no falla al arrancar: sirve, y la capacidad le
  contesta 401 a la primera persona que intente comprar. Es la forma del #56 y la de la llave de
  firma de `Api.Identity` — arrancar verde y reventar delante de alguien es el peor de los tres
  modos de fallar. Gate: `Cada_punto_de_cableado_manda_la_llave_compartida`.
- **La correlación** (`AddHttpMessageHandler<CorrelationForwardingHandler>()`). Con que un solo
  salto la corte, el rastro se parte en dos historias y «mostrame todo lo de esta compra» vuelve a
  no tener respuesta. Gate: `Cada_punto_de_cableado_propaga_la_correlacion`.
- **La llave de idempotencia, determinista sobre QUÉ y no sobre CUÁNDO.**
  `IdempotencyKeyFor(comprador, líneas ordenadas)` es lo que hace que un reintento tras un timeout
  no compre dos veces. Ordenar las líneas no es detalle: sin eso, reordenar la canasta en pantalla
  cambia la llave.
- **El actor viaja como SEUDÓNIMO, nunca el correo.** No es anonimato y no hay que venderlo como
  tal —un correo conocido se puede volver a hashear— es no esparcir lo que no hace falta esparcir.
  `Bff.Tienda` era el último que mandaba el correo entero y se corrigió en el #47, que además
  destapó que el listado devolvía ese identificador como si fuera el correo.
- **Degradar, no reventar.** Encender el modo sin el servicio arriba tiene que dejar el vertical
  sirviendo catálogo y fichas; lo que se para es la transacción, y se dice.

### 5.7 La pantalla, y las claves que cruzan

El controller emite el JSON que lee la app del catálogo, y los dos gates cross-repo lo cruzan:
**G-6** (`tools/contract-keys.mjs`, respuestas) y **G-7** (`tools/contract-bodies.mjs`, cuerpos de
petición). El vertical nuevo entra en la tabla `app ↔ controllers` de esos scripts.

**G-7 es el que mira donde de verdad dolió**: en los ocho verticales auditados lo caro estuvo
siempre en los cuerpos —un `planId` que el record no declaraba y cobraba el plan equivocado, `lat`
/`lng` planos que publicaban un inmueble en (0,0), la dirección de entrega descartada—. Nada de
eso falla a la vista: `System.Text.Json` descarta en silencio lo que no mapea.

> **Y la guarda de G-6 hay que mirarla al escribir la fila.** La tabla `app ↔ controller` es a
> mano y una mal escrita congela un verde falso para siempre: `storefront` apuntaba a
> `ShopController` (110 líneas) en vez de `ShopCatalogController` (1 372) y cruzaba **3 claves de
> 94**. La guarda rechaza un vertical que cruce menos de una de cada cinco.

### 5.8 El gate del vertical

Lo que el molde comprueba es la **forma**. Lo que el vertical **rechaza y por qué** lo escribe él,
en su propio `*WiringTests`: que la cita no adivine el identificador del recurso (#25), que la
visita toque una sola capacidad (#33a), que el acto no se pueda leer sin registrar el acceso
(#62), que la bitácora se repita sin firmar y la canasta no (#14 / #72).

Por eso el molde exige que ese gate exista —`Cada_punto_de_cableado_tiene_un_gate_que_nombra_su_cliente`—
y lo busca por el **nombre del cliente `Http*`**, no por el del vertical: el gate de Tienda se
llama `ShopWiringTests`, y uno que cruzara por nombre lo daría por ausente.

**Y el gate se muta**: se reintroduce el defecto, se confirma el rojo, se restaura **tocando el
fichero**. Un gate que no se vio fallar no está vigilando nada.

## 6. Lo que NO hay que hacer

Todo lo de aquí abajo ya se hizo, y cada línea costó una ola.

1. **No mirar «cuántos pasos compone» para decidir si hace falta orquestador.** Se hizo cuatro
   veces seguidas. Radicar un trámite compone media docena de seams y **no** necesita orquestador,
   porque el propio código decide no abortar el trámite si la captura no sale: si no se aborta, no
   hay nada que deshacer, y un orquestador sería la máquina de compensar sin compensación.
   **Componer no es orquestar.**
2. **No cablear el catálogo a una capacidad.** Es un retroceso: cambia una lectura en proceso por
   una ida a la red y le quita al editor la superficie donde publica.
3. **No dejar que el CMS llame a varias capacidades sueltas cuando hay algo que deshacer.** Estaría
   reimplementando la máquina de sagas, y peor, porque no tiene dónde anotar una compensación
   pendiente.
4. **No hacer que el default sea el modo cableado.** «No configurado» pasaría a significar «roto»,
   y lo primero que vería alguien nuevo sería un vertical caído por una razón ajena a su código.
5. **No adivinar el identificador del recurso con una convención.** Lo genera la capacidad, así
   que ninguna convención del CMS puede acertarlo. Se resuelve preguntando por el sujeto
   (`GET /v1/resources?subjectKind=&subjectId=`). `SaludSettings` tuvo un `ResourceIdPrefix` y
   costó una vuelta entera. **Y desde el #118 tampoco se escribe a mano en el backoffice**: un
   campo del DocType para teclearlo sería lo mismo con otra cara y peor, porque lo teclearía un
   editor. Hay gate sobre el XML (`El_schema_del_profesional_NO_lleva_el_identificador_del_recurso`);
   sobre la prosa no servía — la prosa ya lo decía en `SaludSettings` y el campo habría entrado
   igual.
6. **No dejar dos relojes sobre el mismo apartado.** El barrido del CMS vence sus propias
   reservas; `Api.Booking` vence las suyas. Si el cliente cableado creara además una reserva local,
   ganaría el que corriera antes.
7. **No mandar el correo de nadie a una capacidad.** Un seudónimo estable basta, y lo que se
   manda queda escrito en el disco del otro servicio.
8. **No dejar que el CMS y el orquestador cobren a la vez.** Un despliegue con `Tienda:Mode=Bff` y
   llaves reales de Wompi de este lado falla al arrancar, a propósito. La pregunta es *¿quién
   tiene la plata?*, y con la tienda cableada la tiene el orquestador.
9. **No esperar a cablear un seam compartido «de una vez».** `StubReservationService` entró al
   mapa como «un cableado, seis verticales» y los seis se cablearon de a uno, cada uno por la
   puerta que le tocaba. **El apalancamiento de un seam no baja: se evapora.**
10. **No inventar una tercera palabra para el interruptor.** `Api` y `Bff` no son estilo: son las
    dos formas del eje, y elegir entre ellas **es** contestar la primera pregunta. Si de verdad no
    es ninguna de las dos, lo que se queda corto es este documento.

## 7. Los hallazgos de haber medido

Tres, y ninguno se ve leyendo una implementación.

### 7.1 `CorrelationTests` vigila con una lista escrita a mano, y ya tiene huecos

`Los_clientes_del_CMS_hacia_el_arbol_de_servicios_lo_PROPAGAN` cruza contra **cuatro** ficheros de
composer escritos a mano. Los puntos de cableado que hablan con el árbol son **trece**, en
**nueve** composers. Los cinco que entraron después —Educación (#45), seguimiento (#46), pagos
(#27), identidad (#14) y Viajes (#36)— no están en esa lista.

**Medido, no supuesto:** quitándole la correlación a `SeamComposer.Academy.cs`, los 15 tests de
`CorrelationTests` siguen en **verde**. Con el gate nuevo sale rojo.

Es «una lista sacada de la cabeza en vez de medida contra el fichero» —lo que `CLAUDE.md`
documenta cuatro veces— dentro del gate que existe para que no se pierda el rastro. No se borra
aquel test: cubre los **dos** árboles y esto solo cubre el CMS. Lo que se cierra es su hueco.

### 7.2 Salud no tenía el primer eje — **cerrado, y el gate ya lo exige** (#118)

Al medir era el único vertical **sin DocType y sin fuente de contenido**: el paciente, el
profesional y la agenda salían de stubs sembrados en C#, así que publicar un médico exigía un
despliegue. Exactamente lo que le pasaba a Educación antes del #100.

¿Molde mal o vertical mal? **Vertical**, y así se resolvió. La decisión que faltaba —«el
directorio de profesionales sí es contenido editorial, como un `instructorPage`»— se tomó: hoy
hay `professionalPage`, `UmbracoProfessionalDirectorySource` + `ProfessionalContentRules`, y
`Synergos:Catalog:Sources:Salud` con `demo` de default. **Lo que sigue en pie es el resto del
párrafo**: el destino del EHR-lite es un EHR externo y el paciente y la agenda NO se autoran acá.
El eje 1 de un vertical es su **objeto central**, y el de Salud es el profesional.

> Y por eso el gate ya **sí** exige el eje 1 a los siete
> (`Cada_vertical_tiene_su_EJE_1`). La excepción existía porque exigirlo dejaba el build rojo por
> una decisión de producto que nadie había tomado, y un gate siempre rojo deja de leerse. Tomada
> la decisión, la excepción se fue con ella — que es la mitad de lo que valía el ticket.
>
> **El cruce es por COMPOSER y no por nombre**, porque los dos ejes hablan vocabularios distintos
> —el interruptor dice `Tienda`, `Gob`, `Viajes`, `Eventos` y la fuente dice `Shop`, `Gov`,
> `Booking`, `Events`— y escribir a mano esa tabla de siete filas es lo que §2 dice que congela un
> error. Lo que sí está en el disco es que el composer parcial **es** el cableado del vertical.
> **Lo que ese corte no ve, dicho para que nadie confíe de más**: dentro de un composer que
> cablea varios verticales —hoy sólo `SeamComposer.EventsPropertiesGov.cs`— el gate cuenta, no
> empareja.

### 7.3 Dos puntos de cableado son anteriores al molde

`Synergos:SearchAnalytics:Mode` (ADR 0130 — el consumidor **más viejo** del árbol, escrito antes
de que hubiera molde) no tiene POCO: `BaseUrl`, `ApiKey` y un timeout de 5 s fijo se leen a pelo
del `IConfiguration`. `Synergos:BundleRegistry:Mode` (ADR 0132) tiene POCO pero sin `ApiKey` ni
`TimeoutSeconds`, y eso **es correcto**: el CDN es público y no lleva llave compartida.

Ninguno de los dos es un vertical. Se dejan como están —arreglarlos cambia comportamiento y eso
es otro ticket— pero **no se escriben en una lista de excepciones**: el gate lleva un
**trinquete** (`PuntosAnterioresAlMolde = 2`), la misma forma que `tools/contract-keys.baseline.json`.
Un punto nuevo fuera del vocabulario del molde lo sube a tres y rompe el build.

### 7.4 El seudónimo está escrito siete veces con tres nombres

`Seudonimo` en `HttpVisitSchedulingService`, `HttpPaymentProvider` y `HttpAuditTrailWriter`;
`BuyerId` en `HttpShopOrderService` y `HttpEventTicketingService`; e inline en
`HttpHotelBookingService` y `HttpClinicalSchedulingService`. Siete copias de una decisión que
`CLAUDE.md` explica con cuidado —«no es anonimato», «nunca el nombre: dos personas se llaman
igual»— y las decisiones sutiles no sobreviven al copiar-pegar.

Por la regla del repo —se promueve al **segundo** consumidor— lleva cinco de retraso. No se
promueve aquí porque mover código y escribir un molde en el mismo commit es mezclar feature y
refactor; queda anotado con su sitio: es fontanería del CMS, o sea `Synergos.CMS.Web/Services/`,
no `Interfaces`.

## 8. Lo que este molde no contesta

- **Si un vertical necesita orquestador.** Eso lo deciden las tres preguntas y ninguna se lee del
  disco. Lo que el gate vigila es la **consecuencia** de haberlas contestado mal.
- **Qué capacidad.** Que un aforo sea `Api.Inventory` y una ventana sea `Api.Booking` sale del
  doc 07, y para el caso dudoso hay precedente: el vuelo de Viajes se consideró para
  `Api.Inventory` y se descartó **con el disparador escrito** —que haga falta sobreventa por clase
  tarifaria—. Un descarte sin disparador se vuelve a discutir cada seis meses.
- **El octavo orquestador.** Social no tiene eje 2 y hoy no lo pide nadie: sus dos capacidades
  candidatas —`Api.Moderation` y `Api.Engagement`— siguen sin primer consumidor, y conviene que el
  primero sea real y no un fake (ver `CLAUDE.md` §11).
