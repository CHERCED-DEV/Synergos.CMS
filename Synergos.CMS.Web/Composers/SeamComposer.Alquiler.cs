using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;
using Synergos.CMS.Web.Services.Catalog;

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
        services.AddSingleton(sp => new EquipmentAgreementIssuer(
            sp.GetRequiredService<EquipmentAgreementLedger>(),
            sp.GetRequiredService<IAgreementSigner>(),
            sp.GetRequiredService<TimeProvider>()));

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
            services.AddSingleton<IEquipmentRentalService>(sp => new StubEquipmentRentalService(
                sp.GetRequiredService<IEquipmentCatalogProvider>(),
                sp.GetRequiredService<IJsonEntityStore>(),
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<IOptionsMonitor<AlquilerSettings>>().CurrentValue.MaxRentalDays));
        }
    }
}
