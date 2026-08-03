# Síntesis de los nueve dominios — Synergos

> Todo lo que sigue cruza los nueve informes. Cuando dos informes se contradicen, abrí el fichero y lo digo. Las verificaciones de código que hice yo en esta sesión van marcadas **[verificado en esta síntesis]**; las cifras de mercado son las de los informes originales, con su marca.

---

## 1. Necesidades comunes

Criterio: aparece en 5+ dominios ⇒ se construye una vez. Y el filtro del doc 07 aplica igual acá: **es capacidad sólo si puede decir NO sola Y es dueña de su almacén.**

| # | Necesidad | Dom. | Cuáles | Veredicto | Por qué |
|---|---|:-:|---|---|---|
| 1 | **Enviar de verdad** (correo/SMS/WhatsApp) | 9 | todos | **Integración externa** detrás de `INotificationSender` | La seam existe. `Api.Notifications/Program.cs:29` registra `LoggingNotificationSender` **[verificado]**. Es un adapter, no diseño. |
| 2 | **Envío programado / diferido** | 8 | Booking, Salud, Viajes, Eventos, Academy, Realty, Gobierno, Social | **Feature de `Api.Notifications`** | Grep sobre `Api.Notifications/Domain/`: no existe `ScheduledFor`/`SendAt`/`Schedule` **[verificado]**. Pasa atomicidad: puede rechazar sola ("fuera de ventana", "ya vencido") y el almacén de entregas ya es suyo. Un recordatorio *es*, por definición, un envío diferido. |
| 3 | **Ventana legal de contacto (Ley 2300/2023)** + acuse de **acceso** (no de envío) | 6+ | Booking, Social, Shop, Realty, Academy, Gobierno (Ley 2080), Viajes | **Feature de `Api.Notifications`** (la misma que #2) | Hoy sólo hay `MaxPerRecipient = 20` por hora: es tope de *volumen*, no de *calendario* **[verificado]**. Ver §6.7: son cuatro regímenes legales distintos sobre la misma capacidad. |
| 4 | **Calendario de días hábiles y festivos colombianos** | 6 | Gobierno (15/10 d. hábiles, Ley 1755), Shop (retracto 5, reversión 15), Eventos (Dec. 587), Salud (RIPS 22), Booking (18 festivos), Viajes | **Tipo puro en `Synergos.Core` — NO capacidad** | Cero coincidencias de `festivo|habil|holiday` en todo el árbol; `Synergos.Core` son 8 tipos y ninguno es calendario **[verificado]**. **No tiene almacén: es una función de la fecha.** Cinco informes lo pidieron por separado y ninguno lo propuso como pieza compartida (§6.6). |
| 5 | **Cobro real (PSP colombiano)** | 8 | todos menos Gobierno (que necesita la variante de recaudo público) | **Integración externa** detrás de `IPaymentProvider` de `Api.Payments` | `Api.Payments/Program.cs:25` registra `LoggingPaymentProvider` **[verificado]**. **Pero ojo con §6.3: hay dos seams de pago incompatibles en el repo.** |
| 6 | **Receptor de confirmación asíncrona (PSE)** | 6 | Viajes, Eventos, Academy, Shop, Booking, Salud | **Feature de `Bff.Core`** | `Bff.Core` ya tiene `Saga.cs`, `SagaEngine.cs`, `SagaStore.cs`, `Compensator.cs`, `CompensationSweeper.cs` **[verificado]**. Un webhook de PSE es *reanudar una saga suspendida*. Ponerlo en cada BFF es escribirlo seis veces. Ningún informe lo propuso así. |
| 7 | **Precio e impuesto por línea que sobrevivan al pedido** | 5+ | Shop, Eventos (parafiscal ≥3 UVT por boleta), Academy (IVA excluido), Viajes (exención a no residentes), Salud (RIPS/FEV) | **Bug de `Bff.Tienda` + endurecer `Api.Orders`** | `PurchaseFlow.cs:121` → `UnitPrice: Money.Zero(total.Currency)` **[verificado]**, y `OrderRules.CheckLines` sólo rechaza negativos. Sin esto no hay factura, ni devolución parcial, ni resumen ítem-por-ítem del Art. 50. Es el defecto más barato de arreglar y el que más bloquea. |
| 8 | **Factura electrónica DIAN** | 7 | Shop, Eventos, Academy, Viajes, Booking, Realty, Salud | **Capacidad nueva `Api.Invoicing`** (la única aprobada — ver §5) | Cero `DIAN|CUFE|IInvoice` en todo el árbol **[verificado]**. |
| 9 | **Retención documental con calendario, marca legal y disposición final** | 5 | Salud (15 años, Res. 839), Gobierno (TRD, Acuerdo 003), Realty (plantillas de contrato), Shop (prueba durable Art. 50), Social (RNBD) | **Feature de `Api.Documents`** | **Corrección de hecho: `Api.Documents` NO tiene retención.** Grep de `Retention` sobre `Api.Documents/` y `Api.Audit/`: cero **[verificado]**. Los informes de Realty y Social afirmaron que sí. Salud lo dijo bien. Mismo almacén, mismo dueño ⇒ feature, no capacidad. |
| 10 | **Consulta por actor ("mis cosas")** | 6 | Booking, Viajes, Eventos, Academy, Shop, Realty | **Feature de cada capacidad afectada** (`Booking`, `Engagement`, `Orders`) | `EngagementService` tiene `ListForTarget` y **no** `ListForActor` **[verificado]**; `IReservationStore` tiene `ForResource` y no índice por `For`. Es un índice de almacén, no orquestación. Ver §6.2: es un sesgo sistemático, no seis huecos locales. |
| 11 | **Consentimiento cableado al borde, con `policyVersion` desde contenido versionado** | 7 | Salud, Gobierno, Realty, Academy, Social, Booking, Viajes | **Feature del BFF** (cero construcción en la capacidad) | `Api.Consent` está construida, exige propósito acotado y versión de política, y **ningún dominio la llama**. Es la mejor relación valor/esfuerzo de todo el ejercicio, y es el único puente CMS↔árbol que no es cosmético (§4). |
| 12 | **Ciclo de vida de reserva más rico** (NoShow, Completed, CheckIn/CheckOut, reagenda atómica, excepciones de calendario por fecha) | 5 | Booking, Viajes (SIRE exige llegada *y* salida), Salud, Eventos, Academy | **Feature de `Api.Booking`** | `ReservationStatus` = `Confirmed\|Cancelled`; `OpeningRule` es sólo recurrencia semanal. Reagendar desde el BFF pierde el cupo entre pasos ⇒ la atomicidad es propiedad de la capacidad. |
| 13 | **Calendario de tarifas (precio por fecha, temporada, estancia mínima)** | 4 | Viajes ("el hueco estructural"), Booking, Realty (canon con tope IPC), Eventos (parcial: `Promotion.Validity`) | **Feature de `Api.Pricing`** (`PriceCalendar(Subject, TimeWindow, Money)`) | Booking sabe de tiempo y no de plata; Pricing sabe de plata y no de tiempo. La tarifa es la intersección, y el corte atómico la dejó fuera. Mismo dueño de "cuánto vale" ⇒ feature. |
| 14 | **Firma con validez probatoria (Ley 527 / Dec. 2364)** | 4 | Salud (receta), Gobierno (acto administrativo), Realty (contrato), Academy (parcial) | **Integración externa** (Certicámara / Andes SCD / AutenTIC) | `Api.Signing` es HMAC con llave del servidor: prueba que *el servidor* firmó. Cuatro informes llegaron a la misma conclusión por caminos distintos. Hay que decirlo en un ADR para que nadie vuelva a proponer "ya tenemos Signing". |
| 15 | **Los orquestadores que faltan** | 7 | Viajes, Eventos, Realty, Gobierno, Academy, Social, Booking | **Feature del BFF** | Sólo existen `Bff.Salud` y `Bff.Tienda` **[verificado]**. Pero no son siete proyectos: son **dos flujos** que se repiten — *comprar-algo-con-cupo* (ya está en `Bff.Tienda`/`Bff.Salud`) y *radicar-un-expediente-con-plazo* (no existe). Ver §2 paso 3. |
| 16 | **Schema del vertical en el CMS** | 3 vacíos de 9 | Academy (0 `coursePage` entre 252 ContentTypes **[verificado]**), Salud (un `elementSynEhr` con 4 campos), Booking (sólo el wizard de hoteles) | **Schema del CMS** | Hallazgo de cruce: en 6 de 9 dominios el CMS está **adelante** del árbol de capacidades; en 3 está muy atrás. La narrativa "el árbol está construido, falta el CMS" es falsa en dos tercios de los casos. |

---

## 2. Orden de construcción

Cada paso desbloquea a los siguientes, y el orden está sesgado a favor del dominio de §3. No es una lista de deseos: los pasos 1–4 los consumen 6+ dominios, y los pasos 3 y 5 son la salida a mercado.

**Paso 1 — El borde avisa.** Emisor real detrás de `INotificationSender` (SES/Resend + un canal SMS) y **acuse de acceso**, no de envío.
*Desbloquea:* los nueve. Sin esto no hay confirmación de compra, ni recordatorio de cita, ni notificación electrónica del CPACA, ni newsletter, ni alerta de búsqueda guardada. Es el único ítem que aparece en los nueve backlogs de prioridad 1.
*Cuesta:* S–M. Es un adapter sobre una seam que ya existe.

**Paso 2 — El tiempo entra al producto.** (a) Calendario hábil colombiano como tipo puro en `Synergos.Core`; (b) envío programado + ventana Ley 2300 + tope por canal/semana en `Api.Notifications`.
*Desbloquea:* recordatorios de verdad (Booking, Salud, Viajes, Eventos, Academy), el reloj de términos de Gobierno, el reloj de retracto/reversión de Shop y Eventos, el reentrenamiento anual de SST en Academy.
*Por qué acá:* el paso 1 sin diferimiento sólo sirve para el correo transaccional inmediato. El 80% del valor de notificar está en notificar *después*.

**Paso 3 — El primer orquestador nuevo: `Bff.Gobierno`, sobre el reloj de términos.** Reloj de vencimiento sobre la instancia de `Api.Workflow` (días hábiles del paso 2, semáforo, alerta, escalamiento) + saga que cablea el vertical del CMS a `Workflow` + `Documents` + `Audit` + `Consent`, reemplazando `StubCaseWorkflowService`.
*Desbloquea:* Gobierno vendible (§3), y **el molde reutilizable de "expediente con plazo"** — que es el mismo flujo de la autorización clínica (Salud), la matrícula con cartera (Academy), el embudo de lead (Realty) y el RMA con reloj de retracto (Shop). Es el segundo de los dos flujos que faltan; el primero (`comprar-algo-con-cupo`) ya está escrito dos veces.
*Nota:* `Api.Workflow` ya trae guarda por rol y la definición como dato, con la clave de ejemplo `gov.tramite`. Es la capacidad mejor ajustada del árbol a este paso.

**Paso 4 — El cumplimiento se cablea (casi sin construir).** (a) `Api.Consent` conectada a cada formulario/checkout, con `policyVersion` viniendo de una página de política versionada en el CMS; (b) retención en dos fases + marca legal en `Api.Documents`; (c) cadena de hash + exportación sellada en `Api.Audit`.
*Desbloquea:* Salud (Res. 839 y 3100), Gobierno (Acuerdo 003 y control interno), Social/corporativo (RNBD ante la SIC), Realty (sustrato de SAGRILAFT), y **el argumento comercial de §4b en los nueve**.
*Por qué acá y no antes:* (a) es cableado puro y podría ir en el paso 1; lo agrupo con (b) y (c) porque juntas son *una historia de venta*, no tres tickets.

**Paso 5 — Kit de conformidad publicable + gate WCAG 2.1 AA en el pipeline.** El menú de Transparencia con los 10 niveles del Anexo 2 como schema, esquema de publicación, registro de activos, declaración de accesibilidad, UI kit GOV.CO.
*Desbloquea:* el cierre de la venta de Gobierno (el ITA es lo que mide la Procuraduría y publica). Y el **gate WCAG sirve a los nueve** y no lo tiene ningún competidor local de ningún dominio: es la única pieza de este plan que es diferencial técnico verificable en un pliego.

**Paso 6 — El borde cobra.** `WompiPaymentProvider` para la seam de `Api.Payments` + **reanudación de saga por webhook en `Bff.Core`** (paso 6 de §1).
*Desbloquea:* Shop, Eventos, Viajes, Academy, Booking. Va después del paso 5 y no antes porque **el beachhead no lo necesita** (un trámite se radica y puede pagarse después o nunca — el propio doc 07 justificó separar `Orders` de `Payments` con ese caso).
*Trampa a resolver antes de escribir código:* §6.3.

**Paso 7 — El pedido guarda la verdad.** `QuoteLine → OrderLine` con precio e impuesto; `DeliveredAt` en `Api.Orders` leído desde `Api.Fulfillment`.
*Desbloquea:* factura, devolución parcial legalmente correcta, retracto con reloj, parafiscal por boleta, resumen del Art. 50. Es S de esfuerzo y es prerequisito duro del paso 8.

**Paso 8 — `Api.Invoicing` (la única capacidad nueva).** Rangos autorizados, correlativo, CUFE, XML, transmisión, notas crédito.
*Desbloquea:* Shop, Eventos (con el anexo de espectáculos y el prefijo EP##), Academy, Viajes, Realty, y la mitad FEV de Salud.

**Paso 9 — `Api.Booking` crece.** Excepciones de calendario por fecha, `NoShow`/`Completed`/`CheckIn`/`CheckOut`, reagenda atómica, índice por `For`. Y `PriceCalendar` en `Api.Pricing`.
*Desbloquea:* Booking de servicios, la mitad de Viajes (SIRE necesita llegada y salida) y la agenda de Salud.

**Paso 10 — Schema donde falta.** `coursePage`/`courseModule`/`courseLesson` (Academy), servicio con duración y buffer (Booking), proyecto/torre/tipología (Realty).
*Va último* porque en 6 de 9 dominios el CMS no es el cuello de botella, y en los 3 donde lo es, esos dominios no son el beachhead.

---

## 3. Con qué dominio salir primero

### **Gobierno / Trámites.**

**Distancia entre lo construido y lo vendible — la más corta del conjunto, medida en piezas que faltan, no en horas:**

- El schema del vertical **ya existe y es el más completo de los nueve**: `tramitePage` (15 propiedades incluidas normativa, tasa, días estimados, requiere cita) + `elementTramiteFormSection` + `elementTramiteFormField`, y `GovController` (803 líneas) ya sirve catálogo → ficha → formulario dinámico → radicar → carpeta del ciudadano → cola del funcionario → decisión, con identidad server-trusted.
- La capacidad núcleo está **diseñada para esto**: `Api.Workflow` define la máquina como dato, con guarda por rol, y su propio ejemplo documentado es `gov.tramite` con transiciones `radicar`/`aprobar`. Ningún otro dominio tiene una capacidad tan literalmente hecha a su medida.
- **No necesita** PSP real, ni factura DIAN, ni firma certificada, ni app móvil nativa, ni channel manager, ni FHIR, ni SCORM, ni habilitación previa ante un ministerio para poder vender. Todos los demás dominios necesitan al menos uno de esos, y varios necesitan tres.
- La carga regulatoria es la más alta de los nueve, y —único caso— **la mitad de esa carga es contenido publicado**: transparencia, publicación proactiva, accesibilidad, datos abiertos. Es el único dominio donde el CMS no es la vitrina del cumplimiento: es parte del cumplimiento.
- **Es uno de los dos únicos dominios donde las dos mitades del producto pesan a la vez** (§6.5): el expediente usa el árbol, el kit ITA usa el CMS.

**Qué falta exactamente para venderlo** — cuatro cosas, y son los pasos 1 a 5 de §2:

1. **Reloj de términos legales** sobre la instancia de `Workflow` (días hábiles CO, vencimiento, semáforo, escalamiento). Hoy `Api.Workflow` no tiene un solo `DateTimeOffset` fuera de `HistoryEntry.AtUtc`. En PQRSD **el plazo es el producto**. Esfuerzo M.
2. **`Bff.Gobierno`** cableando el CMS a `Workflow` + `Documents` + `Audit` + `Consent`, reemplazando `StubCaseWorkflowService`, que hoy duplica la máquina de estados dentro del CMS. Esfuerzo L.
3. **Notificación real con acuse de acceso certificado** (Ley 2080: la notificación electrónica surte efecto cuando el administrado accede, y la administración debe certificarlo). `Api.Messaging` ya tiene `POST /v1/messages/{id}/read` — es el germen correcto. Esfuerzo M.
4. **Kit ITA / Res. 1519**: menú de Transparencia con los 10 niveles precargados como schema + gate WCAG 2.1 AA + UI kit GOV.CO. Esfuerzo L.

Y una quinta que no es software y hay que empezar el mismo día: **resolver el requisito habilitante de experiencia** (entidad ancla pequeña — ESE, empresa de servicios públicos, descentralizada — o unión temporal con un integrador que ya tenga contratos públicos). Va en paralelo, no después.

**Por qué no los otros ocho:**

| Dominio | Por qué no |
|---|---|
| **Salud** | El RDA es obligatorio desde el 15-abr-2026 y hoy es 3-ago-2026: **llegamos tarde a una fecha que ya pasó**, con cero líneas de FHIR en el repo y un proceso de certificación de 8–15 semanas. El comprador cerró 332 instituciones en un semestre. Y el riesgo reputacional es asimétrico: un bug de carrito cuesta una venta, una fuga de PHI cuesta la empresa. |
| **Shop** | El motor (`PurchaseFlow`) es lo mejor del repo y el mercado es lo peor: Tiendanube en COP $24.900 abajo, Shopify al medio, VTEX arriba, y **20 procesos operando para 600 pedidos/mes** (§6.4). Los cinco P1 son de borde y hay un incumbente global que sube su techo editorial cada versión. |
| **Viajes** | El riesgo #1 **no tiene solución de ingeniería**: Booking.com pausó integraciones con nuevos proveedores de conectividad. Sin channel manager el alojamiento sobrevende y el overbooking se nos atribuye. Y el techo de precio ya lo puso LobbyPMS en COP $150.000/mes con PMS + channel + motor + POS + factura. |
| **Eventos** | **Habilitación binaria**: para vender boletería de artes escénicas en línea hay que ser operador autorizado por MinCultura con CIIU 7990. Eso se descubre en la implementación, no en la demo. Mercado de ~358 productores permanentes contra un modelo sectorial de gratis-para-el-organizador. (Es, eso sí, el **segundo** candidato: teatro/congreso es el otro dominio donde ambas mitades pesan.) |
| **Booking servicios** | Seis P1 que el propio informe dice que no bajan, y uno de ellos es la app móvil del profesional — que no es un sprint, es media plataforma. Enfrente, AgendaPro con USD 35M de Serie B, precio publicado en COP y terminal de pago propia. |
| **Realty** | **Es la tentación real y hay que nombrarla**: tres P1 cortos y el CMS *sí* es el argumento en el segmento constructora. Pero se vende por proyecto (sin recurrencia), el ciclo está atado al calendario de lanzamiento (4–8 meses), las iniciaciones cayeron 17,4%, no usa una sola de las 20 capacidades, y el producto —"la máquina de lanzar micrositios"— lo replica una agencia. **Recomendación: cobrarlo como servicio ahora si aparece la oportunidad; no montar la apuesta de producto ahí.** |
| **Academy** | Techo de precio puesto por Q10 en ~USD 57/mes, Moodle gratis y con evaluación, y **el DocType de curso no existe** (0 entre 252). Además el dolor del sector es caja, y ahí ya hay un jugador con US$53,2M. |
| **Social / Medios** | El único dominio donde el CMS es el argumento sin matices, y aun así: los tres P1 incluyen un XL (paywall + cobro recurrente), el comprador de medios está en contracción documentada, y el sub-segmento que sí paga (corporativo/gremial) es —por confesión del propio informe— **una hipótesis no validada con una sola conversación de venta**. |

---

## 4. La crítica del diferencial

### Dónde el CMS SÍ decide la compra

Sólo en tres formas, y ninguna es un dominio entero:

1. **Cuando el contenido *es* el producto** — Social/Medios. Único caso sin matices.
2. **Cuando el contenido *es* el canal de venta** — Realty-constructora (la campaña es el producto), Viajes-operador de experiencias (la web cambia de cara cuatro veces al año por estacionalidad), Eventos-teatro/congreso (diez páginas de contenido por cada página de checkout), Academy (el IETDH vende porque alguien googleó "curso de alturas Bogotá").
3. **Cuando el contenido *es* el cumplimiento** — Gobierno. Transparencia, publicación proactiva, accesibilidad y datos abiertos son literalmente gestión de contenido, y son la mitad de la obligación legal del dominio.

### Dónde no decide nada

- **Salud**: el CMS es *fuera de tema* y encima **agrega superficie de riesgo** — hay que explicarle al verificador de habilitación por qué el backoffice de autoría corre en el mismo proceso que el guard de PHI. La historia clínica es lo contrario de la libertad de composición: su estructura la fija la Res. 866 y su formato FHIR R4.
- **Shop**: quinto o sexto lugar en la lista de decisión del comprador, después de checkout, medios de pago colombianos, factura DIAN y guía. Peor: `productPriceBase` es un `Umbraco.TextBox` y `productVariantsJson` es un `TextArea` con JSON crudo — el schema del CMS **crea exactamente la segunda verdad que `Api.Catalog` se prohibió a sí mismo por escrito**.
- **Booking de servicios**: irrelevante para el 80% unipersonal / 68% informal del mercado, y no cierra la brecha que importa, que es móvil.
- **Realty-arriendo** (donde están los $27 billones) y **Viajes-hotel con PMS**: irrelevante.

### El veredicto sobre la promesa tal como está escrita

> *"Un CMS que robustece la creación de contenido estático porque da la libertad para hacerlo, y además la flexibilización de las interfaces y de las aplicaciones. La idea es podernos adaptar fácilmente a cualquier sistema de negocio."*

**En ninguno de los nueve informes la adaptabilidad fue el argumento que ganaba la venta. En cuatro es activamente contraproducente:**

- **Gobierno**: la flexibilidad no es un argumento en un pliego, es un riesgo. El comité evalúa "cumple la Res. 1519", no elegancia arquitectónica. Vender adaptabilidad a un comité es vender incertidumbre.
- **Booking**: quien evalúa AgendaPro contra "una plataforma que sirve para todo" elige AgendaPro, porque su problema es específico.
- **Salud**: vender flexibilidad donde la ley exige rigidez genera desconfianza, no interés.
- **Eventos masivos**: lo que se compra es capacidad de aguantar el on-sale.

Y hay un problema estructural, no de mensaje: **la promesa está escrita desde el lado de la oferta** ("podernos adaptar"). Ningún comprador de los nueve compra que *nosotros* nos adaptemos.

### Cómo reformularla

**No existe una promesa única que se sostenga en los nueve.** Buscarla es lo que produce la frase actual, que no le habla a nadie. Hay **dos promesas, para dos compradores distintos, y una ventaja interna que no se dice en voz alta.**

**Promesa A — la del ritmo de cambio (se sostiene en 7 de 9):**
> *"Lo que cambia por decisión de negocio lo cambia su equipo el mismo día, sin contrato de desarrollo y sin ventana de despliegue."*

Verdadera y demostrable en: Gobierno (el trámite cambia por acto administrativo, no por sprint), Realty (proyecto nuevo cada lanzamiento), Social (portada tres veces al día), Viajes (temporada), Eventos (se cae un artista, se abre una segunda fecha), Academy (programa nuevo), Shop (landing de campaña). Falsa en Salud. Débil en Booking.
La diferencia con la frase actual: **es un resultado del comprador, medible en un cronómetro delante de él**, no una propiedad de nuestra arquitectura.

**Promesa B — la de la conformidad como contenido (se sostiene en 7 de 9, y es la que cierra los tratos regulados):**
> *"Su obligación legal la mantiene su abogada en el backoffice, no su proveedor en un despliegue — y el sistema registra qué versión aceptó cada persona."*

Es la única frase del ejercicio donde **libertad editorial y rigor regulatorio son la misma feature en vez de opuestos**. Y no es aspiracional: `Api.Consent` ya exige `policyVersion` en cada otorgamiento, y el CMS ya versiona contenido. Aplica a: consentimiento informado (Salud), habeas data en el lead (Realty, Social, Academy), leyenda ESCNNA + RNT (Viajes), datos del vendedor del Art. 50 (Shop), menú de Transparencia (Gobierno), canales autorizados de venta (Eventos). Siete de nueve, con un solo puente de código.

**La ventaja interna que NO es promesa de venta:** el árbol de 20 capacidades agnósticas no gana ninguna reunión —ningún comprador de los nueve preguntó por él— pero **es lo que hace que el noveno vertical cueste una fracción del primero**. Es una ventaja de costo de producción, no de producto. Confundir las dos cosas es exactamente lo que produjo la frase actual. Regla operativa: *lo que nos hace ganar plata no es lo que nos hace ganar la reunión, y no se dice en la misma sala.*

---

## 5. Capacidades nuevas propuestas

Los nueve informes propusieron entre todos ~14 capacidades nuevas. Aplicando el filtro estricto (**puede decir NO sola** Y **es dueña de su almacén**; lo que no tiene almacén es un tipo, no un servicio):

### Se construye: **una.**

**`Synergos.Api.Invoicing` — documento fiscal electrónico.**
- **¿Puede decir NO sola?** Sí, y con rechazos que sólo ella conoce: `numbering_range_exhausted`, `range_not_authorized_for_subject`, `document_not_conformant`, `already_invoiced` (idempotencia), `receiver_tax_id_invalid`, `credit_note_exceeds_invoice`.
- **¿Es dueña de su almacén?** Sí, y es un almacén que **nadie más puede tener**: rangos de numeración autorizados por resolución (con vigencia y cupo), el correlativo consumido, el estado de transmisión y su cola de reintento, el CUFE y el XML firmado, y las notas crédito ligadas al documento original. Un correlativo es un **recurso escaso con estado**, y perderlo es un problema fiscal, no un bug.
- **¿Por qué no es "sólo un adaptador"?** El proveedor tecnológico autorizado es el transporte. La numeración no. Eventos lo prueba: la Res. 2890/2017 exige prefijo `EP` + rango autorizado **por cada evento**.
- **Alcance:** 7 de 9 dominios.
- **Lo que NO va adentro:** la firma XAdES (integración externa), el almacenamiento del PDF de cortesía (`Api.Documents`), y el disparo en el orden correcto (el BFF).

### Pasan el filtro pero NO se construyen ahora

| Propuesta | Veredicto del filtro | Por qué no ahora |
|---|---|---|
| **`Api.Assessment`** (ítems, intentos, calificación) | **Pasa.** Dice NO sola (intentos agotados, fuera de plazo, examen cerrado, pregunta inexistente) y su almacén —banco de ítems, intentos, notas— no es de nadie más. | Sirve a **1 dominio**, y ese dominio (Academy) tiene el techo de precio más bajo de los nueve y a Moodle gratis enfrente. Capacidad correcta, momento equivocado. Se construye el día que Academy sea el vertical, no antes. |
| **`Api.ClinicalExchange`** (FHIR R4 / RDA) y **servidor de terminologías** (CIE-10/CUPS/IUM con vigencias) | **Pasan las dos.** Bundle no conforme / código no vigente son rechazos propios; los almacenes (mapeos, versiones de perfil, cola de reintentos; vigencias terminológicas) no caben en ninguna existente — y en particular **no** en `Api.Catalog`, que es índice de lo publicable: una terminología no se publica, se versiona y se vence. | 1 dominio, y es el dominio de §3 que recomiendo no atacar por llegada tardía. Si alguna vez se entra a Salud, se entra por acá (conector RDA/RIPS como producto independiente) y no por el EHR. |

### Rechazadas — son features, tipos o integraciones

| Propuesta | Veredicto | Por qué |
|---|---|---|
| Redención de ticket / voucher en puerta | **Feature de `Api.Signing`** | Cero `redeem|nonce|single-use` en `Api.Signing` **[verificado]** — el hueco es real. Pero una firma redimida cambia el resultado de `Verify`, que es de Signing; el ledger es del mismo objeto. Capacidad aparte sería partir un agregado. |
| Calendario ARI / tarifa por fecha | **Feature de `Api.Pricing`** | Mismo dueño de "cuánto vale", mismo almacén. |
| RMA / devoluciones con reloj de retracto | **Definición de `Api.Workflow` + BFF + calendario de `Core`** | `Api.Workflow` ya es la máquina de estados definida como dato. Un `Api.Returns` sería duplicar Workflow con otro nombre — el error caro de esta arquitectura. |
| Reversión de pago / disputa / contracargo | **Feature de `Api.Payments`** (estados `Disputed`/`ChargedBack` + evento) | Mismo almacén de pagos. Es un estado que falta, no un servicio. |
| Recaudo recurrente / suscripción | **Feature de `Orders` + `Payments`** — *con reserva* | Una suscripción es un pedido que se repite. **Pero** si aparece firme en 3+ dominios (Social-paywall, Academy-cuotas, Booking-membresías) hay que reabrir la discusión: "próximo ciclo, método guardado, ciclo ya cobrado" empieza a oler a almacén propio. Hoy: feature. |
| Screening SAGRILAFT / listas restrictivas | **Integración externa** | El "no" lo da el tercero, no nosotros. |
| Contabilidad de arriendo, causación, retenciones | **Fuera del producto** | No es una ola: es otro producto, con auditores y actualización normativa anual. SIMI lleva desde 1992. |
| Ad serving / GAM | **Schema del CMS + integración** | |
| Cohortes, asistencia, calendario académico | **Posponer** | Dudoso contra el filtro (¿es Booking con otro nombre?). No decidir hasta que Academy sea el vertical. |
| Comisiones por profesional | **Feature del BFF** | Es un cálculo sobre reservas y pedidos ya existentes. |
| Analítica de lectura / pageview | **Feature de `Api.Sessions`** (ampliar la forma de ingesta) | No una capacidad #21. |
| Grafo social (seguir/bloquear) | **Feature de `Api.Identity` o del CMS** | 1 dominio. |
| Cola virtual / anti-bot | **Infraestructura**, no capacidad | No tiene reglas de negocio propias. |

**Balance: de 14 propuestas, 1 se construye.** Ese ratio es el punto: nueve investigadores mirando su dominio produjeron nueve listas de capacidades; el filtro atómico aplicado en frío deja una. Si esto se hubiera hecho por dominio, el árbol tendría 34 capacidades en un año.

---

## 6. Lo que nadie miró

### 6.1. Cinco reimplementaciones del mismo error, y ningún informe lo vio como un patrón

**[Verificado en esta síntesis]**: la única referencia a `Synergos.Api.*` en todo el CMS (`Web` + `Application` + `Interfaces`) son dos líneas, ambas a `Sessions` — un comentario y un HTTP client. Los `ProjectReference` de `CMS.Web` son dos: `Application` e `Interfaces`.

Y cada informe documentó, por su lado, un duplicado distinto:

| Duplicado en el CMS | Capacidad que ya existe | Lo reportó |
|---|---|---|
| `StubCaseWorkflowService` (máquina `Radicado→EnRevision→Subsanacion→Resuelto`) | `Api.Workflow` | Gobierno |
| `CommentsController` + `CommentsModerationController` + `FileSystemCommentRepository` | `Api.Engagement` + `Api.Moderation` | Social |
| `BookingController` (561 líneas, hoteles, con seams stub locales) | `Api.Booking` | Booking, Viajes |
| `StubEnrollmentService` (orden y compensación de matrícula, sin saga) | `Bff.Core` | Academy |
| `WompiPaymentProvider` sobre **otra** `IPaymentProvider` | `Api.Payments` | Shop |
| `StubSavedSearchService`, `StubSocialGraphService`, `StubMessagingService`, `StubApplicationService` | `Api.Catalog`, `Api.Identity`, `Api.Messaging`, `Api.Orders` | Realty, Social, Gobierno |

**La decisión que nadie tomó:** ¿el CMS llama al árbol por HTTP, o el árbol es un producto separado y el CMS se queda con sus stubs? Hoy se paga el costo de las dos opciones y se cobra el beneficio de ninguna: se mantienen dos implementaciones de siete cosas, y ninguna venta usa el árbol. Es la decisión arquitectónica más cara que está pendiente, no está en ningún ADR citado por los nueve informes, y **es prerequisito de los pasos 3 y 6 de §2**.

### 6.2. El árbol se diseñó desde el negocio, no desde la persona — y eso rompe habeas data

**[Verificado]**: `EngagementService` expone `ListForTarget` y no `ListForActor`. `IReservationStore` expone `ForResource` y no índice por `For`. `Api.Audit.CheckQuery` exige filtro. Cada informe lo reportó como un hueco local; **es un sesgo sistemático**: el árbol sabe contestar "todo lo de este recurso" y no sabe contestar "todo lo mío".

La primera consecuencia la vieron seis informes: la pantalla "mis citas / mis pedidos / mis inmuebles guardados / mis entradas" no se puede construir sobre el árbol, y esa es la pantalla de retención de seis dominios.

**La segunda no la vio nadie, y es peor.** El `Ref(Kind, Id)` es opaco *por diseño* y ninguna capacidad ramifica sobre el Kind. Eso significa que **es estructuralmente imposible responder "dame todo lo de este titular a través de las 20 capacidades"** — que es exactamente lo que exigen el derecho de consulta y el de supresión de la Ley 1581, y lo que un requerimiento de la SIC pide por escrito. `Api.Consent` tiene `POST /v1/grants/forget`, que revoca el consentimiento sin destruir la prueba — está bien hecho — **pero después de revocar, nada en el sistema puede ir a buscar los datos.** Aplica a los nueve dominios, aparece en cero informes, y en Social y Salud es sanción de 1 a 2.000 SMLMV.

*Implicación:* hace falta un índice de sujeto transversal (un registro de "qué capacidades tocaron a este `Ref`"), y su lugar natural es `Bff.Core` o una vista construida sobre `Api.Audit` — **no** una capacidad nueva.

### 6.3. Ocho informes recomiendan conectar un PSP. Hay dos seams de pago incompatibles.

`Api.Payments` usa `IPaymentProvider` con `Authorize`/`Capture`/`Refund`/`Void`. El CMS usa **otra** `IPaymentProvider` (en `Synergos.CMS.Interfaces`) con `CreateSessionAsync`/`GetStatusAsync`/`RefundAsync`, y ahí vive el `WompiPaymentProvider` real, con firma de integridad, ventana anti-replay y *mark-after-confirm*. Sólo el informe de Shop lo vio; los otros siete escribieron "conectar Wompi" en su P1 como si fuera una tarea.

No es una tarea: **es la decisión de a cuál motor le llega la plata**, y de ella depende si el árbol participa del negocio o es decoración. Además: `Api.Payments.Capture(string id, IdempotencyKey key)` captura **el monto autorizado completo** **[verificado]** — sin captura parcial no hay anticipo+saldo (Viajes), ni cuotas (Academy), ni captura noche a noche. El seam del CMS ya tenía documentado ese mismo hueco; **se replicó en la capacidad**. Nadie cruzó las dos observaciones.

### 6.4. El costo de operación no lo sumó nadie, y por sí solo descarta tres dominios

Sólo Shop lo planteó, y para su dominio: 20 capacidades + 3 BFF + el CMS, cada una con su almacén, su despliegue, su monitoreo, su backup y su guardia — para una marca de 600 pedidos/mes que no tiene a nadie que se levante a las 3 a.m.

Cruzado con los techos de precio verificados en los otros informes:

| Dominio | Techo de precio verificado del sector |
|---|---|
| Viajes | LobbyPMS **COP $150.000/mes**, sin permanencia, con PMS + channel manager + motor + POS + factura electrónica |
| Academy | Q10 ~**USD 57/mes** (300 alumnos), con SNIES nativo |
| Booking | AgendaPro desde **COP $50.000/mes** |
| Realty | Wasi / CRMs locales **COP $120k–400k/mes** |
| Shop | Tiendanube desde **COP $24.900/mes**, 0% de comisión |

**Ninguno de esos techos paga la operación de 24 procesos.** La conclusión de cruce es más dura que cualquiera de los nueve informes: en Viajes, Academy, Booking y Shop el producto **no es vendible como SaaS de bajo ticket, independientemente de las features**. O se vende con operación gestionada —y entonces el negocio es hosting administrado con márgenes de hosting administrado— o se vende arriba, donde el ciclo es de meses. Los backlogs de esos cuatro dominios están resolviendo el problema equivocado.

### 6.5. Las dos mitades del producto se venden a compradores distintos y no se refuerzan

Mapeado sobre los nueve:

| | **El árbol pesa** | **El árbol casi no participa** |
|---|---|---|
| **El CMS decide la compra** | **Gobierno** (expediente + publicación obligatoria)<br>**Eventos-teatro/congreso** (boletería + marca) | Social/Medios (usa 1 de 20 vía CMS)<br>Realty-constructora (usa 0)<br>Viajes-operador (usa el motor, no el CMS) |
| **El CMS es extra o irrelevante** | Salud, Shop, Booking, Viajes-alojamiento, Academy | Realty-arriendo |

**Sólo dos casillas tienen las dos mitades a la vez.** Ese cruce —que ningún informe individual podía ver— es, por sí solo, el argumento de §3: Gobierno no es el mercado más grande ni el más rico; es el único donde lo construido en las dos mitades apunta al mismo comprador. Y Eventos-teatro/congreso es el segundo, con la habilitación de MinCultura como única barrera dura.

Corolario incómodo: **fuera de esas dos casillas, Synergos es dos productos.** Si eso no se decide explícitamente, cada venta va a arrastrar la mitad que no le sirve al comprador y va a pagar su costo de operación.

### 6.6. Cinco informes pidieron el mismo calendario de días hábiles y ninguno lo propuso como pieza compartida

Gobierno lo puso como P1 ("días hábiles colombianos con festivos"), Booking como P1 ("18 festivos"), Shop en el reloj del retracto, Eventos en el SLA de reversión, Salud en los 22 días de radicación, Viajes en temporadas. Cinco esfuerzos estimados por separado para **una constante y una función pura de ~80 líneas**, que no tiene almacén y por lo tanto no es capacidad. Es el ejemplo canónico de por qué existe esta fase de síntesis, y el más barato de cobrar.

### 6.7. Cuatro regímenes legales distintos apuntan a `Api.Notifications` y ningún informe pidió los cuatro

| Régimen | Qué exige | Quién lo reportó |
|---|---|---|
| Ley 2300/2023 | Ventana horaria, 1/día/canal, no más de un canal por semana | Booking, Social |
| Ley 2080/2021 (CPACA) | Acuse de **acceso** certificado, no de envío | Gobierno |
| Ley 820/2003 | Aviso por servicio postal autorizado o mecanismo pactado | Realty |
| Ley 1480 Art. 50 | Constancia de PQR con fecha y seguimiento | Shop |

Hoy `Api.Notifications` tiene un tope de 20 envíos/hora/destinatario y un `LoggingNotificationSender`. Vistos juntos, lo que necesita es: **programación + ventana legal + acuse de acceso + canal certificado**. Ningún informe pidió los cuatro; el paso 2 de §2 los agrupa porque son la misma capacidad.

### 6.8. Dos informes afirmaron un hecho falso sobre el código

Realty ("`Api.Documents` guarda ficheros con versión y retención") y Social ("adjuntos con URL firmada y retención") están equivocados: **grep de `Retention` sobre `Api.Documents/` y `Api.Audit/` devuelve cero** **[verificado]**. Salud lo dijo bien y lo dijo primero. Ambos errores parecen venir de leer `docs/product/07-diseno-atomico-capacidades.md` y `08-despiece-apis.md` como fuente. Viajes detectó, por su lado, que la matriz del doc 08 no asigna `Consent` a Viajes siendo que corresponde.

**Regla para la siguiente fase: la documentación de producto de este repo no es fuente de verdad sobre el código; está desactualizada al menos en tres puntos.** Y hay una tarea barata que sale de acá: reconciliar el doc 07/08 con el árbol real antes de que alguien planifique sobre él otra vez.

### 6.9. Nadie miró el CMS como pasivo regulatorio

Salud lo planteó (PHI y backoffice de autoría en el mismo proceso; almacén cifrado "baseline, NO HIPAA-grade"; single-instance sin lock distribuido). Aplica igual en Gobierno (expediente y autoría en la misma app), Realty (leads con datos personales), Social y Academy (RNBD, datos de menores). Ningún informe propuso la segregación de despliegue que eso implica, y es una pregunta que va a aparecer en el primer pliego serio o en la primera due diligence.

---

## 7. Calidad de la evidencia

### Por informe

| Informe | Fortaleza | Debilidad que importa |
|---|---|---|
| **Gobierno** | **El mejor en evidencia comercial de los nueve.** Diez contratos individuales de SECOP con entidad, objeto, valor y fecha — es la **única** evidencia de gasto real del comprador en todo el ejercicio. Normativa citada con URL. | La incógnita que él mismo declara es la que decide su conclusión: si Mi Colombia Digital sigue vigente. Si se desfinanció, su §6 pasa de "extra" a "argumento". |
| **Eventos** | Normativa citada por artículo (Res. 2890/2017 arts. 2/4/7), cifras PULEP oficiales, pliego de cargos de la SIC con número de resolución. Verificó la ausencia de redención con grep sobre cuatro árboles. | No pudo enumerar los operadores autorizados por MinCultura — **el dato que decide si el riesgo #2 es barrera o trámite**. Y la base de boletería (~$970 mil millones) es una derivación propia, bien marcada. |
| **Shop** | Fetch directo de las páginas oficiales, incluido el hallazgo que sostiene todo el pitch: **Colombia no está en Shopify Payments**. Encontró el defecto de precio cero por línea leyendo el código, no la doc. | El tamaño del segmento objetivo (marcas D2C colombianas) es cualitativo. El precio de Umbraco Commerce —el competidor de la casa— no es público. |
| **Viajes** | 31 verificadas, ocho exigencias legales con URL, y el riesgo #1 (pausa de conectividad de Booking.com) verificado en el Partner Hub oficial. | El eje del ahorro que vende —la comisión real de Booking en Colombia— **no lo pudo verificar** (viene de una consultora). Y no sabe si el SIAT/SIRE exponen API, que es la diferencia entre una integración de una semana y un scraper. |
| **Realty** | Cifras Camacol y Fedelonjas sólidas; 16 verificaciones de código leyendo fichero. | **El ancla de precio de todo su pitch —cuánto cobra una agencia por un micrositio de proyecto— es justamente lo que no tiene.** Su demo cierra con "multiplicá por lo que le paga a la agencia" y no hay número. Se resuelve con tres llamadas. Además, error de hecho en retención (§6.8). |
| **Salud** | **El mejor en verificación de código**: leyó completos catorce ficheros de dominio y endpoints. El análisis de `AppointmentFlow` (la mutación `VoidPayment → RefundPayment` al capturar) es el mejor pasaje técnico de los nueve. | **El hecho más consecuente es el peor sostenido**: la Circular 019/2026 (migración MIPRES→RDA) la conoce sólo por un blog de proveedor, y no pudo extraer texto de la Res. 1888. Y no pudo averiguar cuántas IPS ya certificaron — el dato que decide si el mercado se cerró. |
| **Academy** | Buena normativa (contenido del certificado de aptitud ocupacional, Res. 4272/2021, exclusión subjetiva de IVA). Verificó la ausencia de `coursePage` contando los 252 ContentTypes. | El precio de Q10 —el techo que descarta el dominio— es de un tercero; las páginas del proveedor dan 404. Y su propia mejor recomendación es que no investigó lo que importaba: si los IETDH compran software o siguen en Excel. |
| **Social** | Datos de mercado publicitario (IAB) y Reuters Institute sólidos y bien usados. Verificó por grep que el CMS sólo consume `Api.Sessions`. | **Todos los rangos de gasto en COP de su §1 son inferidos**, y la mitigación central del informe (el comprador corporativo/gremial) está declarada como no validada con una sola conversación. Error de hecho en retención (§6.8). |
| **Booking servicios** | Análisis de código impecable (once verificaciones, cada una con el fichero abierto), y honestidad ejemplar sobre sus propias debilidades. | **El más flojo en fundamento comercial.** El eje entero del pitch —el peaje del 20%/30% de Fresha y Booksy— viene de blogs de terceros porque no pudo abrir ninguna página oficial de precios. Y las tasas de no-show, que son el argumento del depósito, salen de blogs de vendedores de software. **Su §7 y §8 no son accionables como material comercial hasta verificar eso.** |

### Patrones transversales de calidad

1. **La evidencia de código es uniformemente la más sólida.** Los nueve abrieron ficheros y las afirmaciones que verifiqué en esta síntesis (proveedores stub, `Money.Zero`, ausencia de redención, ausencia de scheduling, ausencia de `ListForActor`, ausencia de `coursePage`, ausencia de calendario hábil, ausencia de DIAN, sólo dos BFF, sólo Sessions consumida) **resultaron todas correctas**. Las dos excepciones (§6.8) vienen de creerle a la documentación de producto en vez de al código.

2. **La evidencia de precio de la competencia es sistemáticamente débil y sesgada a la baja.** Casi todo precio verificado viene de la página del propio vendedor —es decir, el precio de entrada publicado, no el ticket real— y los competidores que de verdad importan no publican precio: Zeus, SIMI, SINCO, Hosvital, Servinte, Piano, Poool, Umbraco Commerce, PrintTicket, VTEX, UBITS, Q10, Whatoko/ITACA/K2B. **No hay un solo dominio de los nueve donde sepamos el ticket real contra el que competiríamos.** Por eso todas las secciones §8 de riesgo de precio son cualitativas, incluida la mía en §6.4.

3. **Nadie habló con un comprador. Cero entrevistas en nueve informes.** Todos los §1 —quién firma, cuánto gasta, qué le duele— son inferencia desde documentos públicos. El informe de Academy lo dice mejor que yo: *"valen más diez llamadas a rectores de IETDH que otro mes de features"*. Es la recomendación más importante que sale del conjunto, y aplica a los nueve.

4. **Tamaño de universo ≠ mercado direccionable, y casi todos lo advirtieron.** 36.000 salones (80% unipersonales), 112.582 RNT (sin discriminar anfitrión de operador), 10.839 IPS, 1.100 municipios (88% categoría 6), 4.097 IETDH. En los cinco casos el informe correspondiente hizo el descuento honestamente. Los únicos números de **flujo de dinero** verificados son los de Gobierno (contratos SECOP), Eventos (recaudo parafiscal PULEP), Shop (CCCE), Realty (Camacol/Fedelonjas) y Social (IAB) — y sólo el de Gobierno es gasto *en software*, contrato por contrato.

5. **Las secciones §7 (demos) son diseño, no evidencia**, y varias describen un demo que hoy no se puede hacer. Salud lo declara ("hoy este demo no existe, le falta el minuto 4"), Eventos lo declara (el minuto de la redención), Shop lo resuelve con honestidad en escena. Los demás no lo declaran y deberían: **ningún demo de los nueve es ejecutable hoy de punta a punta, porque el borde no cobra y no notifica en ninguno.**