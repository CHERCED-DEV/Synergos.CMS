# 11 — El mapa del cableado

> Los 47 `Stub*` de `Synergos.CMS.Application/Services/Impl/`, y qué se hace con cada uno.
>
> **Esto es un inventario, no un rediseño.** Describe lo que hay y decide destino. El rediseño
> de algo concreto es otro ticket.

Lo vigila `Synergos.CMS.Tests/Architecture/WiringMapTests.cs`. Un stub nuevo sin entrada acá
**rompe el build**, y una entrada que nombre una capacidad inexistente también. Un mapa que se
mantiene a mano se desactualiza en la tercera ola.

## Lo primero: son 47, no 45

El ticket decía 45. La cuenta real al levantar el inventario fue **46**, y no porque alguien
añadiera uno: la cuenta de 45 era de memoria. Es exactamente la razón por la que este documento
lleva un gate detrás en vez de una lista escrita a mano.

**Hoy son 47**, y cómo se enteró este documento vale más que la cifra. Durante tres olas el
inventario de abajo se mantuvo al día —el gate lo exige— mientras las cifras de esta sección se
quedaron en 46: quien cableaba movía su fila y nadie le pedía tocar el resumen. El gate estaba
verde y tenía razón; lo que mentía era la prosa (#50).

Ahora **las cifras de esta sección también las comprueba el gate**, contándolas contra el
inventario y contra el disco. Un número escrito acá que no cuadre rompe el build, que es la única
forma conocida de que una cifra a mano sobreviva a la cuarta ola.

## Lo segundo, y es lo que cambia la épica

**El grueso de estos stubs no es cableado pendiente.** El reparto:

| Familia | Cuántos | Qué le pasa |
|---|---:|---|
| **A — cableado pendiente** | 12 | va a una capacidad o a un BFF |
| **B — ya resuelto desde el contenido** | 5 | sale de DocTypes; cablearlo sería un retroceso |
| **C — se queda en stub a propósito** | 30 | no hay capacidad detrás, y no debería haberla |

> **La brecha es menor de lo que «una capacidad de veinte conectada» sugiere**, y por una razón
> que no se ve desde el conteo: **más de un tercio de los stubs ya son durables**. 18 de los 47
> escriben tras `IJsonEntityStore`, `IPrivateFileStore` o `IPhiStore` (ADR 0105, ADR 0116 fase 6, doc 25,
> T6). «Stub» en este repo dejó hace tiempo de querer decir «en memoria», y leerlo así es lo que
> hace que un ticket prometa arreglar algo que ya está arreglado — ver la nota sobre #26 al final.

---

## Familia A — cableado pendiente (12)

Lo que sí tiene una capacidad o un orquestador detrás. **La columna «a qué nivel» es la decisión
que no hay que equivocar**: contra el BFF cuando hay varios pasos que pueden fallar a la mitad y
hay que deshacer; contra la capacidad cuando es un solo paso que puede decir NO solo.

| Stub | Destino | A qué nivel, y por qué | Estado |
|---|---|---|---|
| `StubShopOrderService` | `Bff.Tienda` `POST /v1/purchases` | **Orquestador.** Reservar + cobrar + crear pedido pueden fallar a la mitad; si el cobro falla hay que soltar el stock. Contra las capacidades sueltas, el CMS reimplementaría la máquina de sagas — y peor, porque no tiene dónde anotar una compensación pendiente | **HU #24** |
| `StubReservationService` | `Api.Booking` `/v1/holds` | **Capacidad.** Apartar es un paso que dice NO solo (`insufficient_capacity`). Era «el motor polimórfico que comparten seis verticales, y cablearlo los mueve de una vez» — y **el apalancamiento se agotó**: los seis se cablearon de a uno (#24, #25, #33a, #35, #36, #40). Con los seis flags en su modo cableado **no le queda un solo llamador de negocio**; el único que lo llama fuera de un flag es `HoldExpirationScannerHostedService`, que se registra incondicionalmente. Lo que queda no es cablear: es decidir el destino del motor en proceso y que su barredor lo siga (#33) | premisa agotada |
| `StubPaymentProvider` | `Api.Payments` `/v1/payments` | **Capacidad.** Ya hay `RoutingPaymentProvider` + `WompiPaymentProvider` delante; el stub es el respaldo cuando no hay credencial. Bloqueado por la misma HU que hace que `Api.Payments` cobre de verdad | épica #2 |
| `StubCaseWorkflowService` | `Api.Workflow` `/v1/instances/{id}/fire` | **Capacidad.** Una transición de estado es un paso que dice NO solo (`transition_not_allowed`). La tabla de transiciones vivía en C# dentro del stub, que es justo lo que `Api.Workflow` existe para no repetir por dominio | **HU #44** |
| `StubCertificateService` | `Api.Signing` `POST /v1/seals` — **NO `/v1/signatures`** | **Capacidad.** El motivo sigue en pie: el HMAC local (ADR 0124) guarda su llave y **no sabe retirarla**, y la capacidad sí. Lo que no encajaba era el endpoint: el token de `/v1/signatures` **vence** (≤365 d), **no es determinista** y **publica el payload** —o sea el alumno— dentro del id que se imprime. El **sello** (#45) es la operación que faltaba: determinista, sin vencimiento, opaca, y se comprueba **contra el sujeto**. El firmante local **se conserva** verificando los ids anteriores, o cada QR ya impreso dejaría de valer | **HU #45** |
| `StubOrderTrackingService` | `Api.Workflow` | **Capacidad.** Un timeline es una máquina de estados con otro nombre. Las cuatro instancias (tienda / viaje / eventos / academia) usan **una definición cada una** — compartirla las haría leer «enviado» donde dice «matriculado». El CMS conserva su almacén como modelo de LECTURA: con la capacidad caída el timeline se sigue viendo, y sólo se para avanzarlo | **HU #46** |
| `StubReturnService` | `Api.Orders` + `Api.Payments` | **Orquestador** (`Bff.Tienda`, sin construir esa cara). Un RMA reembolsado son dos pasos con plata en medio: si el reembolso sale y la orden no se marca, el dinero se fue sin rastro. Es la compensación que cambia de carácter al capturar | pendiente |
| `StubApplicationService` | `Bff.Gob` (sin construir) | **Orquestador.** Radicar valida, calcula tasa, cobra si aplica y asienta estado — con pago de por medio. Es el agregado raíz de Gobierno y hoy lo compone media docena de stubs hermanos por DIP | pendiente |
| `StubClinicalSchedulingService` | `Bff.Salud` `POST /v1/appointments` | **Orquestador.** Apartar el cupo + cobrar el copago + avisar, con compensación si el copago falla. Ver la corrección de abajo | **HU #25** |
| `StubEventTicketingService` | `Bff.Eventos` `POST /v1/ticketing` | **Orquestador.** Aforo + cobro pueden fallar a la mitad. La compra se parte: el orquestador mueve aforo y plata, y **el artefacto se queda acá** —la entrada, su QR, su portador, el check-in— porque el firmante vive de este lado | **HU #35** |
| `StubHotelBookingService` | `Bff.Viajes` `POST /v1/trips` | **Orquestador.** Apartar, cobrar y confirmar pueden fallar a la mitad. Las **dos** vías: la reserva de hotel y el carrito multi-producto, que además pide confirmación PARCIAL y **ordena la devolución** de lo que no se cumplió — el orquestador cotiza el viaje entero y no sabe cuánto vale el ítem caído | **HU #36 + #40** |
| `StubVisitSchedulingService` | `Api.Booking` `/v1/holds` | **Capacidad, no orquestador.** Una visita NO se cobra, así que no hay segundo paso que compensar — una saga de un paso sería la máquina de compensar sin nada que compensar. Ver la corrección de abajo | **HU #33a** |

> ### Corrección: este stub estaba mal clasificado, y por qué importa
>
> `StubClinicalSchedulingService` salió en la familia C de la primera versión de este mapa, con
> el argumento de que «ya reusa `IReservationService`, así que se mueve solo el día que ése se
> cable». **Es cierto y no alcanza.** Mirando el código de cerca, el stub no solo *usa* el motor
> de reservas: hace `HoldItemAsync` → sesión de pago → `ConfirmAsync`, con un `CancelAsync` de
> respaldo si el copago no se captura. Eso es **una saga con compensación, reimplementada del
> lado del CMS** — exactamente lo que la familia A existe para señalar.
>
> El error es instructivo: **«compone otro seam» y «orquesta varios pasos que pueden fallar a la
> mitad» se parecen desde el registro del composer y no son lo mismo.** El filtro que los separa
> no es de qué depende, sino *qué pasa si el segundo paso falla*. Si hay algo que deshacer, es
> orquestación, y la orquestación no vive en el CMS.
>
> El otro del grupo, `StubClinicalOrderService`, se revisó con el mismo filtro y **sí** se queda
> en C.
>
> **`StubVisitSchedulingService` NO, y es la segunda vez que este mismo error se cuela** (HU #33a).
> Se clasificó C porque «confirma con el paso de pago desactivado (`visit-free`), así que no hay
> segundo paso que pueda fallar» — lo cual es cierto y **contesta otra pregunta**. Que no necesite
> orquestador no quiere decir que no haya que cablearlo: quiere decir que va **directo a la
> capacidad**. Son dos preguntas distintas y este mapa las tenía fundidas:
>
> | Pregunta | Qué decide |
> |---|---|
> | ¿hay algo que deshacer si el segundo paso falla? | si hace falta un **orquestador** |
> | ¿el cupo lo lleva alguien más? | si hace falta **cablearlo** |
>
> Con las dos separadas, la visita inmobiliaria es A con destino `Api.Booking` **sin BFF** — y es
> el primer consumidor del repo que le habla a una capacidad de frente.

### Los tres primeros, y por qué esos

1. **`StubShopOrderService`** (#24) — es el único con el orquestador **ya construido, probado y
   sin un solo consumidor**. Es la distancia más corta entre «tenemos 20 APIs» y «tenemos un
   producto», y no cuesta código nuevo del lado del servicio.
2. ~~**`StubReservationService`**~~ — **ya no es candidato, y la lección es la buena.** Era
   «un cableado, seis verticales»; los seis llegaron a su destino por su cuenta, cada uno por
   la puerta que le tocaba (#24, #25, #33a, #35, #36, #40). **El apalancamiento de un seam se
   evapora si se tarda**: cada consumidor que se cablea por separado se lo lleva consigo, y al
   final no quedó nada que mover de una vez. Lo que queda de #33 es otra cosa —qué se hace con
   el motor en proceso y con su barredor de holds, que corre siempre— y está anotado allí.
3. **`StubPaymentProvider`** — mientras no cobre, ningún demo de venta corre de punta a punta.
   Va tercero y no primero porque lo que falta es una credencial, no un cliente HTTP.

---

## Familia B — ya resuelto desde el contenido de Umbraco (5)

**Cablear estos a `Api.Catalog` sería un retroceso**, y es el error caro de la épica. El dato ya
tiene dueño: lo autora un editor en el backoffice y lo sirve el árbol de contenido. Meter una
llamada HTTP en medio cambiaría una lectura en proceso por una ida a la red **y** le quitaría al
editor la superficie donde publica.

Los cinco tienen la misma forma: un flag `Synergos:Catalog:Sources:{vertical}` que vale `demo`
(el stub sembrado) o `cms` (el contenido). El rollback es esa línea, sin redespliegue.

| Stub | Qué lo resolvió | Fuente de contenido |
|---|---|---|
| `StubEventCatalogProvider` | **ADR 0107** + **ADR 0117** | `UmbracoEventCatalogSource` sobre `eventPage` |
| `StubPropertyCatalogProvider` | **ADR 0118** | `UmbracoPropertyCatalogSource` sobre `propertyListing` |
| `StubStayContentProvider` | **ADR 0119** | `UmbracoStayContentSource` sobre `stayListing` |
| `StubTramiteCatalogProvider` | **ADR 0123** | `UmbracoTramiteCatalogSource` sobre `tramitePage` |
| `StubProductCatalogProvider` | **ADR 0107** | `UmbracoProductCatalogSource` sobre el catálogo de Tienda |

> `StubProductCatalogProvider` es el raro de los cinco: **no lo reemplaza una clase hermana**, le
> cambian la fuente por debajo (`ICatalogSource<CatalogProduct>`). Sigue siendo el que se
> instancia en los dos modos. Es family B igual — el dato sale del contenido — pero quien lea
> solo el nombre de la clase registrada no lo va a ver.

---

## Familia C — se queda en stub a propósito (30)

No hay capacidad detrás, y no debería haberla. Tres razones distintas, y conviene no mezclarlas
porque «qué haría falta para que dejara de ser stub» es diferente en cada una.

### C.1 — Cálculo puro: no tiene almacén, luego no es un servicio (6)

El filtro de atomicidad al revés (doc 07): **lo que no tiene almacén es un tipo, no un
servicio.** Ponerlos tras HTTP añadiría una ida a la red y un modo de fallo a una función que
hoy no puede fallar.

| Stub | Qué es | Para que dejara de ser stub |
|---|---|---|
| `StubMortgageCalculator` | amortización francesa, determinista | nada — está terminado |
| `StubGovFeeCalculator` | tasa del trámite (≤0 = exento) | nada — está terminado |
| `StubCancellationPolicyEvaluator` | penalidad por fecha | nada — está terminado |
| `StubCabinSeatMapProvider` | mapa de cabina determinista (ADR 0127) | un proveedor **exógeno**: quien conoce el inventario de butacas lo publica. Autorar butacas en un backoffice es una hoja de cálculo que se desincroniza en el primer vuelo |
| `StubFlightAvailabilityProvider` | itinerarios sembrados | un GDS/NDC real. **No es una capacidad nuestra**: es un tercero |
| `StubCarRentalProvider` | categorías SIPP sembradas | un agregador de rentadoras. Tercero, igual que el anterior |

### C.2 — Proyección derivada: cablearlos duplicaría el estado que existe para no duplicar (7)

No guardan nada propio: **componen otros seams y calculan**. Darles almacén —o capacidad— sería
crear una segunda verdad que se desincroniza, que es exactamente lo que una capacidad existe
para evitar.

| Stub | De qué se deriva |
|---|---|
| `StubNotificationFeed` | del grafo social + las reacciones |
| `StubEhrInBasketService` | de resultados + refills + mensajería |
| `StubClinicalMedicationService` | de las recetas vivas |
| `StubEventManagementService` | del ticketing (asistentes/aforo/vendidos) |
| `StubCaseTrackingProvider` | del agregado de expedientes |
| `StubSavedSearchService` | de `IUserCollection` + el catálogo de inmuebles |
| `StubSocialProfileProjection` | del padrón de Members |

### C.3 — Demo del vertical: es contenido sembrado, no un sistema (20)

Sirven datos coherentes para que una app Angular corra de punta a punta. **Su destino no es una
capacidad: es contenido**, igual que la familia B — o un sistema externo de verdad (un EHR, un
LMS), que no es nuestro.

**Healthcare EHR-lite (9)** — capa ADITIVA de demo, distinta del núcleo PHI de producción de
ADR 0098, que ya es durable y cifrado. Su destino real es **un EHR externo**, no una capacidad
nuestra: `Api.Documents` guarda documentos, no historias clínicas.
`StubPatientRegistry` · `StubDoctorDirectory` · `StubClinicalRecordService` ·
`StubClinicalPrescriptionService` · `StubClinicalResultsProvider` · `StubClinicalOrderService` ·
`StubClinicalBillingService` · `StubRoomAvailabilityProvider`

> `StubClinicalSchedulingService` y `StubVisitSchedulingService` **salieron de este grupo**: el
> primero orquesta y el segundo lleva cupo que es de una capacidad (ver la corrección de la
> familia A). `StubRoomAvailabilityProvider` se queda: es disponibilidad hotelera, y su destino es
> un PMS/channel-manager, un tercero.

**Motor social y de engagement (5)** — `StubSocialGraphService` · `StubReactionService` ·
`StubContentStream` · `StubUserCollection` · `StubLeadCaptureService`.
Los cinco **ya son durables** (ADR 0105). `Api.Engagement` existe y guarda «engagements» con
visibilidad, que no es lo mismo que un grafo dirigido de seguimiento ni una wishlist por Member.
Forzarlos ahí sería meter un sustantivo de negocio dentro de una capacidad agnóstica.

**Academia y eventos (3)** — `StubCourseCatalogProvider` · `StubEnrollmentService` ·
`StubEventTicketingService`.
Academia es **el único vertical con catálogo y sin ninguna superficie CMS**: no existe
`coursePage`, así que un editor no puede publicar un curso ni con el flag puesto. Su destino es
familia B —una rebanada de contenido, como las ADR 0117/0118/0119/0123— **no una capacidad**.
El ticketing y la matrícula son motores transaccionales con pago: irían a `Bff.Eventos` y
`Bff.Academy`. **`StubEventTicketingService` ya está cableado** (HU #35): pasa a familia A con
destino `Bff.Eventos`, activable con `Synergos:Eventos:Mode=Bff` y con el stub de default. Y
cablearlo obligó a partirlo antes: comprar se va al orquestador, pero **el artefacto —la entrada,
su QR, su portador, el check-in— se queda en el CMS**, porque el firmante vive de este lado. Vive
en `EventTicketLedger` y lo comparten los dos caminos de compra. `StubEnrollmentService` sigue
esperando a `Bff.Academy`.

**Mensajería y documentos (2)** — `StubMessagingService` · `StubDocumentUploadService`.
Ver la nota de abajo: **es la HU #26, y su premisa no se sostiene.**

---

## La nota sobre #26, que este inventario existe para dar

La HU #26 dice que estos dos «guardan en memoria» y que «un reinicio del CMS borra las
conversaciones y los archivos subidos». **No es así, y no lo es desde hace varias olas.**

- **`StubMessagingService`** escribe tras `IJsonEntityStore` (**ADR 0105**), un documento por
  hilo, con el `ThreadId` determinista como clave. La propia clase documenta el arreglo en
  pasado: *«un reinicio borraba cada mensaje directo que alguien hubiera mandado»*.
- **`StubDocumentUploadService`** guarda **los bytes** en `IPrivateFileStore` desde **T6** —
  cifrados y bajo `App_Data/`, donde ningún middleware de estáticos llega— y adjunta la metadata
  al expediente, que es durable vía `StubApplicationService`. Y en el orden correcto: primero
  los bytes, después la metadata, *«al revés, un fallo del almacén dejaría en el expediente un
  documento que se puede listar pero no descargar»*.

Lo que #26 propone arreglar **ya está arreglado**. Queda una pregunta legítima detrás —si el
dueño del almacén debe ser el CMS o la capacidad— pero **es otra pregunta**, con otro valor y
otro riesgo, y la decisión es del arquitecto. Está escrita en el ticket.

> Y sobre lo delicado que #26 sí anticipaba —«¿el binario también?»—: **hoy no está partido en
> dos sitios.** Bytes y metadata viven ambos del lado del CMS. Cablear a `Api.Documents` es lo
> que lo partiría, y ahí sí habría que decidirlo antes de codificar.

---

## Los 47, en una lista

Para el gate. La familia va entre corchetes; el destino, cuando lo hay, es un directorio que
tiene que existir en la raíz del repo.

<!-- MAPA:INICIO -->
| Stub | Familia | Destino |
|---|---|---|
| `StubApplicationService` | A | `Synergos.Bff.Gob` |
| `StubCabinSeatMapProvider` | C | — |
| `StubCancellationPolicyEvaluator` | C | — |
| `StubCarRentalProvider` | C | — |
| `StubCaseTrackingProvider` | C | — |
| `StubCaseWorkflowService` | A | `Synergos.Api.Workflow` |
| `StubCertificateService` | A | `Synergos.Api.Signing` |
| `StubClinicalBillingService` | C | — |
| `StubClinicalMedicationService` | C | — |
| `StubClinicalOrderService` | C | — |
| `StubClinicalPrescriptionService` | C | — |
| `StubClinicalRecordService` | C | — |
| `StubClinicalResultsProvider` | C | — |
| `StubClinicalSchedulingService` | A | `Synergos.Bff.Salud` |
| `StubContentStream` | C | — |
| `StubCourseCatalogProvider` | C | — |
| `StubDoctorDirectory` | C | — |
| `StubDocumentUploadService` | C | — |
| `StubEhrInBasketService` | C | — |
| `StubEnrollmentService` | C | — |
| `StubEventCatalogProvider` | B | — |
| `StubEventManagementService` | C | — |
| `StubHotelBookingService` | A | `Synergos.Bff.Viajes` |
| `StubEventTicketingService` | A | `Synergos.Bff.Eventos` |
| `StubFlightAvailabilityProvider` | C | — |
| `StubGovFeeCalculator` | C | — |
| `StubLeadCaptureService` | C | — |
| `StubMessagingService` | C | — |
| `StubMortgageCalculator` | C | — |
| `StubNotificationFeed` | C | — |
| `StubOrderTrackingService` | A | `Synergos.Api.Workflow` |
| `StubPatientRegistry` | C | — |
| `StubPaymentProvider` | A | `Synergos.Api.Payments` |
| `StubProductCatalogProvider` | B | — |
| `StubPropertyCatalogProvider` | B | — |
| `StubReactionService` | C | — |
| `StubReservationService` | A | `Synergos.Api.Booking` |
| `StubReturnService` | A | `Synergos.Bff.Tienda` |
| `StubRoomAvailabilityProvider` | C | — |
| `StubSavedSearchService` | C | — |
| `StubShopOrderService` | A | `Synergos.Bff.Tienda` |
| `StubSocialGraphService` | C | — |
| `StubSocialProfileProjection` | C | — |
| `StubStayContentProvider` | B | — |
| `StubTramiteCatalogProvider` | B | — |
| `StubUserCollection` | C | — |
| `StubVisitSchedulingService` | A | `Synergos.Api.Booking` |
<!-- MAPA:FIN -->

> `Synergos.Bff.Gob` **no existe todavía** y por eso `StubApplicationService` es el único destino
> de la tabla que el gate no puede comprobar contra un directorio. El gate acepta un destino
> marcado como no construido sólo si está en su lista explícita de orquestadores pendientes —
> los seis de `CLAUDE.md` §11. Un destino inventado sigue rompiendo.
