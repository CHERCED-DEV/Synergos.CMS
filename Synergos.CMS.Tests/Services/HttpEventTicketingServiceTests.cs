using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Comprando entradas de verdad contra <c>Bff.Eventos</c> (HU #35, rebanada 2b).
/// </summary>
/// <remarks>
/// <para>Lo que se prueba acá no es «comprar». Es lo que este camino tiene de distinto a todos
/// los demás: <b>la compra se parte en dos mitades que viven en sitios distintos</b>. El
/// orquestador mueve aforo y plata; el CMS se queda con el artefacto y con lo único que el
/// orquestador deliberadamente NO lleva — quién va a sentarse.</para>
///
/// <list type="bullet">
///   <item>que el CMS <b>anote los asistentes de su lado</b>, porque si no la compra existiría
///   allá y no habría de dónde emitir;</item>
///   <item>que una saga que responde 200 pero <b>no quedó Completed</b> no emita entradas;</item>
///   <item>que un timeout <b>no se trague la excepción</b> ni cree una segunda compra;</item>
///   <item>que «mis entradas», transferir y la puerta <b>no toquen la red</b> — quien ya compró
///   no se queda fuera del concierto porque el BFF esté caído;</item>
///   <item>que la puerta escanee lo comprado por ESTE camino, que es el defecto que la rebanada
///   anterior destapó.</item>
/// </list>
/// </remarks>
public sealed class HttpEventTicketingServiceTests
{
    private sealed class OrquestadorFalso : HttpMessageHandler
    {
        private readonly Dictionary<string, Func<HttpResponseMessage>> _rutas = new(StringComparer.OrdinalIgnoreCase);

        public List<(string Method, string Path, string? Key)> Llamadas { get; } = new();

        /// <summary>Lo que se mandó, tal cual. Sin esto, «el correo no sale por el cable» no se
        /// puede afirmar: se estaría mirando la URL, que nunca lo llevó.</summary>
        public List<string> Cuerpos { get; } = new();

        /// <summary>Las cabeceras de cada petición, por nombre.</summary>
        public List<string> Cabeceras { get; } = new();
        public HashSet<string> Caidas { get; } = new(StringComparer.OrdinalIgnoreCase);

        public OrquestadorFalso Ok(string ruta, string json)
        {
            _rutas[ruta] = () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            return this;
        }

        public OrquestadorFalso Falla(string ruta, HttpStatusCode codigo, string code, string detail)
        {
            _rutas[ruta] = () => new HttpResponseMessage(codigo)
            {
                Content = new StringContent(
                    $$"""{"title":"x","status":{{(int)codigo}},"detail":"{{detail}}","code":"{{code}}"}""",
                    Encoding.UTF8, "application/problem+json"),
            };
            return this;
        }

        public OrquestadorFalso Caida(string ruta) { Caidas.Add(ruta); return this; }

        public int Veces(string method, string sufijo)
            => Llamadas.Count(l => l.Method == method && l.Path.EndsWith(sufijo, StringComparison.Ordinal));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            var path = req.RequestUri!.AbsolutePath;
            var clave = $"{req.Method.Method} {path}";
            req.Headers.TryGetValues("Idempotency-Key", out var k);
            Llamadas.Add((req.Method.Method, path, k?.FirstOrDefault()));
            Cabeceras.AddRange(req.Headers.Select(h => h.Key));
            if (req.Content is not null)
            {
                Cuerpos.Add(req.Content.ReadAsStringAsync(ct).GetAwaiter().GetResult());
            }

            if (Caidas.Contains(clave)) throw new HttpRequestException("guionado: caída");

            return Task.FromResult(_rutas.TryGetValue(clave, out var f)
                ? f()
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class FabricaFalsa : IHttpClientFactory
    {
        private readonly OrquestadorFalso _handler;
        public FabricaFalsa(OrquestadorFalso h) => _handler = h;
        public HttpClient CreateClient(string name)
            => new(_handler, disposeHandler: false) { BaseAddress = new Uri("http://eventos.local/") };
    }

    private sealed class Monitor<T> : IOptionsMonitor<T>
    {
        public Monitor(T v) => CurrentValue = v;
        public T CurrentValue { get; }
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> l) => null;
    }

    private static readonly ITicketSigner Firmante =
        new HmacTicketSigner(Encoding.UTF8.GetBytes("llave-de-tests-eventos"));

    private const string CompraCorriendo = """
        {"id":"tp-1","buyerKind":"eventos.comprador","buyerId":"ana@ejemplo.co","eventId":"evt-1",
         "status":"Running","total":{"amount":240000,"currency":"COP"},
         "held":[{"tier":"GEN","seat":null,"quantity":2}],"pendingCompensations":0,"lastError":null}
        """;

    private static readonly string CompraCompleta = CompraCorriendo.Replace("\"Running\"", "\"Completed\"");

    private static OrquestadorFalso Feliz() => new OrquestadorFalso()
        .Ok("POST /v1/ticket-purchases", CompraCorriendo)
        .Ok("GET /v1/ticket-purchases/tp-1", CompraCorriendo)
        .Ok("POST /v1/ticket-purchases/tp-1/confirm", CompraCompleta);

    /// <summary>El instante de las compras de estos tests: un mes antes del evento.</summary>
    internal static readonly DateTimeOffset Hoy = new(2026, 7, 1, 12, 0, 0, TimeSpan.FromHours(-5));

    /// <summary>
    /// El evento que se compra, tal como lo publica el catálogo: su inicio y la ventana de su
    /// localidad son el calendario de venta que el CMS aplica antes de salir a la red (#195).
    /// </summary>
    internal static EventDetail EventoUno(
        DateTimeOffset? empieza = null, DateTimeOffset? abre = null, DateTimeOffset? cierra = null)
        => new(
            new EventSummary("evt-1", "evt-1", "Noche de prueba", "Música", "Bogotá", "Teatro",
                empieza ?? Hoy.AddDays(30), string.Empty, 120_000m, "COP", "general"),
            string.Empty,
            string.Empty,
            new[]
            {
                new EventTier("GEN", "General", 120_000m, "COP", 100, 100, 10,
                    SaleWindow: "Hasta la víspera", SaleOpensUtc: abre, SaleClosesUtc: cierra),
            },
            null);

    internal static IEventCatalogProvider Cartelera(EventDetail? evento = null)
    {
        var catalogo = Substitute.For<IEventCatalogProvider>();
        catalogo.GetEventAsync("evt-1", Arg.Any<CancellationToken>()).Returns(evento ?? EventoUno());
        return catalogo;
    }

    private static (HttpEventTicketingService Svc, EventTicketLedger Registro) Nuevo(
        OrquestadorFalso orq, EventDetail? evento = null)
    {
        var registro = new EventTicketLedger(signer: Firmante);
        return (new HttpEventTicketingService(
            new FabricaFalsa(orq),
            new Monitor<EventosSettings>(new EventosSettings { Mode = "Bff" }),
            registro,
            Cartelera(evento),
            NullLogger<HttpEventTicketingService>.Instance,
            now: () => Hoy), registro);
    }

    private static readonly IReadOnlyList<EventCheckoutItem> DosGenerales =
        new[] { new EventCheckoutItem("GEN", null, 2) };

    private static readonly IReadOnlyList<EventAttendeeInfo> Dos = new[]
    {
        new EventAttendeeInfo("Ana Compradora", "ana@ejemplo.co", "1001"),
        new EventAttendeeInfo("Beto Acompañante", "beto@ejemplo.co", "1002"),
    };

    // ── Comprar ─────────────────────────────────────────────────────────────

    /// <summary>
    /// La comisión que el carrito muestra viaja al orquestador, que es quien la autoriza (#194).
    /// </summary>
    /// <remarks>
    /// Antes no viajaba nada: la sumaba sólo el navegador. Se afirma sobre el CUERPO que sale, que
    /// es lo que el orquestador lee, y con un valor que no es el de por defecto.
    /// </remarks>
    [Fact]
    public async Task Comprar_manda_al_orquestador_la_comision_del_sitio()
    {
        var orq = Feliz();
        var negocio = Substitute.For<INegocioDelSitio<NegocioDeEventos>>();
        negocio.Actual().Returns(new NegocioDeEventos("/api/eventos", 8.5m, 10m));
        var svc = new HttpEventTicketingService(
            new FabricaFalsa(orq),
            new Monitor<EventosSettings>(new EventosSettings { Mode = "Bff" }),
            new EventTicketLedger(signer: Firmante),
            Cartelera(),
            NullLogger<HttpEventTicketingService>.Instance,
            now: () => Hoy,
            negocio: negocio);

        await svc.CheckoutAsync("evt-1", DosGenerales, Dos);

        using var cuerpo = System.Text.Json.JsonDocument.Parse(orq.Cuerpos[0]);
        Assert.Equal(8.5m, cuerpo.RootElement.GetProperty("serviceFeePercent").GetDecimal());
    }

    [Fact]
    public async Task Sin_configuracion_de_negocio_no_pide_comision()
    {
        var orq = Feliz();
        var (svc, _) = Nuevo(orq);

        await svc.CheckoutAsync("evt-1", DosGenerales, Dos);

        using var cuerpo = System.Text.Json.JsonDocument.Parse(orq.Cuerpos[0]);
        Assert.Equal(0m, cuerpo.RootElement.GetProperty("serviceFeePercent").GetDecimal());
    }

    /// <summary>
    /// La mitad que el orquestador NO lleva se anota de este lado, y sin ella no habría entradas.
    /// </summary>
    [Fact]
    public async Task Comprar_anota_los_asistentes_del_lado_del_CMS()
    {
        var (svc, registro) = Nuevo(Feliz());

        var compra = await svc.CheckoutAsync("evt-1", DosGenerales, Dos);

        Assert.Equal(240000m, compra.Amount);

        // La orden que vuelve al navegador NO es la saga; la saga queda anotada de este lado
        // (OrdenDelInvitadoEnEventosTests).
        var orden = await registro.LoadAsync(compra.OrderRef);
        Assert.NotNull(orden);
        Assert.Equal("tp-1", orden!.PaymentSessionId);
        Assert.Equal(EventOrderStatus.Pending, orden.Status);
        Assert.Equal(2, orden.Units.Count);
        Assert.Equal("Ana Compradora", orden.Units[0].AttendeeName);
        Assert.Equal("beto@ejemplo.co", orden.Units[1].AttendeeEmail);

        // Y los identificadores de lo apartado son distintos entre sí: dos entradas de la misma
        // localidad no pueden ser la misma entrada.
        Assert.NotEqual(orden.Units[0].ReservationId, orden.Units[1].ReservationId);
    }

    /// <summary>
    /// El id de lo apartado sale SIN guiones, o el QR verificaría y devolvería otra entrada.
    /// </summary>
    /// <remarks>
    /// El identificador de la saga sí los lleva, así que hay que quitarlos. Lo descubrió el
    /// primer test de confirmación, no una revisión: el token se firmaba bien y el
    /// <c>ticketId</c> volvía troceado.
    /// </remarks>
    [Fact]
    public void El_id_de_lo_apartado_no_lleva_guiones()
    {
        var seatRef = HttpEventTicketingService.SeatRef("evt-9f3a2b", 7);

        Assert.DoesNotContain('-', seatRef);
        Assert.Equal(seatRef, HttpEventTicketingService.SeatRef("evt-9f3a2b", 7));
        Assert.NotEqual(seatRef, HttpEventTicketingService.SeatRef("evt-9f3a2b", 8));

        // Y el emisor no lo deja pasar si alguien lo derivara de otra forma.
        Assert.Throws<ArgumentException>(() => EventTicketIssuer.TicketIdOf("evt-9f3a2b-07"));
    }

    [Fact] // el orquestador cotiza; el precio NO viaja del CMS hacia allá.
    public async Task Comprar_NO_manda_precio()
    {
        var orq = Feliz();
        var (svc, _) = Nuevo(orq);
        await svc.CheckoutAsync("evt-1", DosGenerales, Dos);

        Assert.Equal(1, orq.Veces("POST", "/v1/ticket-purchases"));
        Assert.All(orq.Llamadas, l => Assert.NotNull(l.Key ?? "x"));
    }

    [Fact] // la llave es determinista: dos veces lo mismo es un reintento, no una segunda compra.
    public void La_llave_sale_de_lo_que_se_compra_y_no_del_reloj()
    {
        var a = HttpEventTicketingService.IdempotencyKeyFor("evt-1", "ana@ejemplo.co", DosGenerales);
        var b = HttpEventTicketingService.IdempotencyKeyFor("evt-1", "ana@ejemplo.co", DosGenerales);
        var otroEvento = HttpEventTicketingService.IdempotencyKeyFor("evt-2", "ana@ejemplo.co", DosGenerales);

        Assert.Equal(a, b);
        Assert.NotEqual(a, otroEvento);
        Assert.StartsWith("evt-", a, StringComparison.Ordinal);
    }

    /// <summary>
    /// Quien compra viaja SEUDONIMIZADO: el orquestador no tiene por qué guardar un correo.
    /// </summary>
    /// <remarks>
    /// Lo corrigió una verificación con procesos reales. Con el correo en crudo, la saga lo
    /// persistía en el disco del orquestador y el <c>buyerId</c> de la lista de compras pasaba a
    /// ser un dato personal en un servicio que no tiene ninguna razón para tenerlo.
    /// </remarks>
    [Fact]
    public async Task El_comprador_viaja_seudonimizado()
    {
        var id = HttpEventTicketingService.BuyerId(Dos[0]);

        Assert.DoesNotContain("@", id, StringComparison.Ordinal);
        Assert.DoesNotContain("ana", id, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(id, HttpEventTicketingService.BuyerId(new EventAttendeeInfo("Otra", "ANA@ejemplo.co")));
        Assert.NotEqual(id, HttpEventTicketingService.BuyerId(Dos[1]));

        // Y lo que de verdad importa: no sale por el cable. Se mira el CUERPO, que es por donde
        // viajaba, y no la URL, que nunca lo llevó.
        var orq = Feliz();
        var (svc, _) = Nuevo(orq);
        await svc.CheckoutAsync("evt-1", DosGenerales, Dos);

        Assert.NotEmpty(orq.Cuerpos);
        Assert.All(orq.Cuerpos, c =>
        {
            Assert.DoesNotContain("ana@ejemplo.co", c, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("beto@ejemplo.co", c, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Ana Compradora", c, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("1001", c, StringComparison.Ordinal);   // el documento tampoco
        });
        Assert.Contains(orq.Cuerpos, c => c.Contains(id, StringComparison.Ordinal));
    }

    /// <summary>Un timeout dice «no sé», así que se PREGUNTA antes de crear una segunda compra.</summary>
    [Fact]
    public async Task Un_timeout_pregunta_si_la_compra_existio()
    {
        var orq = Feliz().Caida("POST /v1/ticket-purchases");
        // La consulta responde por el identificador de la saga, que es la llave — derivada del
        // comprador SEUDONIMIZADO, que es lo que el servicio manda de verdad.
        var key = HttpEventTicketingService.IdempotencyKeyFor(
            "evt-1", HttpEventTicketingService.BuyerId(Dos[0]), DosGenerales);
        orq.Ok($"GET /v1/ticket-purchases/{key}", CompraCorriendo);

        var (svc, registro) = Nuevo(orq);
        var compra = await svc.CheckoutAsync("evt-1", DosGenerales, Dos);

        Assert.Equal("tp-1", (await registro.LoadAsync(compra.OrderRef))?.PaymentSessionId);
    }

    /// <summary>Y si de verdad no existía, se falla. Nunca «compra exitosa».</summary>
    [Fact]
    public async Task Un_timeout_sin_compra_detras_NO_dice_que_salio_bien()
    {
        var (svc, registro) = Nuevo(Feliz().Caida("POST /v1/ticket-purchases"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.CheckoutAsync("evt-1", DosGenerales, Dos));

        Assert.Empty(await registro.LoadAllAsync());
    }

    [Fact] // un 401 es un defecto de despliegue, no un mensaje para quien compra.
    public async Task Un_401_no_le_cuenta_al_comprador_lo_que_no_es_suyo()
    {
        var orq = new OrquestadorFalso()
            .Falla("POST /v1/ticket-purchases", HttpStatusCode.Unauthorized, "unauthorized", "llave inválida");
        var (svc, _) = Nuevo(orq);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.CheckoutAsync("evt-1", DosGenerales, Dos));
        Assert.DoesNotContain("401", ex.Message, StringComparison.Ordinal);
        Assert.Contains("No se te cobró", ex.Message, StringComparison.Ordinal);
    }

    [Fact] // un rechazo del negocio SÍ llega con su motivo: «se agotó» es accionable.
    public async Task Un_rechazo_del_negocio_llega_con_su_motivo()
    {
        var orq = new OrquestadorFalso().Falla("POST /v1/ticket-purchases",
            HttpStatusCode.Conflict, "inventory.insufficient_stock", "Ya no queda aforo en esa localidad.");
        var (svc, _) = Nuevo(orq);

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => svc.CheckoutAsync("evt-1", DosGenerales, Dos));
        Assert.Contains("Ya no queda aforo", ex.Message, StringComparison.Ordinal);
    }

    [Fact] // filter: tantos asistentes como entradas, o no hay a quién nombrar.
    public async Task Sin_un_asistente_por_entrada_no_se_compra()
    {
        var (svc, _) = Nuevo(Feliz());
        await Assert.ThrowsAsync<ArgumentException>(
            () => svc.CheckoutAsync("evt-1", DosGenerales, new[] { Dos[0] }));
    }

    // ── El calendario de venta (#195) ───────────────────────────────────────
    //
    // Ninguna capacidad del orquestador lo conoce: Api.Pricing fija precios sin vigencia y
    // Api.Inventory cuenta existencias sin ventana, y al BFF le llega solo el id del evento. Así
    // que lo aplica este lado, con el catálogo, y ANTES de salir a la red: el orquestador falso
    // contesta que sí a todo, de modo que si la regla no estuviera, la compra saldría bien.

    [Fact]
    public async Task Un_evento_que_ya_empezo_no_llega_al_orquestador()
    {
        var orq = Feliz();
        var (svc, registro) = Nuevo(orq, EventoUno(empieza: Hoy.AddHours(-1)));

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => svc.CheckoutAsync("evt-1", DosGenerales, Dos));

        Assert.Contains("ya comenzó", ex.Message, StringComparison.Ordinal);
        Assert.Empty(orq.Llamadas);
        Assert.Empty(await registro.TicketsOfAsync("ana@ejemplo.co"));
    }

    [Fact]
    public async Task Una_localidad_con_la_venta_cerrada_no_llega_al_orquestador()
    {
        var orq = Feliz();
        var (svc, _) = Nuevo(orq, EventoUno(cierra: Hoy));

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => svc.CheckoutAsync("evt-1", DosGenerales, Dos));

        Assert.Equal("La venta de la localidad 'General' ya cerró (Hasta la víspera).", ex.Message);
        Assert.Empty(orq.Llamadas);
    }

    [Fact]
    public async Task Una_localidad_que_todavia_no_abre_no_llega_al_orquestador()
    {
        var orq = Feliz();
        var (svc, _) = Nuevo(orq, EventoUno(abre: Hoy.AddTicks(1)));

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => svc.CheckoutAsync("evt-1", DosGenerales, Dos));

        Assert.Contains("todavía no abre", ex.Message, StringComparison.Ordinal);
        Assert.Empty(orq.Llamadas);
    }

    [Fact] // happy: dentro de la ventana —abre justo ahora y cierra en un tick— sí se compra.
    public async Task Dentro_de_la_ventana_la_compra_llega_al_orquestador()
    {
        var orq = Feliz();
        var (svc, registro) = Nuevo(orq, EventoUno(abre: Hoy, cierra: Hoy.AddTicks(1)));

        var compra = await svc.CheckoutAsync("evt-1", DosGenerales, Dos);

        Assert.Equal("tp-1", (await registro.LoadAsync(compra.OrderRef))?.PaymentSessionId);
        Assert.Equal(1, orq.Veces("POST", "/v1/ticket-purchases"));
    }

    [Fact] // sin calendario contra el que vender, no se vende: igual que el motor en proceso.
    public async Task Un_evento_que_el_catalogo_no_conoce_no_llega_al_orquestador()
    {
        var orq = Feliz();
        var (svc, _) = Nuevo(orq);

        await Assert.ThrowsAsync<ArgumentException>(
            () => svc.CheckoutAsync("evt-que-no-existe", DosGenerales, Dos));
        Assert.Empty(orq.Llamadas);
    }

    // ── Confirmar ───────────────────────────────────────────────────────────

    [Fact] // happy: confirmar emite las entradas, con QR que el firmante reconoce.
    public async Task Confirmar_emite_entradas_con_QR_verificable()
    {
        var (svc, _) = Nuevo(Feliz());
        var compra = await svc.CheckoutAsync("evt-1", DosGenerales, Dos);

        var confirmacion = await svc.ConfirmAsync(compra.OrderRef);

        Assert.Equal("Confirmed", confirmacion.Status);
        Assert.Equal(2, confirmacion.Tickets.Count);
        foreach (var entrada in confirmacion.Tickets)
        {
            var token = Firmante.Verify(entrada.Qr);
            Assert.NotNull(token);
            Assert.Equal(entrada.Id, token!.TicketId);
            Assert.Equal("evt-1", token.EventId);
        }
    }

    /// <summary>
    /// Una saga que responde 200 pero no quedó <c>Completed</c> NO es una compra confirmada.
    /// </summary>
    /// <remarks>
    /// Es el caso feo: el orquestador contesta bien porque la petición fue válida, y por dentro
    /// está deshaciendo lo que hizo. Emitir ahí sería entregar entradas de butacas que se están
    /// soltando en ese mismo instante.
    /// </remarks>
    [Fact]
    public async Task Una_saga_que_no_quedo_Completed_NO_emite_entradas()
    {
        var orq = Feliz();
        var (svc, registro) = Nuevo(orq);
        var compra = await svc.CheckoutAsync("evt-1", DosGenerales, Dos);

        orq.Ok("POST /v1/ticket-purchases/tp-1/confirm",
            CompraCorriendo.Replace("\"Running\"", "\"Compensated\"")
                           .Replace("\"lastError\":null", "\"lastError\":\"el cobro fue rechazado\""));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ConfirmAsync(compra.OrderRef));
        Assert.Contains("rechazado", ex.Message, StringComparison.Ordinal);

        var orden = await registro.LoadAsync(compra.OrderRef);
        Assert.Equal(EventOrderStatus.Pending, orden!.Status);
        Assert.Empty(await registro.TicketsOfAsync("ana@ejemplo.co"));
    }

    /// <summary>
    /// La ruta vieja NO pide el aviso del orquestador: lo sigue mandando el CMS, por
    /// <c>EventPurchaseNotification</c>. Si además mandara el contacto, el comprador recibiría dos.
    /// </summary>
    /// <remarks>
    /// Desde la ADR 0140 F3 el orquestador avisa al cerrar cuando la puerta le pone
    /// <c>X-Synergos-Contacto</c>. Conviven hasta el retiro, así que la frontera es ésa: quien compra
    /// por la ruta vieja recibe el aviso del CMS y sólo ése.
    /// </remarks>
    [Fact]
    public async Task La_ruta_vieja_no_manda_el_contacto_del_aviso_al_orquestador()
    {
        var orq = Feliz();
        var (svc, _) = Nuevo(orq);

        var compra = await svc.CheckoutAsync("evt-1", DosGenerales, Dos);
        await svc.ConfirmAsync(compra.OrderRef);

        Assert.Equal(1, orq.Veces("POST", "/confirm"));
        Assert.DoesNotContain("X-Synergos-Contacto", orq.Cabeceras, StringComparer.OrdinalIgnoreCase);
    }

    [Fact] // idempotent: re-confirmar devuelve lo mismo y NO vuelve a salir a la red.
    public async Task Re_confirmar_no_vuelve_a_llamar_al_orquestador()
    {
        var orq = Feliz();
        var (svc, _) = Nuevo(orq);
        var compra = await svc.CheckoutAsync("evt-1", DosGenerales, Dos);

        var primera = await svc.ConfirmAsync(compra.OrderRef);
        var segunda = await svc.ConfirmAsync(compra.OrderRef);

        Assert.Equal(1, orq.Veces("POST", "/confirm"));
        Assert.Equal(
            primera.Tickets.Select(t => t.Qr),
            segunda.Tickets.Select(t => t.Qr));
    }

    [Fact] // empty: confirmar algo que este CMS no anotó no inventa una compra.
    public async Task Confirmar_lo_que_no_existe_de_este_lado_falla()
    {
        var (svc, _) = Nuevo(Feliz());
        await Assert.ThrowsAsync<ArgumentException>(() => svc.ConfirmAsync("no-existe"));
    }

    // ── El artefacto no depende del orquestador ─────────────────────────────

    /// <summary>
    /// Con el orquestador CAÍDO, quien ya compró sigue teniendo su entrada y puede entrar.
    /// </summary>
    /// <remarks>
    /// Es la mitad del punto de que el registro viva de este lado. El BFF se apaga después de
    /// confirmar y todo lo que sigue —mis entradas, transferir, la puerta— sale igual.
    /// </remarks>
    [Fact]
    public async Task Con_el_orquestador_caido_la_entrada_sigue_sirviendo()
    {
        var orq = Feliz();
        var (svc, registro) = Nuevo(orq);
        var compra = await svc.CheckoutAsync("evt-1", DosGenerales, Dos);
        await svc.ConfirmAsync(compra.OrderRef);

        // Se cae TODO el orquestador.
        orq.Caida("POST /v1/ticket-purchases");
        orq.Caida("GET /v1/ticket-purchases/tp-1");
        orq.Caida("POST /v1/ticket-purchases/tp-1/confirm");
        var llamadasAntes = orq.Llamadas.Count;

        var mias = await svc.GetTicketsAsync("ana@ejemplo.co");
        Assert.Single(mias);

        var transferida = await svc.TransferTicketAsync(mias[0].Id, "carla@ejemplo.co");
        Assert.NotEqual(mias[0].Qr, transferida.NewQr);

        // La puerta: el QR nuevo entra, el viejo ya no.
        Assert.Equal("valid", (await registro.CheckInAsync(transferida.NewQr)).Status);
        Assert.Equal("invalid", (await registro.CheckInAsync(mias[0].Qr)).Status);

        // Y nada de esto tocó la red.
        Assert.Equal(llamadasAntes, orq.Llamadas.Count);
    }

    /// <summary>
    /// La puerta escanea lo comprado por ESTE camino — el defecto que destapó la rebanada 2a.
    /// </summary>
    [Fact]
    public async Task La_cara_de_organizador_ve_lo_comprado_contra_el_orquestador()
    {
        var (svc, registro) = Nuevo(Feliz());
        var compra = await svc.CheckoutAsync("evt-1", DosGenerales, Dos);
        await svc.ConfirmAsync(compra.OrderRef);

        var asistentes = await registro.ConfirmedAttendeesAsync("evt-1");

        Assert.Equal(2, asistentes.Count);
        Assert.Contains(asistentes, a => a.Email == "beto@ejemplo.co");
        Assert.All(asistentes, a => Assert.False(a.CheckedIn));
    }
}
