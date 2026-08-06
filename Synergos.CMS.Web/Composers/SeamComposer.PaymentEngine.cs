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
    private void ComposePaymentEngine(IUmbracoBuilder builder)
    {
        var services = builder.Services;

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
        // Solo el ROUTER decide la FORMA del registro, así que es lo único que hay que leer en
        // tiempo de composición. Qué proveedor concreto se sirve se resuelve dentro de la
        // fábrica, contra IOptions — misma sección, pero un valor y no dos fuentes.
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
                var wompi = TryBuildWompi(sp, settings);
                if (wompi is not null)
                {
                    members.Add(wompi);
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
            // Proveedor ÚNICO, el que diga Synergos:Payments:Provider.
            //
            // Aquí había un switch con un solo brazo vivo —default → stub— y el caso "wompi"
            // comentado. Es decir: Provider="Wompi" con el router apagado servía el STUB EN
            // SILENCIO, contra lo que promete la documentación del propio ajuste ("Stub o
            // Wompi, requiere llaves"). Un operador podía creerse en producción cobrando de
            // verdad mientras el checkout devolvía pagos de mentira. La auditoría lo anotó
            // como olor de OCP; medido, era una config que miente.
            services.AddSingleton<IPaymentProvider>(SelectSingleProvider);
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

    }

    /// <summary>Clave del proveedor Wompi. Es la misma que devuelve su <c>ProviderKey</c>.</summary>
    internal const string WompiProviderKey = "wompi";

    /// <summary>
    /// El proveedor único que sirve cuando el router está apagado, según
    /// <see cref="PaymentsSettings.Provider"/>.
    /// </summary>
    /// <remarks>
    /// <b>Es un método con nombre y no el lambda del registro</b> para poder verificarlo: qué
    /// proveedor termina cobrando es exactamente la decisión que estaba mal y no tenía test.
    /// </remarks>
    internal static IPaymentProvider SelectSingleProvider(IServiceProvider sp)
    {
        var settings = sp.GetRequiredService<IOptions<PaymentsSettings>>().Value;
        var stub = new StubPaymentProvider(sp.GetRequiredService<IJsonEntityStore>(), settings);

        if (!string.Equals(settings.Provider?.Trim(), WompiProviderKey, StringComparison.OrdinalIgnoreCase))
        {
            return stub;
        }

        var wompi = TryBuildWompi(sp, settings);
        if (wompi is not null)
        {
            return wompi;
        }

        // Degradar al stub sigue siendo lo correcto —romper el arranque dejaría la demo sin
        // checkout por una llave que falta—, pero ahora se degrada A GRITOS. El silencio era
        // el problema, no el fallback.
        sp.GetRequiredService<ILogger<SeamComposer>>().LogWarning(
            "Synergos:Payments:Provider={Provider} pero faltan WompiPublicKey y/o " +
            "WompiIntegritySecret. Se sirve el stub durable: los pagos NO son reales.",
            settings.Provider);
        return stub;
    }

    /// <summary>
    /// El adapter de Wompi, o <c>null</c> si le faltan llaves.
    /// </summary>
    /// <remarks>
    /// <b>Una sola fábrica para las dos ramas</b> —con router y sin él— para que
    /// <c>Provider=Wompi</c> signifique lo mismo en ambas. Estaban duplicadas y divergieron:
    /// la del router construía Wompi de verdad y la otra caía al stub sin decir nada.
    ///
    /// <para>Las llaves que se exigen son las del CHECKOUT (pública + secreto de integridad),
    /// que es lo que necesita firmar una transacción. La privada es opcional a propósito: solo
    /// hace falta para consultar el API, y su ausencia degrada una consulta, no un cobro.</para>
    /// </remarks>
    private static IPaymentProvider? TryBuildWompi(IServiceProvider sp, PaymentsSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.WompiPublicKey)
            || string.IsNullOrWhiteSpace(settings.WompiIntegritySecret))
        {
            return null;
        }

        return new WompiPaymentProvider(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("wompi"),
            settings);
    }
}
