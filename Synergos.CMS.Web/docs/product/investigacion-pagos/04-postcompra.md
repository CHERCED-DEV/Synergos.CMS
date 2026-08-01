# Post-compra — qué falta para que sea un negocio real

> Contexto verificado del repo: `OrderStatus` en `Synergos.CMS.Interfaces/IShopOrderService.cs` solo tiene
> `Pending | Paid | Cancelled`. `StubOrderTrackingService` (`Synergos.CMS.Application/Services/Impl/`) trae un
> pipeline `paid → preparing → shipped → delivered` pero **nada en el codebase llama `AdvanceAsync` más allá
> de `paid`** — preparación/envío/entrega existen como slugs, no como flujo operado. `StubReturnService` ya
> tiene la máquina legal `Requested→Approved→Received→Refunded` completa y correcta, pero vive en
> `ConcurrentDictionary` de proceso (se pierde en cada reinicio). No hay `IShippingRateProvider`, no hay
> `ITaxProvider`, no hay `IInvoiceEmitter`.

## Las tres máquinas de estado (pago / pedido / cumplimiento) y por qué separarlas

Es **consenso de facto** entre los cuatro referentes investigados: ninguno modela el post-compra como una sola
máquina de estados. Todos separan como mínimo pago de cumplimiento, y los más maduros (Shopify, VTEX) añaden
una tercera capa de estado "de negocio"/devoluciones.

- **Shopify** documenta explícitamente cuatro categorías independientes: *Order status* (Open/Archived/Canceled
  — vista general), *Payment status* (Pending/Authorized/Paid/Refunded/Voided/etc.), *Fulfillment status*
  (Unfulfilled/In progress/Partially fulfilled/Fulfilled/On hold) y *Return status* (Requested/In progress/
  Inspection complete/Returned) — cuatro máquinas, no una.
  [Understanding your order statuses](https://help.shopify.com/en/manual/fulfillment/managing-orders/order-status)
- **WooCommerce** funde pago y cumplimiento en una sola cadena de "order status" (`pending`→`processing`→
  `completed`, con `on-hold`/`failed`/`refunded`/`cancelled` como ramas) — es el outlier: no separa las
  máquinas, y por eso "processing" significa simultáneamente "pagado, aún no despachado".
  [WooCommerce Order Statuses](https://woocommerce.com/document/managing-orders/order-statuses/)
- **Medusa.js** separa `order.status` (Pending/Completed/Archived/Canceled/Requires action) de
  `fulfillment_status` (not_fulfilled/fulfilled/shipped/delivered/partially_*) y de `payment_status`
  (authorized/captured/refunded/canceled) — tres campos independientes en la misma entidad Order.
  [Orders Architecture](https://www.docs.test.tglsupplies.com/modules/orders/) ·
  [Fulfillment Module](https://medusajs.com/fulfillment-module/)
- **VTEX** es el más granular: el *order flow* completo encadena `Payment Pending → Payment Approved →
  Window to Cancellation → Ready for Handling → Handling Shipping → Invoiced`, con **Invoiced** y
  **Canceled** como únicos estados terminales; la responsabilidad de pago (marketplace) y de manejo/envío
  (seller) son procesos que corren en paralelo y se combinan en el estado compuesto.
  [Order flow](https://help.vtex.com/en/docs/tracks/order-flow) ·
  [Order flow and status](https://help.vtex.com/docs/tutorials/order-flow-and-status)

**Por qué separar**: (1) un pago puede fallar/reversarse sin que el cumplimiento haya arrancado — mezclar
las máquinas obliga a estados híbridos ambiguos ("processing" de WooCommerce no dice si ya se armó el
paquete); (2) devoluciones y cumplimiento parcial (2 de 3 líneas enviadas) requieren granularidad que un
único campo no soporta bien — de ahí que Shopify y Medusa tengan variantes "partially_*"; (3) la UI de "mis
pedidos" necesita comunicar cosas distintas por audiencia (comprador: "¿ya pagué? ¿ya me llegó?" vs.
operación: "¿qué transportadora, qué guía, qué SLA de devolución"). Synergos ya intuyó esto al separar
`IShopOrderService` (dueño de `OrderStatus` de pago) de `IOrderTrackingService` (timeline de cumplimiento) —
la separación conceptual es correcta, lo que falta es *operarla* end-to-end.

## Comparativa de referentes

| Plataforma | Estados de pedido (order-level) | Estados de envío/cumplimiento | Devoluciones |
|---|---|---|---|
| **Shopify** | Open / Archived / Canceled | Unfulfilled / In progress / Partially fulfilled / Fulfilled / On hold / Fulfillment not required | Return requested / Return in progress / Inspection complete / Returned (máquina separada de refund) |
| **WooCommerce** | Pending payment → On hold/Processing → Completed; ramas Failed / Cancelled / Refunded / Draft | No tiene máquina propia — "Processing"≈pagado-sin-enviar, "Completed"≈enviado/entregado (fusionado) | No nativo; vía plugins (ej. RMA extensions) |
| **Medusa** | Pending / Completed / Archived / Canceled / Requires action | not_fulfilled / partially_fulfilled / fulfilled / partially_shipped / shipped / partially_delivered / delivered | Entidad `Return` propia con `requested_return`/`received`, ligada a `Swap`/refund |
| **VTEX** | Waiting for seller decision → Payment Pending → Payment Approved → Window to Cancellation → Ready for Handling → Handling Shipping → Invoiced (terminal) / Canceled (terminal) | Ready for Handling / Handling Shipping / Verifying Invoice → Invoiced | Post-venta vía módulo separado (Reverse Logistics) |

Fuentes: ver URLs arriba en cada bullet.

## Transportadoras colombianas y agregadores

| Empresa | ¿API propia? | Qué ofrece | Veredicto |
|---|---|---|---|
| **Servientrega** | Sí — API REST documentada públicamente (`mobile.servientrega.com/ApiIngresoCLientes/Help`): cotización por ciudad/producto, generación de guía, tracking. | Cotización, guía, tracking. Es la transportadora más grande de Colombia. | Integrable directo, pero un blog de integrador la describe como "no ofrece servicios de integración completos" — guía es sencilla, cotización es más compleja de integrar sola. [Doc oficial](https://mobile.servientrega.com/ApiIngresoCLientes/Help) · [Nota de integrador](https://blog.saulmoralespa.com/servientrega-esto-deberias-saber-antes-de-integrar/) |
| **Coordinadora** | Sí — portal de desarrollador con API Key/OpenID (`portal.api.coordinador.cl` — dominio nota: es el mismo grupo, verificar si hay portal específico CO vs CL, **no confirmado** cuál es el dominio correcto para Colombia). | Tracking API con ubicación/tránsito/ETA; guía y cotización vía su portal comercial. | API existe pero la documentación pública encontrada es de terceros (TrackingMore, AfterShip) más que oficial exhaustiva — **no confirmado** el alcance completo sin acceso al portal de desarrollador real. |
| **Interrapidísimo** | **No confirmado** — no se encontró documentación de API propia pública; solo integraciones de tracking vía terceros (TrackingMore, 17TRACK, Track123). | Tracking por número de guía (12 dígitos) vía su sitio o agregadores. | Sin API propia documentada al público — depender de un agregador (ver abajo) es la ruta práctica. |
| **TCC** | **No confirmado** directamente — cotización/tracking vía su sitio web y vía agregadores (99 Envíos, Skydropx) que exponen API sobre TCC. | Cotización desde ~$4,970 COP, tracking, portal courier. | Vía agregador es la integración recomendada; no se encontró API B2B propia documentada. |
| **Mipaquete** (agregador) | Sí — API v2 documentada (`api.documentacion.mipaquete.com`) + plugins WooCommerce/Jumpseller/Shopify. | Multi-transportadora: Servientrega, Coordinadora, Envía, TCC, Deprisa. Genera guía y despacho automático en cada venta, incluye contraentrega (COD). | **Mejor candidato** para Synergos: API documentada, cubre las 4 transportadoras del alcance, soporte declarado (~3h respuesta). [API docs](https://api.documentacion.mipaquete.com/) · [Conecta API](https://www.mipaquete.com/conecta-tu-tiendavirtual/api-integracion) |
| **99 Envíos** (agregador) | Sí, según fuentes de terceros — "API robusta" para cotizar, generar guía, rastrear y manejar contraentrega. | Multi-transportadora incluyendo TCC. | Candidato secundario; documentación pública encontrada es indirecta (vía blogs), habría que verificar directamente con el proveedor. |
| **Skydropx / EnvioClick** (agregadores) | Sí — Skydropx tiene guía de conexión de API (`help.skydropx.com.co`); EnvioClick centraliza +10 transportadoras. | Cotización comparativa multi-carrier, plugins Shopify/WooCommerce/Tiendanube/Magento/Mercado Libre. | Buenos para cotización comparativa multi-carrier; Skydropx lista integración con Inter Rapidísimo explícitamente. [Conectar API Skydropx](https://help.skydropx.com.co/articulos-cda/como-conectar-la-api-skydropx) |
| **ShipSmart** | Sí, pero es un player de logística internacional (cross-border, multi-currency checkout, duties) — no es un agregador local colombiano. | Multi-country fulfillment, duties/tax automation. | Fuera de alcance para envíos 100% domésticos CO; interesante solo si Synergos hace cross-border. [ShipSmart solutions](https://shipsmart.global/solutions/) |

**Veredicto general**: para Synergos (single-tenant, "un origen", sin necesidad de negociar tarifas propias
con 4 carriers), un agregador tipo **Mipaquete** (o Skydropx/EnvioClick como alternativa) es la seam correcta
detrás de una interfaz `IShippingProvider`/`IShippingRateProvider` propia — evita acoplar el core a un
carrier específico, igual que ya se hace con `IBundleRegistryClient` para el CDN (ADR 0012).

## Devoluciones: modelo + marco legal colombiano

**Modelo de referencia**: Shopify (`Return requested → In progress → Inspection complete → Returned`, máquina
separada del refund) y Medusa (`Return` entity con `requested`/`received` + `Swap` opcional) coinciden en el
patrón: la devolución es un sub-objeto de la orden con su propia máquina de estados, y el *refund* (dinero) es
un efecto disparado por una transición, no un estado en sí. **Esto es exactamente lo que `StubReturnService`
ya implementa** (`Requested→Approved→Received→Refunded`, con el refund ejecutado vía `IPaymentProvider` al
llegar a `Refunded`) — el diseño está alineado con el estado del arte. El problema no es el modelo, es la
persistencia: vive en memoria de proceso, se pierde en cada restart/deploy.

**Marco legal colombiano — Ley 1480 de 2011 (Estatuto del Consumidor)**:

- **Derecho de retracto** (Art. 47): plazo de **5 días hábiles**, contados desde la entrega del bien (o desde
  la celebración del contrato si es un servicio). Aplica a ventas a distancia / comercio electrónico. El
  proveedor debe reintegrar **todas** las sumas pagadas sin descuentos, en un plazo máximo de **30 días
  calendario** desde que el consumidor ejerció el derecho. El consumidor devuelve el producto en las mismas
  condiciones y asume el costo de transporte de la devolución.
  Fuente: texto del Art. 47 vía [Consumoteca](https://www.consumoteca.com.co/articulo-47-de-la-ley-1480-estatuto-del-consumidor/)
  (glosa secundaria; el texto oficial de la Ley 1480 vive en
  [Secretaría del Senado](http://www.secretariasenado.gov.co/senado/basedoc/ley_1480_2011.html) y
  [SUIN-Juriscol](https://www.suin-juriscol.gov.co/viewDocument.asp?id=1681955) — no pude fetch-ear el texto
  primario directamente por error 503 del gestor normativo de Función Pública, así que el plazo de 5 días
  hábiles y 30 días calendario está confirmado por fuente secundaria jurídica, **no por el texto primario
  fetch-eado en esta sesión**).
- **Garantía legal** (Art. 8 y ss.): si no se indica un término distinto, la garantía es de **1 año para
  productos nuevos**; para perecederos, hasta la fecha de vencimiento; usados sin garantía informada por
  escrito → 3 meses de garantía legal igual. Cubre reparación gratuita, transporte si es necesario, repuestos;
  si no es reparable, el consumidor puede exigir cambio del bien o devolución del dinero. El término se
  suspende mientras el consumidor está privado del producto por la reparación; si hay reemplazo total, la
  garantía arranca de nuevo desde la reposición.
  Fuente: síntesis de Art. 8 vía [Ámbito Jurídico](https://www.ambitojuridico.com/noticias/mercantil/si-no-se-indica-el-termino-de-la-garantia-sera-de-un-ano-para-productos-nuevos) y
  [Consumoteca Art. 8](https://consumoteca.com.co/articulo-8-de-la-ley-1480-estatuto-del-consumidor/) —
  igualmente fuente secundaria, texto primario no verificado directamente en esta sesión (SIC devolvió 503
  también). **Tratar el plazo de 1 año / 3 meses como "reportado por fuentes jurídicas secundarias
  consistentes", no como cita primaria confirmada.**
- Autoridad de aplicación: **Superintendencia de Industria y Comercio (SIC)** — portal
  [sic.gov.co](https://sic.gov.co/fallas-baja-calidad-e-incumplimiento-de-garantias) (no se pudo fetch-ear el
  contenido en esta sesión, 503, pero la URL es la referencia oficial correcta).

**Qué exige esto del sistema**: (1) el reloj del retracto (5 días hábiles desde entrega) debe ser un campo
persistido y calculado desde la fecha real de entrega — hoy Synergos no tiene fecha de entrega porque el
pedido nunca pasa de "pagado"; (2) el SLA de reembolso de 30 días calendario necesita ser visible/monitoreable
(alerta si un RMA lleva >X días en `Refunded` pendiente o si el reembolso no se disparó); (3) la garantía de
1 año por producto requiere que el sistema sepa la fecha de entrega por línea de pedido, no solo la fecha de
pago; (4) todo esto necesita sobrevivir un restart — no puede vivir en `ConcurrentDictionary`.

## Notificaciones del post-compra

| Momento | Canal | Contenido | ¿Obligatorio? |
|---|---|---|---|
| Pago confirmado | Email (± SMS) | N.º de orden arriba/en el asunto, ítems, precio desglosado, método de pago, dirección de envío, próximos pasos, contacto de soporte | De facto obligatorio — es el ancla legal de la compra y lo que el comprador espera de inmediato. [Order Confirmation Best Practices — Klaviyo](https://www.klaviyo.com/blog/order-confirmation-email-tips-examples) |
| Envío despachado (guía generada) | Email + push/SMS opcional | Transportadora, número de guía, link de tracking, ETA estimada | Estándar de la industria, no legal per se, pero altísima expectativa del comprador. [Shipping confirmation tips — Klaviyo](https://www.klaviyo.com/blog/tips-better-shipping-confirmation-emails) |
| En tránsito / actualización de estado | Email/SMS (a veces solo push del carrier) | Estado actual, próxima actualización esperada | Buena práctica, no obligatorio |
| Entregado | Email | Confirmación de entrega, invitación a reseñar (ya cubierto en Synergos: reseña de comprador verificado) | Buena práctica |
| Solicitud de retracto/devolución recibida | Email | Estado de la solicitud, próximos pasos, plazo esperado | Fuertemente recomendado dado el plazo legal de 30 días — el comprador necesita evidencia de cuándo pidió el retracto |
| Reembolso procesado | Email | Monto, método, fecha esperada de acreditación | Recomendado — cierra el ciclo del Art. 47 |
| Factura electrónica emitida | Email (adjunto o link) + XML/CUFE según DIAN | Documento tributario válido | **Obligatorio si el vendedor es sujeto obligado a facturación electrónica** (ver sección DIAN) |

Errores típicos según las fuentes: no poner el número de orden visible desde el asunto/arriba del email,
no dar ETA de entrega, tratar la confirmación de envío como solo "informativa" sin link de tracking activo,
y no separar la notificación de "solicitud de RMA recibida" de "RMA resuelto" (dejar al comprador sin señal
intermedia durante el plazo legal).

## Impuestos, IVA y facturación electrónica DIAN

- **IVA**: tarifa general **19%** sobre bienes y servicios gravados en Colombia; existen tarifas
  diferenciales (5%) y bienes exentos (tarifa 0%, con derecho a descontar IVA de insumos) o excluidos
  (no genera IVA en ningún sentido) — libros, ciertos alimentos básicos, dispositivos móviles de gama baja
  bajo cierto tope de UVT, etc. **La venta por internet tributa igual que la presencial**: el canal no
  cambia el tratamiento tributario. [Siigo — IVA](https://www.siigo.com/blog/obligaciones-fiscales/que-es-el-iva/) ·
  [Dian.com.co — IVA 2026](https://dian.com.co/iva-colombia-2026/)
- **Facturación electrónica DIAN**: régimen bajo Resolución 000042 de 2020 (anexo técnico base) y
  actualizaciones recientes — Resolución 000165 de 2023 y Resolución 005743 de mayo 2025 — que amplían el
  universo de obligados a facturar electrónicamente, incluyendo explícitamente comercio electrónico y
  servicios digitales que antes no estaban claramente contemplados. El incumplimiento tiene sanciones y
  puede implicar bloqueo operativo. [DIAN — normativa](https://www.dian.gov.co/impuestos/factura-electronica/documentacion/Paginas/normativa.aspx) ·
  [Resolución 005743/2025 — Rio Consultores](https://rioconsultores.com/2025/07/08/obligacion-de-facturacion-electronica-resolucion-dian-005743-del-29-de-mayo-de-2025/)
- **Implicación concreta**: un vendedor obligado necesita, por cada orden pagada, generar un documento
  tributario válido (factura electrónica de venta) con CUFE, transmitirlo/validarlo ante DIAN, y entregarlo
  al comprador — típicamente vía un proveedor tecnológico autorizado (Siigo, Alegra, Saphety, etc.) más que
  construyendo el generador desde cero. **No confirmado en esta investigación**: el umbral de ingresos o
  condiciones exactas bajo las cuales *este* negocio específico de Synergos quedaría obligado — depende del
  régimen tributario del vendedor real (persona natural/jurídica, régimen simple vs. común), fuera del
  alcance de esta investigación de producto.

## Costos de envío y sus modelos

- **Peso real vs. peso volumétrico**: los carriers cobran por el mayor entre peso real y peso dimensional
  (volumen/factor); relevante para catálogo con productos voluminosos pero livianos.
  [ShipperHQ — Calculate shipping rates](https://shipperhq.com/blog/how-to-calculate-shipping-rates)
- **Zonas**: tarifas por zona geográfica origen→destino; más zonas cruzadas, mayor costo — en Colombia esto
  típicamente se traduce en tablas urbano/regional/nacional/apartado (San Andrés, Amazonas, etc. con
  sobrecosto).
- **Tablas de tarifa ("table rate")**: reglas multi-condición combinando rango de peso + rango de subtotal +
  cantidad + destino + grupo de envío del producto, con tipos de tarifa fija por pedido, por ítem, por
  unidad de peso, o porcentaje del valor. [Mageplaza — Table Rate Shipping](https://www.mageplaza.com/blog/table-rate-shipping-ecommerce.html)
- **Envío gratis por umbral**: práctica estándar para subir AOV; regla de dedo de la industria es fijar el
  umbral en 1.3×–1.5× el AOV actual para que incentive sin regalar margen en la mayoría de las órdenes.
  [ShipBob — Calculate Shipping Costs](https://www.shipbob.com/ecommerce-shipping/calculate-shipping-costs/)
- **Recogida en tienda (click & collect)**: no encontrado en las fuentes de esta búsqueda puntual con
  suficiente profundidad — **no confirmado** un patrón de referencia específico; conceptualmente es un
  "shipping method" más con costo $0 y sin transportadora, pero requiere inventario por ubicación física, que
  Synergos no tiene modelado (closest: no hay concepto de warehouse/store location en el repo revisado).

## "Mis pedidos" — qué debe mostrar

Según Baymard/Gorgias/Shoplazza (agregado de buenas prácticas de UX de tracking):

- Estado actual claro y humano (no solo un código), con historial de hitos con fecha (igual al patrón
  `OrderTimelineStage` que Synergos ya modela).
- Costos desglosados: ítems, descuentos, impuestos, envío, total — visibles en el detalle del pedido.
- Método y ETA de envío, transportadora + número de guía con link de tracking activo (no solo texto).
- Acceso directo a "solicitar devolución"/"solicitar retracto" desde el detalle del pedido, no como un flujo
  de soporte separado — reduce carga de atención al cliente.
- Reordenar/comprar de nuevo, y descarga de factura electrónica.
- El objetivo explícito declarado por las fuentes es **reducir la ansiedad del comprador** y **reducir la
  dependencia de soporte** vía autoservicio.
  [Baymard — Order Tracking UX](https://baymard.com/blog/integrate-tracking-info) ·
  [Loop Returns — Order tracking page design](https://www.loopreturns.com/blog/design-best-practices-ecommerce-order-tracking-pages/)

## Mapeo a Synergos: qué artefactos concretos faltan

Priorizado por lo que bloquea "negocio real" primero:

1. **Persistir `IOrderTrackingService` y `IReturnService`** — hoy ambos son `Stub*` en memoria de proceso
   (`ConcurrentDictionary`). Sin esto, cualquier RMA o timeline de envío se borra en cada deploy/restart. Es
   el gap más urgente porque el resto (retracto, garantía, notificaciones) depende de que el estado
   sobreviva.
2. **Operar el pipeline de cumplimiento más allá de `paid`** — nada llama `AdvanceAsync("preparing"/"shipped"/
   "delivered")`. Falta el trigger (manual desde backoffice, o vía adapter de carrier) que efectivamente
   mueva el timeline. Sin esto, "envío" y "entrega" son slugs decorativos.
3. **Seam `IShippingRateProvider`/`IShippingProvider`** (nuevo, en Interfaces) — cotización + generación de
   guía + tracking, con un adapter hacia un agregador (Mipaquete recomendado) detrás, siguiendo el mismo
   patrón seam-consumido que `IBundleRegistryClient` (ADR 0012). Necesario para que el checkout muestre un
   costo de envío real en vez de $0/hardcoded.
4. **Fecha de entrega persistida por orden/línea** — requisito duro para calcular el vencimiento del derecho
   de retracto (5 días hábiles desde entrega) y el inicio de la garantía legal (1 año desde entrega). Hoy
   `OrderStatus` no tiene ni `Shipped` ni `Delivered` como estado de pago — correcto que no los tenga ahí
   (son de otra máquina), pero *en algún lado* debe persistirse `DeliveredAt`.
5. **`IInvoiceEmitter`/`ITaxProvider`** (nuevo) — cálculo de IVA por línea (19%/5%/0%/excluido) y generación
   de factura electrónica (o delegación a un proveedor tecnológico DIAN-habilitado). Bloqueante legal si el
   vendedor real es sujeto obligado.
6. **Notificaciones transaccionales del post-compra** — seam de emisión de email/SMS en los momentos
   identificados (pago confirmado, envío despachado, RMA recibido, reembolso procesado, factura emitida).
   Verificar si ya existe un `IEmailSender`/`INotificationEmitter` genérico en el repo antes de proponer uno
   nuevo (no se auditó en esta investigación — pendiente).
7. **Pantalla/endpoint "Mis pedidos" self-service** — exponer `OrderTimeline` + `ShopReturnCase[]` +
   desglose de costos + acceso a solicitud de retracto/devolución desde el detalle del pedido. El contrato
   de datos (`OrderTimeline`, `OrderTimelineStage`) ya existe en Interfaces; falta el consumo Web/UI.
8. **Reloj del retracto como campo de negocio, no solo UX** — un job o chequeo que marque cuándo vence la
   ventana de 5 días hábiles por orden, para poder bloquear/permitir la solicitud en el backend (hoy
   `StubReturnService.RequestAsync` no valida ningún plazo, solo que la orden esté `Paid`).
9. **Click & collect / múltiples ubicaciones de inventario** — no evaluado en profundidad (fuera del
   alcance de esta investigación puntual); si es prioridad de negocio, requiere investigación de producto
   separada porque toca el modelo de catálogo/inventario, no solo post-compra.

## Fuentes (URLs)

- Shopify — [Understanding your order statuses](https://help.shopify.com/en/manual/fulfillment/managing-orders/order-status)
- WooCommerce — [Order Statuses documentation](https://woocommerce.com/document/managing-orders/order-statuses/)
- Medusa — [Orders Architecture Overview](https://www.docs.test.tglsupplies.com/modules/orders/) · [Fulfillment Module](https://medusajs.com/fulfillment-module/)
- VTEX — [Order flow](https://help.vtex.com/en/docs/tracks/order-flow) · [Order flow and status](https://help.vtex.com/docs/tutorials/order-flow-and-status)
- Servientrega — [API oficial](https://mobile.servientrega.com/ApiIngresoCLientes/Help) · [Nota de integración](https://blog.saulmoralespa.com/servientrega-esto-deberias-saber-antes-de-integrar/)
- Coordinadora — [Portal desarrollador](https://portal.api.coordinador.cl/) · [TrackingMore](https://www.trackingmore.com/coordinadora-tracking-api.html)
- Interrapidísimo — [TrackingMore](https://www.trackingmore.com/inter-rapidisimo-tracking) · [El Tiempo — cómo rastrear](https://www.eltiempo.com/economia/empresas/interrapidisimo-asi-puede-rastrear-sus-paquetes-728966)
- TCC — [Skydropx cotizador TCC](https://www.skydropx.com.co/transportadoras/tcc/cotizador/) · [99 Envíos](https://99envios.com/) · [tcc.com.co](https://tcc.com.co/)
- Mipaquete — [API v2 docs](https://api.documentacion.mipaquete.com/) · [Conecta tu ecommerce API](https://www.mipaquete.com/conecta-tu-tiendavirtual/api-integracion)
- Skydropx — [Conectar API](https://help.skydropx.com.co/articulos-cda/como-conectar-la-api-skydropx) · [Skydropx vs EnvioClick](https://blog.skydropx.com/skydropx-vs-envioclick/)
- EnvioClick — [envioclick.com/co/paqueterias](https://www.envioclick.com/co/paqueterias)
- ShipSmart — [Solutions](https://shipsmart.global/solutions/)
- Ley 1480 de 2011 — texto primario (no fetch-eado directamente en esta sesión, 503):
  [Secretaría del Senado](http://www.secretariasenado.gov.co/senado/basedoc/ley_1480_2011.html) ·
  [SUIN-Juriscol](https://www.suin-juriscol.gov.co/viewDocument.asp?id=1681955) ·
  [Función Pública — Gestor Normativo](https://www.funcionpublica.gov.co/eva/gestornormativo/norma.php?i=44306)
- Ley 1480 — Art. 47 retracto (fuente secundaria): [Consumoteca Art. 47](https://www.consumoteca.com.co/articulo-47-de-la-ley-1480-estatuto-del-consumidor/)
- Ley 1480 — Art. 8 garantía (fuente secundaria): [Consumoteca Art. 8](https://consumoteca.com.co/articulo-8-de-la-ley-1480-estatuto-del-consumidor/) ·
  [Ámbito Jurídico](https://www.ambitojuridico.com/noticias/mercantil/si-no-se-indica-el-termino-de-la-garantia-sera-de-un-ano-para-productos-nuevos)
- SIC — [Fallas, baja calidad e incumplimiento de garantías](https://sic.gov.co/fallas-baja-calidad-e-incumplimiento-de-garantias) (503 en esta sesión, URL de referencia oficial)
- DIAN — [Normativa facturación electrónica](https://www.dian.gov.co/impuestos/factura-electronica/documentacion/Paginas/normativa.aspx) ·
  [Resolución 005743/2025 — Rio Consultores](https://rioconsultores.com/2025/07/08/obligacion-de-facturacion-electronica-resolucion-dian-005743-del-29-de-mayo-de-2025/) ·
  [Obligados a facturar — Siigo](https://www.siigo.com/blog/obligaciones-fiscales/quienes-estan-obligados-facturar-electronicamente/)
- IVA — [Siigo — qué es el IVA](https://www.siigo.com/blog/obligaciones-fiscales/que-es-el-iva/) ·
  [Dian.com.co — IVA 2026](https://dian.com.co/iva-colombia-2026/) ·
  [Siigo — productos exentos](https://www.siigo.com/blog/obligaciones-fiscales/productos-y-servicios-exentos-de-iva/)
- Notificaciones — [Klaviyo — Order confirmation](https://www.klaviyo.com/blog/order-confirmation-email-tips-examples) ·
  [Klaviyo — Shipping confirmation](https://www.klaviyo.com/blog/tips-better-shipping-confirmation-emails) ·
  [ShipBob — Order confirmation 101](https://www.shipbob.com/blog/order-confirmation/)
- Envío — [ShipperHQ — Calculate shipping rates](https://shipperhq.com/blog/how-to-calculate-shipping-rates) ·
  [Mageplaza — Table rate shipping](https://www.mageplaza.com/blog/table-rate-shipping-ecommerce.html) ·
  [ShipBob — Calculate shipping costs](https://www.shipbob.com/ecommerce-shipping/calculate-shipping-costs/)
- Mis pedidos — [Baymard — Order Tracking UX](https://baymard.com/blog/integrate-tracking-info) ·
  [Loop Returns — Design best practices](https://www.loopreturns.com/blog/design-best-practices-ecommerce-order-tracking-pages/) ·
  [Shoplazza — Order tracking optimization](https://www.shoplazza.com/blog/practices-for-order-tracking-optimization)
