using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Notifications;
using Synergos.CMS.Web.Services;
using Synergos.CMS.Web.Services.Catalog;
using Umbraco.Cms.Core.Notifications;

namespace Synergos.CMS.Web.Composers;

public sealed partial class SeamComposer
{
    /// <summary>
    /// El vertical Alquiler (#147, piloto 2 de la fábrica): equipos que se llevan, vuelven, y
    /// tienen una garantía retenida mientras están fuera.
    /// </summary>
    /// <remarks>
    /// <b>Estrena su propio composer parcial</b>, que es lo que el #155 dejó escrito: agrupar se
    /// hace cuando hay una razón, no por defecto.
    /// </remarks>
    private static void ComposeAlquiler(IUmbracoBuilder builder)
    {
        var services = builder.Services;

        // Las dos secciones se ENLAZAN. Sin esto el cliente recibe un AlquilerSettings recién
        // construido y lo que no viaja por el HttpClient —el Kind del arrendatario, el tope de
        // días— se queda en su default EN SILENCIO: el olvido que arrastraron Tienda (#24) y
        // Salud (#25). La del sello va ANIDADA y en su propio POCO, que es la corrección del
        // #154: una sección hermana acaba a una letra de la del vertical.
        services.Configure<AlquilerSettings>(builder.Config.GetSection("Synergos:Alquiler"));
        services.Configure<AgreementSettings>(builder.Config.GetSection("Synergos:Alquiler:Agreement"));

        // La configuración de negocio del alquiler (ADR 0137): dónde vive su API, por sitio. La lee el
        // resolver del elemento; el editor ya no la ve.
        services.AddSeccionDeNegocio<AlquilerFeatureSettings, NegocioDeAlquiler>(
            builder.Config.GetSection("Synergos:Features:Alquiler"));

        // ── EJE 1 · el catálogo (forma A del doc 13 §5.bis) ─────────────────────────
        //
        // La colección es PROPIA y de sólo lectura, así que la fuente REEMPLAZA al seed en vez
        // de sembrarlo — al revés que Social (#146), cuyo almacén comparte Educación y donde el
        // producto escribe. Rollback de una línea: Synergos:Catalog:Sources:Alquiler = demo.
        //
        // Singleton por lo mismo que las otras ocho fuentes: UmbracoEquipmentCatalogSource sólo
        // sostiene un ACCESSOR (resuelve el contexto por llamada), IOptionsMonitor e ILogger.
        // Ninguno es Scoped, así que no hay dependencia cautiva.
        services.AddSingleton<UmbracoEquipmentCatalogSource>(sp =>
            ActivatorUtilities.CreateInstance<UmbracoEquipmentCatalogSource>(sp));
        services.AddSingleton<ICatalogSource<RentalEquipment>>(sp =>
            sp.GetRequiredService<UmbracoEquipmentCatalogSource>());
        // La raíz del sitio con la MISMA regla del catálogo: la pregunta de la oferta al publicar (línea 005).
        services.AddSingleton<IRaizDelAlquiler>(sp => sp.GetRequiredService<UmbracoEquipmentCatalogSource>());
        services.AddSingleton<IEquipmentCatalogProvider>(sp =>
            IsCmsSource(sp, UmbracoEquipmentCatalogSource.Vertical)
                ? new CatalogEquipmentCatalogProvider(sp.GetRequiredService<ICatalogSource<RentalEquipment>>())
                : new StubEquipmentCatalogProvider());

        // ── EJE 3 · el artefacto, FUERA del seam del eje 2 ──────────────────────────
        //
        // Va ANTES del eje 2 a propósito (doc 13 §5.1): el comprobante existe antes que la
        // pantalla que lo enseña, y tiene que estar fuera del seam antes de que haya dos
        // implementaciones que compartirlo. Con el orquestador detrás, el contrato se emite y se
        // anota aquí igual — que es lo que evita que la puerta lea un almacén vacío (#35).
        //
        // El REGISTRO nunca sale a la red; el SELLO sí podría (su llave puede custodiarla
        // Api.Signing el día que haga falta). Es la invariante del #153.
        services.AddSingleton<AgreementSigningKeyProvider>();
        services.AddSingleton<IAgreementSigner, LazyAgreementSigner>();
        services.AddSingleton(sp => new EquipmentAgreementLedger(sp.GetRequiredService<IJsonEntityStore>()));

        // El sello conmuta sin invalidar nada (research R11 de la línea 005): sella el del interruptor y comprueba el
        // que registra cada contrato. El LOCAL se registra en los dos modos —comprueba todo lo anterior al
        // interruptor—, y el de Api.Signing también con Mode=Local si su sección sigue puesta, para comprobar lo que
        // selló. Con Api.Signing caído el contrato sale con el sello pendiente: nunca se cae a la llave local.
        services.AddSingleton(sp => new SelladorLocal(sp.GetRequiredService<IAgreementSigner>()));
        var seccionDelSello = builder.Config.GetSection("Synergos:Alquiler:Agreement:Signing");
        if (Interruptor.Encendido(builder.Config, "Synergos:Alquiler:Agreement:Mode", "Api", new AgreementSettings().Mode))
        {
            RegistrarSelladorDeApiSigning(services, seccionDelSello);
            services.AddSingleton(sp => new EquipmentAgreementIssuer(
                sp.GetRequiredService<EquipmentAgreementLedger>(),
                sp.GetRequiredService<HttpAgreementSealer>(),
                sp.GetRequiredService<TimeProvider>(),
                [sp.GetRequiredService<SelladorLocal>()]));
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(seccionDelSello["BaseUrl"]))
            {
                RegistrarSelladorDeApiSigning(services, seccionDelSello);
            }

            services.AddSingleton(sp => new EquipmentAgreementIssuer(
                sp.GetRequiredService<EquipmentAgreementLedger>(),
                sp.GetRequiredService<SelladorLocal>(),
                sp.GetRequiredService<TimeProvider>(),
                sp.GetService<HttpAgreementSealer>() is { } api ? [api] : []));
        }

        // ── EJE 2 · la transacción ──────────────────────────────────────────────────
        //
        // UN interruptor para UN flujo, que es la forma orquestada: el ORDEN entre apartar la
        // ventana y retener la garantía es precisamente lo que el orquestador aporta
        // (`the_switch_count_tells_the_form`). El default es el motor en proceso.
        if (Interruptor.Encendido(builder.Config, "Synergos:Alquiler:Mode", "Bff", new AlquilerSettings().Mode))
        {
            // La pieza pone base, llave compartida, timeout, correlación, telemetría y reintento
            // (ClienteDelArbolDeServicios): ningún composer los vuelve a escribir a mano.
            services.AddClienteDelArbolDeServicios(
                HttpEquipmentRentalService.ClientName,
                DestinoDelArbol.De(builder.Config.GetSection("Synergos:Alquiler"), "http://127.0.0.1:5305/", 30));

            services.AddSingleton<IEquipmentRentalService>(sp => new HttpEquipmentRentalService(
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<IEquipmentCatalogProvider>(),
                sp.GetRequiredService<IOptionsMonitor<AlquilerSettings>>(),
                sp.GetRequiredService<ILogger<HttpEquipmentRentalService>>()));
        }
        else
        {
            services.AddSingleton<IEquipmentRentalService>(sp =>
            {
                var alquiler = sp.GetRequiredService<IOptionsMonitor<AlquilerSettings>>().CurrentValue;
                return new StubEquipmentRentalService(
                    sp.GetRequiredService<IEquipmentCatalogProvider>(),
                    sp.GetRequiredService<IJsonEntityStore>(),
                    sp.GetRequiredService<TimeProvider>(),
                    alquiler.MaxRentalDays,
                    TimeZoneInfo.FindSystemTimeZoneById(alquiler.TimeZone));
            });
        }

        // ── Lo que pasa después del cierre con un alquiler de la PUERTA (línea 005, T018) ─────
        //
        // Declarar y confirmar van al orquestador por el que salió el alquiler, no al motor del Mode. No
        // depende del modo de la ruta directa: la puerta habla con el orquestador también con el motor en
        // proceso de default. Hay destino si el despliegue dice dónde vive (BaseUrl) o si la directa ya va
        // por él (Mode=Bff); sin destino no sale a la red, como el artefacto de Eventos.
        var seccionDeAlquiler = builder.Config.GetSection("Synergos:Alquiler");
        var hayOrquestadorDeAlquiler = !string.IsNullOrWhiteSpace(seccionDeAlquiler["BaseUrl"])
            || Interruptor.Encendido(builder.Config, "Synergos:Alquiler:Mode", "Bff", new AlquilerSettings().Mode);
        if (hayOrquestadorDeAlquiler)
        {
            services.AddClienteDelArbolDeServicios(
                HttpAlquilerDeLaPuerta.ClientName, DestinoDelArbol.De(seccionDeAlquiler, "http://127.0.0.1:5305/", 30));
        }
        // Cancelar lo confirmado (NN-8, D-005-NN8): el inquilino por el flujo posterior alquiler.cancelacion o por la
        // liquidación propia, que se conserva detrás de este interruptor; el personal, siempre por la propia.
        var cancelaPorElFlujo = Interruptor.Modo(builder.Config, "Synergos:Alquiler:Cancellation",
            new AlquilerSettings().Cancellation, AlquilerSettings.CancelacionPropia) == AlquilerSettings.CancelacionPosterior;
        services.AddSingleton<IAlquilerDeLaPuerta>(sp => new HttpAlquilerDeLaPuerta(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<IOptionsMonitor<AlquilerSettings>>(),
            sp.GetRequiredService<ILogger<HttpAlquilerDeLaPuerta>>(),
            hayOrquestadorDeAlquiler,
            cancelaPorElFlujo));

        // El contrato de un alquiler de la PUERTA (línea 005, T024): se emite al leerlo, con lo que contesta el
        // orquestador, y «mis alquileres» reconcilia los completados que el registro no tiene. Mismo destino.
        if (hayOrquestadorDeAlquiler)
        {
            services.AddClienteDelArbolDeServicios(
                ArtefactoDeAlquiler.ClientName, DestinoDelArbol.De(seccionDeAlquiler, "http://127.0.0.1:5305/", 10));
        }
        services.AddSingleton(sp => new ArtefactoDeAlquiler(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<EquipmentAgreementLedger>(),
            sp.GetRequiredService<EquipmentAgreementIssuer>(),
            sp.GetRequiredService<IEquipmentCatalogProvider>(),
            sp.GetRequiredService<IOptionsMonitor<PuertaSettings>>(),
            sp.GetRequiredService<ILogger<ArtefactoDeAlquiler>>(),
            hayOrquestadorDeAlquiler));

        // La OFERTA de cada equipo (línea 005, T029): lo que la renta por la puerta cotiza. Se publica al publicar un
        // equipmentPage y se retira al despublicarlo o borrarlo, NUNCA al arrancar (ADR 0013). Sólo tiene sentido
        // donde la puerta tiene destino —Synergos:Alquiler:BaseUrl, la misma condición con que abre alquiler.renta—;
        // sin él no sale a la red y no falla.
        var hayDestinoParaLaOferta = !string.IsNullOrWhiteSpace(seccionDeAlquiler["BaseUrl"]);
        if (hayDestinoParaLaOferta)
        {
            services.AddClienteDelArbolDeServicios(
                HttpEquipmentOfferPublisher.ClientName, DestinoDelArbol.De(seccionDeAlquiler, "http://127.0.0.1:5305/", 10));
        }
        services.AddSingleton<IEquipmentOfferPublisher>(sp => new HttpEquipmentOfferPublisher(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<HttpEquipmentOfferPublisher>>(),
            hayDestinoParaLaOferta));
        builder.AddNotificationAsyncHandler<ContentPublishedNotification, OfertaDeEquipoAlPublicar>();
        builder.AddNotificationAsyncHandler<ContentUnpublishedNotification, OfertaDeEquipoAlRetirar>();
        builder.AddNotificationAsyncHandler<ContentMovedToRecycleBinNotification, OfertaDeEquipoAlRetirar>();
        builder.AddNotificationAsyncHandler<ContentDeletedNotification, OfertaDeEquipoAlRetirar>();
    }

    /// <summary>
    /// El sellador de <c>Api.Signing</c> para los contratos, con su cliente por la pieza del árbol: sella con
    /// <c>Mode=Api</c>, y con <c>Mode=Local</c> comprueba lo que selló mientras estuvo encendido.
    /// </summary>
    private static void RegistrarSelladorDeApiSigning(IServiceCollection services, IConfiguration seccionDelSello)
    {
        services.AddClienteDelArbolDeServicios(
            HttpAgreementSealer.ClientName, DestinoDelArbol.De(seccionDelSello, "http://127.0.0.1:5218/", 10));
        services.AddSingleton(sp => new HttpAgreementSealer(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<IOptions<AgreementSettings>>(),
            sp.GetRequiredService<ILogger<HttpAgreementSealer>>()));
    }
}
