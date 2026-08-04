using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Proxies.Impl;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Notifications;
using Synergos.CMS.Web.Services;
using Synergos.CMS.Web.Services.Catalog;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Web;

namespace Synergos.CMS.Web.Composers;

public sealed partial class SeamComposer
{
    private void ComposeShop(IUmbracoBuilder builder)
    {
        var services = builder.Services;

        // OLA 2 Tienda — motor del marketplace e-commerce (doc tienda-app-spec).
        // Dos seams stub-first, aditivos (no tocan Booking/Travel ni el carrito
        // cookie IShopQuery/ICartService de los bloques Razor del CMS). ADR 0002
        // (Application pura, sin Umbraco) + ADR 0075 (seam con tests canónicos).
        //   - IProductCatalogProvider: búsqueda facetada (texto + categoría +
        //     facetas → productos + facetas derivadas) + detalle del producto
        //     (PDP: variantes + reviews + Q&A). Stub: catálogo sembrado en memoria
        //     (3 categorías × 6 productos). Adapter real: Examine/Lucene o Algolia.
        //     Singleton — stateless, catálogo estático.
        //   - IShopOrderService: motor transaccional del checkout, calcando
        //     TravelCartService. Resuelve precio/stock real desde el catálogo
        //     (anti-tampering), aparta stock vía IReservationService.HoldItemAsync,
        //     abre UNA sesión de pago (IPaymentProvider) por el total; Confirm
        //     captura y confirma. Idempotente. Singleton — el estado de las órdenes
        //     (orderRef → líneas + sesión) vive en memoria del proceso.
        // T5 Ola A (ADR 0107) — de dónde salen los productos: el seed de demo o el CONTENIDO
        // que autoró el editor. Es EL swap que T5 construyó: cambia la fuente, no el motor ni
        // la fachada ni el controller. Rollback = `Synergos:Catalog:Sources:Shop = demo`, sin
        // redeploy.
        //
        // Singleton, y NO Transient como se temía: UmbracoProductCatalogSource solo sostiene
        // IUmbracoContextAccessor (un ACCESSOR, que resuelve el contexto por llamada — el
        // proyecto ya tiene otro Singleton con él: UmbracoContentContextAccessor), más
        // IOptionsMonitor e ILogger. Ninguno es Scoped, así que no se repite la trampa del
        // gate Singleton que resolvía un servicio Scoped. Además StubShopOrderService es
        // Singleton y captura IProductCatalogProvider por constructor: hacerlo Transient le
        // daría una dependencia cautiva y no cambiaría nada.
        services.AddSingleton<ICatalogSource<CatalogProduct>>(sp =>
        {
            var settings = sp.GetRequiredService<IOptionsMonitor<CatalogSettings>>().CurrentValue;
            var source = settings.Sources.TryGetValue(UmbracoProductCatalogSource.Vertical, out var s) ? s : "demo";
            return string.Equals(source, "cms", StringComparison.OrdinalIgnoreCase)
                ? ActivatorUtilities.CreateInstance<UmbracoProductCatalogSource>(sp)
                : new ShopDemoCatalogSource();
        });

        services.AddSingleton<IProductCatalogProvider>(sp =>
            new StubProductCatalogProvider(
                sp.GetRequiredService<ICatalogSource<CatalogProduct>>(),
                sp.GetRequiredService<IOptionsMonitor<CatalogSettings>>().CurrentValue));

        // OLA 1 Tienda (entrega A, fase T0 del spec tienda.md) — seams de la cara
        // compradora post-venta + cuenta, stub-first y aditivos (ADR 0002 + 0075).
        // Dos son GENÉRICOS (plan doc 21 §1.4 — nacen genéricos, los reusan las
        // 8 olas) y dos componen el motor existente:
        //   - IUserCollection (P11, GENÉRICO): favoritos/wishlist/listas/saved-
        //     searches por Member. itemRef opaco (sku/listingId/courseId…) — un
        //     contrato, N dominios. Singleton — estado en memoria del proceso.
        //   - IOrderTrackingService (P4, GENÉRICO): timeline de estados de una
        //     orden/expediente (Order≈Booking≈Ticket≈Radicado). Pipeline default
        //     de Tienda (pago→preparación→envío→entrega); otros dominios
        //     construyen su instancia con su pipeline. Las órdenes de shop lo
        //     ALIMENTAN: StubShopOrderService avanza a "paid" al confirmar.
        //   - IShopOrderService: mismo motor transaccional de la OLA 2, ahora
        //     construido con el tracking enchufado (factory manual — mismo patrón
        //     StubEventTicketingService) + GetOrderAsync (detalle de mis compras).
        //   - IReturnService (Tienda): devoluciones/reclamos (RMA) sobre una
        //     línea de una orden pagada — máquina solicitada→aprobada/rechazada→
        //     recibida→reembolsada; reembolso vía IPaymentProvider.RefundAsync;
        //     AUDITADO con IAuditTrailWriter (ADR 0037), igual que Gobierno.
        //   - IMessagingService (P7 v1, GENÉRICO): hilos 1:1 comprador↔vendedor
        //     (post-venta). ThreadId determinista por (contexto+par) — sin hilos
        //     duplicados. v1 simple; SH-7 v2/v3 (DM/In Basket) agregan encima.
        // Durabilidad (ADR 0105) — el seam de MAYOR impacto de esta tanda: lo comparten la
        // wishlist de Tienda, los favoritos de Propiedades y los guardados de Blogs. Un
        // reinicio borraba las tres listas de un solo golpe, en tres verticales distintos.
        services.AddSingleton<IUserCollection>(sp =>
            new StubUserCollection(
                null,
                sp.GetRequiredService<IJsonEntityStore>(),
                StubUserCollection.DefaultResourceType));
        // ADR 0116 fase 6 — el timeline pasa a disco. Cada dominio con su
        // ESPACIO propio: los cuatro pipelines tienen distinta longitud y el
        // estado guarda el índice de etapa, así que compartir espacio haría que
        // "enviado" se leyera como "matriculado" sin que nada fallara.
        services.AddSingleton<IOrderTrackingService>(sp =>
            new StubOrderTrackingService(
                StubOrderTrackingService.ShopPipeline, null,
                sp.GetRequiredService<IJsonEntityStore>(), "tracking-shop"));
        // T1 (doc 25) — persistencia durable de órdenes tras el seam genérico IJsonEntityStore.
        // El motor no cambia; solo su backing store pasa de memoria a disco (JSON por
        // orderRef, App_Data/syn-orders/). Una orden confirmada sobrevive un reinicio.
        // HU #24 — contra quién compra la tienda. Dos orígenes, mismo contrato, elegidos por
        // Synergos:Tienda:Mode:
        //   - Stub (default): el motor en proceso. Un clon limpio arranca y vende sin levantar
        //     seis servicios, y por eso el default no se mueve.
        //   - Bff: contra Synergos.Bff.Tienda, que reserva, cobra y crea el pedido — y lo
        //     DESHACE si algo falla a la mitad.
        //
        // Contra el ORQUESTADOR y no contra las capacidades sueltas: si el CMS llamara a
        // Api.Inventory + Api.Payments + Api.Orders por separado estaría reimplementando la
        // máquina de sagas, y peor, porque no tiene dónde anotar una compensación pendiente.
        // Lo vigila ShopWiringTests.
        //
        // Encenderlo sin el BFF arriba degrada —no se puede comprar, y lo dice— pero la tienda
        // sigue sirviendo catálogo y fichas.
        if (string.Equals(builder.Config["Synergos:Tienda:Mode"], "Bff", StringComparison.OrdinalIgnoreCase))
        {
            var tiendaBase = builder.Config["Synergos:Tienda:BaseUrl"];
            var cartBase = builder.Config["Synergos:Tienda:CartBaseUrl"];
            var tiendaKey = builder.Config["Synergos:Tienda:ApiKey"];
            var timeout = int.TryParse(builder.Config["Synergos:Tienda:TimeoutSeconds"], out var t) && t > 0 ? t : 30;

            ConfigurarClienteTienda(services, HttpShopOrderService.BffClientName, tiendaBase, "http://127.0.0.1:5300/", tiendaKey, timeout);
            ConfigurarClienteTienda(services, HttpShopOrderService.CartClientName, cartBase, "http://127.0.0.1:5210/", tiendaKey, timeout);

            services.AddSingleton<IShopOrderService, HttpShopOrderService>();
        }
        else
        {
            services.AddSingleton<IShopOrderService>(sp =>
            new StubShopOrderService(
                sp.GetRequiredService<IProductCatalogProvider>(),
                sp.GetRequiredService<IReservationService>(),
                sp.GetRequiredService<IPaymentProvider>(),
                sp.GetRequiredService<IOrderTrackingService>(),
                sp.GetRequiredService<IJsonEntityStore>(),
                now: null,
                notifier: sp.GetRequiredService<ITransactionalNotifier>(),
                // El read-model del dashboard se alimenta acá: es el único punto
                // por el que una orden llega a Paid. Sin esto el panel de ventas
                // quedaba en $0 con órdenes reales en disco.
                checkoutRecorder: sp.GetRequiredService<ICheckoutRecorder>()));
        }
        services.AddSingleton<IReturnService>(sp =>
            new StubReturnService(
                sp.GetRequiredService<IShopOrderService>(),
                sp.GetRequiredService<IPaymentProvider>(),
                sp.GetRequiredService<IAuditTrailWriter>(),
                null,
                // ADR 0116 fase 6 — los RMA a disco. Vivían en memoria mientras
                // órdenes y pagos ya estaban persistidos: un reinicio borraba
                // la devolución que un comprador ya había pedido.
                sp.GetRequiredService<IJsonEntityStore>()));
        // Durabilidad (ADR 0105): un mensaje directo que alguien ya envió sobrevive el
        // reinicio, que es lo mínimo que un usuario espera de un buzón.
        services.AddSingleton<IMessagingService>(sp =>
            new StubMessagingService(
                null,
                sp.GetRequiredService<IJsonEntityStore>(),
                StubMessagingService.DefaultResourceType));

    }

    /// <summary>Un cliente nombrado hacia el árbol de servicios, con su llave compartida.</summary>
    /// <remarks>
    /// El timeout es generoso a propósito: comprar cruza seis servicios y NO es auxiliar.
    /// Cortarlo pronto no evita el problema —un timeout no dice «no se cobró», dice «no sé»—,
    /// solo lo hace más probable.
    /// </remarks>
    private static void ConfigurarClienteTienda(
        IServiceCollection services, string nombre, string? baseUrl, string porDefecto, string? apiKey, int timeoutSegundos)
    {
        services.AddHttpClient(nombre, http =>
        {
            var url = string.IsNullOrWhiteSpace(baseUrl) ? porDefecto : baseUrl;
            http.BaseAddress = new Uri(url.EndsWith('/') ? url : url + "/");
            http.Timeout = TimeSpan.FromSeconds(timeoutSegundos);
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                http.DefaultRequestHeaders.Add(HttpShopOrderService.ApiKeyHeader, apiKey);
            }
        });
    }
}
