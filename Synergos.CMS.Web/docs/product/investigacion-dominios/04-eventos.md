## 1. El comprador

No es "el organizador de eventos". Es, en orden de probabilidad de cierre:

**A. El productor permanente / venue con programación propia.** VERIFICADO: en el PULEP hay **3.973 productores registrados, de los cuales 91% son ocasionales y 9% permanentes** ([Valora Analitik, dic-2025](https://www.valoraanalitik.com/entre-enero-y-noviembre-se-realizaron-mas-de-24-000-espectaculos-en-colombia-estos-fueron-los-de-mayor-recaudo/)). INFERIDO: eso deja **~358 productores permanentes** en todo el país — el universo real al que se le puede vender software recurrente. Sumale los venues: VERIFICADO, **34 salas del Programa Distrital de Salas Concertadas de Bogotá 2026** ([bogota.gov.co](https://bogota.gov.co/mi-ciudad/cultura-recreacion-y-deporte/34-salas-del-programa-distrital-de-salas-concertadas-bogota-2026)).

**B. El congreso / feria / evento corporativo B2B** (Corferias, gremios, universidades, farmacéuticas). No tiene aforo numerado ni reventa; tiene acreditación, agenda, patrocinadores y una web que ES el producto. INFERIDO: es donde el diferencial CMS pesa más y donde el incumbente de boletería no compite.

**C. El club de fútbol / estadio.** VERIFICADO: el Decreto 1622 de 2022 obliga a atar boleta ↔ documento de identidad y crear un Sistema Nacional de Validación de ingreso ([Función Pública](https://www.funcionpublica.gov.co/eva/gestornormativo/norma.php?i=192812)). Comprador con presupuesto y obligación legal, pero ciclo político y de seguridad muy largo.

**Quién firma el cheque:** en A, el gerente/productor general o el director de la sala (no hay CTO). En B, el director de mercadeo o el gerente de la unidad de eventos. En C, el gerente del club y la secretaría de gobierno municipal. En ningún caso firma un área de TI — lo que implica que el argumento tiene que ser económico y operativo, no arquitectónico.

**El dolor por el que ya paga hoy:** el cargo por servicio. VERIFICADO: en Colombia el cargo por servicio de boletería está **entre 10% y 15% del valor de la boleta** ([El Tiempo](https://www.eltiempo.com/economia/finanzas-personales/que-le-cobran-en-el-cargo-por-servicio-cuando-compra-una-boleta-en-colombia-815191)). Ese dinero hoy se lo lleva la ticketera, no el productor. El segundo dolor es que **el comprador es de la ticketera, no del productor**: el productor no tiene la base de datos de su propia audiencia.

**Cuánto se mueve.** VERIFICADO: entre enero y noviembre de 2025 se registraron **24.389 espectáculos** en PULEP (12.440 teatro, 8.844 música, 2.010 danza, 897 circo, 198 magia), con un recaudo de contribución parafiscal de **$97.019 millones** en el período y **$486.034 millones** acumulados; por ciudad, Bogotá $50.280 millones, Medellín $19.233 millones, Cali $6.286 millones; el evento top fue Bad Bunny en Medellín con $6.853 millones ([misma fuente](https://www.valoraanalitik.com/entre-enero-y-noviembre-se-realizaron-mas-de-24-000-espectaculos-en-colombia-estos-fueron-los-de-mayor-recaudo/)). INFERIDO, y con cuidado: como la contribución es el 10% de la boletería de entradas de ≥3 UVT (Ley 1493), un recaudo de $97.019 millones implica una **base de boletería del orden de $970 mil millones COP/año solo en artes escénicas** — sin fútbol, sin cine, sin las boletas baratas. Es un mercado real; el problema es que el software que lo mueve hoy se regala.

---

## 2. La competencia

| Competidor real | Qué hace bien | Dónde deja el hueco | Fuente |
|---|---|---|---|
| **TuBoleta** (Ticket Fast S.A.S.) | Domina el mercado; **acuerdo exclusivo con Movistar Arena**; red de puntos físicos y call center | La SIC le formuló **pliego de cargos (Res. 38063 de 2026)** por fallas en el deber de información, incumplir el aviso a la SIC en 3 días hábiles ante cancelaciones y **dos cláusulas abusivas**. El productor no controla la marca, ni la devolución, ni el dato del comprador | [Forbes Colombia](https://forbes.co/2025/03/17/negocios/ticketmaster-llega-a-colombia-a-competir-con-tuboleta-y-eticket), [SIC](https://sedeelectronica.sic.gov.co/comunicado/la-sic-del-cambio-formula-pliego-de-cargos-tuboleta-por-presuntas-fallas-en-el-deber-de-informacion-las-clausulas-abusivas-y-las-reglas) |
| **Ticketmaster Colombia** | Entró en 2025, **compró el 51% de La Tiquetera** y en julio de 2026 completó la integración; eticket desaparece. Promete precio fijo (sin tarifa dinámica) y anti-bots | Va por arenas y grandes promotores. El teatro de 400 sillas, la feria gremial y el congreso universitario no son su cliente — y su modelo se lleva la relación con el comprador | [Forbes](https://forbes.co/2025/05/08/editors-picks/ticketmaster-en-colombia/), [El Colombiano](https://www.elcolombiano.com/negocios/ticketmaster-culmina-integracion-tiquetera-cambiara-boletas-colombia-PL39326494) |
| **Passline** (chilena, opera en CO) | Autogestión total: el organizador crea el evento, personaliza, vende y controla acceso. **Gratis para el organizador** — cobra cargo al comprador. ~9.000 eventos/mes | Es *su* plantilla, no *tu* sitio: cero capa editorial real, cero multi-marca por hostname, cero SSR de marca. Y nada específico de PULEP / parafiscal / factura de espectáculos | [home.passline.com](https://home.passline.com/que-es-passline) |
| **Eventbrite** | Descubrimiento + self-serve. **6,99% + IVA** por pago procesado | Sin mapa de asientos serio, sin cumplimiento colombiano, sin cara de organizador operativa en puerta | [Eventbrite ayuda](https://www.eventbrite.com.ar/help/es-ar/articles/755615/costo-por-usar-eventbrite-para-los-organizadores/) |
| **PrintTicket** (Colombia) | Marca blanca colombiana: venta, administración y métricas en un panel | Producto cerrado y monolítico: no hay capa de contenido ni capacidades reutilizables para el resto del negocio del cliente | [printticket.com.co](https://www.printticket.com.co/) |
| **WS Ticketing S.A.S. / eTicketaBlanca** | Opera como operador de boletería **o como marca blanca** para emprendedores de eventos | Ídem: es servicio, no plataforma que el cliente controle | [eticketablanca.com](https://www.eticketablanca.com/terminos-y-condiciones/) |
| **vivenu** | Ticketing API-first, white-label puro, escala enterprise | No opera en Colombia ni cumple PULEP/DIAN. Si el cliente colombiano lo quiere, alguien tiene que construirle la capa local — eso es el hueco | [G2](https://www.g2.com/products/vivenu-ticketing-platform/reviews) |
| **Spektrix / AudienceView-OvationTix / Tessitura** | Ticketing + CRM + fundraising para teatros y artes escénicas. Es exactamente "CMS+boletería" bien hecho | Mercado anglo, precio enterprise, sin español-CO ni normativa local. Es la prueba de que el concepto vende — y de que nadie lo ofrece acá | [AudienceView](https://audienceview.com/thought-leadership/best-event-ticketing-platforms-for-theaters-2026-guide/), [Spektrix](https://www.spektrix.com/en-gb/compare-spektrix-audienceview-unlimited-professional-campus) |

---

## 3. Lo que la ley obliga

Este es, de los nueve dominios, uno de los de **carga regulatoria más alta y más específica de Colombia**. No es opcional y no se resuelve con una feature genérica.

| Exigencia | Jurisdicción | Traducida a capacidad o feature | Fuente |
|---|---|---|---|
| Registrar **cada evento** en PULEP y obtener un **Código Único del Evento** antes de pedir permiso municipal | Colombia — MinCultura, Res. 2890/2017 art. 4 | `schema-del-cms`: campo `eventPulepCode` obligatorio en `eventPage`, propagado a la boleta y a la factura | [Res. 2890 de 2017](https://normograma.mincultura.gov.co/compilacion/docs/resolucion_mincultura_2890_2017.htm) |
| La boleta electrónica debe llevar como **campos individuales** en el XML UBL para DIAN: **Código PULEP, Nombre del evento, Localidad, Cortesía** | Colombia — Res. 2890/2017 art. 7 | `integracion-externa` + `schema-del-cms`: emisor de factura electrónica con anexo de espectáculos; "cortesía" pasa a ser un estado de primera clase del ticket | ídem |
| Prefijo de numeración **"EP" + 2 alfanuméricos por evento**, con rango de numeración autorizado por DIAN **por cada evento** | Colombia — Res. 2890/2017 art. 7 | `capacidad-nueva` o `integracion-externa`: gestor de rangos de numeración por evento. Ninguna capacidad actual numera facturas | ídem |
| El **operador de boletería en línea** debe tener el código CIIU **7990** como actividad principal o secundaria en el RUT y estar autorizado por MinCultura | Colombia — Res. 2890/2017 art. 2 | **No es feature: es habilitación.** O el cliente ya es operador autorizado, o Synergos tiene que serlo. Define el go-to-market | ídem, y el [listado público de operadores](https://pulepapp.mincultura.gov.co/Informespublicos/operadores) |
| **Contribución parafiscal del 10%** sobre la boletería de espectáculos de artes escénicas cuyo precio individual sea **≥ 3 UVT**; declaración bimestral (productores permanentes) o dentro de los 5 días hábiles siguientes (ocasionales) | Colombia — Ley 1493 de 2011 | `feature-del-bff`: liquidador por boleta con umbral (3 UVT × **$52.374 = $157.122** en 2026) + reporte declarable por evento. **`Api.Pricing` no lo cubre**: su impuesto es una tasa en basis points sobre la base, no una contribución con umbral por unidad, destinatario distinto y periodicidad propia | [Ley 1493](https://www.funcionpublica.gov.co/eva/gestornormativo/norma.php?i=45246), [UVT 2026 = $52.374, Res. DIAN 000238/2025](https://valoruvt.com/valor-uvt-2026-colombia/) |
| Los espectáculos públicos de artes escénicas están **excluidos de IVA** | Colombia — Ley 1493 de 2011 | Configuración de impuesto por tipo de evento, no global | [Ley 1493](https://www.funcionpublica.gov.co/eva/gestornormativo/norma.php?i=45246) |
| Fútbol profesional: **boleta atada al documento de identidad**, verificación de antecedentes previa a la compra y bloqueo de ingreso a sancionados, vía Sistema Nacional de Validación | Colombia — Decreto 1622 de 2022 | `integracion-externa` + `capacidad-nueva`: boleta nominativa y consulta de listas de restricción. **Nada en `Synergos.Api.*` lo modela** | [Decreto 1622](https://www.funcionpublica.gov.co/eva/gestornormativo/norma.php?i=192812) |
| Ante cancelación o reprogramación, informar a la **SIC dentro de los 3 días hábiles** el procedimiento de devolución/canje | Colombia — Estatuto del Consumidor; es uno de los cargos vivos contra TuBoleta | `feature-del-bff`: flujo de cancelación masiva con notificación regulatoria y bitácora en `Api.Audit` | [SIC](https://sedeelectronica.sic.gov.co/comunicado/la-sic-del-cambio-formula-pliego-de-cargos-tuboleta-por-presuntas-fallas-en-el-deber-de-informacion-las-clausulas-abusivas-y-las-reglas) |
| **Reversión del pago** en compras electrónicas: **15 días hábiles** desde la solicitud | Colombia — Ley 1480 art. 51 + Decreto 587 de 2016 | `feature-del-bff`: ruta de reversión distinta del refund comercial, con SLA. `Api.Payments` sabe devolver, pero nadie ordena ni cronometra la reversión | [Decreto 587](https://www.funcionpublica.gov.co/eva/gestornormativo/norma.php?i=69037) |
| Deber de información: municipio del evento, promotor, horarios y **canales autorizados de venta** visibles al comprador | Colombia — cargo SIC 2026 | `schema-del-cms`: campos obligatorios en `eventPage` y bloque de "canales autorizados". Es literalmente lo que la SIC le está reprochando al líder del mercado | [El Tiempo](https://www.eltiempo.com/economia/empresas/sic-formula-cargos-a-tuboleta-por-presuntas-fallas-en-informacion-comercio-electronico-y-devoluciones-3562032) |
| Reventa: **no está tipificada ni regulada** en general (libertad de precios); excepción, eventos deportivos vía Decreto 1622 | Colombia | No hay obligación que cumplir hoy; sí una oportunidad (reventa oficial trazable) | [El Tiempo](https://www.eltiempo.com/economia/es-legal-en-colombia-la-reventa-de-boletas-para-espectaculos-818538) |
| **Riesgo pendiente, no ley:** el proyecto de reforma tributaria radicado en 2026 grava con 19% las boletas de espectáculos por encima de ~10 UVT | Colombia — proyecto, **no aprobado a la fecha de este informe** | Motivo para que el impuesto sea configurable por evento y por umbral, no cableado | [Infobae](https://www.infobae.com/colombia/2026/07/21/gobierno-petro-radico-una-nueva-reforma-tributaria-para-recaudar-219-billones-en-2027-cuales-impuestos-incluye/), [La República](https://www.larepublica.co/especiales/tributaria-2-0/entradas-para-conciertos-y-eventos-deportivos-tambien-asumiran-iva-con-la-tributaria-4214749) |

---

## 4. Ajuste contra lo construido

### Ya cubierto

- **Aforo por zona y mapa de asientos con coordenadas → `Synergos.Api.Inventory`.** Leí `Domain/StockItem.cs`, `Domain/InventoryRules.cs` y `Endpoints/InventoryEndpoints.cs`. `StockUnit(Code, Row, Column)` es literalmente la butaca; `CheckUnits` distingue `unit_not_found` (elegiste una butaca que no está en el mapa) de `unit_taken` (perdiste la carrera), que es la distinción que importa en una venta de teatro. `Available/Held/TakenUnits` sostienen el "quedan N" sin recalcular. Holds con TTL (default 15 min, máx 4 h) y `CheckAdjust` que impide bajar el aforo por debajo de lo ya apartado. Y `GET /v1/items?subjectKind&subjectId` permite llegar al stock desde el `Ref` del tier sin mapa aparte.
- **Funciones con horario → `Synergos.Api.Booking`.** `Domain/BookingRules.cs`: `CheckOpeningHours` con huso por recurso, `CheckCapacity` contando ventanas **solapadas** (no reservas del día), `CheckNotInThePast`, `CheckHoldIsUsable` y `CheckCancellable` contra una `CancellationPolicy` de antelación mínima. Sirve para "función de las 20:00 del jueves" sin tocar nada.
- **Entrada con código verificable → `Synergos.Api.Signing`.** `Domain/SigningRules.cs`: HMAC-SHA256, `keyId` y vencimiento **dentro** de lo firmado, `FixedTimeEquals`, firma verificada **antes** del vencimiento para no filtrar qué `keyId` existen, y llaves retiradas que siguen verificando (rotar no invalida lo que ya circula). Llave por propósito, con el rechazo explícito "con una sola llave para todo, quien firma una entrada firma un certificado".
- **Preventa y promoción → `Synergos.Api.Pricing`.** `Promotion(… TimeWindow Validity)` + `CheckPromotion` que devuelve `Expired` (no `NotFound`) cuando el código era real pero se venció — eso es exactamente una preventa cerrada. `Discount` acotado para que nunca quede total negativo.
- **Compra de varias entradas → `Api.Cart` + `Api.Orders` + `Api.Payments`.** `Domain/PaymentRules.cs` tiene el ciclo completo `Authorized → Captured/Voided → Refunded` con `CheckVoidable` idempotente y `CheckRefundable` acumulativo (la suma de devoluciones no pasa lo capturado). Es mejor que el stub del CMS.
- **Cartelera buscable → `Api.Catalog`.**
- **Ficha editorial del evento → CMS, y está sorprendentemente completa.** `uSync/v9/ContentTypes/eventpage.config` tiene cuatro pestañas (Contenido / Ficha / Entradas / Mapa de asientos), `eventTiers` y `eventZones` como BlockList, `eventSessions` (agenda), `eventHighlights`, artista + descriptor + seguidores, geo del venue. Más `elementSynEventos` (bloque CDN con `role = attendee|organizer` y `apiBase`), su `SynHost/Eventos.cshtml`, y `CatalogEventCatalogProvider` sobre `UmbracoEventCatalogSource` ya registrado en `SeamComposer.EventsPropertiesGov.cs`.

### Parcial

- **`Api.Signing` — firma pero NO quema.** `SigningService.Verify()` es puro: valida firma y vigencia y devuelve el payload. **No hay ledger de redención.** Hice `grep -rn "redeem\|scan\|nonce\|single.use"` sobre `Synergos.Api.*`, `Synergos.Bff.*`, `Synergos.Core` y `Synergos.Shared`: **cero coincidencias**. El único "already-used" del producto vive en `StubEventManagementService` del CMS, en memoria del proceso. Traducción: hoy una foto del QR entra dos veces por dos puertas distintas. Falta `POST /v1/signatures/redeem` idempotente con almacén de redenciones.
- **`Api.Inventory` — unidades sí, geometría no.** Guarda `Row`/`Column` y explícitamente no los interpreta (correcto por la regla del `Ref`), pero eso deja fuera "dame 4 asientos juntos", que es la operación #1 de una venta de teatro. Y `MaxHoldTtl = 4 h` no modela un **bloqueo de producción** ni una **cortesía** (que la Res. 2890 exige como campo de la boleta): un bloqueo es indefinido, no un hold.
- **`Api.Payments` — el motor está, el borde no.** Registra un `LoggingPaymentProvider`: no cobra. Y la investigación previa ya lo documentó con precisión en `docs/product/investigacion-pagos/03-necesidades-por-vertical.md`: para Eventos no hay reembolso en la seam, y `PaymentWebhookController` tiene `KnownProviders` solo con `"stub"` y el despacho cableado a `IShopOrderService` — **si PSE confirma un pago de Eventos horas después, no hay endpoint que lo reciba**. En boletería, donde PSE y Nequi son medios de pago centrales, eso es una venta perdida y un asiento fantasma.
- **`Api.Notifications` — `LoggingNotificationSender`.** "Recordatorio" y "cambio de función" no salen. Y en eventos el aviso de reprogramación no es cortesía: es lo que la SIC está sancionando.
- **`Api.Documents` — existe, nadie la usa para la entrada.** No hay generador de PDF/PKPass.
- **`eventPage` — el activo comercial está en TextBox.** `eventMode` ("general"/"reserved"), `eventPriceFrom` (dígitos a mano, "escribe 180000, nunca 180.000") y `eventLat`/`eventLng` son `Umbraco.TextBox`. Para copy está bien; para el dato que decide si el checkout es por cantidad o por asiento, no.
- **Booking ↔ Inventory sin costura.** La función vive en Booking, el aforo en Inventory, y **nadie los ata**: eso es trabajo del orquestador, que no existe.

### Falta

| Necesidad | Por qué ninguna existente la cubre | Tipo |
|---|---|---|
| **Redención idempotente del ticket en puerta** | `Signing` verifica sin estado; `Inventory.ConsumeHold` consume *stock*, no *derecho de admisión*, y no tiene el token | `capacidad-nueva` (o endpoint nuevo con almacén en `Api.Signing`) |
| **`Synergos.Bff.Eventos`** | No existe. `Bff.Core` tiene la máquina de sagas y el `CompensationSweeper`, pero nadie pone el orden hold-inventory → quote → authorize → capture → firmar tickets → notificar, ni la compensación cuando falla a mitad | `feature-del-bff` |
| **Escáner con modo offline** | Ninguna capacidad contempla operar sin red. Un venue sin cobertura en puerta necesita lista firmada descargable y reconciliación posterior | `integracion-externa` + `capacidad-nueva` |
| **Factura electrónica DIAN con anexo de espectáculos** (Código PULEP, localidad, cortesía, prefijo EP##, rango por evento) | Ninguna de las 20 emite facturas. `Pricing` cotiza, `Orders` totaliza, nadie factura | `integracion-externa` |
| **Liquidación y declaración de la parafiscal del 10%** | `Pricing.Tax` es una tasa sobre la base; la parafiscal tiene umbral por boleta (3 UVT), destinatario distinto y periodicidad propia | `feature-del-bff` + `schema-del-cms` |
| **Código PULEP como dato de primera clase** | `eventPage` no lo tiene; y sin él la boleta es ilegal | `schema-del-cms` |
| **Boleta nominativa cédula ↔ ticket + listas de restricción** | `Api.Identity` guarda principales; nada ata documento a boleta ni consulta el Sistema Nacional de Validación | `integracion-externa` |
| **Reembolso / cancelación de evento** | `IEventTicketingService` no expone reembolso (solo transferencia). `Api.Payments` puede devolver; falta quien lo ordene sobre N tickets y compense el aforo | `feature-del-bff` |
| **Reversión de pago con SLA de 15 días hábiles** | Es un flujo distinto del refund comercial y nadie lo cronometra | `feature-del-bff` |
| **Transferencia y reventa oficial trazable en el árbol de capacidades** | Existe `TransferTicketAsync` con rotación de QR y auditoría — **pero solo en el stub del CMS**, no en `Synergos.Api.*` | `capacidad-nueva` / `feature-del-bff` |
| **Cola virtual / anti-bot en on-sale** | Nada en el repo. Es *el* momento de fallo de la boletería y el argumento de marketing de Ticketmaster en Colombia | `feature-del-bff` + `integracion-externa` |

---

## 5. Backlog priorizado

| Feature | Por qué vende | Esfuerzo | Depende de | Prioridad |
|---|---|---|---|---|
| **Redención idempotente en `Api.Signing`** (`/v1/signatures/redeem` + ledger de redenciones) | *"Un QR fotografiado no entra dos veces, y te lo probamos con el mismo código escaneado en dos puertas a la vez."* Es verdad y hoy es falso: el grep no encuentra ningún concepto de redención en el árbol de capacidades | S | — | **1** |
| **`Synergos.Bff.Eventos`** (saga compra + compensación; cancelación de evento con reembolso masivo) | *"Si el pago falla después de apartar los asientos, el aforo vuelve solo."* `Bff.Core` ya tiene la máquina; falta el flujo | L | `Bff.Core`, Inventory, Pricing, Payments, Signing | **1** |
| **PSP real (Wompi) detrás de `Api.Payments` + receptor de webhook para Eventos** | *"Cobramos con PSE y Nequi, y si el banco confirma media hora tarde, el asiento sigue siendo tuyo."* Hoy `LoggingPaymentProvider` no cobra y el webhook solo despacha a Tienda | M | `investigacion-pagos/01` y `/02` (ya hechos) | **1** |
| **Cumplimiento de boleta de artes escénicas**: Código PULEP en `eventPage` + campos de la Res. 2890 + factura electrónica con prefijo EP## y rango por evento | *"Tu boleta sale legal el primer día; no vas a descubrir en el segundo evento que la DIAN no te la valida."* Sin esto no se puede vender boletería de artes escénicas en Colombia, punto | L | `integracion-externa` con proveedor de facturación electrónica | **1** |
| **Escáner (PWA) con modo offline y reconciliación** | *"Se cayó el internet del coliseo y entraron los 3.000 igual."* Es la objeción número uno del jefe de operaciones | M | Redención | 2 |
| **Liquidador de contribución parafiscal 10% con umbral 3 UVT + reporte por evento** | *"El bimestre se declara solo; no lo arma tu contador en Excel el día antes."* Diferencia real contra Passline y Eventbrite, que no lo tocan | M | Orders, Pricing, PULEP en el CMS | 2 |
| **Cancelación/reprogramación: reembolso masivo + aviso a la SIC en 3 días hábiles + comunicado a compradores** | *"Lo que la SIC le está cobrando a TuBoleta, acá es un botón."* Es un cargo vivo y público contra el líder del mercado | M | Bff.Eventos, Notifications real | 2 |
| **Notificaciones reales (email + WhatsApp)** | *"El asistente recibe su entrada y el recordatorio; hoy nadie confía en un ticket que no llegó al correo."* | S | Proveedor externo | 2 |
| **Asientos contiguos + cortesías/bloqueos de producción en `Api.Inventory`** | *"Cuatro juntos, y los 40 de cortesía del patrocinador reservados sin vender."* Cortesía además es campo obligatorio de la boleta | M | Inventory | 2 |
| **Reversión de pago (Dec. 587) con SLA de 15 días hábiles** | *"El proceso de reversión está cronometrado y auditado."* | S | Payments real | 2 |
| **Boleta nominativa + integración Sistema Nacional de Validación (Dec. 1622)** | *"Habilita venta para fútbol profesional."* Abre el segmento clubes | L | Identity, integración estatal | 3 |
| **Reventa oficial con tope de precio y trazabilidad** | *"El productor se queda con el margen de la reventa en vez de perderlo con el revendedor de la esquina."* No hay obligación legal — es diferencial puro | L | Transferencia en capacidades | 3 |
| **Cola virtual / anti-bot para on-sale** | *"Aguanta el minuto en que salen a la venta 20.000 boletas."* Es exactamente lo que Ticketmaster promete al entrar a Colombia | L | Infra | 3 |
| **Editor visual de mapa de asientos en el backoffice** | *"El productor dibuja el aforo del teatro sin llamarte."* Hoy `eventZones` es BlockList: funciona, pero no se demuestra | M | Schema uSync | 3 |

La lista de prioridad 1 son **cuatro** items, y los cuatro son de la forma "sin esto la venta no existe o es ilegal": el dinero entra (PSP), la puerta no se cae (redención), el pedido se compensa (BFF) y la boleta es legal (PULEP/DIAN).

---

## 6. El ángulo CMS — sin ser amable

**Qué puede cambiar el editor sin tocar código, hoy, verificado en el repo:** el título, la descripción, la imagen, la agenda (`eventSessions`), el perfil del artista, los "por qué asistir", **las localidades con su precio, aforo, perks y ventana de venta** (`eventTiers`), **las zonas del mapa de asientos** (`eventZones`), el venue y su geo. Más: dónde vive la app de compra —`elementSynEventos` se dropea en cualquier página del Layout Composer, con `role=attendee` o `role=organizer`— y qué marca y tema tiene el sitio, por hostname. Un festival y un teatro pueden correr sobre el mismo deploy con marcas distintas.

**Por qué importa acá más que en otros dominios:** en boletería el productor **vende marca**. El festival no quiere que su público compre en tuboleta.com con logo ajeno; quiere que compre en el sitio del festival. Y la ficha del evento no es un formulario: es material de marketing que cambia semanalmente (se cae un artista, se abre una segunda fecha, se agota VIP). En un dominio como Salud o Gobierno el CMS es decoración sobre el proceso; acá el contenido **es** parte del producto.

**Y ahora el veredicto honesto: el CMS es un extra fuerte, no el argumento de venta.**

Tres razones, y ninguna es cómoda:

1. **Nadie cambia de ticketera por el CMS.** Se cambia porque el cargo por servicio del 10–15% se lo lleva otro, porque la puerta falló, o porque el dinero no llegó. La conversación empieza en dinero y operación; el CMS aparece en la tercera reunión.
2. **El incumbente no vende software: vende demanda.** TuBoleta tiene exclusiva en Movistar Arena y una base de compradores que Synergos no tiene. A un promotor que vive del tráfico de TuBoleta, "tu propio sitio" le suena a "vendé menos". El CMS solo es argumento donde el cliente **ya tiene audiencia propia**: teatro con abonados, universidad, gremio, club, festival con marca.
3. **El competidor de este segmento cuesta cero.** Passline es gratis para el organizador. Contra cero, "un CMS mejor" no es un argumento de compra — es un argumento de preferencia.

**Dónde el CMS sí cierra la venta:** congresos y ferias corporativas, y teatros/instituciones con programación permanente. Ahí el sitio es el producto, hay diez páginas de contenido por cada página de checkout, y el comprador de software ya está pagando por separado un sitio web y una boletería. "Una sola cosa, tu marca, tu dato" es un argumento real. En conciertos masivos y estadios, el CMS es irrelevante: ahí lo que se compra es capacidad de aguantar el on-sale.

---

## 7. El demo de 5 minutos que cierra

Público objetivo del guion: **director de un teatro o de una feria/congreso, con programación propia.** El principio rector: no mostrar arquitectura, mostrar *su* semana.

**0:00–0:45 — Su sitio, no el mío.** Abro dos hostnames del mismo deploy: el del teatro y el del festival. Marcas y temas distintos, mismo motor. "Esto no es una plantilla con tu logo arriba: es tu sitio."

**0:45–1:45 — El editor mueve el negocio, en vivo.** Entro al backoffice, abro el evento del sábado. Le pido a él que me dicte: subo el precio de VIP, agrego un perk ("meet & greet"), cambio la ventana de venta a "Hasta el 12 de julio", agrego un punto de agenda. Publico. Refresco el sitio: está. **Sin desarrollador, sin ticket de soporte, sin esperar al proveedor.** Ese es el momento en que se le ilumina la cara, porque hoy eso le toma tres días y un correo.

**1:45–3:00 — Compro una entrada como su público.** Selecciono dos butacas contiguas en el mapa, veo el contador de aforo bajar, pago, y me llega la entrada con QR. Muestro el reloj del hold: "si no pagás en 15 minutos, las butacas vuelven solas al mapa" — y lo demuestro dejando vencer un hold en otra pestaña.

**3:00–4:00 — La puerta.** Abro el escáner en el celular, escaneo el QR: *válido*. **Vuelvo a escanear el mismo QR: rechazado, ya usado.** Después escaneo desde un segundo celular al mismo tiempo: rechazado también. Este es el minuto que vende — es el miedo que el director tiene el sábado a las 7 de la noche. *(Nota interna: hoy este minuto no se puede hacer honestamente sobre el árbol de capacidades. Es exactamente el item de prioridad 1 más barato.)*

**4:00–4:40 — El lunes.** Muestro el tablero: vendidos por localidad, asistentes que entraron, y **la liquidación de la contribución parafiscal del bimestre ya calculada, separando las boletas por debajo de 3 UVT**. Le digo el número: "$157.122 en 2026". Que sepa que sabemos.

**4:40–5:00 — El cierre.** "El cargo por servicio que hoy paga tu público se lo lleva otro. Acá lo defines vos, y el comprador queda en tu base de datos, no en la de ellos."

Lo que **no** se muestra: el grafo de dependencias, las 20 capacidades, los `Ref` opacos, el Layout Composer como tal. Nada de eso le importa a quien firma.

---

## 8. El riesgo que mata

**1. El modelo de negocio del mercado es "gratis para el organizador".** VERIFICADO: Passline no cobra al organizador — cobra cargo de servicio al comprador ([home.passline.com](https://home.passline.com/que-es-passline)); Eventbrite igual, con 6,99%+IVA sobre el pago. Vender una licencia contra gratis solo funciona si el cliente tiene volumen suficiente para querer quedarse él con el 10–15%. INFERIDO: eso reduce el mercado a los ~358 productores permanentes más los venues con programación fija. Es un mercado de **cientos de cuentas, no de miles**. Si el plan de negocio asume SaaS de volumen, no cierra; si asume 20–40 cuentas grandes con implementación, puede cerrar.

**2. La habilitación regulatoria, no la feature.** Para vender boletería de artes escénicas en línea en Colombia hay que ser **operador de boletería autorizado por MinCultura**, con CIIU 7990 en el RUT y facturación electrónica con numeración autorizada por evento (Res. 2890/2017). Esto es binario: o el cliente ya es operador y Synergos es su software, o Synergos tiene que serlo. **No decidir esto antes de vender es el error que quema el primer contrato**, porque se descubre en la implementación, no en la demo.

**3. La demanda es del incumbente.** Ticketmaster entró en 2025, compró La Tiquetera y absorbió eticket; TuBoleta tiene exclusiva de Movistar Arena. En eventos masivos no se compite con software: se compite con catálogo y audiencia, y Synergos no tiene ninguno de los dos. Cualquier plan que hable de "quitarle mercado a TuBoleta" es fantasía; el plan real es "no competir donde ellos están".

**4. El día del evento es todo o nada, y eso alarga el ciclo.** Veinte minutos de falla en puerta con 5.000 personas afuera es una noticia y una demanda. INFERIDO, pero con alta confianza: ningún venue serio pone su boletería en un proveedor sin referencias de aforo real. El ciclo es piloto gratis → una temporada → contrato: **12 a 18 meses hasta el primer peso**. Un dominio con esa curva no se financia con las primeras dos ventas.

**5. El riesgo que sí es controlable, y hoy no está controlado:** el producto no cobra (`LoggingPaymentProvider`), no notifica (`LoggingNotificationSender`) y no quema tickets (no hay redención en `Synergos.Api.*`). Un demo que se hace con stubs y se vende como listo es el riesgo reputacional más barato de eliminar y el más caro de sufrir.

---

## 9. Confianza

**VERIFICADAS con URL: 23.** Marco regulatorio completo (Ley 1493, Res. 2890/2017 con sus artículos 2/4/7, Decreto 1622/2022, Decreto 587/2016, UVT 2026 = $52.374, proyecto de reforma tributaria); cifras PULEP 2025 (24.389 eventos por categoría, 3.973 productores 91/9, 185 municipios, recaudos por ciudad y por evento); competencia (entrada de Ticketmaster y compra de La Tiquetera, exclusiva TuBoleta–Movistar Arena, pliego de cargos SIC Res. 38063/2026 con sus cinco imputaciones, modelo y escala de Passline, tarifa Eventbrite, existencia de PrintTicket / WS Ticketing / vivenu / Spektrix / AudienceView, rango 10–15% del cargo por servicio).

**Además, verificadas contra el repo abriendo el fichero (no son "de mercado" pero son afirmaciones fuertes): 12.** `Inventory/Domain/{StockItem,InventoryRules}.cs` y `Endpoints/InventoryEndpoints.cs`; `Booking/Domain/{BookingRules,Resource}.cs` y sus endpoints; `Signing/Domain/{SigningRules,SigningService}.cs`; `Payments/Domain/PaymentRules.cs`; `Pricing/Domain/{PricingRules,Price}.cs`; `uSync/v9/ContentTypes/{eventpage,elementsyneventos}.config`; `Interfaces/{IEventTicketingService,IEventManagementService,IEventCatalogProvider}.cs`; `Composers/SeamComposer.EventsPropertiesGov.cs`; `docs/product/investigacion-pagos/03-necesidades-por-vertical.md`. La ausencia de redención se verificó con `grep -rn "redeem|scan|nonce|single.use|check-in"` sobre `Synergos.Api.*`, `Synergos.Bff.*`, `Synergos.Core`, `Synergos.Shared` → cero coincidencias relevantes.

**INFERIDAS: 14.** El tamaño del universo comprador (~358 productores permanentes, derivado del 9% de 3.973); la base de boletería (~$970 mil millones COP/año, derivada de tratar los $97.019 millones como el 10% de la base — la fuente no lo dice así explícitamente); que congresos y ferias son el segmento donde el CMS pesa más; el ciclo de venta de 12–18 meses; que los venues exigen referencias de aforo real; la clasificación de tipo (`capacidad-nueva` / `feature-del-bff` / etc.) de cada hueco; los esfuerzos S/M/L del backlog.

**Qué NO pude averiguar:**

- **El listado real de operadores de boletería autorizados por MinCultura.** La página de PULEP (`/Informespublicos/operadores`) es un formulario de búsqueda que no devuelve resultados sin POST; no pude enumerar cuántos hay ni quiénes son. Ese dato cambia la lectura del riesgo #2: si hay 8 operadores, la habilitación es una barrera; si hay 200, es un trámite.
- **Cuánto cobra realmente una licencia de software de boletería en Colombia.** Ni PrintTicket ni WS Ticketing publican precio, y Spektrix/AudienceView/Tessitura/vivenu son "custom enterprise". No tengo una cifra defendible de ticket promedio anual, que es justo lo que hace falta para saber si 40 cuentas alcanzan.
- **La ambigüedad de los $97.019 millones vs. $486.034 millones** en la fuente de Valora Analitik: la nota no aclara si el segundo es acumulado del año, del programa o histórico. Usé el primero para derivar la base y marqué la derivación como inferida.
- **El estado legislativo actual del 19% sobre boletas.** Encontré la radicación (julio 2026) pero no confirmación de aprobación o hundimiento. Lo traté como proyecto.
- **El tamaño del segmento MICE/congresos en Colombia** (Corferias y similares) en número de eventos y gasto en software. No encontré una fuente confiable, y es el segmento donde el diferencial CMS sería más fuerte — es el hueco de investigación que más conviene cerrar antes de decidir invertir en este dominio.