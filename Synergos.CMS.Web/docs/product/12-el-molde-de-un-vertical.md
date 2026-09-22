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

**Los siete lo tienen, y ésta es la lista** — que hasta el #153 era una cifra sobre cinco
ejemplos, o sea `feedback_a_named_list_beats_a_count` en el propio documento que predica medir:

| vertical | el artefacto | quién lo emite y lo registra | su almacén | ¿lleva sello? |
|---|---|---|---|---|
| **Tienda** | el RMA | `StubReturnService` | `IJsonEntityStore` · `returns` | no |
| **Salud** | la historia clínica y su rastro de versiones | `FileSystemPatientRepository` | **`IPhiStore`** · `patients` + `patient-history` | no |
| **Realty** | la agenda de visitas | `StubVisitSchedulingService` + `VisitAgenda` (derivada acá) | `IJsonEntityStore` · `realty-visits` — **sólo en modo `Stub`, #158** | no |
| **Gobierno** (`Gob`) | el expediente con su radicado y su bandeja | `StubApplicationService` | `IJsonEntityStore` · `gov-cases` | no |
| **Eventos** | la entrada con su QR, su portador y su check-in | `EventTicketIssuer` + `EventTicketLedger` | `IJsonEntityStore` · `event-orders` | **sí** — `ITicketSigner` |
| **Viajes** | el expediente del viaje | `TravelCartService` | `IJsonEntityStore` · `travel-orders` | no |
| **Educación** (`Academy`) | el diploma | `StubCertificateService` | `IJsonEntityStore` · `certificates` | **sí** — `ICertificateIdSigner` |

> **La fila de Realty es la única con un ticket dentro, y por eso está ahí.** Su registro y el
> seam de su eje 2 son **la misma clase**, así que lo durable existe sólo con
> `Synergos:Realty:Mode=Stub`: `HttpVisitSchedulingService` no guarda nada de este lado. Con el
> modo encendido y `Api.Booking` caída, la agenda se sigue pintando —es una función pura del id y
> del reloj— y quien reservó **no ve su visita**: la pantalla se ve bien y está vacía de lo único
> que fue a buscar. Lo destapó el gate de esta misma HU, al primer arranque (#158).

Dos razones para que se quede acá, y las dos son consecuencias y no gustos. Una: **el firmante
vive de este lado**, así que partir el artefacto obligaría a mover la custodia de la llave con él.
Dos: **la prueba tiene que poder verse con el otro árbol caído** — con `Bff.Eventos` abajo se
sigue viendo «mis entradas», se sigue transfiriendo y se sigue escaneando en la puerta.

> **La regla de reparto que se derivó de esto, y sirve para el octavo:** una lectura que solo
> MUESTRA se queda de este lado; una lectura que DECIDE sale a preguntar. El timeline de un
> pedido (#46) se pinta desde el almacén local porque muestra lo que ya pasó; el proceso de un
> expediente (#44) se lee de la capacidad porque decidir con un proceso que quizá ya no es el
> vigente es otra cosa.

### 3.1 La CUARTA pregunta, y por qué sólo dos de los siete la contestan que sí

Las tres preguntas de §4 deciden el eje 2. El eje 3 tiene la suya, y es la que separa las dos
columnas de la derecha de esa tabla:

> **¿alguien de FUERA tiene que poder comprobar esto sin creernos?**

Una entrada la lee un portero que no es nuestro, y un diploma lo lee un empleador que tampoco.
Los otros cinco artefactos los lee alguien que ya está dentro: el RMA lo mira quien vendió, el
expediente quien lo radicó y el funcionario, la agenda la inmobiliaria, la historia clínica el
médico tratante. Ahí el registro durable ES la prueba, y añadirle un sello sería custodiar una
llave para nadie.

Cuando la respuesta es **sí**, lo que el sello compra está escrito en el propio código, en
`StubCertificateService`: *«El índice NO es la autoridad; la llave sí […] Quien consiga escribir
en el almacén puede inventar un fichero con el id que quiera y el nombre de quien quiera; no le
sirve de nada, porque el id no cuadrará.»* O sea que **el sello es lo único que hace que escribir
en el almacén no alcance para fabricar el artefacto** — que es la diferencia entre una prueba y
un registro nuestro.

### 3.2 Las dos invariantes del eje 3, y las dos son medibles

**Una: el artefacto vive FUERA del seam de la transacción, y lo comparten sus dos
implementaciones.** `EventTicketIssuer` y `EventTicketLedger` no tienen seam y los usan tanto
`StubEventTicketingService` como `HttpEventTicketingService`. No es orden: el emisor metido dentro
del motor en proceso **no lo puede usar el cliente cableado**, y la salida obvia —copiarlo— está
descrita en el `<remarks>` del propio emisor: *«Un QR con dos definiciones es un QR que un día se
firma de dos maneras.»* Lo destapó cablear Eventos (#35 rebanada 2b): la cara de organizador
colgaba del motor de compra concreto, así que cambiar por dónde se compra habría dejado la puerta
leyendo un almacén vacío **sin que nada avisara**.

**Dos: el REGISTRO nunca sale a la red; el SELLO puede.** No hay ni un `Http*` sobre un registro
de artefacto en los siete —eso es lo que sostiene «verse con el otro árbol caído»— y en cambio
`HttpCertificateIdSigner` existe desde el #45: lo que Educación mudó a `Api.Signing` fue **la
custodia de la llave**, no el índice de emitidos. Es el mismo corte de §3 dicho con ficheros.

> **Y los dos que lo tienen lo resolvieron de dos maneras distintas, que es lo que pasa cuando el
> molde no escribe el paso.** `AcademySettings` llevaba la transacción y el sello en **un** POCO y
> una sección (`Synergos:Academy`); Eventos los llevaba en **dos** —`EventosSettings` con
> `Mode`/`BaseUrl`/… y `EventsSettings` con sólo `TicketSigningSecret`— bajo dos secciones que se
> diferenciaban en **una letra**: `Synergos:Eventos` y `Synergos:Events`. Eso no era estilo, era el
> defecto #154, y su causa es ésta: el sello necesitaba sección propia, el nombre del vertical ya
> estaba tomado, y le tocó el que quedaba libre.
>
> **Cerrado (#154):** hoy es `Synergos:Eventos:Ticket`, anidada, y §5.8 escribe la regla. Hay gate
> —`SeccionesDeConfiguracionTests`, trinquete absoluto porque medido daba **cero** pares a esa
> distancia tras arreglarlo— y el arranque **se niega** si alguien todavía puebla la sección vieja:
> descartarla en silencio habría generado otra llave y dejado sin validar todo QR ya impreso.

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

Diez pasos. Cada uno toma **una** decisión, y cada decisión tiene quien la comprueba.

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
> **`Stub<X>` NO es la regla, y decirlo así costó un hallazgo** (#155). Medido sobre el árbol:
> de los **109** seams con implementación declarada, sólo **48** la tienen llamada exactamente
> `Stub<X>` —el 44 %, una mayoría relativa y nada más—. Los otros 61 nombran **lo que la
> implementación ES**: `FileSystem*` (19) donde persiste, `Http*` (15) cuando cabla, `Default*`
> (14) cuando no hay nada más filoso que decir, `Catalog*` (6), `InMemory*` (5), `Umbraco*` (5)
> cuando el dato sale del contenido, `Composite*` (5) cuando abre en abanico, `Hmac*` (2) cuando
> firma. Esa es la convención de verdad, y este documento describía la minoría como si fuera
> universal.
>
> **Dónde deja de ser cosmético**, que es lo único que hace falta recordar: cuando la
> implementación en proceso **es** la funcionalidad y no hay nada fuera por lo que pudiera
> sustituirse. Llamar `Stub` al firmante del QR afirma «esto es un provisional» justo sobre lo
> que hace válida una entrada en la puerta. Y ojo con el instinto contrario: en este repo `Stub`
> **tampoco** quiere decir «en memoria» —19 de los 49 son durables (`CLAUDE.md` §11)—, así que
> renombrar los 48 a otra cosa sería cambiar una convención imprecisa por otra. Lo que sí hay es
> **gate donde importa**: §5.8 rompe el build si aparece un `Stub<X>` de un seam de sello.

> **El seam del SELLO no es éste — es §5.8 — y su implementación en proceso NO se llama
> `Stub<X>`.** Derivar aquí todos los seams del vertical es lo que hace que el plan invente un
> `StubTicketSigner` que no existe ni debe existir: `HmacTicketSigner` no es un doble de nada, es
> el firmante de verdad mientras la custodia viva de este lado (#155).

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

> **El composer parcial NO es uno por vertical, y este documento lo mostraba así** (#155). Medido:
> hay **once** `SeamComposer.*.cs` para siete verticales más la plataforma, y el reparto no
> responde a ninguna regla — `EventsPropertiesGov` lleva **tres** (Eventos, Realty y Gobierno) en
> 499 líneas, `Social` tiene 81 y ni siquiera un interruptor. Es acumulación histórica, no
> diseño, así que **el molde no puede predecir el nombre del fichero**: el spec lo declara
> (`crea.composer`) y el plan deriva su ruta.
>
> **Lo que el molde sí dice: un vertical NUEVO estrena el suyo**, `SeamComposer.<X>.cs`. Agrupar
> es algo que se hace cuando hay una razón —compartir un cliente, un orden de registro— y no por
> defecto; un fichero de 499 líneas con tres verticales dentro es el resultado de no haberlo
> decidido nunca.
>
> **Y lo que se midió y NO da para gate**: «cada vertical en un composer» lo incumple el árbol a
> propósito, y `Cada_vertical_tiene_su_EJE_1` ya lo tenía escrito —*«dentro de un composer que
> cablea varios verticales el gate CUENTA, no empareja»*—. Lo que sí es cierto hoy, comprobado
> cliente por cliente, es que **cada punto de cableado se registra en UN solo composer**: los
> quince `Http*` tienen dueño único. La única apariencia de excepción es `HttpPaymentProvider`,
> que sale en dos — y son dos clientes nombrados distintos (`GovFeeClientName` y
> `SeamClientName`) para los dos alcances del cobro (#27), no un registro duplicado.

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
- **El rechazo se LEE, no se deduce del código de estado.** Si algo se puede volver a intentar lo
  dice la capacidad en su bandera `transient`, y sólo ella: el mismo 503 puede ser
  `{prefijo}.store_busy` —quince milisegundos de cola detrás del turno de escritura, #112— o una
  caída de verdad, y el #112 eligió `Unavailable` y no `Conflict` justo para que un orquestador no
  deshiciera una saga sana. Una tabla de códigos del lado del CMS desharía esa decisión sin
  enterarse. Se lee con `RechazoDelArbolDeServicios`, que es el único sitio del CMS que nombra esa
  clave; lo que sí es de cada cliente es **cómo presentar** el fallo —un 404 es «no existe», un 401
  nombra la llave compartida, un 409 sube como rechazo de negocio con su motivo— y eso se queda en
  el cliente. Gate: `TransitoriedadTests` (#129).
  **Y un cuerpo que no se puede leer es «no consta», nunca «firme»**: tratarlo como rechazo firme
  convierte cualquier intermediario que devuelva HTML en «el banco dijo que no».
- **Degradar, no reventar.** Encender el modo sin el servicio arriba tiene que dejar el vertical
  sirviendo catálogo y fichas; lo que se para es la transacción, y se dice.

### 5.7 El ARTEFACTO: su emisor, su registro durable y su aviso — **fuera del seam**

Lo que queda como prueba cuando la transacción salió bien (§3). Tres piezas, y la decisión que
toman es **dónde viven**:

- **el emisor** — los hechos de UN artefacto, sin nada del camino por el que se compró
  (`EventTicketIssuer`);
- **el registro durable**, con su `ResourceType` propio sobre `IJsonEntityStore` —o sobre
  `IPhiStore` si el dato es clínico, como en Salud— y que **no cachea en el proceso**: un reinicio
  no puede dejar sin verificar algo ya impreso (ADR 0124);
- **el aviso a quien lo recibe** (`EventPurchaseNotification`), *best-effort*: un correo caído
  jamás tumba algo ya pagado y persistido.

**Las tres van FUERA del seam de la transacción y las comparten sus dos implementaciones**, y eso
es §3.2 aplicado: el emisor metido dentro del motor en proceso no lo puede usar el cliente
cableado, y copiarlo es «un QR con dos definiciones». Es lo que obligó a partirlo dos veces al
cablear Eventos (#35 rebanada 2b), con el síntoma que nadie habría relacionado: la puerta leyendo
un almacén vacío sin que nada avisara.

**Y el registro NO tiene cliente `Http*`, nunca.** Es lo único que sostiene la promesa de §3 —que
la prueba se pueda ver con el otro árbol caído— y es medible: en los siete no hay uno solo. Gates:
`Cada_vertical_tiene_su_EJE_3` y `El_registro_de_un_artefacto_NO_sale_a_la_red`.

### 5.8 *(condicional)* El SELLO y la custodia de su llave

Sólo si la cuarta pregunta de §3.1 se contesta que **sí**: si alguien de fuera tiene que poder
comprobar el artefacto sin creernos. Hoy son dos de siete, y las dos piezas son:

- **el seam del sello** en `Synergos.CMS.Interfaces` —`ITicketSigner`, `ICertificateIdSigner`— con
  un `Sign` y su comprobación al lado, y **la implementación en proceso se llama `Hmac<X>`, no
  `Stub<X>`**: no es un doble de nada, es la de verdad mientras no se mude la custodia;
- **la custodia de la llave** en `Synergos.CMS.Web/Services/` —`<X>SigningKeyProvider` + su
  `Lazy<X>Signer`—: lee el secreto configurado y, si no hay, **genera una llave aleatoria una vez,
  la cifra con `IDataProtector` y la guarda**. Perder ese volumen invalida todo lo ya emitido, y
  por eso el respaldo lo nombra (`CLAUDE.md` §11).

**El secreto va en su PROPIO POCO, bajo una sección ANIDADA en la del vertical** —
`Synergos:<X>:<Artefacto>`, como `Synergos:Eventos:Ticket` (#154).

Las dos mitades tienen su razón y ninguna es estilo. **POCO propio** porque el del eje 2 lo recibe
el cliente `Http<X>`, que no tiene por qué llevar dentro la llave con la que se firma nada: es
`feedback_pii_decision_lives_in_the_seam_type` aplicado a un secreto —lo que un tipo carga es una
propiedad del tipo, no una convención que alguien recuerde—. **Sección anidada** por dos cosas: una
hermana acaba a **una letra** de la del vertical, que es el defecto #154 y era el único par a esa
distancia entre las 43 secciones que el CMS enlaza; y anidar es lo que **sobrevive al día que el
sello cruce**, porque entonces hará falta su propio `Mode`/`BaseUrl`/`ApiKey` y `Synergos:<X>:Mode`
ya es del eje 2. Es la forma que el árbol ya usa para un sub-asunto que se cabla por su cuenta:
`Synergos:Gob:Notifications` y `Synergos:Gob:Payments`.

> **Esta regla decía otra cosa hasta el #154, y el error se lee bien:** decía «va en el POCO del
> vertical, en su MISMA sección», derivado de que `AcademySettings` lleva `CertificateSigningSecret`
> junto a `Mode`/`BaseUrl`/… Eso es cierto **y no prueba nada**, porque en Educación el `Mode` de
> ese POCO es el **del sello** (HU #45) y su eje 2 no tiene interruptor: un vertical con UN solo eje
> cableado no distingue las dos formas. Un POCO basta mientras eso sea verdad, y Educación se queda
> como está —renombrar `Synergos:Academy:CertificateSigningSecret` dejaría huérfano el único secreto
> que `CLAUDE.md` §11 dice que el respaldo no puede regenerar—. El disparador para partirlo: el día
> que su matrícula tenga interruptor.

**Esto SÍ puede cruzar, y es lo único del eje 3 que puede.** `Synergos:<X>:Mode=Api` cambia el
firmante por `Http<X>Signer` contra `Api.Signing` (#45) y lo que se gana es **la rotación**: la
llave local no sabe retirarse. Los ids anteriores se siguen verificando **acá**, por su forma, y
ni salen a la red — sin eso, cada QR ya impreso dejaría de valer el día del despliegue, y no
ruidosamente: contestando que la credencial no vale.

### 5.9 La pantalla, y las claves que cruzan

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

### 5.10 El gate del vertical

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

### 7.5 El gate del EJE 1 descubría verticales por su eje 2 — **cerrado** (#146)

`Cada_vertical_tiene_su_EJE_1` recorre `DelMolde()`, que descubre verticales por su **interruptor
de transacción**. Social contestó «¿hay algo que deshacer?» con NO, así que no tiene ninguno — y el
gate del PRIMER eje **no lo miraba**. Un vertical que sólo tuviera catálogo podía quedarse sin
fuente sin que nada se pusiera rojo.

No es una lista que envejeció: es **un gate acoplado al descubrimiento del eje de al lado**, que no
se ve leyéndolo porque durante siete verticales las dos listas coincidían. Se cierra con un
segundo diente derivado del disco por los dos lados —las fuentes que existen y los composers que
las nombran—, que además crece solo con el catálogo:
`Toda_fuente_de_catalogo_esta_registrada_por_un_composer`.

**La pregunta que lo caza, y sirve para cualquier gate del molde:** *¿de qué lista salen los
sujetos de este gate, y esa lista es la del eje que vigila?*

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
