using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Composers;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// El seam de pago contra <c>Api.Payments</c>, como invariante ejecutable (#27).
/// </summary>
/// <remarks>
/// <para><b>Esto abre una exención en <c>ShopWiringTests</c> y por eso tiene que ganársela.</b>
/// Aquel gate prohíbe que cualquier fichero del CMS nombre <c>v1/payments</c>, y con razón:
/// cablear <c>Api.Inventory</c> + <c>Api.Payments</c> + <c>Api.Orders</c> por separado es el
/// atajo natural y deja al CMS orquestando una saga que no sabe deshacer.
/// <c>HttpPaymentProvider</c> es la excepción porque es <b>el proveedor de pago a secas</b>: toca
/// una sola capacidad y no sabe qué se está comprando.</para>
///
/// <para><b>Pero quien podría componer no es el cliente: es su LLAMADOR</b>, y eso no se decide
/// en el código sino en el despliegue. Con <c>Tienda:Mode=Stub</c> el que llama es
/// <c>StubShopOrderService</c>, que aparta stock, cobra y crea el pedido. Por eso la mitad
/// interesante de este gate no mira código: comprueba que el composer se <b>niegue a arrancar</b>
/// con el seam contra la capacidad y algún vertical todavía orquestando de este lado.</para>
///
/// <para><b>Y la tasa de Gobierno va por su propio interruptor</b> justamente porque radicar NO
/// compone: el motor decide no abortar el trámite si la captura no sale, y si no se aborta no hay
/// nada que deshacer. Eso era un comentario hasta que detrás hubo una pasarela de verdad; ahora
/// hay dos tests que lo ejercen.</para>
/// </remarks>
public sealed class PaymentsWiringTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>El cliente, <b>sin comentarios</b>.</summary>
    /// <remarks>
    /// Hace falta y ya lo demostró una mutación en <c>RealtyWiringTests</c>: estos ficheros
    /// explican en prosa lo que hacen, así que un gate que busque una ruta sobre el texto crudo
    /// la encuentra en el <c>&lt;remarks&gt;</c> aunque el código haya dejado de usarla.
    /// </remarks>
    private static string CodigoDelCliente() => SinComentarios(Path.Combine(
        RepoRoot(), "Synergos.CMS.Web", "Services", "HttpPaymentProvider.cs"));

    private static string SinComentarios(string ruta)
        => string.Join('\n', File.ReadAllLines(ruta)
            .Select(l =>
            {
                var t = l.TrimStart();
                if (t.StartsWith("//", StringComparison.Ordinal) || t.StartsWith("*", StringComparison.Ordinal))
                {
                    return string.Empty;
                }
                var i = l.IndexOf("//", StringComparison.Ordinal);
                return i >= 0 ? l[..i] : l;
            }));

    private static IConfiguration Config(params (string Clave, string Valor)[] valores)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(valores.Select(v => new KeyValuePair<string, string?>(v.Clave, v.Valor)))
            .Build();

    private static (string Clave, string Valor)[] TodosCableados() => new[]
    {
        ("Synergos:Tienda:Mode", "Bff"),
        ("Synergos:Salud:Mode", "Bff"),
        ("Synergos:Eventos:Mode", "Bff"),
        ("Synergos:Viajes:Mode", "Bff"),
    };

    // ── El cliente ──────────────────────────────────────────────────────────

    [Fact]
    public void El_cliente_directo_toca_UNA_SOLA_capacidad()
    {
        // ES LA CONDICIÓN DE LA EXENCIÓN, no un detalle. Hablarle a una capacidad sin orquestador
        // está bien mientras no haya un segundo paso que pueda fallar dejando el primero hecho.
        // En cuanto este cliente aparte stock o cree un pedido, hay algo que deshacer, y eso es un
        // BFF — y hasta entonces la exención de ShopWiringTests tapa justo lo que aquel gate
        // existe para impedir.
        var ajenas = new[]
        {
            "v1/orders", "v1/items", "v1/holds", "v1/carts", "v1/shipments", "v1/quotes",
            "v1/reservations", "v1/resources", "v1/deliveries", "v1/threads", "v1/documents",
            "v1/instances", "v1/grants", "v1/seals", "v1/entries",
        };

        var codigo = CodigoDelCliente();
        var encontradas = ajenas.Where(r => codigo.Contains(r, StringComparison.OrdinalIgnoreCase)).ToList();

        Assert.True(encontradas.Count == 0,
            "El proveedor de pago toca más de una capacidad (" + string.Join(", ", encontradas)
            + "). Eso ya es un flujo con algo que deshacer: va por un orquestador, no por acá.");

        // Y el complemento: que la ruta que SÍ usa sea la de la capacidad. Sin esto, borrar la
        // llamada entera también pasaría el gate de arriba.
        Assert.Contains("v1/payments", codigo, StringComparison.Ordinal);
    }

    [Fact]
    public void La_llave_de_idempotencia_va_en_los_TRES_POST_que_mueven_plata()
    {
        // De todas las llaves del sistema, ésta es la que más duele si falta: un reintento tras un
        // timeout cobra dos veces a una persona real. Autorizar, capturar y devolver la exigen;
        // liberar NO, porque es idempotente por diseño y pedir una cabecera que no protege de nada
        // sólo enseña a inventar llaves.
        var codigo = CodigoDelCliente();

        Assert.Equal(3, codigo.Split("Idempotency-Key").Length - 1);

        // Y sale de la referencia que TRAE la petición, no de algo que se lea después. Al revés
        // —derivarla del estado de la sesión, o de lo que contestó el intento anterior— cada
        // reintento sería una llave nueva, o sea un cobro nuevo
        // (feedback_idempotency_before_state).
        Assert.Contains("Llave(nombres, request.OrderReference)", codigo, StringComparison.Ordinal);
    }

    [Fact]
    public void A_la_capacidad_NO_le_viaja_el_correo_de_quien_paga()
    {
        // Api.Payments cuenta plata; no necesita saber quién es. Es lo mismo que ya hacen el
        // cliente de visitas (#33a), el de la tienda (#47) y el asiento de auditoría (#15).
        var codigo = CodigoDelCliente();

        Assert.Contains("payerId = Seudonimo(", codigo, StringComparison.Ordinal);
        Assert.DoesNotContain("payerId = request.CustomerEmail", codigo, StringComparison.Ordinal);
    }

    [Fact]
    public void El_actionUrl_de_la_capacidad_SE_LEE()
    {
        // Api.Payments lo emitía desde la HU #27 sin un solo consumidor, y sin él no se puede
        // cobrar con checkout hospedado: la transacción nace cuando el comprador la completa, así
        // que hasta que vuelva no hay nada que capturar. Ignorarlo haría que el CMS capturara una
        // intención que nadie pagó y escribiera un fallo que nadie causó.
        var codigo = CodigoDelCliente();

        Assert.Contains("ActionUrl", codigo, StringComparison.Ordinal);
        Assert.Contains("PaymentAction.Redirect", codigo, StringComparison.Ordinal);
        Assert.Contains("PaymentStatus.RequiresAction", codigo, StringComparison.Ordinal);
    }

    // ── El despliegue ───────────────────────────────────────────────────────

    [Fact]
    public void El_motor_en_proceso_sigue_siendo_el_default()
    {
        // El camino de un clon limpio: sin capacidades levantadas, los seis verticales cobran con
        // el motor en proceso. Cambiar el default convertiría «no configurado» en «roto».
        Assert.Equal("Engine", new PaymentsSettings().Mode);
        Assert.Equal("Local", new GovFeeSettings().Mode);
    }

    [Fact]
    public void El_modo_Api_se_NIEGA_si_alguien_orquesta_de_este_lado()
    {
        // La mala configuración que tiene que gritar, y es la que cualquiera escribiría primero:
        // encender el seam contra la capacidad dejando la tienda en su motor. Con plata de verdad
        // detrás queda stock apartado que nadie suelta y cobros sin pedido.
        foreach (var (flag, _) in TodosCableados())
        {
            var aMedias = TodosCableados().Select(v => v.Clave == flag ? (flag, "Stub") : v).ToArray();

            var malo = Assert.Throws<InvalidOperationException>(
                () => SeamComposer.ExigirQueNadieOrqueste(Config(aMedias)));

            // El mensaje nombra el flag y el remedio: un fallo de arranque que no dice qué mover
            // deja al operador leyendo código.
            Assert.Contains(flag, malo.Message, StringComparison.Ordinal);
            Assert.Contains("Synergos:Payments:Mode=Engine", malo.Message, StringComparison.Ordinal);
            Assert.Contains("Synergos:Gob:Payments:Mode=Api", malo.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Con_los_cuatro_cableados_el_seam_contra_la_capacidad_arranca()
    {
        // El complemento del anterior: sin esto, una guarda que rechazara SIEMPRE también pasaría
        // el test de arriba, y habría convertido una protección en un muro.
        SeamComposer.ExigirQueNadieOrqueste(Config(TodosCableados()));
    }

    [Fact]
    public void La_tasa_de_Gobierno_NO_espera_a_los_cuatro()
    {
        // Es la razón de que sea su propio interruptor. Radicar no compone una saga, así que la
        // tasa puede ir a la capacidad hoy; el seam entero no. Si esta guarda mirase también el
        // flag de Gobierno, el interruptor no serviría para nada.
        Assert.Contains("Synergos:Gob:Payments", SinComentarios(Path.Combine(
            RepoRoot(), "Synergos.CMS.Web", "Composers", "SeamComposer.EventsPropertiesGov.cs")),
            StringComparison.Ordinal);

        SeamComposer.ExigirQueNadieOrqueste(
            Config(TodosCableados().Append(("Synergos:Gob:Payments:Mode", "Api")).ToArray()));
    }

    // ── La tasa del trámite ─────────────────────────────────────────────────

    [Fact]
    public async Task El_tramite_se_radica_aunque_la_tasa_NO_se_pueda_cobrar()
    {
        // La decisión de negocio que tiene este vertical escrita desde siempre —«no se aborta el
        // trámite si la captura no sale»— era un comentario mientras el motor de pago no podía
        // fallar. Con Api.Payments detrás sí puede, y perder la radicación de un ciudadano porque
        // su banco tardó es peor que arrastrar una tasa pendiente.
        var (caso, expediente) = await Radicar(new PagoCaido());

        Assert.Equal(CaseStatus.Radicado, caso.Status);
        Assert.True(caso.FeeMinor > 0m);

        // Y queda ESCRITO que no se sabe. «Failed» diría que el banco rechazó, que es otra cosa y
        // lleva a otra acción. Se lee del ALMACÉN y no del resultado: lo que importa es lo que
        // quedó en el expediente, que es lo que alguien va a mirar dentro de un mes.
        Assert.Contains($"\"PaymentStatus\":\"{StubApplicationService.FeeUnavailable}\"",
            expediente, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Lo_que_pide_ir_a_pagar_NO_se_captura()
    {
        // Con checkout hospedado la transacción nace cuando el ciudadano la completa: la sesión
        // vuelve pidiendo que se le mande a pagar y no hay nada que capturar todavía. Capturar
        // igual deja escrito un fallo que nadie causó — y sobre un trámite ya radicado, eso es un
        // expediente que dice que el pago se rechazó cuando nadie lo intentó.
        var pasarela = new PagoConRedireccion();
        var (_, expediente) = await Radicar(pasarela);

        Assert.Equal(0, pasarela.Capturas);
        Assert.Contains($"\"PaymentStatus\":\"{nameof(PaymentStatus.RequiresAction)}\"",
            expediente, StringComparison.Ordinal);
    }

    private static async Task<(CaseDetail Caso, string Expediente)> Radicar(IPaymentProvider pasarela)
    {
        var almacen = new InMemoryJsonEntityStore();
        var motor = new StubApplicationService(
            new StubTramiteCatalogProvider(), new StubGovFeeCalculator(), pasarela,
            audit: null, now: null, store: almacen);

        var resultado = await motor.RadicarAsync(
            "trm-pasaporte",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["nombreCompleto"] = "Ana Torres",
                ["cedula"] = "1030567890",
                ["fechaNacimiento"] = "1991-05-20",
                ["ciudad"] = "Bogotá",
                ["correo"] = "ana.torres@correo.co",
            },
            new GovCitizen("Ana Torres", "ana.torres@correo.co"));

        // La misma clave que usa el motor: el id en mayúsculas (StoreKey).
        var guardado = await almacen.ReadAsync("gov-cases", resultado.Case.CaseId.ToUpperInvariant());
        Assert.False(string.IsNullOrWhiteSpace(guardado));

        // Sin espacios: el expediente se escribe indentado y esto se compara literal.
        return (resultado.Case, Regex.Replace(guardado!, @"\s+", string.Empty));
    }

    /// <summary>La capacidad caída: «no sé», que NO es «no».</summary>
    private sealed class PagoCaido : IPaymentProvider
    {
        public string ProviderKey => "caido";

        public Task<PaymentSession> CreateSessionAsync(PaymentSessionRequest request, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Api.Payments no pudo autorizar el cobro (503).");

        public Task<PaymentOutcome> GetStatusAsync(string sessionId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException();

        public Task<PaymentOutcome> CaptureAsync(string sessionId, decimal? amount = null, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException();

        public Task<PaymentOutcome> VoidAsync(string sessionId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException();

        public Task<PaymentOutcome> RefundAsync(string sessionId, decimal? amount = null, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException();
    }

    /// <summary>El checkout hospedado: la sesión vuelve pidiendo que el comprador vaya a pagar.</summary>
    private sealed class PagoConRedireccion : IPaymentProvider
    {
        public int Capturas { get; private set; }

        public string ProviderKey => "api";

        public Task<PaymentSession> CreateSessionAsync(PaymentSessionRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new PaymentSession(
                "pay_1", PaymentStatus.RequiresAction,
                PaymentAction.Redirect(new Uri("https://checkout.example/pay_1")), ProviderKey));

        public Task<PaymentOutcome> GetStatusAsync(string sessionId, CancellationToken cancellationToken = default)
            => Task.FromResult(new PaymentOutcome(sessionId, PaymentStatus.RequiresAction));

        public Task<PaymentOutcome> CaptureAsync(string sessionId, decimal? amount = null, CancellationToken cancellationToken = default)
        {
            Capturas++;
            return Task.FromResult(new PaymentOutcome(sessionId, PaymentStatus.Failed, 0m, "nadie pagó todavía"));
        }

        public Task<PaymentOutcome> VoidAsync(string sessionId, CancellationToken cancellationToken = default)
            => Task.FromResult(new PaymentOutcome(sessionId, PaymentStatus.Cancelled));

        public Task<PaymentOutcome> RefundAsync(string sessionId, decimal? amount = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new PaymentOutcome(sessionId, PaymentStatus.Refunded));
    }

    /// <summary>
    /// La guarda se LLAMA desde el composer — no basta con que exista.
    /// </summary>
    /// <remarks>
    /// <para><b>Este gate se escribió porque el resto de este fichero pasaba en verde con la
    /// guarda DESENCHUFADA.</b> Comentando la llamada en <c>SeamComposer.PaymentEngine.cs</c>,
    /// los 26 tests de pagos, tienda y coexistencia seguían pasando: los demás invocan
    /// <c>ExigirQueNadieOrqueste</c> <b>directamente</b>, así que prueban la REGLA y no el
    /// CABLEADO — y lo que decide es lo que hay entre los dos.</para>
    ///
    /// <para>Es la tercera vez que este repo tropieza con la misma forma, y ya está escrita en
    /// <c>CLAUDE.md</c> §5: <c>feedback_an_exemption_needs_a_signature_behind_it</c> dice que el
    /// gate tiene que medir que la pieza esté <b>ENCHUFADA</b> y no que exista — <c>Api.Notifications</c>
    /// quitó la verificación de firma de su lambda y no falló ni un test. Y la rebanada 5 de la
    /// HU #14 lo dice igual: «el gate mira el CABLEADO, no la regla».</para>
    ///
    /// <para>Se mide sobre la fuente sin comentarios, porque la prosa de arriba nombra la guarda
    /// para explicarla — un gate que se dispara con su propia documentación se acaba desactivando.
    /// Y se exige que la llamada esté <b>dentro</b> de la rama que enciende el modo <c>Api</c>: una
    /// llamada suelta en otro sitio del fichero pasaría el <c>Contains</c> sin proteger nada.</para>
    /// </remarks>
    [Fact]
    public void La_guarda_se_LLAMA_desde_el_composer_y_no_solo_existe()
    {
        var composer = SinComentarios(Path.Combine(
            RepoRoot(), "Synergos.CMS.Web", "Composers", "SeamComposer.PaymentEngine.cs"));

        var enciendeApi = composer.IndexOf("\"Synergos:Payments:Mode\"", StringComparison.Ordinal);
        Assert.True(enciendeApi >= 0, "El composer ya no lee Synergos:Payments:Mode — ¿se movió el cableado?");

        var declara = composer.IndexOf("void ExigirQueNadieOrqueste", StringComparison.Ordinal);
        Assert.True(declara >= 0, "La guarda ya no se declara en este fichero.");

        // La LLAMADA: la declaración no cuenta, y tiene que estar entre el `if` del modo y el
        // cuerpo de la guarda — o sea dentro de la rama que enciende el cableado.
        var llamada = composer.IndexOf("ExigirQueNadieOrqueste(", enciendeApi, StringComparison.Ordinal);

        Assert.True(
            llamada >= 0 && llamada < declara,
            "La guarda existe pero NADIE la llama al cablear. Comentar esa línea deja el modo Api "
            + "encendido sobre un vertical que orquesta de este lado —plata real detrás de una saga "
            + "que el CMS no sabe deshacer— y el resto de los tests siguen verdes, porque invocan "
            + "la guarda directamente. Un gate que mide la regla y no el cableado no vigila nada.");
    }
}
