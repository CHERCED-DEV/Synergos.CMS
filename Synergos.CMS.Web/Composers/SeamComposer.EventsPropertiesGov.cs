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
    private void ComposeEventsPropertiesAndGov(IUmbracoBuilder builder)
    {
        var services = builder.Services;

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
        // Contra qué se aparta la visita (HU #33a), por configuración:
        //   - Stub (default): el motor en proceso. Durabilidad (ADR 0105): los slots apartados
        //     viven tras el store genérico, así que una visita ya agendada sobrevive un reinicio.
        //     Namespace propio — compartirlo haría que el estado de un dominio se leyera contra
        //     la forma de otro.
        //   - Api: DIRECTO contra Synergos.Api.Booking, sin orquestador. Una visita no se cobra,
        //     así que toca una sola capacidad: un BFF sería una saga de un paso.
        //
        // Api.Booking no sabe que el recurso es un inmueble y no puede saberlo (CLAUDE.md §12).
        // Lo vigila RealtyWiringTests.
        // La sección se ENLAZA, y no es ceremonia: sin esto el cliente HTTP recibe un
        // RealtySettings recién construido y todo lo que no viaja por el HttpClient —el Kind del
        // listado, el del interesado— se queda en su valor por defecto en silencio. Configurarlo
        // no haría nada y nadie sabría por qué. Lo mismo se arregló acá para Tienda y Salud, que
        // arrastraban el olvido desde las HU #24 y #25.
        services.Configure<RealtySettings>(builder.Config.GetSection("Synergos:Realty"));

        if (string.Equals(builder.Config["Synergos:Realty:Mode"], "Api", StringComparison.OrdinalIgnoreCase))
        {
            var realtyBase = builder.Config["Synergos:Realty:BaseUrl"];
            var realtyKey = builder.Config["Synergos:Realty:ApiKey"];
            var realtyTimeout = int.TryParse(builder.Config["Synergos:Realty:TimeoutSeconds"], out var rt) && rt > 0 ? rt : 15;

            services.AddHttpClient(HttpVisitSchedulingService.ClientName, http =>
            {
                var url = string.IsNullOrWhiteSpace(realtyBase) ? "http://127.0.0.1:5202/" : realtyBase;
                http.BaseAddress = new Uri(url.EndsWith('/') ? url : url + "/");
                http.Timeout = TimeSpan.FromSeconds(realtyTimeout);
                if (!string.IsNullOrWhiteSpace(realtyKey))
                {
                    http.DefaultRequestHeaders.Add(HttpVisitSchedulingService.ApiKeyHeader, realtyKey);
                }
            });
            services.AddSingleton<IVisitSchedulingService, HttpVisitSchedulingService>();
        }
        else
        {
            services.AddSingleton<IVisitSchedulingService>(sp =>
                new StubVisitSchedulingService(
                    sp.GetRequiredService<IReservationService>(),
                    null,
                    sp.GetRequiredService<IJsonEntityStore>(),
                    "realty-visits"));
        }
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

    }
}
