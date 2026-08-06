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
    private void ComposeTravelAndBooking(IUmbracoBuilder builder)
    {
        var services = builder.Services;

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

        // El flujo transaccional de la reserva de hotel: apartar → cobrar → confirmar, o
        // cancelar. Vivía dentro de BookingController y salió de ahí en la HU #36 — no por
        // estética: un borde de ASP.NET no se puede probar sin levantar el pipeline, y mientras
        // el orden en que se abre la caja viviera ahí, no había forma de llevarlo contra
        // Synergos.Bff.Viajes sin reescribir el borde entero.
        // La sección se ENLAZA: sin esto el cliente recibe un ViajesSettings recién construido y
        // lo que no viaja por el HttpClient —el Kind del viajero— se queda en su valor por
        // defecto en silencio. Es el olvido que arrastraban Tienda (#24) y Salud (#25).
        services.Configure<ViajesSettings>(builder.Config.GetSection("Synergos:Viajes"));

        if (string.Equals(builder.Config["Synergos:Viajes:Mode"], "Bff", StringComparison.OrdinalIgnoreCase))
        {
            var vBase = builder.Config["Synergos:Viajes:BaseUrl"];
            var vKey = builder.Config["Synergos:Viajes:ApiKey"];
            var vTimeout = int.TryParse(builder.Config["Synergos:Viajes:TimeoutSeconds"], out var vt) && vt > 0 ? vt : 30;

            services.AddHttpClient(HttpHotelBookingService.ClientName, http =>
            {
                var url = string.IsNullOrWhiteSpace(vBase) ? "http://127.0.0.1:5304/" : vBase;
                http.BaseAddress = new Uri(url.EndsWith('/') ? url : url + "/");
                http.Timeout = TimeSpan.FromSeconds(vTimeout);
                if (!string.IsNullOrWhiteSpace(vKey))
                {
                    http.DefaultRequestHeaders.Add(HttpHotelBookingService.ApiKeyHeader, vKey);
                }
            })
            .AddHttpMessageHandler<CorrelationForwardingHandler>();

            // OJO: solo la vía HOTEL. El carrito multi-producto (ITravelCartService) sigue contra
            // el motor en proceso, y no por falta de ganas: TravelCartItem no lleva fechas y un
            // apartado de Api.Booking ES una ventana sobre un recurso. Ver ViajesSettings.
            services.AddSingleton<IHotelBookingService>(sp => new HttpHotelBookingService(
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<IOptionsMonitor<ViajesSettings>>(),
                sp.GetRequiredService<ICancellationPolicyEvaluator>(),
                sp.GetRequiredService<IJsonEntityStore>(),
                sp.GetRequiredService<ILogger<HttpHotelBookingService>>(),
                sp.GetRequiredService<IAuditTrailWriter>()));
        }
        else
        {
            services.AddSingleton<IHotelBookingService>(sp =>
                new StubHotelBookingService(
                    sp.GetRequiredService<IReservationService>(),
                    sp.GetRequiredService<IPaymentProvider>(),
                    sp.GetRequiredService<ICancellationPolicyEvaluator>(),
                    sp.GetRequiredService<IAuditTrailWriter>()));
        }

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

    }
}
