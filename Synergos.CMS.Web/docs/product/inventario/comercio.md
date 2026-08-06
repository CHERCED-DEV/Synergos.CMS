# Dominio: Comercio

## Resumen ejecutivo

Hoy se puede armar un flujo de e-commerce **real de punta a punta, sin PSP externo**: un
editor publica `productPage` en el CMS, un visitante los busca/filtra (motor de catálogo
transversal `ICatalogIndex<T>`), un comprador (member logueado o invitado) hace checkout,
paga (pasarela *stub* que auto-aprueba, con estado **durable** en disco), la orden
sobrevive un reinicio del CMS, un webhook firmado confirma el pago de forma asíncrona,
el comprador puede pedir una devolución (RMA con reembolso real vía la misma pasarela) y
dejar una reseña si compró de verdad (rating derivado de UGC, nunca hardcodeado). Todo
esto está **verificado end-to-end en los ADRs 0103/0104/0107/0114** con curl + reinicio de
proceso, no es solo diseño en el papel.

Las grietas reales: (1) el panel de ventas (`ICheckoutRecorder`) **nunca se llama** desde
el checkout real, así que el dashboard de ingresos siempre muestra 0 aunque haya ventas
pagadas de verdad; (2) el tracking de envío (`IOrderTrackingService`) vive en memoria del
proceso — un reinicio borra el timeline aunque la orden siga "Paid" en disco, y **no existe
ningún endpoint** para avanzarlo más allá de "pago confirmado" (nunca preparación/envío/
entrega); (3) las devoluciones (`IReturnService`) también viven solo en memoria del
proceso — un RMA "aprobado" desaparece si el CMS reinicia antes de recibir/reembolsar; y
(4) el endpoint que mueve el estado de un RMA (`POST return/{rmaId}/advance`, la acción del
vendedor) **no tiene ningún guard de auth** — cualquiera que conozca un `rmaId` puede
aprobar/rechazar/reembolsar. El pago real (Wompi/PayU) está diseñado y gated mas no
construido ("Ola B").

## Capacidades

### Catálogo (búsqueda/PLP/PDP)

- **Madurez**: VIVO (motor + wiring) con fuente conmutable DEMO↔VIVO por flag.
- **Seams**: `IProductCatalogProvider` (`Synergos.CMS.Interfaces/IProductCatalogProvider.cs`),
  `ICatalogSource<T>` (`ICatalogSource.cs`), `ICatalogIndex<T>` (`ICatalogIndex.cs`),
  `IShopQuery` (`IShopQuery.cs`, usado por los bloques Razor del CMS y por
  `GET products/sku/{sku}`).
- **Implementación**:
  - `StubProductCatalogProvider` (`Synergos.CMS.Application/Services/Impl/StubProductCatalogProvider.cs:25-215`)
    — fachada pura que traduce `ProductQuery`→`CatalogQuery`, delega el matching/facetado/orden
    a `InMemoryCatalogIndex<T>` y compone la fuente que el composer le inyecte.
  - `ShopDemoCatalogSource` (Application) — catálogo **sembrado hardcodeado en C#** (3
    categorías × 6 productos), es el fallback DEMO.
  - `UmbracoProductCatalogSource` (`Synergos.CMS.Web/Services/Catalog/UmbracoProductCatalogSource.cs:26-268`)
    — fuente **VIVA**: lee `productPage` publicados bajo el `siteRoot` cuyo `brandKey`
    coincide con `Synergos:Catalog:Scopes:Shop`; valida el precio con una regla anti-1000×
    (línea 244-267: rechaza cualquier `productPriceBase` que no sea solo dígitos, para que
    "49.000" no se lea como 49); pega el rating/reviews reales vía `ICatalogSocialProof`
    (línea 105, 125-162) **antes** de que el motor calcule facetas — así "4 estrellas o más"
    no queda huérfano.
  - `DefaultShopQuery` (`Synergos.CMS.Web/Services/DefaultShopQuery.cs`) — query síncrona
    plana sobre `productPage` para los bloques Razor SSR (ADR 0028), independiente del motor
    facetado de arriba.
- **Persistencia**: contenido Umbraco (published cache) cuando `Source=cms`; C# en memoria
  cuando `Source=demo`. Sin caché propia (ADR 0107: cada búsqueda relee, así se obtiene
  read-your-writes gratis).
- **Superficie HTTP**: `GET /api/shop/search?q=&category=&brand=&minRating=&sort=&page=&take=`
  (`ShopCatalogController.cs:142-195`, sin auth), `GET /api/shop/product/{id}`
  (línea 224), `GET /api/shop/products/sku/{sku}` (línea 299-338, sin auth).
- **Schema CMS**: `productPage` (`uSync/v9/ContentTypes/productpage.config`, compone
  `compCoreBase`+`compSeo`+`compTagging`+`compCoreLifecycle`), `productCategoryPage`.
- **UI/CDN**: `elementShopProductCard`/`-Grid`/`-Detail` (`<synergos-product-card>`,
  `<synergos-product-grid>`, `<synergos-product-detail>`) — estos son **Razor SSR wrappers**
  (`Views/Partials/blockgrid/Components/elementShopProductCard.cshtml:1-3` solo delega a un
  partial Razor), no bloques CDN vía `IBundleRegistryClient`; el consumo Angular "app" real
  vive aparte en `synergos.ui/platforms/angular/apps/elements/modules/storefront` +
  `apps/domains/shop/*` (product-grid, product-card, cart-item…), que sí habla
  `/api/shop/*` (`shop-api.client.ts`).
- **Flags**: `Synergos:Catalog:Sources:Shop` = `"demo"` (default en código,
  `SeamComposer.cs:307-311`) | `"cms"` (activo en `appsettings.Development.json:106`, junto a
  `Scopes:Shop = "ecommerce"`). En el `appsettings.json` base NO hay sección `Catalog`, así
  que en un deploy sin overrides el flag cae a `demo`. Voltear a `cms` sin `Scopes:Shop`
  configurado sirve catálogo **vacío** (fail-closed, `UmbracoProductCatalogSource.cs:74-85`),
  no un error — el flag SÍ tiene efecto real en ambas posiciones (no es el caso inerte).
- **Tests**: `StubProductCatalogProviderTests.cs` (19), `CatalogEngineReplicationTests.cs`
  (20 — 5 verticales incl. Tienda), `InMemoryCatalogIndexTests.cs`, `CatalogTextTests.cs`
  (10, plegado de tildes/ñ), `ShopCatalogSearchTests.cs` (9, la FRONTERA query-string→seam
  que ADR 0107 dice que faltaba), `CatalogSocialProofEnrichmentTests.cs` (5).
- **Huecos**: `IShopQuery` (bloques Razor SSR) y `IProductCatalogProvider`/motor (API
  Angular) son **dos fuentes de verdad separadas y no sincronizadas** para el mismo
  `productPage` — coexisten a propósito (ADR 0028) pero un editor podría ver el producto en
  una superficie y no en la otra si difieren en cómo cada una filtra publicación/siteRoot.

### Carrito (cart cookie del visitante anónimo)

- **Madurez**: VIVO.
- **Seams**: `ICartService` (`Synergos.CMS.Interfaces/ICartService.cs`).
- **Implementación**: `DefaultCartService` (`Synergos.CMS.Web/Services/DefaultCartService.cs`)
  — cookie `{base64(json)}.{base64(hmacSha256)}`, HMAC con
  `CryptographicOperations.FixedTimeEquals` (anti-tampering), hidrata SKU/variant contra
  `productPage` publicados en cada `GetCart()` (precio nunca confiado a la cookie), y llama
  `_abandonmentTracker.MarkActivity`/`MarkCompleted` en cada mutación (línea 131, 163).
- **Persistencia**: cookie HTTP-only del navegador (sin DB, sin login). `CartSettings`
  (`Synergos.CMS.Application/Configuration/CartSettings.cs`): `CookieName=syn_cart`,
  `SecretKey` default `"synergos-dev-secret-change-me"` (riesgo documentado en ADR 0028 si
  no se sobreescribe en prod), `MaxItems=50`, `Currency=COP`, `CookieLifetimeDays=30`.
- **Superficie HTTP**: `ShopController.cs` — `GET /api/shop/cart`,
  `POST /api/shop/cart/add|update|remove|clear` (sin auth, sin CSRF token —
  idempotentes + HMAC).
- **Schema CMS**: ninguno propio (lee `productPage`).
- **UI/CDN**: `elementShopCartItem`/`elementShopCartSummary` (`<synergos-cart-item>`,
  `<synergos-cart-summary>`) — Razor SSR; `libs/shop/src/lib/cart.store.ts` en Angular UI
  para la app real.
- **Flags**: `Synergos:Cart:*` (ver arriba). No hay flag demo|cms — es la única fuente.
- **Tests**: no se encontró un archivo de test dedicado a `DefaultCartService` en
  `Synergos.CMS.Tests` (no verificado si está cubierto indirectamente por otro test).
- **Huecos**: dos carritos distintos coexisten sin puente — el cookie de `ICartService`
  (bloques Razor) y el `items[]` que el checkout Angular arma manualmente contra
  `IShopOrderService.CheckoutAsync` (no lee la cookie). Un visitante que agregó al cart vía
  bloque Razor y luego usa la app Angular para pagar no ve el mismo carrito.

### Checkout / Motor de órdenes

- **Madurez**: VIVO (persistencia durable, ownership real, anti-tampering verificado
  ADR 0103).
- **Seams**: `IShopOrderService` (`Synergos.CMS.Interfaces/IShopOrderService.cs`).
- **Implementación**: `StubShopOrderService`
  (`Synergos.CMS.Application/Services/Impl/StubShopOrderService.cs:28-412`) — resuelve
  precio/stock REAL del catálogo por línea (líneas 138-176, rechaza precio del cliente),
  aparta stock con `IReservationService.HoldItemAsync` (reusa el motor de Booking con
  `TravelProductType.Hotel` como discriminador neutro, línea 184-193), abre UNA sesión de
  pago por el total, persiste `PersistedOrder` vía `IJsonEntityStore` (línea 231), y
  `ConfirmAsync` es idempotente (línea 236-292: captura pago → confirma reservas → avanza
  tracking → notifica). "Stub" en el nombre es histórico: la lógica de negocio es real,
  solo el PSP detrás es simulado.
- **Persistencia**: `IJsonEntityStore` → `App_Data/syn-orders/{orderRef}.json` (FileSystem,
  escritura atómica temp+`File.Move`, ADR 0103/0105). **Sobrevive reinicio, verificado**.
- **Superficie HTTP**: `POST /api/shop/checkout` (línea 433-484, identidad server-trusted
  si hay sesión — ignora name/email del body; guest checkout sigue abierto),
  `POST /api/shop/confirm` (línea 488-521), `GET /api/shop/orders` (línea 529-554, **requiere
  member**, filtra por `OwnerMemberKey` — cierra el IDOR por email enumerable que existía
  antes de ADR 0103).
- **Schema CMS**: ninguno propio (usa `productPage` + `ProductVariantsJson`).
- **UI/CDN**: consumido por `libs/transaction-engine` (Angular) — checkout/pago no tienen
  bloque `elementShop*` propio en el CMS (es un flujo de "app", no de página editorial).
- **Flags**: ninguno propio; hereda `PaymentsSettings.Provider` (ver Pagos).
- **Tests**: `StubShopOrderServiceTests.cs` (17 — incluye ownership, guest, idempotencia de
  confirm).
- **Huecos**: **`ICheckoutRecorder.Record()` nunca se invoca** desde este flujo (grep
  confirmado: cero call-sites fuera de tests/composer/lectura,
  `Synergos.CMS.Web/Services/DefaultDashboardReadModel.cs:44` lee `GetCheckouts` pero nadie
  escribe) — el panel de ventas (`SalesOverviewVm`) siempre reporta 0 órdenes/revenue aunque
  haya compras Paid reales. Este era el estado documentado en ADR 0097 *antes* de que el
  checkout fuera real; sigue sin cerrarse.

### Pagos

- **Madurez**: DEMO (motor y persistencia reales; el PSP es un simulador que auto-aprueba).
- **Seams**: `IPaymentProvider` (`Synergos.CMS.Interfaces/IPaymentProvider.cs`).
- **Implementación**: `StubPaymentProvider`
  (`Synergos.CMS.Application/Services/Impl/StubPaymentProvider.cs:25-183`) — auto-autoriza
  al crear sesión (o simula `Failed`/`RequiresAction` vía knobs `DeclineTriggerSku`/
  `SimulateRequiresAction`), captura/reembolsa con lock async (`SemaphoreSlim`, línea 40) e
  idempotencia real. NINGÚN adaptador HTTP a un PSP real (Wompi/PayU) existe todavía — las
  llaves en `PaymentsSettings` (línea 47-58) son placeholders sin consumidor.
- **Persistencia**: `IJsonEntityStore` → `App_Data/syn-payments/{sessionId}.json`.
  Sobrevive reinicio (ADR 0104, cierra la brecha que dejó T1).
- **Superficie HTTP**: `POST /api/payments/webhook/{provider}` (`PaymentWebhookController.cs:59-160`,
  `[AllowAnonymous]`, la firma HMAC ES la autorización) — único `provider` conocido hoy:
  `"stub"` (`KnownProviders`, línea 36).
- **Schema CMS**: ninguno.
- **Flags**: `Synergos:Payments:Provider` default `"Stub"` (`PaymentsSettings.cs:17`); rama
  `"Wompi"` está en el composer pero **comentada/sin adapter** — si se apunta a `"Wompi"` sin
  código real, cae de vuelta al stub durable (documentado en ADR 0104: "la demo nunca se
  bloquea"). `WebhookSecret` vacío = firma no exigida para `provider=stub`; para cualquier
  otro provider la firma es obligatoria y sin secreto configurado responde 500
  (`PaymentWebhookController.cs:93-95`).
- **Tests**: `StubPaymentProviderTests.cs` (7), `PaymentWebhookControllerTests.cs` (8).
- **Huecos**: sin adapter real; "Ola B" (Wompi CO) queda explícitamente diferida en
  ADR 0104 hasta tener llaves de sandbox.

### Órdenes — historial / "mis compras"

- **Madurez**: VIVO.
- **Seams**: `IShopOrderService.GetOrdersAsync`/`GetOrdersByMemberAsync`/`GetOrderAsync`.
- **Implementación**: mismo `StubShopOrderService` — `GetOrdersByMemberAsync` filtra por
  `OwnerMemberKey` (línea 326-339), ownership real cerrado tras el fix del claim de member
  en ADR 0103 (`DefaultMemberAccessGate.CurrentMemberKey` resolvía siempre `null` antes).
- **Superficie HTTP**: `GET /api/shop/orders` (requiere member — 401 si anónimo).
- **Huecos**: `GetOrdersAsync(customerEmail)` (por email) sigue existiendo en la interfaz y
  el motor pero **ya no se expone** por HTTP (el controller solo llama la variante por
  member) — es código vivo sin consumidor HTTP directo, mantenido por compatibilidad de
  seam.

### Devoluciones (RMA)

- **Madurez**: DEMO (lógica de negocio real y compone pagos reales, pero el estado del
  caso **no sobrevive reinicio** — a diferencia de órdenes/pagos que sí se durabilizaron).
- **Seams**: `IReturnService` (`Synergos.CMS.Interfaces/IReturnService.cs`).
- **Implementación**: `StubReturnService`
  (`Synergos.CMS.Application/Services/Impl/StubReturnService.cs:27-256`) — máquina de
  estados legal completa (Requested→Approved|Rejected·Approved→Received·Received→Refunded),
  resuelve el monto a reembolsar de la orden real (anti-tampering, línea 94-99), ejecuta
  `IPaymentProvider.RefundAsync` real al llegar a `Refunded` (línea 168-183), y audita cada
  transición vía `IAuditTrailWriter` (línea 230-255). Pero el estado de los casos vive en
  `ConcurrentDictionary<string, ShopReturnCase> _cases` (línea 43) — **en memoria del
  proceso**, sin `IJsonEntityStore` (a diferencia de órdenes/pagos/reservas que sí lo
  tienen).
- **Persistencia**: memoria de proceso. Un reinicio del CMS **borra todos los RMA abiertos**
  aunque la orden y el reembolso ya ejecutado sigan siendo consultables (el reembolso en sí
  sí queda en `App_Data/syn-payments/` porque lo escribe `StubPaymentProvider`, pero el
  registro del *caso* de devolución desaparece).
- **Superficie HTTP**: `POST /api/shop/order/{orderRef}/return` (línea 671-701, requiere
  resolver la orden + `DenyIfForeignMember`), `GET /api/shop/order/{orderRef}/return`
  (línea 703-718, mismo guard), `POST /api/shop/return/{rmaId}/advance` (línea 720-749).
- **Flags**: ninguno.
- **Tests**: `StubReturnServiceTests.cs` (8).
- **Huecos**:
  1. **Sin durabilidad** — `StubReturnService.cs:43` (`ConcurrentDictionary` de proceso,
     nunca pasa por `IJsonEntityStore`). Contradice el patrón que ADR 0105 declaró "único
     seam de persistencia durable" y que ya se aplicó a órdenes/pagos/reservas.
  2. **`POST /api/shop/return/{rmaId}/advance` no tiene NINGÚN guard de autenticación**
     (`ShopCatalogController.cs:720-749` — a diferencia de `RequestReturn`/`ReturnsForOrder`
     que sí llaman `DenyIfForeignMember`, este endpoint no llama `RequireMember` ni ningún
     chequeo de rol/ownership). Es la acción que aprueba/rechaza/marca-recibido/reembolsa —
     hoy cualquiera que adivine o intercepte un `rmaId` (`rma_{guid:N}`, no expuesto
     públicamente pero tampoco protegido en el servidor) puede dispararla, incluido el
     reembolso real contra `IPaymentProvider`.

### Tracking de envío / timeline de orden

- **Madurez**: DEMO (alimentado por eventos reales de pago, pero solo llega a UNA etapa).
- **Seams**: `IOrderTrackingService` (`Synergos.CMS.Interfaces/IOrderTrackingService.cs`) —
  seam GENÉRICO reusado por 8 dominios.
- **Implementación**: `StubOrderTrackingService`
  (`Synergos.CMS.Application/Services/Impl/StubOrderTrackingService.cs:24-167`) — pipeline
  de Tienda declarado (`ShopPipeline`, línea 30-36: pago→preparación→envío→entrega),
  avance idempotente/monotónico. `StubShopOrderService.ConfirmAsync` lo alimenta
  automáticamente a la etapa `"paid"` al confirmar el pago (best-effort,
  `StubShopOrderService.cs:276-285`).
- **Persistencia**: `ConcurrentDictionary<string, TimelineState>` **en memoria del proceso**
  (`StubOrderTrackingService.cs:40`) — sin `IJsonEntityStore`. Un reinicio del CMS borra el
  timeline aunque `ShopOrder.Status` siga `Paid` en disco: la UI de tracking mostraría "sin
  timeline" para una orden que el propio sistema dice pagada.
- **Superficie HTTP**: `GET /api/shop/order/{orderRef}/tracking` (línea 637-664, con
  `DenyIfForeignMember`). **No existe ningún endpoint POST que permita avanzar la etapa**
  más allá de "paid" — ni para el vendedor, ni para un admin, ni para un carrier webhook.
- **Flags**: ninguno.
- **Tests**: `StubOrderTrackingServiceTests.cs` (7).
- **Huecos**: (a) sin durabilidad (mismo patrón que Devoluciones); (b) **el pipeline nunca
  avanza de "Pago confirmado"** en producción — grep confirmado, `_tracking.AdvanceAsync`
  solo se llama una vez, desde `StubShopOrderService.ConfirmAsync` con la etapa `StagePaid`
  (`StubShopOrderService.cs:280-284`). "En preparación"/"Enviado"/"Entregado" son
  alcanzables por la API (`AdvanceAsync` los acepta) pero **nada en el sistema los dispara**
  — es una capacidad de motor completa sin superficie de operador que la use.

### Prueba social (ratings / reseñas)

- **Madurez**: VIVO (contra lo que dice el propio ADR 0114 — el cableado que el ADR dejó
  "pendiente" ya ocurrió en el código actual).
- **Seams**: `ICatalogSocialProof` (`Synergos.CMS.Interfaces/ICatalogSocialProof.cs`).
- **Implementación**: `FileSystemCatalogSocialProof`
  (`Synergos.CMS.Web/Services/FileSystemCatalogSocialProof.cs:25-123`) — una entrada por
  SKU en el store genérico (`resourceType="reviews"`), agregado `{average,count}` **derivado
  en lectura** (nunca almacenado, línea 40-54), ausencia real (`null`) cuando no hay
  reseñas — nunca `{0,0}` (ADR 0112). Idempotente por `AuthorKey` (una reseña por comprador
  y producto). Wiring confirmado:
  `UmbracoProductCatalogSource.cs:35,105,125-162` (pega rating a los productos ANTES de
  facetar) y `ShopCatalogController.cs:62,299-338,359-417` (expone rating en
  `products/sku/{sku}` y el endpoint de alta de reseña).
- **Persistencia**: `IJsonEntityStore` → `App_Data/syn-reviews/{sku}.json` (durable,
  sobrevive reinicio).
- **Superficie HTTP**: `GET /api/shop/products/sku/{sku}` (rating incluido, público),
  `POST /api/shop/products/{sku}/reviews` (línea 359-417, **requiere member** + **exige
  compra verificada**: `HasPurchasedAsync` línea 423-428 comprueba una orden `Paid` real que
  contenga el SKU antes de aceptar la reseña — anti-spam by design).
- **Schema CMS**: ninguno editorial (deliberado — "un editor no autora la reseña de un
  comprador", ADR 0114). `productPage` no tiene campo `rating`.
- **UI/CDN**: `elementSynRatingStars`/`<synergos-rating-stars>` publicado en el registry
  (`vitals/contracts/src/element-registry.json:748-749`, tier `composition`) — no se
  verificó si ya está consumido por `product-card` en el frontend Angular (fuera del
  alcance de este repo CMS).
- **Flags**: ninguno; tooling dev opcional detrás de `Synergos:DevSeed:Enabled` —
  `DevProductReviewSeeder`/`DevPaidOrderSeeder` (`Synergos.CMS.Web/Services/`) siembran
  reseñas/una orden pagada de PRUEBA pasando por el flujo real de checkout, nunca en boot.
- **Tests**: `FileSystemCatalogSocialProofTests.cs` (10), `CatalogSocialProofEnrichmentTests.cs`
  (5), `ShopReviewSubmissionTests.cs` (9, incluye el caso "sin compra → 403").
- **Huecos**: sin cola de moderación (documentado como deliberado en ADR 0114 —
  "capacidad sin consumidor" que se evita a propósito hasta que haya UI de moderador o
  abuso real).

## Flujo end-to-end que HOY funciona (paso a paso, honesto)

1. Arquitecto publica `productPage` con `productSku`/`productPriceBase` (solo dígitos)/
   `productImages` bajo un `siteRoot` con `brandKey` que coincide con
   `Synergos:Catalog:Scopes:Shop`, con `Synergos:Catalog:Sources:Shop=cms`.
2. Visitante llama `GET /api/shop/search?q=...` → ve el producto con facetas reales
   (marca/categoría/rating si tiene reseñas).
3. Member logueado hace `POST /api/shop/checkout` con items — la identidad (nombre/email/
   memberKey) sale del gate de sesión, nunca del body; el motor resuelve precio/stock del
   catálogo real y aparta stock.
4. `POST /api/shop/confirm { orderRef }` → captura el pago stub, confirma las reservas,
   persiste la orden `Paid` en disco, avanza el tracking a "Pago confirmado", envía email
   de confirmación (best-effort).
5. **Se reinicia el proceso del CMS.**
6. `GET /api/shop/orders` (con la misma sesión) → la orden Paid sigue ahí, con su total y
   líneas correctas.
7. El PSP (simulado) llama `POST /api/payments/webhook/stub` con la firma correcta →
   confirmación idempotente, sin doble captura, incluso si `confirm` ya se había llamado
   antes.
8. El comprador pide una devolución: `POST /api/shop/order/{orderRef}/return`, el vendedor
   la aprueba/recibe/reembolsa vía `POST /api/shop/return/{rmaId}/advance` → al llegar a
   `Refunded`, el dinero se reembolsa de verdad contra la sesión de pago real.
9. El comprador dejó una reseña porque tiene una orden Paid con ese SKU → el rating de la
   PLP/PDP cambia de verdad, sin tocar código.

Todo esto está verificado en vivo por los propios ADRs (0103/0104/0107/0114), con curl +
cookie jar + reinicio real del proceso — no es una lectura optimista del código.

## Flujo que NO cierra y por qué

- **El operador nunca ve sus ventas.** El checkout de arriba ocurre de verdad y la orden
  queda "Paid" en disco, pero nadie llama `ICheckoutRecorder.Record()`
  (`DefaultDashboardReadModel.cs` solo LEE `GetCheckouts`, nada escribe). El "panel de
  ventas" (revenue, AOV, serie temporal) del dashboard administrativo reporta 0 para
  siempre, sin importar cuánto se venda por el flujo real.
- **Un pedido nunca "sale de preparación".** El pipeline de tracking llega a "Pago
  confirmado" automáticamente y ahí se queda: no hay endpoint ni proceso que llame
  `IOrderTrackingService.AdvanceAsync` con "preparing"/"shipped"/"delivered". El comprador
  que consulta `GET .../tracking` después del primer día verá exactamente lo mismo que el
  día de la compra.
- **Un reinicio del CMS entre "solicité la devolución" y "me reembolsaron" pierde el
  caso.** A diferencia de orden/pago (durables desde ADR 0103/0104), el estado del RMA
  vive solo en `ConcurrentDictionary` de proceso.
- **El endpoint que ejecuta reembolsos (`return/{rmaId}/advance`) no verifica quién lo
  llama.** No hay guard de rol vendedor/admin ni de ownership — es una acción de back
  office expuesta como si fuera pública.
- **No hay PSP real.** "Stub" no es un nombre cosmético: el único proveedor que existe es
  un simulador que auto-aprueba (con knobs para forzar rechazo/3DS). Cualquier cifra de
  "ventas reales cobradas" hoy es dinero que nunca tocó un banco.

## Tabla de artefactos

| DocType/Schema | Seam | Impl (madurez) | Endpoint | Elemento UI | Madurez capacidad |
|---|---|---|---|---|---|
| `productPage`/`productCategoryPage` | `IProductCatalogProvider` | `StubProductCatalogProvider` + `UmbracoProductCatalogSource`\|`ShopDemoCatalogSource` (VIVO/DEMO por flag) | `GET /api/shop/search`, `GET /api/shop/product/{id}`, `GET /api/shop/products/sku/{sku}` | `synergos-product-card`, `synergos-product-grid`, `synergos-product-detail` | VIVO |
| `productPage` (SSR) | `IShopQuery` | `DefaultShopQuery` (VIVO) | render Razor `ProductPage.cshtml`/`ProductCategoryPage.cshtml` | bloques Razor `Elements/Shop/*` | VIVO |
| — (cookie) | `ICartService` | `DefaultCartService` (VIVO) | `GET/POST /api/shop/cart/*` | `synergos-cart-item`, `synergos-cart-summary` | VIVO |
| — (`App_Data/syn-orders`) | `IShopOrderService` | `StubShopOrderService` (VIVO) | `POST checkout`, `POST confirm`, `GET orders` | `libs/transaction-engine` (Angular) | VIVO |
| — (`App_Data/syn-payments`) | `IPaymentProvider` | `StubPaymentProvider` (DEMO — auto-aprueba) | `POST /api/payments/webhook/{provider}` | — | DEMO |
| — (en memoria) | `IReturnService` | `StubReturnService` (DEMO — sin durabilidad) | `POST/GET order/{ref}/return`, `POST return/{rmaId}/advance` (sin auth guard) | — | DEMO |
| — (en memoria) | `IOrderTrackingService` | `StubOrderTrackingService` (DEMO — solo etapa "paid" alcanzable en la práctica) | `GET order/{orderRef}/tracking` | — | DEMO |
| — (`App_Data/syn-reviews`) | `ICatalogSocialProof` | `FileSystemCatalogSocialProof` (VIVO) | `POST products/{sku}/reviews`, rating embebido en `products/sku/{sku}` | `synergos-rating-stars` | VIVO |
| — | `ICheckoutRecorder` | `FileSystemCheckoutRecorder` (SÓLO SEAM — registrado, nunca invocado desde checkout real) | (solo lectura interna del dashboard) | — | SÓLO SEAM |
| — | `ICartAbandonmentTracker`/`Notifier` | `InMemoryCartAbandonmentTracker` + notifiers (VIVO, alimentado por `DefaultCartService`) | background `CartAbandonmentScannerHostedService` | — | VIVO |
