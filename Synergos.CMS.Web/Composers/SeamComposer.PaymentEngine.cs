using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Web.Composers;

public sealed partial class SeamComposer
{
    private static void ComposePaymentEngine(IUmbracoBuilder builder)
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

        // HU #27 — las dos mitades no pueden cobrar de verdad a la vez. Falla al CABLEAR, no en
        // la primera petición: un despliegue que arranca verde, contesta /health y revienta
        // cuando una persona intenta pagar es el peor de los tres modos de fallo.
        ExigirUnaSolaPlomeria(builder.Config);

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

        // #27, la parte que quedó viva — el seam entero contra la capacidad.
        //
        // `Synergos:Payments:Mode=Api` quita el motor de pago de este lado: el CMS deja de tener
        // pasarela y le pide los cobros a Api.Payments. Es lo que cierra la decisión de la HU #27
        // —«Api.Payments es lo único que mueve plata de verdad»— para los verticales que todavía
        // cobran en proceso.
        //
        // El default es Engine y no es una transición: el motor en proceso es lo que permite
        // levantar el repo entero sin ningún servicio.
        if (string.Equals(builder.Config["Synergos:Payments:Mode"], "Api", StringComparison.OrdinalIgnoreCase))
        {
            // Y NO se enciende mientras alguien orqueste de este lado. Revienta al cablear, como
            // ExigirUnaSolaPlomeria y por lo mismo.
            ExigirQueNadieOrqueste(builder.Config);

            var payBase = builder.Config["Synergos:Payments:BaseUrl"];
            var payKey = builder.Config["Synergos:Payments:ApiKey"];
            var payTimeout = int.TryParse(builder.Config["Synergos:Payments:TimeoutSeconds"], out var pt) && pt > 0 ? pt : 30;

            services.AddHttpClient(HttpPaymentProvider.SeamClientName, http =>
            {
                var url = string.IsNullOrWhiteSpace(payBase) ? "http://127.0.0.1:5204/" : payBase;
                http.BaseAddress = new Uri(url.EndsWith('/') ? url : url + "/");
                http.Timeout = TimeSpan.FromSeconds(payTimeout);
                if (!string.IsNullOrWhiteSpace(payKey))
                {
                    http.DefaultRequestHeaders.Add(HttpPaymentProvider.ApiKeyHeader, payKey);
                }
            })
            .AddHttpMessageHandler<CorrelationForwardingHandler>();

            services.AddSingleton<IPaymentProvider>(sp =>
            {
                var opciones = sp.GetRequiredService<IOptionsMonitor<PaymentsSettings>>();
                return new HttpPaymentProvider(
                    sp.GetRequiredService<IHttpClientFactory>(),
                    HttpPaymentProvider.SeamClientName,
                    () => new PaymentWireKinds(
                        opciones.CurrentValue.SubjectKind, opciones.CurrentValue.PayerKind, "cms"),
                    sp.GetRequiredService<ILogger<HttpPaymentProvider>>(),
                    // El mismo emisor que la tasa: quien decide si se presenta identidad es el
                    // proveedor —solo con MemberKey detras— y no el composer. Cablear uno si y
                    // otro no dejaria el mismo tipo comportandose distinto segun por donde se
                    // construyo, que es como se esconde un defecto detras de una seam.
                    sp.GetRequiredService<IIdentityTokenIssuer>());
            });
        }
        else if (routingEnabled)
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


    /// <summary>
    /// Que sólo una de las dos mitades cobre de verdad (HU #27).
    /// </summary>
    /// <remarks>
    /// <para><b>La plata la mueve <c>Api.Payments</c>.</b> Este motor en proceso sigue vivo porque
    /// es lo que permite levantar el repo entero sin ningún servicio —<c>Tienda:Mode=Stub</c>—,
    /// pero con la tienda cableada contra el orquestador la plata la tiene él, y dejar las dos
    /// plomerías cobrando hace que un mismo cobro pase por proveedores distintos según un
    /// interruptor. <b>Eso ya ocurrió</b>: es el defecto #57, donde el RMA le pedía el reembolso
    /// al proveedor local con el identificador de la saga —que ese proveedor no conocía— y el
    /// caso no llegaba nunca a reembolsado <i>sin que nada fallara</i>.</para>
    ///
    /// <para><b>Se revienta al arrancar y no al primer cobro</b>, que es la forma de #56 (el modo
    /// <c>Http</c> del CDN sin URL) y la de la llave de firma de <c>Api.Identity</c>. Un
    /// despliegue mal configurado que arranca verde, contesta <c>/health</c> y pasa la prueba de
    /// humo <b>parece uno bueno</b>, y lo desmiente la primera persona que intenta pagar.</para>
    ///
    /// <para><b>Lo que se mira es si este lado COBRARÍA</b>, no si el nombre está escrito: hacen
    /// falta las dos llaves del checkout —sin ellas <see cref="TryBuildWompi"/> devuelve
    /// <c>null</c> y acá no se cobra nada—, y además que algo pueda elegirlo, o sea el proveedor
    /// único puesto en <c>wompi</c> o el router encendido, que lo admite como miembro.</para>
    /// </remarks>
    internal static void ExigirUnaSolaPlomeria(IConfiguration config)
    {
        var tiendaCableada = string.Equals(
            config["Synergos:Tienda:Mode"], "Bff", StringComparison.OrdinalIgnoreCase);

        // Y el seam entero contra la capacidad cuenta igual (#27, la parte que quedó viva): con
        // Synergos:Payments:Mode=Api este lado no cobra, así que unas llaves de Wompi aquí no
        // cobrarían tampoco — se quedarían calladas. Una config que no hace nada y parece que sí
        // es el mismo defecto de `Provider=Wompi` sirviendo el stub, sólo que al revés.
        var seamCableado = string.Equals(
            config["Synergos:Payments:Mode"], "Api", StringComparison.OrdinalIgnoreCase);

        if (!tiendaCableada && !seamCableado) return;

        var tieneLlaves = !string.IsNullOrWhiteSpace(config["Synergos:Payments:WompiPublicKey"])
            && !string.IsNullOrWhiteSpace(config["Synergos:Payments:WompiIntegritySecret"]);
        if (!tieneLlaves) return;

        var loPuedeElegir = string.Equals(
                config["Synergos:Payments:Provider"]?.Trim(), WompiProviderKey, StringComparison.OrdinalIgnoreCase)
            || config.GetValue<bool>("Synergos:Payments:Routing:Enabled");
        if (!loPuedeElegir) return;

        throw new InvalidOperationException(
            "La plata la mueve Api.Payments en este despliegue —Synergos:Tienda:Mode=Bff, o "
            + "Synergos:Payments:Mode=Api— y ADEMÁS tiene "
            + "ADEMÁS llaves reales de Wompi del lado del CMS "
            + "(Synergos:Payments:WompiPublicKey + WompiIntegritySecret, con "
            + "Synergos:Payments:Provider=Wompi o Routing:Enabled=true). Las dos mitades cobrarían, "
            + "y un mismo cobro pasaría por proveedores distintos según un interruptor — es el "
            + "defecto #57, que no falla a la vista. Desde la HU #27 la plata la mueve "
            + "Api.Payments: quitá las llaves de acá (Payments__wompi__* van en la capacidad) o "
            + "volvé la tienda a Synergos:Tienda:Mode=Stub.");
    }

    /// <summary>
    /// Los verticales que ORQUESTAN de este lado cuando están en su motor en proceso.
    /// </summary>
    /// <remarks>
    /// Cada uno compone varios pasos que pueden fallar a la mitad —apartar, cobrar, confirmar— y
    /// el CMS no tiene dónde anotar una compensación pendiente. Es lo que
    /// <c>ShopWiringTests</c> defiende en compilación.
    /// </remarks>
    private static readonly (string Flag, string Cableado, string Que)[] Orquestadores =
    {
        ("Synergos:Tienda:Mode", "Bff", "la tienda"),
        ("Synergos:Salud:Mode", "Bff", "la cita clínica"),
        ("Synergos:Eventos:Mode", "Bff", "la compra de entradas"),
        ("Synergos:Viajes:Mode", "Bff", "la reserva de viaje"),
    };

    /// <summary>
    /// Que con el seam contra la capacidad no quede nadie orquestando de este lado (#27).
    /// </summary>
    /// <remarks>
    /// <para><b>Es la condición de la excepción, no una precaución.</b> Hablarle a
    /// <c>Api.Payments</c> de frente está bien mientras el llamador sea un solo paso. Con
    /// <c>Tienda:Mode=Stub</c> el que llama es <c>StubShopOrderService</c>, que aparta stock,
    /// cobra y crea el pedido: si el cobro sale y el pedido no, hay plata movida sin nada que la
    /// deshaga, y el CMS no tiene libro de compensaciones. Ése es exactamente el atajo que
    /// <c>ShopWiringTests</c> existe para impedir — sólo que ese gate mira el código y esto mira
    /// el despliegue, que es donde se decide con qué motor corre cada vertical.</para>
    ///
    /// <para><b>Revienta al arrancar y no en el primer cobro</b>, que es la forma de #56 y la de
    /// la llave de firma de <c>Api.Identity</c>: un despliegue que arranca verde, contesta
    /// <c>/health</c> y pasa la prueba de humo <b>parece uno bueno</b>, y lo desmiente la primera
    /// persona que compra.</para>
    ///
    /// <para><b>Lo que esta guarda NO cubre, y va dicho:</b> la matrícula de Educación
    /// (<c>StubEnrollmentService</c>) también compone —abre la sesión, guarda la matrícula
    /// pendiente y captura en un confirm aparte— y no tiene interruptor que mirar porque
    /// <c>Bff.Academy</c> no existe. Va en el orden correcto (cerrar puertas al final) y su
    /// confirm es idempotente, así que la ventana es «capturado y la activación no se escribió»,
    /// que se rescata repitiendo el confirm. El día que exista <c>Bff.Academy</c>, su bandera
    /// entra en la lista de arriba.</para>
    /// </remarks>
    internal static void ExigirQueNadieOrqueste(IConfiguration config)
    {
        var enProceso = Orquestadores
            .Where(o => !string.Equals(config[o.Flag], o.Cableado, StringComparison.OrdinalIgnoreCase))
            .Select(o => $"{o.Que} ({o.Flag}={o.Cableado})")
            .ToList();

        if (enProceso.Count == 0) return;

        throw new InvalidOperationException(
            "Synergos:Payments:Mode=Api le pide los cobros a Api.Payments, y estos verticales "
            + "siguen orquestando con el motor en proceso: " + string.Join(", ", enProceso)
            + ". Cada uno aparta, cobra y confirma en varios pasos que pueden fallar a la mitad, y "
            + "el CMS no tiene dónde anotar una compensación pendiente: con plata de verdad detrás "
            + "queda stock apartado que nadie suelta y cobros sin pedido. O se cablean contra su "
            + "orquestador, o el seam se queda en Synergos:Payments:Mode=Engine. Para cobrar sólo "
            + "la tasa de un trámite —que NO orquesta— está Synergos:Gob:Payments:Mode=Api.");
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
