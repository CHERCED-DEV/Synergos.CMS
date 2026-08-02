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

/// <summary>
/// Wires the extension seams declared in
/// <c>Synergos.CMS.Interfaces</c> to their Ola 1 defaults and Ola 3
/// adapters, and registers the dictionary notification handler.
/// </summary>
/// <remarks>
/// Per ADR 0005 all <see cref="IComposer"/> implementations live in
/// <c>Synergos.CMS.Web/Composers/</c>. This composer extracts
/// <c>IOptions&lt;T&gt;.Value</c> and injects the POCO into defaults,
/// honouring the decision taken in Ola 1 not to add
/// <c>Microsoft.Extensions.Options</c> as a reference of
/// <c>Synergos.CMS.Application</c>.
/// </remarks>
[ComposeAfter(typeof(OptionsComposer))]
public sealed class SeamComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        var services = builder.Services;

        // Olas 146-147 — Resilience tuning hot-reloadable. WebhookResilienceSettings
        // bind via AddOptions().Bind(...) registra el IConfigurationChangeTokenSource
        // automático — cuando appsettings.json cambia, IOptionsMonitor<...> fire,
        // y el siguiente handler rotation (HttpClientFactory default ~2min) re-
        // ejecuta Configure<TDep> con los valores frescos. Sin rebuild manual de
        // pipelines.
        services.AddOptions<WebhookResilienceSettings>()
            .Bind(builder.Config.GetSection("Synergos:Admin:WebhookResilience"));

        // Ola 54.1 — IBrandingProvider resuelve el brand activo según el
        // hostname del request, matchéandolo contra los siteConfigSettings
        // publicados (cada uno con su brandKey via compBranding). Si no
        // hay match, fallback al brand de BrandingSettings (config
        // estática). Singleton — depende de accessors per-request.
        services.AddSingleton<IBrandingProvider>(sp =>
            new HostBasedBrandingProvider(
                sp.GetRequiredService<IHttpContextAccessor>(),
                sp.GetRequiredService<IUmbracoContextAccessor>(),
                sp.GetRequiredService<IOptions<BrandingSettings>>().Value));

        services.AddSingleton<IFeatureGate>(sp =>
            new AppsettingsFeatureGate(
                sp.GetRequiredService<IOptions<FeatureFlagsSettings>>().Value));


        // Ola 8.5 + Cap-280 Batch B — SynHost stack (ADR 0015 + 0089).
        // El IBundleRegistryClient se registra condicionalmente por
        // Synergos:BundleRegistry:Mode (default Stub):
        //   - Stub: siempre null. Default cuando no hay CDN.
        //   - FileSystem: lee registry.json + manifests del filesystem,
        //     hot-reload via FileSystemWatcher, SRI lazy compute.
        //     Útil con CDN local (e.g. C:\LOCAL_CDN).
        //   - Http: GET al registry endpoint remoto (deferred).
        var bundleRegistryMode = builder.Config["Synergos:BundleRegistry:Mode"] ?? "Stub";
        if (string.Equals(bundleRegistryMode, "FileSystem", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IBundleRegistryClient, FileSystemBundleRegistryClient>();
        }
        else
        {
            services.AddSingleton<IBundleRegistryClient, StubBundleRegistryClient>();
        }
        services.AddSingleton<ISynHostEmitter, DefaultSynHostEmitter>();

        // Warmup hosted service fuerza la construcción del IBundleRegistryClient
        // singleton al boot — sin esto el adapter queda lazy hasta el primer
        // SynHost render, ocultando logs de "registry loaded" + watcher status.
        services.AddHostedService<BundleRegistryWarmupHostedService>();

        // Ola 3 — Web-side adapters.
        services.AddSingleton<IContentContextAccessor, UmbracoContentContextAccessor>();

        // Ola 37 — Brand theme provider reads themeSettings nodes from
        // the Umbraco content tree (ADR 0020). Transient because it
        // depends on the per-request IUmbracoContextAccessor; the cost
        // is negligible (single projection).
        services.AddTransient<IBrandThemeProvider, DefaultBrandThemeProvider>();

        // Ola 49 — Page render context resolver. Cascada page →
        // siteRoot → defaults inline para chrome/theme (ADR 0022).
        // Transient por la misma razón que el theme provider.
        services.AddTransient<IPageRenderContextResolver, DefaultPageRenderContextResolver>();

        // Ola 50 — Global component resolver. Lee siteConfigSettings.
        // globalComponents (BlockList) y devuelve la pieza activa
        // aplicable. Ola 52.A extendió a 4 cfg* (Alert/Banner/
        // FooterNote/Modal). Cada cfg* nuevo añade un método hermano
        // en el resolver sin tocar la lógica existente.
        services.AddTransient<IGlobalComponentResolver, DefaultGlobalComponentResolver>();

        // Ola 52.C — Member access gate + handler que aplica
        // compMemberGating.requiresAuth + allowedRolesCsv en el
        // routing del request. Singleton porque el gate solo lee
        // del HttpContext via accessor.
        services.AddSingleton<IMemberAccessGate, DefaultMemberAccessGate>();

        // Ola 56.2 — Blog query service. Recorre el published cache
        // buscando postPage descendientes del siteRoot, aplica filtros
        // de categoría/tags y proyecta a PostSummary records.
        // Consumido por renderers ArticleList, BlogHighlight y
        // PostCategoryPage.cshtml. Transient — depende del
        // IUmbracoContextAccessor per-request.
        services.AddTransient<IBlogQuery, DefaultBlogQuery>();

        // Ola 57.1 — Cart service. Persiste cart en cookie HMAC-firmada
        // del visitante (sin DB, sin login required). Hidrata items
        // cruzando SKUs con productPage publicados. Transient porque
        // depende de IHttpContextAccessor + IUmbracoContextAccessor.
        services.AddTransient<ICartService, DefaultCartService>();

        // Ola 81 — Cart abandonment tracker (ADR 0044). Singleton por
        // diseño — el state in-memory persiste entre requests del
        // mismo proceso. Background scanner emite "cart.abandoned"
        // events via IAnalyticsTracker cada N minutos para los carts
        // que excedan el threshold.
        services.AddSingleton<ICartAbandonmentTracker, InMemoryCartAbandonmentTracker>();
        services.AddHostedService<CartAbandonmentScannerHostedService>();

        // Ola 57.2 — Shop query service. Recorre productPage descendientes,
        // aplica filtros de categoría/sort y proyecta a ProductSummary.
        // Consumido por ProductGrid block + ProductCategoryPage.cshtml.
        services.AddTransient<IShopQuery, DefaultShopQuery>();

        // Bucket B (B-1) — Price formatter es-CO centralizado. Unifica el
        // render del precio (miles con punto, sin decimales, + moneda) que
        // antes se duplicaba inline en 6+ renderers Razor de Shop. Lógica
        // pura en Application; el composer extrae IOptions<CartSettings>.Value
        // y la inyecta como POCO (mismo patrón que AppsettingsFeatureGate)
        // para no referenciar Microsoft.Extensions.Options desde Application.
        // Singleton — stateless, solo cierra sobre la moneda default.
        services.AddSingleton<IPriceFormatter>(sp =>
            new EsCoPriceFormatter(
                sp.GetRequiredService<IOptions<CartSettings>>().Value));

        // Motor de pago (PSP) — seam IPaymentProvider, stub-first (doc 16/17).
        // T3 (doc 25): DOS ejes ortogonales.
        // (1) DURABILIDAD: el estado de las sesiones de pago pasa de memoria a disco
        //     tras el seam GENÉRICO IJsonEntityStore + el ledger de idempotencia GENÉRICO
        //     (IIdempotencyLedger, tambien usado por T4 notificaciones). Registro INCONDICIONAL → hasta el stub queda
        //     durable y una sesión sobrevive un reinicio (cierra el confirm-tras-reinicio).
        // (2) SELECCIÓN de proveedor: config-gated por Synergos:Payments:Provider,
        //     calcando el gating de BundleRegistry.Mode. Ola A: solo "Stub" durable
        //     está vivo; "Wompi" es Ola B (adapter HTTP real con llaves de sandbox) —
        //     sin adapter construido cae al stub durable (la demo nunca se bloquea).
        // Store JSON durable GENÉRICO — la ÚNICA impl de persistencia del proyecto.
        // Sirve a TODAS las familias por resourceType (orders/payments/reservations/
        // travel-orders → App_Data/syn-{resourceType}/). Colapsa los 4 stores dedicados
        // que T1/T3/Booking habían duplicado (regla de oro doc 25).
        services.AddSingleton<IJsonEntityStore, FileSystemJsonEntityStore>();
        services.AddSingleton<IIdempotencyLedger, FileSystemIdempotencyLedger>();

        // T10 (doc 26) — prueba social del catálogo: las reseñas son UGC y el rating se
        // DERIVA de ellas. Sobre el mismo store genérico (resourceType "reviews"), no un
        // store dedicado (ADR 0105). Registrado, pero AÚN SIN CONSUMIRSE: el cableado
        // (UmbracoProductCatalogSource → ProductSummary → ProductBySkuDto) es el paso
        // siguiente, y hasta que ocurra el catálogo sigue emitiendo Rating: 0d.
        services.AddSingleton<ICatalogSocialProof, FileSystemCatalogSocialProof>();

        // T4 (doc 25) — notificaciones transaccionales: UN dispatcher transversal para los
        // 6 hechos de los 5 verticales (regla de oro: no un notifier por dominio). Los
        // CANALES se registran primero y el composite AL FINAL (calca IAlertNotifier
        // :527-536). SMS/push = un AddSingleton más, sin tocar motores.
        services.AddSingleton<ITransactionalNotifierChannel, EmailTransactionalNotifier>();
        services.AddSingleton<ITransactionalNotifier, CompositeTransactionalNotifier>();

        // ADR 0116 — el motor de pago admite N proveedores detrás del MISMO
        // contrato. Los adapters concretos se registran siempre que tengan
        // llaves; quién cobra cada petición lo decide el router.
        //
        // Con Routing:Enabled=false (default) se registra un proveedor único y
        // el comportamiento es idéntico al de antes: el router es capacidad
        // nueva, no un peaje obligatorio.
        var paymentProvider = builder.Config["Synergos:Payments:Provider"] ?? "Stub";
        var routingEnabled = builder.Config.GetValue<bool>("Synergos:Payments:Routing:Enabled");

        // Named client de Wompi. Sandbox y producción se distinguen SÓLO por la
        // base: las llaves ya vienen con su prefijo (pub_test_ / pub_prod_), así
        // que apuntar a producción con llaves de prueba falla en Wompi y no en
        // silencio. Hereda la resiliencia que el repo ya aplica a sus 12
        // clientes (ADR 0064/0069).
        services.AddHttpClient("wompi", (sp, http) =>
        {
            var settings = sp.GetRequiredService<IOptions<PaymentsSettings>>().Value;
            http.BaseAddress = new Uri(
                string.IsNullOrWhiteSpace(settings.WompiApiBaseUrl)
                    ? "https://sandbox.wompi.co/v1/"
                    : settings.WompiApiBaseUrl);
            if (!string.IsNullOrWhiteSpace(settings.WompiPrivateKey))
            {
                http.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue(
                        "Bearer", settings.WompiPrivateKey);
            }
        });

        if (routingEnabled)
        {
            services.AddSingleton<IPaymentProvider>(sp =>
            {
                var settings = sp.GetRequiredService<IOptions<PaymentsSettings>>().Value;
                var store = sp.GetRequiredService<IJsonEntityStore>();

                // El stub siempre está disponible: es el destino de rollback si
                // un adapter real se cae o si una regla lo manda a "stub".
                var members = new List<IPaymentProvider> { new StubPaymentProvider(store, settings) };

                // Wompi entra sólo si TIENE llaves. Registrarlo sin ellas
                // dejaría que una regla lo eligiera y reventara a mitad del
                // checkout; así, una config a medias degrada al stub en vez de
                // romper la compra.
                if (!string.IsNullOrWhiteSpace(settings.WompiPublicKey)
                    && !string.IsNullOrWhiteSpace(settings.WompiIntegritySecret))
                {
                    members.Add(new WompiPaymentProvider(
                        sp.GetRequiredService<IHttpClientFactory>().CreateClient("wompi"),
                        settings));
                }

                var rules = settings.Routing.Rules
                    .Where(r => !string.IsNullOrWhiteSpace(r.ProviderKey))
                    .Select(r => new PaymentRoutingRule(
                        r.ProviderKey, r.Vertical, r.CountryCode, r.Currency, r.Method))
                    .ToList();

                return new RoutingPaymentProvider(
                    members, rules, settings.Routing.DefaultProviderKey);
            });
        }
        else
        {
            switch (paymentProvider.ToLowerInvariant())
            {
                // case "wompi":  // requiere WompiPaymentProvider + llaves (fase 3).
                //     services.AddSingleton<IPaymentProvider, WompiPaymentProvider>();
                //     break;
                default:
                    services.AddSingleton<IPaymentProvider>(sp =>
                        new StubPaymentProvider(
                            sp.GetRequiredService<IJsonEntityStore>(),
                            sp.GetRequiredService<IOptions<PaymentsSettings>>().Value));
                    break;
            }
        }

        // ADR 0116 fase 4 — los verticales que quieren enterarse de un cobro
        // asíncrono. Cada sink dice si la referencia es suya; agregar un
        // vertical es una línea más acá y nada en el receptor.
        //
        // Tienda y Viajes: los dos verticales durables con confirmación
        // idempotente y compensación ya corregida (fase 5). Viajes importa
        // especialmente porque su carrito se paga a menudo por PSE, y con PSE el
        // resultado sólo llega por evento.
        //
        // Eventos y Educación quedan fuera todavía: sus seams no tienen búsqueda
        // por referencia de orden, así que un sink no podría siquiera decir si el
        // pago es suyo. Añadir ese método es lo siguiente.
        services.AddSingleton<IPaymentEventSink, ShopPaymentEventSink>();
        services.AddSingleton<IPaymentEventSink, TravelPaymentEventSink>();

        // Motor de reservas (vertical Hoteles) — 3 seams stub-first (doc 17),
        // calcando IPaymentProvider. Hoy sirven la demo end-to-end en memoria;
        // se cambian por adapters reales (PMS / channel-manager) sin tocar el
        // motor. ADR 0002 (Application pura, sin Umbraco).
        //   - IRoomAvailabilityProvider: search por fecha+ocupación → ofertas
        //     (Room Type × Rate Plan). Stub: catálogo sembrado en memoria.
        //   - IReservationService: Hold/Confirm/Cancel/Get. Stub: estado en
        //     ConcurrentDictionary; Confirm idempotente. Singleton para que el
        //     estado persista entre requests del mismo proceso.
        //   - ICancellationPolicyEvaluator: penalidad por fecha. Stub puro/
        //     determinista (non-refundable → total; refundable → 0 si a tiempo).
        services.AddSingleton<IRoomAvailabilityProvider, StubRoomAvailabilityProvider>();
        // T3 (doc 25): el hold de reservas pasa de memoria a disco tras el seam
        // IJsonEntityStore (durable, resourceType 'reservations'). Necesario para cerrar el restart-gap e2e —
        // confirmar una orden tras un reinicio confirma sus reservas, que antes se
        // perdían con el proceso ("Reserva no encontrada"). Reusable por Booking/Eventos.
        services.AddSingleton<IReservationService>(sp =>
            new StubReservationService(
                StubReservationService.DefaultHoldWindow,
                now: null,
                sp.GetRequiredService<IJsonEntityStore>()));
        services.AddSingleton<ICancellationPolicyEvaluator, StubCancellationPolicyEvaluator>();

        // Auto-cancel de holds vencidos (aprendizaje NS.Booking, doc 17): barre
        // cada ~2 min los Held cuyo ExpiresAt pasó y libera el cupo (→ Expired).
        services.AddHostedService<HoldExpirationScannerHostedService>();

        // Motor de vuelos (vertical Aerolíneas, doc 18) — seam stub-first
        // calcando IRoomAvailabilityProvider de Hoteles. StubFlightAvailability
        // Provider (Application, puro/determinista) sirve un catálogo sembrado
        // en memoria (rutas BOG-MDE/CTG/MIA × itinerarios × familias tarifarias)
        // para que la búsqueda corra end-to-end en demo; el adapter real
        // (GDS / NDC con cotización en vivo) se enchufa sin tocar el motor.
        // El flujo hold/pay/confirm reusará IReservationService/IPaymentProvider
        // (generalizar ReservationRequest hotel→genérico es follow-up).
        // Singleton — stateless, catálogo estático. ADR 0002 (Application pura).
        services.AddSingleton<IFlightAvailabilityProvider, StubFlightAvailabilityProvider>();

        // OLA 1 Booking — motor transaccional multi-producto (doc booking-app-spec).
        // Tercer producto del carrito de viaje: autos. Seam stub-first calcando
        // IFlightAvailabilityProvider. StubCarRentalProvider (Application, puro/
        // determinista) sirve un catálogo sembrado (categorías SIPP × rentadoras)
        // para que la búsqueda corra end-to-end; el adapter real (agregador de
        // rentadoras) se enchufa sin tocar el motor. Singleton — stateless.
        services.AddSingleton<ICarRentalProvider, StubCarRentalProvider>();

        // OLA 1 Booking — carrito de viaje multi-producto: toma N ítems
        // heterogéneos (hotel|vuelo|auto), aparta CADA uno como reserva
        // (IReservationService.HoldItemAsync, vía polimórfica aditiva que NO toca
        // el flujo hotel) y abre UNA sola sesión de pago (IPaymentProvider) por el
        // total. ConfirmAsync captura el pago y confirma todas las reservas.
        // Idempotente. Singleton — el estado del carrito (orderRef → reservas +
        // sesión) vive en memoria del proceso, igual que el StubReservationService.
        //
        // OLA 2 Booking (doc 21 §2.2) — cara viajera: el carrito ahora registra el
        // guest del checkout ("Mis viajes" por email), expone GetTrips/GetOrder/
        // CancelOrder (MMB v1: cancela reservas + refund si capturado, idempotente)
        // y ALIMENTA su timeline de viaje al confirmar. El tracker de viaje es una
        // instancia PROPIA del seam genérico IOrderTrackingService con el pipeline
        // travel (paid→confirmed→upcoming→completed) — NO reusa el singleton de
        // Tienda, cuyo pipeline es pago→preparación→envío→entrega.
        // Fan-out de T1 (doc 25): el estado del carrito de viaje pasa de memoria a disco
        // tras el seam genérico IJsonEntityStore (durable, resourceType 'travel-orders'). Con esto un carrito confirmado
        // sobrevive un reinicio — las reservas y el pago ya lo hacían por T3.
        services.AddSingleton<ITravelCartService>(sp =>
            new TravelCartService(
                sp.GetRequiredService<IReservationService>(),
                sp.GetRequiredService<IPaymentProvider>(),
                new StubOrderTrackingService(TravelCartService.TravelPipeline, null,
                    sp.GetRequiredService<IJsonEntityStore>(), "tracking-travel"),
                null,
                sp.GetRequiredService<IJsonEntityStore>(),
                notifier: sp.GetRequiredService<ITransactionalNotifier>(),
                // ADR 0125: la cancelación es anónima —el orderRef es la credencial— y a la
                // vez destructiva y con movimiento de plata. El rastro es lo que permite
                // responder "¿quién canceló este viaje y cuándo?" sin romper la compra de
                // invitado: no pide sesión, solo deja constancia.
                audit: sp.GetRequiredService<IAuditTrailWriter>()));

        // OLA 2 Booking — ficha de estadía rica (galería/amenities/specs/geo/
        // reviews) separada de la disponibilidad (IRoomAvailabilityProvider
        // intacto). Stub: 6 propiedades CO sembradas (Cartagena/Medellín/Bogotá/
        // Eje Cafetero) con geo real; 4 "hospedan" los room types del stub de
        // disponibilidad para que el search de hotel emita lat/lng por oferta
        // (mapa SH-8). Adapter real: contenido CMS / channel-manager. Singleton —
        // stateless, catálogo estático.
        // Rebanada de contenido (ADR 0119) — Booking era el último vertical con catálogo y sin
        // ninguna superficie CMS: cuatro estadías sembradas en C# y ningún sitio donde un
        // hotelero publicara la quinta. Con Synergos:Catalog:Sources:Booking = cms la ficha
        // sale de los stayListing que autoró el editor; el rollback es esa línea a 'demo'.
        //
        // Esta seam es de SOLO LECTURA (GetStayAsync y nada más), así que —a diferencia de
        // Eventos e Inmobiliaria— no lleva capa durable encima: no hay nada que publicar desde
        // la app que haya que persistir aparte.
        services.AddSingleton<ICatalogSource<StayDetail>>(sp =>
            ActivatorUtilities.CreateInstance<UmbracoStayContentSource>(sp));
        services.AddSingleton<IStayContentProvider>(sp =>
            IsCmsSource(sp, UmbracoStayContentSource.Vertical)
                ? new CatalogStayContentProvider(sp.GetRequiredService<ICatalogSource<StayDetail>>())
                : new StubStayContentProvider());

        // Mapa de asientos (ADR 0127) — proveedor EXÓGENO. El CMS configura QUÉ mapa se
        // muestra y cómo se ve; el inventario de butacas —cuáles existen, cuáles están libres,
        // a qué precio— lo publica quien lo conoce. Autorar butacas en un backoffice no es un
        // modelo de contenido: es una hoja de cálculo que se desincroniza en el primer vuelo.
        //
        // El default emula una cabina real (fila 13 ausente, columna I ausente, pasillo donde
        // lo dicta la distribución, secciones por clase, filas de salida, butacas bloqueadas) y
        // es DETERMINISTA: la misma referencia da siempre el mismo mapa, para que una demo se
        // pueda grabar y un test no sea intermitente. Singleton — sin estado, catálogo fijo.
        services.AddSingleton<ISeatMapProvider, StubCabinSeatMapProvider>();

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

        // OLA 3 Blogs — red social (doc blogs-app-spec). Seams stub-first, aditivos
        // (no tocan Booking/Travel/Shop). ADR 0002 (Application pura, sin Umbraco) +
        // ADR 0075 (seam con tests canónicos). Reusa ICommentRepository EXISTENTE
        // para los comentarios del post (BlogsController deriva un nodeId estable del
        // id string del post) — no se crea otro seam de comentarios.
        //   - IContentStream: ABSTRACCIÓN REUSABLE de feed/contenido (Actor-Verb-Object,
        //     ActivityStreams 2.0). Genérica por Kind (post|article|lesson…) para que
        //     EDUCACIÓN la reuse por polimorfismo (filtrando Kind=lesson) sin instanciar
        //     Blogs ni copiar su schema. El stub compone ISocialGraphService (feed
        //     "Siguiendo") + IReactionService (métricas) — DIP, no duplica estado.
        //   - ISocialGraphService: grafo dirigido asimétrico (follow/unfollow idempotente,
        //     followers/following + conteos). Estado en memoria del proceso.
        //   - IReactionService: reacciones/likes por item (toggle idempotente por
        //     (actor,objeto), conteos por tipo + estado-por-usuario). Estado en memoria.
        //   - ISocialProfileProjection: Member → perfil social (handle/bio/banner) para
        //     el header del perfil. Stub sobre el catálogo sembrado.
        // Durabilidad (ADR 0105): el grafo, las reacciones y los posts creados viven tras el
        // store con namespace propio. El stub de ContentStream pide el StubReactionService
        // concreto para leer conteos en UNA pasada: registramos el concreto y lo exponemos
        // bajo la interfaz (composición manual).
        //
        // La siembra NO se escribe en boot (ADR 0013): un documento ausente cae al seed, y la
        // primera mutación escribe uno que desde entonces GANA sobre él. Es lo que hace que
        // dejar de seguir a alguien sembrado sobreviva un reinicio en vez de re-sembrarse.
        //
        // INotificationFeed y ISocialProfileProjection siguen igual a propósito: ninguno
        // guarda estado propio. El feed de notificaciones se DERIVA del grafo y de las
        // reacciones, así que hacer durables esos dos lo hizo durable por composición; darle
        // store propio duplicaría justo el estado que existe para no duplicar.
        services.AddSingleton(sp =>
            new StubReactionService(
                sp.GetRequiredService<IJsonEntityStore>(),
                StubReactionService.DefaultResourceType));
        services.AddSingleton<IReactionService>(sp => sp.GetRequiredService<StubReactionService>());
        services.AddSingleton<ISocialGraphService>(sp =>
            new StubSocialGraphService(
                sp.GetRequiredService<IJsonEntityStore>(),
                StubSocialGraphService.DefaultResourceType));
        services.AddSingleton<IContentStream>(sp =>
            new StubContentStream(
                sp.GetRequiredService<ISocialGraphService>(),
                sp.GetRequiredService<StubReactionService>(),
                null,
                sp.GetRequiredService<IJsonEntityStore>(),
                StubContentStream.DefaultResourceType));
        services.AddSingleton<ISocialProfileProjection, StubSocialProfileProjection>();

        // OLA 6 Blogs (doc 21 §2.3) — app social completa sobre el motor social ya
        // vivo. Aditivo (no toca los otros dominios). El único seam NUEVO es el
        // centro de notificaciones; el resto reusa lo existente:
        //   - INotificationFeed: DERIVA notificaciones dirigidas (follow/reacción/
        //     mención) del grafo + reacciones vivos — sin store paralelo (compone el
        //     StubReactionService concreto para leer las reacciones por objeto, misma
        //     técnica que StubContentStream). Stateless → singleton.
        //   - DMs reusan IMessagingService (contexto "dm"); guardados reusan
        //     IUserCollection (colección "saved"); explore/trending + long-form
        //     (Kind=article) reusan IContentStream + IReactionService; el studio
        //     compone grafo + reacciones. Todo vía BlogsController.
        // La data de demo de DMs/guardados vive en seams GENÉRICOS compartidos con
        // otros dominios, así que se siembra al boot desde un hosted service (no en
        // el ctor de la mensajería genérica) — idempotente.
        services.AddSingleton<INotificationFeed>(sp =>
            new StubNotificationFeed(
                sp.GetRequiredService<ISocialGraphService>(),
                sp.GetRequiredService<StubReactionService>()));
        services.AddHostedService<BlogsDemoSeedHostedService>();

        // OLA 4 Educación — LMS (doc educacion-app-spec). Dos seams stub-first,
        // aditivos (no tocan Booking/Travel/Shop/Blogs). ADR 0002 (Application pura,
        // sin Umbraco) + ADR 0075 (seam con tests canónicos).
        //   - ICourseCatalogProvider: catálogo buscable (texto/categoría/nivel →
        //     cursos) + detalle del curso (PDP: módulos → lecciones + instructor +
        //     planes de precio/cuotas). Stub: catálogo sembrado (3 categorías ×
        //     cursos × módulos × lecciones). Adapter real: Examine sobre coursePage.
        //     POLIMORFISMO Blogs: el contenido editorial de cada lección NO se
        //     duplica — se SIEMBRA en el IContentStream EXISTENTE con Kind=lesson y
        //     se referencia por id (DIP; depende de la abstracción de feed, no del
        //     módulo Blogs ni de su schema; no instancia Blogs). Por eso el catálogo
        //     recibe el IContentStream ya registrado arriba.
        //   - IEnrollmentService: motor transaccional de matrícula + progreso,
        //     calcando StubShopOrderService/StubReservationService. Resuelve el
        //     precio real desde el catálogo (anti-tampering), abre UNA sesión de
        //     pago (IPaymentProvider) si el curso es de pago o activa la matrícula
        //     de inmediato si es gratis; Confirm captura y activa. Añade lo propio
        //     del LMS: progreso por lección (idempotente) + certificado al 100%.
        // OLA 5 Educación (doc 21 §2.4) — app completa: certificado verificable
        // público (seam DEDICADO ICertificateService), panel del instructor
        // (GetForInstructorAsync con métricas + PublishCourseAsync al catálogo) y
        // tracking del ciclo de aprendizaje (enrolled→in-progress→completed). El
        // motor de matrícula ahora ALIMENTA su timeline de aprendizaje (instancia
        // PROPIA del seam genérico IOrderTrackingService con AcademyPipeline — NO
        // reusa el singleton de Tienda, cuyo pipeline es de envío) y expone la cara
        // de lectura IEnrollmentMetrics (alumnos/ingresos por curso) que el catálogo
        // COMPONE para el panel (DIP, sin duplicar el estado de matrículas). El
        // catálogo recibe ese seam por property injection DESPUÉS de construir ambos
        // singletons, para no crear un ciclo catálogo↔matrícula en el ctor.
        //
        // Singletons — el estado (matrículas + progreso + lecciones sembradas +
        // cursos publicados + timelines) vive en el proceso, igual que el resto de
        // stubs del motor.
        services.AddSingleton<StubCourseCatalogProvider>(sp =>
            new StubCourseCatalogProvider(sp.GetRequiredService<IContentStream>()));
        services.AddSingleton<ICourseCatalogProvider>(sp => sp.GetRequiredService<StubCourseCatalogProvider>());
        services.AddSingleton<StubEnrollmentService>(sp =>
        {
            // Durabilidad (doc 25): el estado de matrículas + progreso vive tras el store
            // genérico (resourceTypes "enrollments"/"course-progress") → sobrevive un reinicio.
            var enrollment = new StubEnrollmentService(
                sp.GetRequiredService<ICourseCatalogProvider>(),
                sp.GetRequiredService<IPaymentProvider>(),
                new StubOrderTrackingService(StubEnrollmentService.AcademyPipeline, null,
                    sp.GetRequiredService<IJsonEntityStore>(), "tracking-academy"),
                sp.GetRequiredService<IJsonEntityStore>(),
                null,
                notifier: sp.GetRequiredService<ITransactionalNotifier>());
            // Enchufa la cara de lectura en el catálogo (DIP) para el panel del
            // instructor — se resuelve tras construir ambos singletons.
            sp.GetRequiredService<StubCourseCatalogProvider>().EnrollmentMetrics = enrollment;
            return enrollment;
        });
        services.AddSingleton<IEnrollmentService>(sp => sp.GetRequiredService<StubEnrollmentService>());
        services.AddSingleton<IEnrollmentMetrics>(sp => sp.GetRequiredService<StubEnrollmentService>());
        // Certificado verificable — seam dedicado, compone catálogo + motor.
        //
        // ADR 0124: el id lo FIRMA ICertificateIdSigner (HMAC-SHA256 con llave del servidor,
        // persistida cifrada por CertificateSigningKeyProvider, calcando el firmante de los
        // e-tickets). Antes era un FNV-1a de 31 bits SIN secreto sobre "curso|alumno": quien
        // supiera un id de curso —que es público— y adivinara un correo calculaba el id del
        // certificado de esa persona. Con la verificación abierta al público, eso no habría
        // sido una credencial sino un padrón consultable de quién estudió qué.
        //
        // El índice de emitidos pasa además a IJsonEntityStore (familia "certificates") para
        // que un QR impreso en un diploma siga verificando después de un reinicio.
        services.AddSingleton<CertificateSigningKeyProvider>();
        services.AddSingleton<ICertificateIdSigner, LazyCertificateIdSigner>();
        services.AddSingleton<ICertificateService>(sp =>
            new StubCertificateService(
                sp.GetRequiredService<ICourseCatalogProvider>(),
                sp.GetRequiredService<IEnrollmentService>(),
                sp.GetRequiredService<ICertificateIdSigner>(),
                null,
                sp.GetRequiredService<IJsonEntityStore>()));

        // Ola 59.1 — Boot-time guard: log Critical si CartSettings.SecretKey
        // sigue en su valor default bajo env != "Development". No detiene
        // el app; solo señala en el log para que el operador rote la clave
        // antes de exponer al público (ver ADR 0028 — TODO cerrado).
        services.AddHostedService<CartSecretKeyValidationHostedService>();

        // Ola 60 — Forms internal submission path (ADR 0030).
        // FileSystemFormSubmissionHandler escribe JSON por submission a
        // App_Data/syn-form-submissions/{formKey}/. Para producción de
        // volumen, swap por adapter sobre queue/email — la seam
        // IFormSubmissionHandler aisla al controller del storage.
        // InMemoryFormRateLimiter mantiene sliding window por (IP, formKey)
        // — singleton para que el estado persista entre requests del
        // mismo proceso.
        // Ola 115 — el FileSystem handler implementa AMBAS interfaces
        // (write + read). Composer registra una sola instancia bajo los
        // 2 contratos. Para adapters fire-and-forget (queue/webhook) el
        // arquitecto registra otro IFormSubmissionHandler y deja el
        // reader como NoOp o swap por DB-backed reader.
        services.AddSingleton<FileSystemFormSubmissionHandler>();
        services.AddSingleton<IFormSubmissionHandler>(sp => sp.GetRequiredService<FileSystemFormSubmissionHandler>());
        services.AddSingleton<IFormSubmissionReader>(sp => sp.GetRequiredService<FileSystemFormSubmissionHandler>());
        services.AddSingleton<InMemoryFormRateLimiter>();

        // Definición del formulario (qué campos declaró el autor), para que el endpoint de
        // envío pueda EXIGIR los obligatorios. Singleton como el resto: no guarda estado, lee
        // el published cache en cada llamada vía IUmbracoContextAccessor.
        services.AddSingleton<IFormDefinitionReader, UmbracoFormDefinitionReader>();

        // Ola 61 — Search infrastructure (ADR 0031). ExamineSearchProvider
        // usa el ExternalIndex out-of-the-box de Umbraco (Examine 3.1.0)
        // y reproyecta los hits cruzando con el published cache para
        // resolver URL/cultura/siteRoot consistentes. Transient porque
        // depende de IUmbracoContextAccessor per-request.
        services.AddTransient<ISearchQuery, ExamineSearchProvider>();

        // Ola 86 — Search analytics store (ADR 0045). InMemory persiste
        // top queries + no-result queries en ConcurrentDictionary.
        // Singleton para compartir state entre requests del mismo proceso.
        services.AddSingleton<ISearchAnalyticsStore, FileSystemSearchAnalyticsStore>();

        // Ola 64 — Member self-service (ADR 0034). DefaultMemberAuthService
        // wraps IMemberManager + IMemberSignInManager para Register/Login/
        // Logout/ChangePassword. AccountController + Razor templates
        // consumen este seam. Transient porque IMemberManager/SignInManager
        // son scoped (per-request).
        services.AddTransient<IMemberAuthService, DefaultMemberAuthService>();

        // Olas 144-145 — Member roster admin (ADR 0060). UmbracoMemberRosterReader
        // wraps IMemberService + IMemberGroupService para listing/filter de
        // Members en /admin/members. Transient porque depende de scoped
        // services Umbraco (IMemberService es scoped per-request).
        services.AddTransient<IMemberRosterReader, UmbracoMemberRosterReader>();

        // Olas 155-156 — Member roster writer split (lock/unlock) — ISP-clean.
        services.AddTransient<IMemberRosterWriter, UmbracoMemberRosterWriter>();

        // Olas 233-235 — GDPR RTBF coordinator (Cap-240 Batch B). Orquesta
        // hard-delete del Member + anonimización inline de comments y form
        // submissions (App_Data/syn-comments/, App_Data/syn-form-submissions/)
        // + audit terminal "gdpr.rtbf-processed". Transient — depende del
        // roster writer transient.
        services.AddTransient<IGdprRtbfCoordinator, FileSystemGdprRtbfCoordinator>();

        // Olas 153-154 — Audit trail (ADR 0066). FileSystemAuditTrailWriter
        // persiste eventos admin en App_Data/syn-audit/{yyyy-MM-dd}.jsonl.
        // Singleton — solo depende de IHostEnvironment + ILogger; concurrency
        // gestionada via lock interno.
        services.AddSingleton<IAuditTrailWriter, FileSystemAuditTrailWriter>();

        // Ola 162 + Cap-270 Batch B — Retention sweep generalizado
        // (ADRs 0070 + 0088). RetentionSweepHostedService itera todas
        // las IRetentionPolicy registradas cada 24h con try-catch
        // per-policy. Reemplaza al antiguo AuditRetentionHostedService
        // (deletado) extendiendo el pattern a 4 stores: audit + comments
        // (rejected) + form-submissions + search-analytics. Cada policy
        // opt-in via setting de retention days (0 = nunca purgar).
        services.AddSingleton<IRetentionPolicy, Synergos.CMS.Web.Services.Retention.AuditRetentionPolicy>();
        services.AddSingleton<IRetentionPolicy, Synergos.CMS.Web.Services.Retention.CommentsRetentionPolicy>();
        services.AddSingleton<IRetentionPolicy, Synergos.CMS.Web.Services.Retention.FormSubmissionsRetentionPolicy>();
        services.AddSingleton<IRetentionPolicy, Synergos.CMS.Web.Services.Retention.SearchAnalyticsRetentionPolicy>();
        // ADR 0097 D5 — retención de los archivos de checkouts del dashboard.
        services.AddSingleton<IRetentionPolicy, Synergos.CMS.Web.Services.Retention.DashboardOrdersRetentionPolicy>();
        // ADR 0098 H3 — retención de registros clínicos PHI (6 años por defecto).
        services.AddSingleton<IRetentionPolicy, Synergos.CMS.Web.Services.Retention.HealthcareRetentionPolicy>();
        services.AddHostedService<Synergos.CMS.Web.Services.Retention.RetentionSweepHostedService>();

        // Olas 278-279 — SQLite maintenance pragmas (Cap-270 Batch D).
        // Cierra audit finding: WAL crece sin checkpoint reciente.
        // Auto-detect SQLite via ProviderName; NO-OP si SQL Server.
        services.AddHostedService<SqliteMaintenanceHostedService>();

        // Olas 165-166 — Webhook telemetry store (ADR 0071). Ring buffer
        // in-memory por canal con last 1000 outcomes. Singleton — thread-safe
        // via per-channel lock. Reset on restart (no persistencia).
        services.AddSingleton<IWebhookTelemetryStore, InMemoryWebhookTelemetryStore>();

        // Olas 195-196 + 236-237 + 254-256 — Telemetry alerts
        // (ADRs 0080 + 0085 + 0087). Scanner extraído del hosted service
        // (Cap-240) + Cap-260 Batch B refactor a Composite IAlertNotifier
        // paralelo a Comments/Forms/Cart. Channels: email (existente) +
        // slack + discord + teams + webhook (paralelo). Cada uno opt-in
        // por URL configurada — vacío = canal no-op silencioso.
        services.AddSingleton<IAlertNotifierChannel, EmailAlertNotifier>();
        services.AddHttpClient(WebhookAlertNotifier.FactoryName).AddWebhookResilience();
        services.AddSingleton<IAlertNotifierChannel, WebhookAlertNotifier>();
        services.AddHttpClient(SlackAlertNotifier.FactoryName).AddWebhookResilience();
        services.AddSingleton<IAlertNotifierChannel, SlackAlertNotifier>();
        services.AddHttpClient(DiscordAlertNotifier.FactoryName).AddWebhookResilience();
        services.AddSingleton<IAlertNotifierChannel, DiscordAlertNotifier>();
        services.AddHttpClient(TeamsAlertNotifier.FactoryName).AddWebhookResilience();
        services.AddSingleton<IAlertNotifierChannel, TeamsAlertNotifier>();
        services.AddSingleton<IAlertNotifier, CompositeAlertNotifier>();

        services.AddSingleton<WebhookTelemetryAlertScanner>();
        services.AddHostedService<WebhookTelemetryAlertHostedService>();

        // Ola 216 — Host bridge (ADR 0083). DefaultHostBridgeContextBuilder
        // arma el shape canónico de window.synergos consumed by UI components
        // via _SynergosBridge.cshtml partial. Transient — depende de scoped
        // services Umbraco.
        services.AddTransient<IHostBridgeContextBuilder, DefaultHostBridgeContextBuilder>();

        // Olas 178-180 + 221-224 — Member 2FA TOTP (ADRs 0074 + 0084).
        // FileSystemMemberTwoFactorStore persiste secrets en App_Data/syn-2fa/
        // {memberKey}.json encrypted via IDataProtectionProvider (Ola 221).
        // Service wraps Otp.NET para TOTP generation/verification.
        // QrCodeRenderer (singleton) renderiza el provisioning URI como SVG.
        services.AddSingleton<FileSystemMemberTwoFactorStore>();
        services.AddTransient<IMemberTwoFactorService, UmbracoMemberTwoFactorService>();
        services.AddSingleton<QrCodeRenderer>();

        // Ola 161 — Validator que warn al boot/reload si una key del
        // PerChannel dict no matchea ningún FactoryName conocido.
        services.AddSingleton<
            Microsoft.Extensions.Options.IValidateOptions<WebhookResilienceSettings>,
            WebhookResilienceSettingsValidator>();

        // Ola 65 — Email transaccional (ADR 0035). DefaultEmailService
        // wraps Umbraco.Cms.Core.Mail.IEmailSender — Umbraco gestiona
        // SMTP transport via Umbraco:CMS:Global:Smtp + pickup directory.
        // Singleton OK — solo depende de IEmailSender (singleton) +
        // IOptions + ILogger. Habilita password reset, email confirmation,
        // form notifications cuando se cableen los consumidores.
        services.AddSingleton<IEmailService, DefaultEmailService>();

        // Ola 82 — Email template rendering (ADR 0044). RazorEmailTemplateRenderer
        // permite a consumers (AccountController, FormSubmissionsController)
        // componer emails con branding consistente sin string concat.
        // Singleton — depende de IRazorViewEngine + ITempDataProvider +
        // IServiceProvider (todos singleton).
        services.AddSingleton<IEmailTemplateRenderer, RazorEmailTemplateRenderer>();

        // Ola 66 — Output cache para endpoints operacionales sitemap/RSS
        // (ADR 0036). IMemoryCache es estandar ASP.NET Core — registra
        // explicito por si Umbraco no lo cableo. Idempotente.
        services.AddMemoryCache();

        // Ola 67 — Analytics tracker (ADR 0037). LoggerAnalyticsTracker
        // emite eventos como log estructurado — el operador agrega via
        // su sink standard (Serilog/AI/Elastic). Singleton porque solo
        // depende de ILogger (singleton).
        //
        // ADR 0097 — Dashboard: el tracker se DECORA con ProjectingAnalyticsTracker,
        // que además proyecta cada evento (O(1), en memoria, sin IO) al
        // IMetricsProjectionStore que alimenta el panel. Umbraco 13 no trae
        // Scrutor → composición manual: registramos el inner concreto y lo
        // envolvemos. Los consumidores siguen inyectando IAnalyticsTracker.
        services.AddSingleton<LoggerAnalyticsTracker>();
        services.AddSingleton<InMemoryMetricsProjectionStore>();
        services.AddSingleton<IMetricsProjectionStore>(sp =>
            sp.GetRequiredService<InMemoryMetricsProjectionStore>());
        services.AddSingleton<IAnalyticsTracker>(sp =>
            new ProjectingAnalyticsTracker(
                sp.GetRequiredService<LoggerAnalyticsTracker>(),
                sp.GetRequiredService<IMetricsProjectionStore>(),
                sp.GetRequiredService<ILogger<ProjectingAnalyticsTracker>>()));

        // ADR 0097 — Captura explícita de checkouts (revenue, append-only)
        // + flush periódico de la proyección a JSONL (background, no bloquea
        // requests).
        services.AddSingleton<ICheckoutRecorder, FileSystemCheckoutRecorder>();
        services.AddHostedService<DashboardSnapshotFlushHostedService>();

        // ADR 0097 D2 — read-model compartido (/admin SSR + API Angular).
        // Scoped: IMemberRosterReader depende de servicios per-request de
        // Umbraco → no capturarlo en un singleton (captive dependency).
        services.AddScoped<IDashboardReadModel, DefaultDashboardReadModel>();

        // ADR 0097 D5 — export CSV de métricas (singleton; solo depende del store).
        services.AddSingleton<IMetricsExporter, DefaultMetricsExporter>();

        // ADR 0098 — Healthcare PHI (H1, núcleo de seguridad): store cifrado
        // atómico (IDataProtector) + libro de consentimientos + access-guard
        // fail-closed. El guard es Scoped porque IMemberAccessGate es per-request.
        services.AddSingleton<IPhiStore, FileSystemEncryptedPhiStore>();
        services.AddSingleton<IConsentLedger, FileSystemConsentLedger>();
        services.AddScoped<IPhiAccessGuard, DefaultPhiAccessGuard>();

        // T6 — almacén de ficheros PRIVADOS (bytes subidos por usuarios): cifrado
        // at-rest y bajo App_Data/, donde NINGÚN middleware de estáticos llega. Es el
        // contrapeso de wwwroot/media, que es público por construcción: ahí van las
        // fotos de producto, aquí la cédula del ciudadano. Quien sirva estos bytes
        // comprueba permiso en cada descarga (el id opaco no es la autorización).
        services.AddSingleton<IPrivateFileStore, FileSystemPrivateFileStore>();

        // T9 — firma del QR de las entradas. La llave sale del secreto configurado o, si
        // no hay, se genera una vez y se guarda CIFRADA en el almacén de arriba: así el
        // QR sobrevive un reinicio (el bug que T9 corrige era justo lo contrario) sin
        // meter ningún secreto en el repo.
        // T7 — realtime por SSE (ADR 0111). UN hub transversal: los verticales publican
        // en canales y no saben del transporte. Fan-out EN PROCESO (un deploy = un origen).
        services.AddSingleton<SseRealtimeHub>();
        services.AddSingleton<IRealtimeNotifier>(sp => sp.GetRequiredService<SseRealtimeHub>());

        services.AddSingleton<TicketSigningKeyProvider>();
        services.AddSingleton<ITicketSigner, LazyTicketSigner>();

        // ADR 0098 H2 — repositorio de historia clínica (versionado, sobre el PHI store).
        services.AddSingleton<IPatientRepository, FileSystemPatientRepository>();

        // ADR 0098 H2b — agenda de citas: lógica pura (Application) + scheduler Web
        // con lock async por-doctor (anti-overbooking) sobre el PHI store.
        services.AddSingleton<Synergos.CMS.Application.Services.AppointmentSchedulingService>();
        services.AddSingleton<IAppointmentScheduler, LockingAppointmentScheduler>();

        // ADR 0098 H2c — recetas (RECORD-KEEPER, sobre el PHI store).
        services.AddSingleton<IPrescriptionService, FileSystemPrescriptionService>();

        // ADR 0098 H3 — de-identificador PHI (lo invoca el coordinador RTBF
        // antes del hard-delete del Member).
        services.AddSingleton<IHealthcareDataAnonymizer, FileSystemHealthcareDataAnonymizer>();

        // OLA 5 Healthcare EHR-lite — dashboard clínico de DEMO (doc healthcare-app-spec).
        // Capa ADITIVA distinta del núcleo PHI de producción de arriba (ADR 0098):
        // sirve datos sembrados coherentes para la app Angular module-healthcare-ehr
        // (/api/ehr → EhrController). Seams stub-first, lógica pura (ADR 0002), tipos
        // prefijados por dominio (Ehr*/Clinical*/MedicalDoctor) para no colisionar con
        // los records de IPatientRepository/IAppointmentScheduler en el namespace.
        //   - IPatientRegistry / IDoctorDirectory: padrón + staff sembrados (memoria).
        //   - IClinicalRecordService: historia + encuentros (SOAP). REUSA IAuditTrailWriter
        //     (ADR 0037) — cada lectura/escritura de PHI emite un evento append-only.
        //   - IClinicalPrescriptionService: recetas por paciente (RECORD-KEEPER), idem audit.
        //   - IClinicalSchedulingService: agenda. REUSA el motor de reservas (cita = ítem
        //     reservable polimórfico): HoldItemAsync → copago opcional vía IPaymentProvider
        //     → ConfirmAsync (misma máquina Held→Confirmed de hoteles/aerolíneas).
        // Singletons — el estado (citas creadas, encuentros, recetas) vive en el proceso,
        // igual que el resto de stubs del motor.
        services.AddSingleton<IPatientRegistry, StubPatientRegistry>();
        services.AddSingleton<IDoctorDirectory, StubDoctorDirectory>();
        services.AddSingleton<IClinicalRecordService, StubClinicalRecordService>();
        services.AddSingleton<IClinicalPrescriptionService, StubClinicalPrescriptionService>();
        services.AddSingleton<IClinicalSchedulingService, StubClinicalSchedulingService>();

        // OLA 7 Healthcare EHR-lite (doc 21 §2.5) — DOS portales de un mismo grafo
        // clínico. Seams NUEVOS, aditivos (no tocan el vertical Healthcare de
        // PRODUCCIÓN de ADR 0098 ni los seams EHR-lite de OLA 5 de arriba). Todos
        // reusan lo existente y auditan PHI vía IAuditTrailWriter (ADR 0037):
        //   - IClinicalResultsProvider: labs por paciente (valor/rango/flag). Semilla
        //     coherente con la historia (Jorge diabético→HbA1c alta; Valentina
        //     hipotiroidea→TSH alta). El order-entry lo ALIMENTA (bucle order→result).
        //   - IClinicalMedicationService: medicación activa DERIVADA de las recetas
        //     vivas (DIP, compone IClinicalPrescriptionService) + refill que enruta al
        //     In Basket del médico vía IMessagingService (contexto 'clinical', genérico
        //     Ola 1 reusado). El concreto expone GetPendingRefillsForProvider para que
        //     el In Basket lea las solicitudes por composición.
        //   - IClinicalOrderService: order-entry (lab/eRx/imaging/referral); una orden
        //     de lab libera un resultado coherente (compone IClinicalResultsProvider).
        //   - IClinicalBillingService: estado de cuenta + saldo + plan, sembrado.
        //   - IEhrInBasketService: cola tipada del proveedor (result|refill|message)
        //     que DERIVA de resultados + refills + mensajería — sin store paralelo
        //     (compone el StubClinicalMedicationService concreto, mismo patrón que
        //     StubContentStream→StubReactionService).
        // Singletons — el estado (resultados/refills/órdenes creados) vive en el
        // proceso, igual que el resto de stubs del motor.
        services.AddSingleton<IClinicalResultsProvider, StubClinicalResultsProvider>();
        services.AddSingleton<StubClinicalMedicationService>(sp =>
            new StubClinicalMedicationService(
                sp.GetRequiredService<IClinicalPrescriptionService>(),
                sp.GetRequiredService<IMessagingService>(),
                sp.GetRequiredService<IAuditTrailWriter>()));
        services.AddSingleton<IClinicalMedicationService>(sp => sp.GetRequiredService<StubClinicalMedicationService>());
        services.AddSingleton<IClinicalOrderService>(sp =>
            new StubClinicalOrderService(
                sp.GetRequiredService<IDoctorDirectory>(),
                sp.GetRequiredService<IClinicalResultsProvider>(),
                sp.GetRequiredService<IAuditTrailWriter>()));
        services.AddSingleton<IClinicalBillingService, StubClinicalBillingService>();
        services.AddSingleton<IEhrInBasketService>(sp =>
            new StubEhrInBasketService(
                sp.GetRequiredService<IClinicalResultsProvider>(),
                sp.GetRequiredService<StubClinicalMedicationService>(),
                sp.GetRequiredService<IMessagingService>(),
                sp.GetRequiredService<IPatientRegistry>()));

        // OLA 6 Eventos — plataforma de eventos enterprise (doc eventos-app-spec).
        // Tres seams stub-first, aditivos (no tocan Booking/Travel/Shop/Blogs/Educación/
        // Healthcare). ADR 0002 (Application pura, sin Umbraco) + ADR 0075 (tests
        // canónicos). REUSA el motor (no reinventa): cada asiento/cupo es un recurso
        // reservable polimórfico (Event×Tier×Seat), apartado vía IReservationService.
        // HoldItemAsync con UNA sesión IPaymentProvider — igual que TravelCartService/
        // StubShopOrderService. Tipos prefijados Event*/Ticket* para no colisionar.
        //   - IEventCatalogProvider: search (texto) + ficha (tiers + seat-map JSON
        //     compatible con synergos-seat-map). Stub: catálogo sembrado (4 eventos,
        //     mezcla general/reserved). Adapter real: Examine sobre eventPage / ticketing.
        //   - IEventTicketingService: checkout (resuelve precio/aforo real → HoldItemAsync
        //     por unidad → 1 PaymentSession por el total) + confirm (captura + e-tickets
        //     QR determinista). Idempotente por orderRef. Singleton — el estado (órdenes
        //     + tickets) vive en el proceso, igual que el resto de stubs del motor.
        //   - IEventManagementService: dashboard (asistentes/aforo/vendidos) + check-in
        //     idempotente. NO duplica estado: lee del StubEventTicketingService concreto
        //     por composición (DIP, mismo patrón que StubContentStream→StubReactionService),
        //     por eso registramos el concreto y lo exponemos bajo la interfaz.
        // OLA 3 Eventos (doc 21 §2.6) — app completa: "Mis tickets" (holder email),
        // transferir (QR rotativo SafeTix-like + auditado), geo en el search,
        // crear evento (organizador → publica al catálogo) y tracking del ciclo
        // (paid→confirmed→attended). El ticketing ahora ALIMENTA su timeline de
        // eventos (instancia PROPIA del seam genérico IOrderTrackingService con
        // EventPipeline — NO reusa el singleton de Tienda cuyo pipeline es de
        // envío) y AUDITA las transferencias (IAuditTrailWriter, ADR 0037). El
        // management publica los eventos creados vía IEventCatalogProvider (DIP).
        //
        // T5 Ola A + rebanada de contenido (ADR 0117): de dónde sale la agenda. Con el flag en
        // 'demo' es el seed de siempre; con 'cms' es el CONTENIDO que el editor autoró, servido
        // por CatalogEventCatalogProvider sobre UmbracoEventCatalogSource. El rollback sigue
        // siendo esa línea de config, sin redeploy.
        services.AddSingleton<IEventCatalogProvider>(sp =>
            IsCmsSource(sp, UmbracoEventCatalogSource.Vertical)
                ? new CatalogEventCatalogProvider(
                    sp.GetRequiredService<ICatalogSource<EventDetail>>(),
                    sp.GetRequiredService<IJsonEntityStore>())
                : new StubEventCatalogProvider());

        // T5 Ola A (ADR 0107) — de dónde salen los eventos: el seed de demo o el CONTENIDO que
        // autoró el editor (eventPage). Calco del registro de ICatalogSource<CatalogProduct>
        // de Tienda (:304): mismo flag, mismo rollback de una línea sin redeploy.
        //
        // Singleton por la misma razón que la fuente de Tienda: UmbracoEventCatalogSource solo
        // sostiene IUmbracoContextAccessor (un ACCESSOR, que resuelve el contexto por llamada),
        // IOptionsMonitor e ILogger. Ninguno es Scoped, así que no hay dependencia cautiva.
        //
        // La rebanada de contenido (ADR 0117) cerró el hueco que dejaba esta fuente inerte:
        // eventPage ya modela localidades, aforo, agenda y zonas, así que ahora emite las dos
        // caras — el RESUMEN que el buscador lista y la FICHA comprable. Se registra la clase
        // concreta una vez y las dos seams apuntan a esa misma instancia: dos recorridos del
        // árbol de contenido por request serían el doble de trabajo para la misma respuesta.
        services.AddSingleton<UmbracoEventCatalogSource>(sp =>
            ActivatorUtilities.CreateInstance<UmbracoEventCatalogSource>(sp));
        services.AddSingleton<ICatalogSource<EventSummary>>(sp =>
            IsCmsSource(sp, UmbracoEventCatalogSource.Vertical)
                ? sp.GetRequiredService<UmbracoEventCatalogSource>()
                : new EventsDemoCatalogSource(sp.GetRequiredService<IEventCatalogProvider>()));

        // La cara de ficha SOLO existe respaldada por contenido. En modo 'demo' nadie la
        // resuelve (el provider es el stub, que tiene su catálogo adentro), y registrarla igual
        // es lo que evita que un futuro consumidor se encuentre un ObjectDisposedException de
        // DI en vez de una agenda vacía.
        services.AddSingleton<ICatalogSource<EventDetail>>(sp =>
            sp.GetRequiredService<UmbracoEventCatalogSource>());

        // Durabilidad (doc 25): las órdenes de tickets viven tras el store genérico
        // (resourceType "event-orders") → una compra confirmada sobrevive un reinicio.
        services.AddSingleton<StubEventTicketingService>(sp =>
            new StubEventTicketingService(
                sp.GetRequiredService<IEventCatalogProvider>(),
                sp.GetRequiredService<IReservationService>(),
                sp.GetRequiredService<IPaymentProvider>(),
                new StubOrderTrackingService(StubEventTicketingService.EventPipeline, null,
                    sp.GetRequiredService<IJsonEntityStore>(), "tracking-events"),
                sp.GetRequiredService<IAuditTrailWriter>(),
                sp.GetRequiredService<IJsonEntityStore>(),
                null,
                notifier: sp.GetRequiredService<ITransactionalNotifier>(),
                // T9: sin firmante no se emite QR ni se valida en la puerta (fail-closed).
                signer: sp.GetRequiredService<ITicketSigner>()));
        services.AddSingleton<IEventTicketingService>(sp => sp.GetRequiredService<StubEventTicketingService>());
        services.AddSingleton<IEventManagementService>(sp =>
            new StubEventManagementService(
                sp.GetRequiredService<StubEventTicketingService>(),
                sp.GetRequiredService<IEventCatalogProvider>()));

        // OLA 7 Propiedades — portal inmobiliario (doc propiedades-app-spec).
        // Cuatro seams stub-first, aditivos (no tocan Booking/Travel/Shop/Blogs/
        // Educación/Healthcare/Eventos). ADR 0002 (Application pura, sin Umbraco) +
        // ADR 0075 (tests canónicos). Caso especial del marco: la transacción central
        // NO es un pago — es agendar visita + generar lead. REUSA el motor (no lo
        // reinventa) con el paso de pago DESACTIVADO, validando su generalidad.
        // Tipos prefijados Property*/Visit*/Mortgage*/Lead* para no colisionar.
        //   - IPropertyCatalogProvider: search facetado (texto/tipo/precio/hab/
        //     ubicación → listados + facetas) + ficha (specs + galería + geo). Stub:
        //     catálogo sembrado con geo (varias ciudades CO × tipos). Adapter real:
        //     Examine sobre propertyListing o API MLS. Singleton — stateless.
        //   - IVisitSchedulingService: agendar visita = recurso reservable
        //     POLIMÓRFICO (igual que habitación/asiento/médico). BookAsync llama
        //     IReservationService.HoldItemAsync → ConfirmAsync con PaymentSessionId
        //     neutro "visit-free" (la visita es gratis): demuestra el flujo
        //     seleccionar→[pagar]→confirmar con el paso de pago OFF. Idempotente por
        //     slot. Singleton — el estado de slots apartados vive en el proceso.
        //   - IMortgageCalculator: amortización francesa pura/determinista (sin
        //     estado). Singleton.
        //   - ILeadCaptureService: captura el lead (contactar agente). REUSA
        //     IAuditTrailWriter (ADR 0037) + IAnalyticsTracker (ADR 0067) — no crea
        //     seams nuevos. Singleton — el estado de leads vive en el proceso.
        //
        // Rebanada de contenido (ADR 0118) — de dónde sale el inventario. Inmobiliaria era el
        // único vertical con catálogo SIN ninguna superficie CMS: no existía propertyListing,
        // así que un editor no podía publicar un inmueble ni con el flag puesto. Ahora
        // Synergos:Catalog:Sources:Realty = cms sirve lo que el editor autoró, y el rollback
        // es esa misma línea a 'demo' sin redespliegue.
        //
        // Singleton por lo mismo que las otras dos fuentes: UmbracoPropertyCatalogSource solo
        // sostiene accessors, IOptionsMonitor, la factory del diccionario y el logger. Ninguno
        // es Scoped, así que no hay dependencia cautiva.
        services.AddSingleton<ICatalogSource<PropertyDetail>>(sp =>
            ActivatorUtilities.CreateInstance<UmbracoPropertyCatalogSource>(sp));
        services.AddSingleton<IPropertyCatalogProvider>(sp =>
            IsCmsSource(sp, UmbracoPropertyCatalogSource.Vertical)
                ? new CatalogPropertyCatalogProvider(
                    sp.GetRequiredService<ICatalogSource<PropertyDetail>>(),
                    sp.GetRequiredService<IJsonEntityStore>())
                : new StubPropertyCatalogProvider());
        // Durabilidad (ADR 0105): los slots apartados viven tras el store genérico, así que
        // una visita que un comprador ya agendó sobrevive un reinicio. Namespace propio —
        // compartirlo haría que el estado de un dominio se leyera contra la forma de otro.
        services.AddSingleton<IVisitSchedulingService>(sp =>
            new StubVisitSchedulingService(
                sp.GetRequiredService<IReservationService>(),
                null,
                sp.GetRequiredService<IJsonEntityStore>(),
                "realty-visits"));
        services.AddSingleton<IMortgageCalculator, StubMortgageCalculator>();
        // OLA 4 Propiedades (doc 21 §2.7) — cara completa: la captura de leads ahora
        // compone el catálogo para resolver el agente dueño del inmueble y alimentar el
        // mini-CRM del agente (kanban Nuevo→Contactado→Visita→Cerrado, avance auditado
        // vía IAuditTrailWriter). El itemRef del lead sigue siendo forense.
        // Durabilidad (ADR 0105): el lead que un agente está trabajando ya no se pierde en
        // un reinicio, que era justo el dato que más dolía perder de este vertical.
        services.AddSingleton<ILeadCaptureService>(sp =>
            new StubLeadCaptureService(
                sp.GetRequiredService<IAuditTrailWriter>(),
                sp.GetRequiredService<IAnalyticsTracker>(),
                sp.GetRequiredService<IPropertyCatalogProvider>(),
                null,
                sp.GetRequiredService<IJsonEntityStore>(),
                "realty-leads"));
        // OLA 4 Propiedades — búsquedas guardadas + alertas: da SEMÁNTICA a las
        // saved-searches sobre el seam GENÉRICO IUserCollection (colección
        // "saved-searches") + re-ejecuta los criterios contra IPropertyCatalogProvider
        // para contar nuevos matches (la alerta). Los favoritos van directo sobre
        // IUserCollection (colección "favorites") desde el RealtyController.
        // Durabilidad (ADR 0105): el índice id→criterios —lo que resuelve la alerta— vive
        // tras el store. La LISTA de qué búsquedas tiene cada usuario no está aquí: vive en
        // IUserCollection, y sobrevive por la durabilidad de ESE seam.
        services.AddSingleton<ISavedSearchService>(sp =>
            new StubSavedSearchService(
                sp.GetRequiredService<IUserCollection>(),
                sp.GetRequiredService<IPropertyCatalogProvider>(),
                null,
                sp.GetRequiredService<IJsonEntityStore>(),
                "realty-saved-searches"));

        // +1 GOBIERNO — portal de trámites (doc gobierno-app-spec; rescate D7 doc 21).
        // Seis seams stub-first, aditivos (no tocan los otros verticales). ADR 0002
        // (Application pura, sin Umbraco) + ADR 0075 (tests canónicos). El objeto
        // transaccional NO es una orden que cierra: es un EXPEDIENTE de larga vida con
        // máquina de estados AUDITADA (radicado→en-revisión→subsanación→resuelto/
        // rechazado) — cada transición es un evento append-only en IAuditTrailWriter
        // (el diferenciador del dominio). REUSA el motor: la tasa es un pago OPCIONAL
        // vía IPaymentProvider (trámite gratuito = paso de pago OFF, igual que la
        // visita de Propiedades).
        //   - ITramiteCatalogProvider: catálogo buscable (texto+categoría) + ficha con
        //     formulario dinámico DATA-DRIVEN (campos tipo/label/required/opciones —
        //     patrón GOV.UK task-list: cada trámite varía sin tocar el módulo). Stub:
        //     5 trámites CO sembrados (gratuitos y con tasa). Adapter real: SUIT /
        //     Content Delivery API. Singleton — stateless.
        //   - IGovFeeCalculator: tasa pura/determinista (≤0 o desconocido = exento).
        //   - IApplicationService (StubApplicationService): AGREGADO RAÍZ del
        //     expediente — radicar valida el trámite + campos requeridos, calcula la
        //     tasa, abre sesión de pago solo si cobra, y asienta el primer estado.
        //     Fuente de verdad del estado; siembra expedientes en varios estados para
        //     la demo del ciclo de vida. Registramos el concreto y lo exponemos bajo
        //     la interfaz para que los hermanos compongan (DIP, mismo patrón
        //     StubEventTicketingService).
        //   - ICaseWorkflowService: máquina de estados (tabla de transiciones legales,
        //     idempotente al re-transicionar al estado actual); CADA transición legal
        //     → gov.case-transition append-only.
        //   - ICaseTrackingProvider: expediente+timeline + bandeja por rol (ciudadano
        //     ve los suyos por email / funcionario ve la cola). Solo LEE el agregado.
        //   - IDocumentUploadService: T6 — guarda los BYTES en IPrivateFileStore (cifrado,
        //     fuera de wwwroot) y adjunta la metadata con el puntero + audita la subida.
        //     El tipo (PDF/JPG/PNG) y el peso (≤10 MB) los valida el controller, que es
        //     quien ve el multipart.
        // Singletons — el estado (expedientes) vive en memoria del proceso, igual que
        // el resto de stubs del motor.
        // Rebanada de contenido (ADR 0123) — el último catálogo que quedaba sembrado en C#.
        // Con Synergos:Catalog:Sources:Gov = cms el portal sirve los tramitePage que autoró la
        // entidad, y el rollback es esa misma línea a 'demo'. Sin capa durable: la seam es de
        // solo lectura, así que no hay nada que publicar desde la app que persistir aparte.
        services.AddSingleton<ICatalogSource<TramiteDetail>>(sp =>
            ActivatorUtilities.CreateInstance<UmbracoTramiteCatalogSource>(sp));
        services.AddSingleton<ITramiteCatalogProvider>(sp =>
            IsCmsSource(sp, UmbracoTramiteCatalogSource.Vertical)
                ? new CatalogTramiteCatalogProvider(sp.GetRequiredService<ICatalogSource<TramiteDetail>>())
                : new StubTramiteCatalogProvider());
        services.AddSingleton<IGovFeeCalculator, StubGovFeeCalculator>();
        // Durabilidad (doc 25): los expedientes viven tras el store genérico
        // (resourceType "gov-cases") → un trámite radicado y sus decisiones sobreviven un
        // reinicio. El seed es seed-if-absent para no pisar las mutaciones reales.
        services.AddSingleton<StubApplicationService>(sp =>
            new StubApplicationService(
                sp.GetRequiredService<ITramiteCatalogProvider>(),
                sp.GetRequiredService<IGovFeeCalculator>(),
                sp.GetRequiredService<IPaymentProvider>(),
                sp.GetRequiredService<IAuditTrailWriter>(),
                null,
                sp.GetRequiredService<IJsonEntityStore>(),
                notifier: sp.GetRequiredService<ITransactionalNotifier>()));
        services.AddSingleton<IApplicationService>(sp => sp.GetRequiredService<StubApplicationService>());
        services.AddSingleton<ICaseWorkflowService>(sp =>
            new StubCaseWorkflowService(
                sp.GetRequiredService<StubApplicationService>(),
                sp.GetRequiredService<IAuditTrailWriter>(),
                null,
                notifier: sp.GetRequiredService<ITransactionalNotifier>()));
        services.AddSingleton<ICaseTrackingProvider>(sp =>
            new StubCaseTrackingProvider(sp.GetRequiredService<StubApplicationService>()));
        services.AddSingleton<IDocumentUploadService>(sp =>
            new StubDocumentUploadService(
                sp.GetRequiredService<StubApplicationService>(),
                sp.GetRequiredService<IPrivateFileStore>(),
                sp.GetRequiredService<IAuditTrailWriter>(),
                null));
        // OLA 8 Gobierno — correspondencia del expediente sobre el seam GENÉRICO
        // IMessagingService (contexto 'gov', contextRef = radicado). Se siembra al boot
        // desde un hosted service (no en el ctor de la mensajería, compartida por varios
        // dominios), igual que BlogsDemoSeedHostedService. Idempotente.
        services.AddHostedService<GovCorrespondenceSeedHostedService>();

        // Ola 68 — Comments runtime (ADR 0038). FileSystemCommentRepository
        // persiste un JSON por nodo (App_Data/syn-comments/{nodeId}.json).
        // Singleton — solo depende de IOptions + IHostEnvironment + ILogger.
        // Para concurrent-heavy o > 1000 comments por nodo, swap por adapter
        // sobre DB.
        services.AddSingleton<ICommentRepository, FileSystemCommentRepository>();

        // Olas 89 + 90 — Comment moderation notifier composite.
        // El consumer (CommentsController) inyecta ICommentModerationNotifier;
        // el composite default itera todos los ICommentModerationNotifierChannel
        // registrados (email + webhook). Cada canal es no-op si su settings
        // están vacíos, por lo que registrar ambos es seguro by default.
        services.AddSingleton<ICommentModerationNotifierChannel, EmailCommentModerationNotifier>();
        services.AddHttpClient(WebhookCommentModerationNotifier.FactoryName).AddWebhookResilience();
        services.AddSingleton<ICommentModerationNotifierChannel, WebhookCommentModerationNotifier>();
        services.AddHttpClient(SlackCommentModerationNotifier.FactoryName).AddWebhookResilience();
        services.AddSingleton<ICommentModerationNotifierChannel, SlackCommentModerationNotifier>();
        services.AddHttpClient(DiscordCommentModerationNotifier.FactoryName).AddWebhookResilience();
        services.AddSingleton<ICommentModerationNotifierChannel, DiscordCommentModerationNotifier>();
        services.AddHttpClient(TeamsCommentModerationNotifier.FactoryName).AddWebhookResilience();
        services.AddSingleton<ICommentModerationNotifierChannel, TeamsCommentModerationNotifier>();
        services.AddSingleton<ICommentModerationNotifier, CompositeCommentModerationNotifier>();

        // Ola 91 — Form submission notifier composite (paralelo del de
        // comments). Reemplaza la lógica inline de email del controller
        // con un seam swappable; cada canal es no-op si su settings
        // están vacíos.
        services.AddSingleton<IFormSubmissionNotifierChannel, EmailFormSubmissionNotifier>();
        services.AddHttpClient(WebhookFormSubmissionNotifier.FactoryName).AddWebhookResilience();
        services.AddSingleton<IFormSubmissionNotifierChannel, WebhookFormSubmissionNotifier>();
        services.AddHttpClient(SlackFormSubmissionNotifier.FactoryName).AddWebhookResilience();
        services.AddSingleton<IFormSubmissionNotifierChannel, SlackFormSubmissionNotifier>();
        services.AddHttpClient(DiscordFormSubmissionNotifier.FactoryName).AddWebhookResilience();
        services.AddSingleton<IFormSubmissionNotifierChannel, DiscordFormSubmissionNotifier>();
        services.AddHttpClient(TeamsFormSubmissionNotifier.FactoryName).AddWebhookResilience();
        services.AddSingleton<IFormSubmissionNotifierChannel, TeamsFormSubmissionNotifier>();
        services.AddSingleton<IFormSubmissionNotifier, CompositeFormSubmissionNotifier>();

        // Ola 102 — Cart abandonment notifier composite (paralelo de
        // comments + forms). Hook en CartAbandonmentScannerHostedService
        // por cart detectado. Email + webhook channels con HMAC opt-in.
        services.AddSingleton<ICartAbandonmentNotifierChannel, EmailCartAbandonmentNotifier>();
        services.AddHttpClient(WebhookCartAbandonmentNotifier.FactoryName).AddWebhookResilience();
        services.AddSingleton<ICartAbandonmentNotifierChannel, WebhookCartAbandonmentNotifier>();
        services.AddHttpClient(SlackCartAbandonmentNotifier.FactoryName).AddWebhookResilience();
        services.AddSingleton<ICartAbandonmentNotifierChannel, SlackCartAbandonmentNotifier>();
        services.AddHttpClient(DiscordCartAbandonmentNotifier.FactoryName).AddWebhookResilience();
        services.AddSingleton<ICartAbandonmentNotifierChannel, DiscordCartAbandonmentNotifier>();
        services.AddHttpClient(TeamsCartAbandonmentNotifier.FactoryName).AddWebhookResilience();
        services.AddSingleton<ICartAbandonmentNotifierChannel, TeamsCartAbandonmentNotifier>();
        services.AddSingleton<ICartAbandonmentNotifier, CompositeCartAbandonmentNotifier>();

        // Ola 41 — Flow engine runtime. FlowResolver queries the content
        // tree (Transient for the same per-request reason as theme). The
        // FlowController is picked up by AddControllers() above;
        // IHttpContextAccessor is registered by ASP.NET Core and is
        // consumed by the elementFlowProgress renderer to read the
        // syn-flow-{flowKey} cookie.
        services.AddTransient<FlowResolver>();
        services.AddHttpContextAccessor();

        // Ola 47 — Dev tooling: content seeder para smoke-test. Gated
        // por Synergos:DevSeed:Enabled=true en appsettings. El controller
        // /dev/seed-test-site invoca el seeder. Scope: solo crea/borra
        // el siteRoot "Test Site", nunca otros árboles.
        services.AddTransient<DevTestContentSeeder>();
        services.AddTransient<SynergosIdentitySeeder>();

        // Autoría server-side de contenido (Umbraco 13 no tiene Management
        // API). SchemaBlockDefaults siembra props multi-value editor-safe;
        // DevContentFiller puebla el BlockGrid de páginas existentes.
        services.AddTransient<SchemaBlockDefaults>();
        services.AddTransient<DevMediaFactory>();
        services.AddTransient<DevContentFiller>();
        // Tooling dev-only: crea los member groups de dominio (funcionario/organizador/
        // doctor…) y los asigna. Sin esto, las consolas que T2-Gov/T2-Eventos/T9/T7
        // cerraron por rol quedan correctas pero IMPOSIBLES de demostrar.
        services.AddTransient<DevMemberRoleSeeder>();
        // T10 (ADR 0114): siembra de reseñas de demo. Tooling dev — el endpoint que lo usa
        // devuelve 404 sin el flag; nada corre en boot (ADR 0013).
        services.AddTransient<DevProductReviewSeeder>();
        services.AddTransient<DevPaidOrderSeeder>();

        // Health probes. Each registers as an ISchemaHealthProbe; the
        // HealthController resolves them as IEnumerable<ISchemaHealthProbe>.
        services.AddSingleton<ISchemaHealthProbe>(_ => new SchemaVersionProbe());
        services.AddSingleton<ISchemaHealthProbe>(sp =>
        {
            var env = sp.GetRequiredService<IWebHostEnvironment>();
            var absolutePath = Path.Combine(env.ContentRootPath, "uSync", "v9");
            return new UsyncFolderProbe(absolutePath);
        });

        // Cap-280 Batch C — BundleRegistry probe (ADR 0089). Reporta el
        // estado del adapter activo: Stub healthy con mensaje informativo,
        // FileSystem resuelve un probe tag canónico contra el registry,
        // Unknown mode reporta unhealthy. Visible vía /_health.
        services.AddSingleton<ISchemaHealthProbe, BundleRegistryProbe>();

        // MVC controller discovery for attribute-routed endpoints
        // (HealthController, future controllers). AddControllers is
        // idempotent so calling it here is safe even if Umbraco already
        // registered MVC.
        services.AddControllers();

        // Ola 42.5 — pre-fill Layout Preset blocks with sensible
        // defaults on first save so the editor doesn't face empty
        // dropdowns every time they drop a preset. See
        // LayoutPresetDefaults for the all-empty heuristic.
        builder.AddNotificationHandler<
            ContentSavingNotification,
            LayoutPresetDefaults>();

        // Ola 42.7 — starter scaffold: opt-in via
        // Synergos:LayoutComposer:EnableStarterScaffold. Seeds a
        // minimal Hero + 2ColEven on first save of a blank pageBase.
        builder.AddNotificationHandler<
            ContentSavingNotification,
            LayoutComposerStarterScaffold>();

        // Ola 52.C — Member gating: cuando el routing resuelve a un
        // PublishedContent que compone compMemberGating con
        // requiresAuth=true, el handler verifica IMemberAccessGate y
        // redirige a /login con returnUrl si el miembro no califica.
        builder.AddNotificationHandler<
            RoutingRequestNotification,
            MemberGatingHandler>();
    }

    /// <summary>
    /// Si el catálogo de un vertical se sirve del CONTENIDO del CMS o del seed de demo.
    /// </summary>
    /// <remarks>
    /// Una sola lectura del flag para los tres verticales que ya tienen fuente Umbraco-backed
    /// (Tienda, Eventos, Inmobiliaria). El default es <c>demo</c> — un vertical al que se le
    /// olvide la clave sirve la demo, no un catálogo vacío.
    /// </remarks>
    private static bool IsCmsSource(IServiceProvider sp, string vertical)
    {
        var settings = sp.GetRequiredService<IOptionsMonitor<CatalogSettings>>().CurrentValue;
        var source = settings.Sources.TryGetValue(vertical, out var s) ? s : "demo";
        return string.Equals(source, "cms", StringComparison.OrdinalIgnoreCase);
    }
}
