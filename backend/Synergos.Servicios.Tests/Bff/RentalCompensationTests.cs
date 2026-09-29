using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Synergos.Bff.Alquiler.Clients;
using Synergos.Bff.Alquiler.Contracts;
using Synergos.Bff.Alquiler.Domain;
using Synergos.Bff.Core;
using Synergos.Core;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// El alquiler que se deshace, y el que se cierra días después (HU #147).
/// </summary>
/// <remarks>
/// <para><b>Lo que este dominio aporta y los cuatro anteriores no: DOS COBROS CON VIDAS
/// DISTINTAS.</b> El alquiler se autoriza y se CAPTURA al reservar; la garantía se autoriza y se
/// queda <b>sin capturar</b> mientras el equipo está fuera. Los cuatro flujos anteriores autorizan
/// para capturar, así que ninguno tenía ese estado intermedio — y es el que decide si deshacer
/// significa anular o devolver.</para>
///
/// <para><b>Y la saga se CIERRA al reservar</b>, que es la propiedad que más barato se pierde: una
/// que siguiera <c>Running</c> hasta la devolución la daría por muerta el barrido de abandono a la
/// hora, soltando la ventana y anulando la garantía de un alquiler VIVO. Por eso hay un test que
/// adelanta el reloj cuatro días y exige que el barrido no la toque.</para>
///
/// <para><b>El doble de <c>Api.Booking</c> es estricto</b>, como el de <c>TripCompensationTests</c>
/// y por la misma razón: soltar un apartado ya confirmado se RECHAZA. Un doble más permisivo que
/// la cosa real dejaría pasar en verde un flujo que en producción no cancelaría nunca una reserva.
/// </para>
///
/// <para><b>Y este fichero es la SÉPTIMA copia del doble de capacidades</b>, con cinco formas
/// distintas ya en el repo (dos idénticas, tres que perdieron o ganaron <c>Caida</c>,
/// <c>Secuencia</c>, <c>Veces</c> o el comodín de <c>Coincide</c>). Medido, no contado. No se
/// promueve acá porque sería mezclar un refactor de seis ficheros con un vertical nuevo
/// (<c>CLAUDE.md</c> §4.3) — queda anotado como hallazgo de la fábrica: S14 no tiene sub-spec de
/// arnés, así que el séptimo copia el que tenga más cerca y hereda o pierde una variación sin
/// saberlo (<c>the_same_algorithm_is_not_the_same_thing</c>).</para>
/// </remarks>
public sealed class RentalCompensationTests
{
    private sealed class RelojFalso : TimeProvider
    {
        private DateTimeOffset _now;
        public RelojFalso(DateTimeOffset inicio) => _now = inicio;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Avanzar(TimeSpan d) => _now += d;
    }

    private sealed class CapacidadesFalsas : HttpMessageHandler
    {
        private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _rutas = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Queue<string>> _secuencias = new(StringComparer.Ordinal);

        public List<(string Method, string Path, string? Query, string? Key, string? Body)> Llamadas { get; } = new();

        public CapacidadesFalsas Cuando(string patron, Func<HttpRequestMessage, HttpResponseMessage> responde)
        {
            _rutas[patron] = responde;
            return this;
        }

        public CapacidadesFalsas Ok(string patron, string json)
            => Cuando(patron, _ => Json(HttpStatusCode.OK, json));

        /// <summary>Respuestas distintas para llamadas sucesivas a la misma ruta.</summary>
        public CapacidadesFalsas Secuencia(string patron, params string[] jsons)
        {
            _secuencias[patron] = new Queue<string>(jsons);
            return Cuando(patron, _ =>
            {
                var cola = _secuencias[patron];
                return Json(HttpStatusCode.OK, cola.Count > 1 ? cola.Dequeue() : cola.Peek());
            });
        }

        public CapacidadesFalsas Falla(string patron, HttpStatusCode codigo, string code)
            => Cuando(patron, _ => new HttpResponseMessage(codigo)
            {
                Content = new StringContent($$"""{"code":"{{code}}","detail":"guionado"}""",
                    Encoding.UTF8, "application/problem+json"),
            });

        public int Veces(string method, string pathContiene)
            => Llamadas.Count(l => l.Method == method && l.Path.Contains(pathContiene, StringComparison.Ordinal));

        /// <summary>
        /// Las veces que se llamó a ESA ruta y no a una que la contenga.
        /// </summary>
        /// <remarks>
        /// <b>Hace falta acá y en los otros seis dobles no</b>, y la razón dice algo del vertical:
        /// en Viajes apartar y confirmar son dos peticiones del CMS, así que un test que cuente
        /// <c>/v1/holds</c> sólo ve apartados. Acá <c>ReserveAsync</c> aparta, cobra Y confirma en
        /// una, así que <c>Contains("/v1/payments")</c> cuenta también los
        /// <c>/v1/payments/{id}/capture</c> — medido: daba 3 donde hay 2 autorizaciones. Un
        /// contador que cuenta de más no falla en el caso feliz: falla en el que prueba la regla.
        /// </remarks>
        public int VecesEn(string method, string pathExacto)
            => Llamadas.Count(l => l.Method == method
                && l.Path.EndsWith(pathExacto, StringComparison.Ordinal));

        private static HttpResponseMessage Json(HttpStatusCode c, string body)
            => new(c) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var clave = $"{request.Method.Method} {path}";
            var cuerpo = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);

            lock (Llamadas)
            {
                Llamadas.Add((request.Method.Method, path, request.RequestUri.Query,
                    request.Headers.TryGetValues("Idempotency-Key", out var v) ? v.FirstOrDefault() : null,
                    cuerpo));
            }

            foreach (var (patron, responde) in _rutas)
            {
                if (Coincide(patron, clave)) return responde(request);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("""{"code":"stub.no_route","detail":"sin guion"}""",
                    Encoding.UTF8, "application/problem+json"),
            };
        }

        private static bool Coincide(string patron, string clave)
            => patron.EndsWith('*')
                ? clave.StartsWith(patron[..^1], StringComparison.Ordinal)
                : string.Equals(patron, clave, StringComparison.Ordinal);
    }

    /// <summary>
    /// El doble se comporta como <c>Api.Booking</c>: soltar un apartado ya confirmado se RECHAZA.
    /// </summary>
    private static CapacidadesFalsas ComoBookingDeVerdad(
        CapacidadesFalsas caps, params (string Hold, string Reserva)[] items)
    {
        var confirmados = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (hold, reserva) in items)
        {
            var h = hold;
            var r = reserva;
            caps.Cuando($"POST /v1/holds/{h}/confirm", _ =>
            {
                confirmados.Add(h);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""{"id":"{{r}}","status":"Confirmed"}""",
                        Encoding.UTF8, "application/json"),
                };
            });
            caps.Cuando($"POST /v1/holds/{h}/release", _ => confirmados.Contains(h)
                ? new HttpResponseMessage(HttpStatusCode.Conflict)
                {
                    Content = new StringContent(
                        """{"code":"booking.hold_already_confirmed","detail":"el apartado ya es una reserva"}""",
                        Encoding.UTF8, "application/problem+json"),
                }
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $$"""{"id":"{{h}}","resourceId":"rec-andamio","expiresAt":"2026-09-01T10:10:00+00:00"}""",
                        Encoding.UTF8, "application/json"),
                });
            caps.Cuando($"POST /v1/reservations/{r}/cancel", _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($$"""{"id":"{{r}}","status":"Cancelled"}""",
                    Encoding.UTF8, "application/json"),
            });
        }
        return caps;
    }

    private sealed class FabricaFalsa : IHttpClientFactory
    {
        private readonly CapacidadesFalsas _handler;
        public FabricaFalsa(CapacidadesFalsas h) => _handler = h;
        public HttpClient CreateClient(string name)
            => new(_handler, disposeHandler: false) { BaseAddress = new Uri("http://capacidad.local/") };
    }

    private sealed class MemoriaSagas : ISagaStore<RentalSaga>
    {
        private readonly Dictionary<string, RentalSaga> _s = new(StringComparer.Ordinal);
        public RentalSaga? Find(string id) => _s.GetValueOrDefault(id);
        public IReadOnlyList<RentalSaga> WithPendingCompensations()
            => _s.Values.Where(x => x.IsUnwinding() && x.Pending().Count > 0).ToList();
        public IReadOnlyList<RentalSaga> StartedBefore(DateTimeOffset limite)
            => _s.Values.Where(x => x.Status == SagaStatus.Running && x.StartedAtUtc < limite).ToList();
        public void Put(RentalSaga saga) => _s[saga.Id] = saga;
        public void Invalidate() { }
    }

    private static readonly DateTimeOffset Ahora = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly Ref Arrendatario = Ref.Create("alquiler.arrendatario", "u-1");
    private static readonly TimeWindow Semana = TimeWindow.Of(
        new DateTimeOffset(2026, 9, 5, 8, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 9, 12, 18, 0, 0, TimeSpan.Zero));

    private static Money Cop(decimal m) => Money.Of(m, Money.Cop);

    private sealed record Contexto(RentalFlow Flow, CapacidadesFalsas Caps, MemoriaSagas Sagas, RelojFalso Reloj);

    /// <summary>
    /// El guion feliz. La garantía y el alquiler son DOS cobros con identificadores distintos, y
    /// eso no es decoración del fixture: con un solo <c>pg</c> para los dos, «se capturó el
    /// alquiler y no la garantía» y «se capturó todo» darían el mismo resultado.
    /// </summary>
    private static CapacidadesFalsas Feliz() => new CapacidadesFalsas()
        .Ok("GET /v1/resources",
            """{"id":"rec-andamio","subjectKind":"alquiler.equipo","subjectId":"andamio-6m","capacity":8}""")
        .Secuencia("POST /v1/holds",
            """{"id":"h-1","resourceId":"rec-andamio","expiresAt":"2026-09-01T10:10:00+00:00"}""",
            """{"id":"h-2","resourceId":"rec-andamio","expiresAt":"2026-09-01T10:10:00+00:00"}""")
        // El primer POST /v1/payments es la GARANTÍA y el segundo el ALQUILER: ése es el orden
        // del flujo, y probarlo al revés no distinguiría cuál de los dos se capturó.
        .Secuencia("POST /v1/payments",
            """{"id":"pg-dep","status":"Authorized","amount":{"amount":400000,"currency":"COP"},"refundable":{"amount":0,"currency":"COP"}}""",
            """{"id":"pg-ren","status":"Authorized","amount":{"amount":350000,"currency":"COP"},"refundable":{"amount":0,"currency":"COP"}}""")
        .Ok("POST /v1/payments/pg-ren/capture",
            """{"id":"pg-ren","status":"Captured","amount":{"amount":350000,"currency":"COP"},"refundable":{"amount":350000,"currency":"COP"}}""")
        .Ok("POST /v1/payments/pg-ren/void",
            """{"id":"pg-ren","status":"Voided","amount":{"amount":350000,"currency":"COP"},"refundable":{"amount":0,"currency":"COP"}}""")
        .Ok("POST /v1/payments/pg-ren/refund",
            """{"id":"pg-ren","status":"Captured","amount":{"amount":350000,"currency":"COP"},"refundable":{"amount":0,"currency":"COP"}}""")
        .Ok("GET /v1/payments/pg-ren",
            """{"id":"pg-ren","status":"Captured","amount":{"amount":350000,"currency":"COP"},"refundable":{"amount":350000,"currency":"COP"}}""")
        .Ok("POST /v1/payments/pg-dep/capture",
            """{"id":"pg-dep","status":"Captured","amount":{"amount":400000,"currency":"COP"},"refundable":{"amount":400000,"currency":"COP"}}""")
        .Ok("POST /v1/payments/pg-dep/void",
            """{"id":"pg-dep","status":"Voided","amount":{"amount":400000,"currency":"COP"},"refundable":{"amount":0,"currency":"COP"}}""")
        .Ok("POST /v1/payments/pg-dep/refund",
            """{"id":"pg-dep","status":"Captured","amount":{"amount":400000,"currency":"COP"},"refundable":{"amount":0,"currency":"COP"}}""")
        .Ok("GET /v1/payments/pg-dep",
            """{"id":"pg-dep","status":"Captured","amount":{"amount":400000,"currency":"COP"},"refundable":{"amount":400000,"currency":"COP"}}""")
        .Ok("POST /v1/deliveries", """{"id":"d1","status":"Sent"}""");

    private static CapacidadesFalsas FelizEstricto()
        => ComoBookingDeVerdad(Feliz(), ("h-1", "res-1"), ("h-2", "res-2"));

    private static Contexto Nuevo(CapacidadesFalsas caps)
    {
        var sagas = new MemoriaSagas();
        var reloj = new RelojFalso(Ahora);
        var fabrica = new FabricaFalsa(caps);
        var api = new AlquilerCapabilities(fabrica);
        var vocabulario = new SagaVocabulary("alquiler", "el alquiler de equipos");
        var comp = new Compensator<RentalSaga>(
            new AlquilerCompensationExecutor(api), reloj, NullLogger<Compensator<RentalSaga>>.Instance);
        var aviso = new CompensationAlert(fabrica, vocabulario, Options.Create(new AlertOptions
        {
            ToKind = "alquiler.guardia",
            ToId = "operaciones",
            Address = "guardia@ejemplo.co",
            TemplateKey = "alquiler.compensacion.colgada",
        }));
        var motor = new SagaEngine<RentalSaga>(sagas, comp, aviso, ArriendoDePrueba.Nuevo(), vocabulario, reloj,
            NullLogger<SagaEngine<RentalSaga>>.Instance);

        return new Contexto(new RentalFlow(api, motor, reloj, NullLogger<RentalFlow>.Instance), caps, sagas, reloj);
    }

    private static Task<Result<RentalSaga>> Reservar(
        RentalFlow flow, string id = "alq-1", int unidades = 1, decimal garantia = 400000m)
        => flow.ReserveAsync(
            Arrendatario, "andamio-6m", unidades, Semana, Cop(350000m), Cop(garantia), id, CancellationToken.None);

    // ── Los dos cobros tienen vidas distintas ──────────────────────────────

    /// <summary>
    /// La garantía se AUTORIZA y no se captura; el alquiler sí.
    /// </summary>
    /// <remarks>
    /// Es la propiedad entera del vertical. Si la garantía se capturara al reservar, devolver el
    /// equipo sin daño obligaría a un reembolso —plata que se mueve dos veces y comisión que se
    /// paga dos veces— en vez de una anulación que no mueve nada.
    /// </remarks>
    [Fact]
    public async Task La_garantia_se_RETIENE_sin_capturar_y_el_alquiler_se_cobra()
    {
        var ctx = Nuevo(FelizEstricto());

        var r = await Reservar(ctx.Flow);

        Assert.True(r.IsOk);
        Assert.Equal("pg-dep", r.Value.DepositPaymentId);
        Assert.Equal("pg-ren", r.Value.RentalPaymentId);

        // Dos autorizaciones, UNA captura, y sobre el alquiler.
        Assert.Equal(2, ctx.Caps.VecesEn("POST", "/v1/payments"));
        Assert.Equal(1, ctx.Caps.Veces("POST", "/capture"));
        Assert.Equal(1, ctx.Caps.Veces("POST", "/v1/payments/pg-ren/capture"));
        Assert.Equal(0, ctx.Caps.Veces("POST", "/v1/payments/pg-dep/capture"));
    }

    /// <summary>La garantía se retiene ANTES de cobrar el alquiler.</summary>
    /// <remarks>
    /// Al revés se cobraría por un equipo que no va a salir, y deshacerlo costaría una devolución
    /// en vez de una anulación. El orden se lee en qué sujeto nombra cada autorización.
    /// </remarks>
    [Fact]
    public async Task La_garantia_va_antes_que_el_cobro()
    {
        var ctx = Nuevo(FelizEstricto());

        Assert.True((await Reservar(ctx.Flow)).IsOk);

        var autorizaciones = ctx.Caps.Llamadas
            .Where(l => l.Method == "POST" && l.Path.EndsWith("/v1/payments", StringComparison.Ordinal))
            .Select(l => l.Body!)
            .ToList();

        Assert.Equal(2, autorizaciones.Count);
        Assert.Contains("alquiler.garantia", autorizaciones[0], StringComparison.Ordinal);
        Assert.Contains("alquiler.alquiler", autorizaciones[1], StringComparison.Ordinal);
    }

    /// <summary>Sin garantía que retener, no se autoriza una de cero.</summary>
    /// <remarks>
    /// Un alquiler sin depósito es legítimo —una herramienta de mano— y una autorización de cero
    /// la rechazaría la pasarela. El fixture lo exige con <c>garantia: 0</c>, que es el caso que
    /// la rama SÍ produce.
    /// </remarks>
    [Fact]
    public async Task Sin_garantia_no_se_autoriza_una_de_cero()
    {
        var ctx = Nuevo(FelizEstricto());

        var r = await Reservar(ctx.Flow, garantia: 0m);

        Assert.True(r.IsOk);
        Assert.Null(r.Value.DepositPaymentId);
        Assert.Equal(1, ctx.Caps.VecesEn("POST", "/v1/payments"));
    }

    // ── Lo que se deshace, y CÓMO ──────────────────────────────────────────

    /// <summary>
    /// Si no se puede retener la garantía, la ventana vuelve y NO se cobra nada.
    /// </summary>
    [Fact]
    public async Task Sin_garantia_retenible_se_suelta_la_ventana_y_no_se_cobra()
    {
        var caps = FelizEstricto();
        caps.Falla("POST /v1/payments", HttpStatusCode.PaymentRequired, "payments.declined");
        var ctx = Nuevo(caps);

        var r = await Reservar(ctx.Flow);

        Assert.False(r.IsOk);
        Assert.Equal(1, ctx.Caps.Veces("POST", "/v1/holds/h-1/release"));
        Assert.Equal(0, ctx.Caps.Veces("POST", "/capture"));
        Assert.Equal(SagaStatus.Compensated, ctx.Sagas.Find("alq-1")!.Status);
    }

    /// <summary>
    /// Un intento DESHECHO no se proyecta como reservado, y no dice que retiene nada.
    /// </summary>
    /// <remarks>
    /// <para><b>Este test existe porque los otros dieciséis no lo veían, y no por falta de
    /// cobertura: por REPARTO.</b> Todos miran el ALMACÉN —¿se soltó el apartado?, ¿se anuló la
    /// garantía?, ¿en qué <c>SagaStatus</c> quedó?— y ninguno miraba la PROYECCIÓN que es lo
    /// único que el CMS lee. Las dos mitades en verde y el hueco justo en medio (addendum #116
    /// de <c>no_read_without_a_write_path</c>).</para>
    ///
    /// <para><b>Lo destapó un proceso vivo</b> (#147): con <c>Api.Payments</c> caída, reservar
    /// rechazó con 503, la ventana volvió —<c>taken</c> de vuelta a 0 sobre el recurso— y
    /// <c>GET /v1/rentals/{id}</c> seguía contestando <c>"state":"reserved"</c> con
    /// <c>"depositHeld":400000</c>. Nada fallaba: el alquiler no existía y la respuesta decía
    /// que había un andamio fuera y cuatrocientos mil retenidos.</para>
    ///
    /// <para><b>Y se llega desde el producto</b>: el cliente del CMS deriva la llave de
    /// idempotencia del propio alquiler, así que el id del intento muerto es exactamente el que
    /// vuelve a consultar quien reintenta.</para>
    ///
    /// <para><b>El fixture tiene que afirmar las DOS cosas.</b> Con sólo el estado, rellenar
    /// <c>depositHeld</c> con el monto seguiría pasando en verde — y «cuatrocientos mil
    /// retenidos» dicho de una garantía anulada es la mitad cara del defecto, porque es la que
    /// alguien reclama.</para>
    /// </remarks>
    [Fact]
    public async Task Un_intento_deshecho_NO_se_proyecta_como_reservado()
    {
        var caps = FelizEstricto();
        caps.Falla("POST /v1/payments", HttpStatusCode.PaymentRequired, "payments.declined");
        var ctx = Nuevo(caps);

        Assert.False((await Reservar(ctx.Flow)).IsOk);

        var muerta = ctx.Sagas.Find("alq-1")!;
        Assert.Equal(SagaStatus.Compensated, muerta.Status);

        var proyectada = RentalResponse.From(muerta);
        Assert.Equal("failed", proyectada.State);

        // Cero y no nulo: compensada ES que la anulación salió, así que de verdad no queda nada
        // retenido. El nulo está reservado para cuando nadie lo sabe — ver el test de abajo.
        Assert.Equal(0m, proyectada.DepositHeld);
    }

    /// <summary>
    /// Con la compensación COLGADA nadie sabe si la garantía sigue retenida, y eso no se rellena.
    /// </summary>
    /// <remarks>
    /// <para>Es el caso que separa «no queda nada» de «no se sabe». Cero ahí es una afirmación
    /// —«no hay nada retenido»— hecha por el borde sin haberla comprobado, que es
    /// <c>an_omitted_key_can_be_an_assertion</c> con el valor puesto a mano. Y el monto entero
    /// sería la afirmación contraria, igual de inventada.</para>
    ///
    /// <para>Se prueba proyectando la saga en ese estado y no llegando a él por el flujo: para
    /// que el compensador se rinda hace falta agotar sus ocho reintentos con retroceso
    /// exponencial, que es tiempo de pared dentro de un test
    /// (<c>a_clock_test_measures_a_property_it_cannot_own</c>).</para>
    /// </remarks>
    [Fact]
    public async Task Con_la_compensacion_colgada_lo_retenido_es_NULO_y_no_cero()
    {
        var caps = FelizEstricto();
        var ctx = Nuevo(caps);
        Assert.True((await Reservar(ctx.Flow)).IsOk);

        var colgada = ctx.Sagas.Find("alq-1")! with { Status = SagaStatus.CompensationFailed };

        var proyectada = RentalResponse.From(colgada);
        Assert.Equal("failed", proyectada.State);
        Assert.Null(proyectada.DepositHeld);
    }

    /// <summary>
    /// Si el alquiler no se puede cobrar, la garantía se ANULA — no se devuelve.
    /// </summary>
    /// <remarks>
    /// Devolverla sería el error caro y callado: la garantía nunca se capturó, así que un
    /// <c>refund</c> lo rechazaría la pasarela y la retención se quedaría viva hasta vencer sola,
    /// con el dinero del arrendatario inmovilizado y el alquiler ya cancelado.
    /// </remarks>
    [Fact]
    public async Task Si_no_se_cobra_el_alquiler_la_garantia_se_ANULA()
    {
        var caps = FelizEstricto();
        caps.Falla("POST /v1/payments/pg-ren/capture", HttpStatusCode.PaymentRequired, "payments.declined");
        var ctx = Nuevo(caps);

        var r = await Reservar(ctx.Flow);

        Assert.False(r.IsOk);
        Assert.Equal(1, ctx.Caps.Veces("POST", "/v1/payments/pg-dep/void"));
        Assert.Equal(0, ctx.Caps.Veces("POST", "/v1/payments/pg-dep/refund"));
        Assert.Equal(1, ctx.Caps.Veces("POST", "/v1/holds/h-1/release"));
    }

    /// <summary>
    /// Confirmado un apartado, deshacerlo deja de ser «soltar» y pasa a ser «cancelar».
    /// </summary>
    /// <remarks>
    /// <para>Y el cobro ya capturado deja de anularse y pasa a devolverse. Las DOS reescrituras
    /// viven en el mismo escenario porque el fallo llega DESPUÉS de capturar y DESPUÉS de
    /// confirmar el primero de dos apartados: es el único punto donde las dos formas coexisten
    /// (<c>compensation_changes_character</c>).</para>
    ///
    /// <para>Con el doble estricto esto no puede pasar por accidente: si el flujo no reescribiera
    /// la compensación, soltar <c>h-1</c> daría 409 y la compensación quedaría colgada.</para>
    /// </remarks>
    [Fact]
    public async Task Al_confirmar_soltar_pasa_a_cancelar_y_anular_pasa_a_devolver()
    {
        var caps = FelizEstricto();
        caps.Falla("POST /v1/holds/h-2/confirm", HttpStatusCode.Conflict, "booking.hold_expired");
        var ctx = Nuevo(caps);

        var r = await Reservar(ctx.Flow, unidades: 2);

        Assert.False(r.IsOk);

        // El primero ya era reserva: se CANCELA, no se suelta.
        Assert.Equal(1, ctx.Caps.Veces("POST", "/v1/reservations/res-1/cancel"));

        // El segundo nunca llegó a confirmarse: ése sí se suelta.
        Assert.Equal(1, ctx.Caps.Veces("POST", "/v1/holds/h-2/release"));

        // Y el alquiler ya estaba capturado: se DEVUELVE, no se anula.
        Assert.Equal(1, ctx.Caps.Veces("POST", "/v1/payments/pg-ren/refund"));
        Assert.Equal(0, ctx.Caps.Veces("POST", "/v1/payments/pg-ren/void"));

        // La garantía, en cambio, nunca se capturó: ésa sí se anula.
        Assert.Equal(1, ctx.Caps.Veces("POST", "/v1/payments/pg-dep/void"));

        Assert.Equal(SagaStatus.Compensated, ctx.Sagas.Find("alq-1")!.Status);
    }

    // ── La saga se cierra al reservar ──────────────────────────────────────

    /// <summary>
    /// Reservado, la saga queda <c>Completed</c> y sin nada pendiente.
    /// </summary>
    /// <remarks>
    /// <b>Y el barrido de abandono no la toca cuatro días después</b>, que es lo que de verdad hay
    /// que vigilar: una saga que siguiera <c>Running</c> hasta la devolución sería deshecha por el
    /// barrido a los <c>AbandonAfterMinutes</c> — soltando la ventana y anulando la garantía de un
    /// alquiler vivo, sin que nada fallara y con el equipo ya en la obra.
    /// </remarks>
    [Fact]
    public async Task La_saga_se_cierra_al_reservar_y_el_barrido_no_la_toca()
    {
        var ctx = Nuevo(FelizEstricto());

        var r = await Reservar(ctx.Flow);

        Assert.True(r.IsOk);
        Assert.Equal(SagaStatus.Completed, r.Value.Status);
        Assert.Empty(r.Value.Pending());

        // Cuatro días después —un alquiler perfectamente normal— sigue sin ser candidata.
        ctx.Reloj.Avanzar(TimeSpan.FromDays(4));
        Assert.Empty(ctx.Sagas.StartedBefore(ctx.Reloj.GetUtcNow()));
        Assert.Empty(ctx.Sagas.WithPendingCompensations());
    }

    // ── Devolver y cancelar ────────────────────────────────────────────────

    /// <summary>Devolver sin daño ANULA la garantía: nunca fue un cobro.</summary>
    [Fact]
    public async Task Devolver_sin_dano_anula_la_garantia()
    {
        var ctx = Nuevo(FelizEstricto());
        Assert.True((await Reservar(ctx.Flow)).IsOk);

        var r = await ctx.Flow.ReturnAsync("alq-1", Cop(0m), CancellationToken.None);

        Assert.True(r.IsOk);
        Assert.Equal("returned", RentalResponse.From(r.Value).State);
        Assert.Equal(1, ctx.Caps.Veces("POST", "/v1/payments/pg-dep/void"));
        Assert.Equal(0, ctx.Caps.Veces("POST", "/v1/payments/pg-dep/capture"));

        // Devuelto el equipo, ya no hay nada retenido.
        Assert.Equal(0m, RentalResponse.From(r.Value).DepositHeld);
    }

    /// <summary>
    /// Con daño se captura la garantía y se devuelve la diferencia.
    /// </summary>
    /// <remarks>
    /// El fixture cobra <b>120 000 de una garantía de 400 000</b> a propósito: con el daño igual a
    /// la garantía, «capturar y devolver el sobrante» y «capturar y ya» darían el mismo resultado
    /// y la rama del sobrante pasaría en verde sin ejecutarse.
    /// </remarks>
    [Fact]
    public async Task Con_dano_se_captura_la_garantia_y_se_devuelve_el_sobrante()
    {
        var ctx = Nuevo(FelizEstricto());
        Assert.True((await Reservar(ctx.Flow)).IsOk);

        var r = await ctx.Flow.ReturnAsync("alq-1", Cop(120000m), CancellationToken.None);

        Assert.True(r.IsOk);
        Assert.Equal(120000m, r.Value.DamageCharged.Amount);
        Assert.Equal(1, ctx.Caps.Veces("POST", "/v1/payments/pg-dep/capture"));

        var devolucion = ctx.Caps.Llamadas.Single(
            l => l.Path.EndsWith("/v1/payments/pg-dep/refund", StringComparison.Ordinal));
        Assert.Contains("280000", devolucion.Body!, StringComparison.Ordinal);
    }

    /// <summary>Cobrar más daño que garantía se rechaza.</summary>
    /// <remarks>
    /// No hay con qué: la retención es por un monto y la pasarela no da más. Aceptarlo dejaría un
    /// alquiler cerrado diciendo que cobró algo que nadie cobró.
    /// </remarks>
    [Fact]
    public async Task El_dano_no_puede_superar_la_garantia()
    {
        var ctx = Nuevo(FelizEstricto());
        Assert.True((await Reservar(ctx.Flow)).IsOk);

        var r = await ctx.Flow.ReturnAsync("alq-1", Cop(500000m), CancellationToken.None);

        Assert.False(r.IsOk);
        Assert.Equal("alquiler.damage_exceeds_deposit", r.Rejection!.Code);
        Assert.Equal(0, ctx.Caps.Veces("POST", "/v1/payments/pg-dep/capture"));
    }

    /// <summary>Cerrar dos veces no cobra dos veces.</summary>
    /// <remarks>
    /// Sin esto, un reintento por timeout capturaría la garantía otra vez — y ahí hay plata de
    /// verdad, no un cupo.
    /// </remarks>
    [Fact]
    public async Task Cerrar_dos_veces_no_vuelve_a_capturar()
    {
        var ctx = Nuevo(FelizEstricto());
        Assert.True((await Reservar(ctx.Flow)).IsOk);

        Assert.True((await ctx.Flow.ReturnAsync("alq-1", Cop(120000m), CancellationToken.None)).IsOk);
        var otra = await ctx.Flow.ReturnAsync("alq-1", Cop(120000m), CancellationToken.None);

        Assert.True(otra.IsOk);
        Assert.Equal(1, ctx.Caps.Veces("POST", "/v1/payments/pg-dep/capture"));
        Assert.Equal(1, ctx.Caps.Veces("POST", "/v1/payments/pg-dep/refund"));
    }

    /// <summary>
    /// Cancelar CANCELA las reservas; devolver no.
    /// </summary>
    /// <remarks>
    /// <b>Y el estado sale distinto</b>, que es lo que costó ver: los dos caminos dejan
    /// <c>ReservationId</c> puestos, así que derivar el estado de «¿tiene reservas?» habría
    /// contestado «devuelto» de todo alquiler cancelado — una fabricación que sale de datos reales
    /// y por eso no se ve rara. Por eso el test exige los dos valores y no sólo uno.
    /// </remarks>
    [Fact]
    public async Task Cancelar_libera_la_ventana_y_devolver_no()
    {
        var ctx = Nuevo(FelizEstricto());
        Assert.True((await Reservar(ctx.Flow)).IsOk);

        var cancelada = await ctx.Flow.CancelAsync("alq-1", Cop(50000m), CancellationToken.None);

        Assert.True(cancelada.IsOk);
        Assert.Equal("cancelled", RentalResponse.From(cancelada.Value).State);
        Assert.Equal(1, ctx.Caps.Veces("POST", "/v1/reservations/res-1/cancel"));

        // Y el mismo alquiler DEVUELTO no cancela nada: el equipo salió y volvió.
        var otro = Nuevo(FelizEstricto());
        Assert.True((await Reservar(otro.Flow)).IsOk);
        var devuelta = await otro.Flow.ReturnAsync("alq-1", Cop(0m), CancellationToken.None);

        Assert.True(devuelta.IsOk);
        Assert.Equal("returned", RentalResponse.From(devuelta.Value).State);
        Assert.Equal(0, otro.Caps.Veces("POST", "/v1/reservations/res-1/cancel"));
    }

    // ── La llave, y lo que NO encierra ─────────────────────────────────────

    /// <summary>
    /// Un alquiler deshecho entero NO encierra a quien lo intentó.
    /// </summary>
    /// <remarks>
    /// <para>Es el defecto #41 aplicado acá: la llave se deriva de QUÉ se alquila —equipo, fechas,
    /// arrendatario— así que nunca cambia. Si devolver la saga muerta fuera «idempotencia», quien
    /// intentó alquilar un andamio y no pudo no podría volver a intentarlo jamás.</para>
    ///
    /// <para>La regla vive en <c>SagaEngine.Abrir</c> y <b>no</b> en este flujo, que es la mitad
    /// que importa: escrita acá se corregiría una vez y se olvidaría en el siguiente
    /// orquestador.</para>
    /// </remarks>
    [Fact]
    public async Task Un_alquiler_deshecho_deja_volver_a_intentar_con_la_MISMA_llave()
    {
        var caps = FelizEstricto();
        caps.Falla("POST /v1/payments", HttpStatusCode.PaymentRequired, "payments.declined");
        var ctx = Nuevo(caps);

        var primero = await Reservar(ctx.Flow);
        Assert.False(primero.IsOk);
        Assert.Equal(SagaStatus.Compensated, ctx.Sagas.Find("alq-1")!.Status);

        // La pasarela vuelve en sí y el arrendatario reintenta con la misma llave.
        caps.Secuencia("POST /v1/payments",
            """{"id":"pg-dep","status":"Authorized","amount":{"amount":400000,"currency":"COP"},"refundable":{"amount":0,"currency":"COP"}}""",
            """{"id":"pg-ren","status":"Authorized","amount":{"amount":350000,"currency":"COP"},"refundable":{"amount":0,"currency":"COP"}}""");
        caps.Secuencia("POST /v1/holds",
            """{"id":"h-1","resourceId":"rec-andamio","expiresAt":"2026-09-01T10:10:00+00:00"}""");

        var segundo = await Reservar(ctx.Flow);

        Assert.True(segundo.IsOk);

        // Con identidad PROPIA: sobrescribir la muerta borraría qué falló.
        Assert.NotEqual("alq-1", segundo.Value.Id);
        Assert.Equal(SagaStatus.Compensated, ctx.Sagas.Find("alq-1")!.Status);
    }

    /// <summary>Un reintento sobre un alquiler VIVO devuelve ése, sin alquilar dos veces.</summary>
    [Fact]
    public async Task Un_reintento_sobre_un_alquiler_vivo_devuelve_el_mismo()
    {
        var ctx = Nuevo(FelizEstricto());

        var primero = await Reservar(ctx.Flow);
        var segundo = await Reservar(ctx.Flow);

        Assert.True(primero.IsOk);
        Assert.True(segundo.IsOk);
        Assert.Equal(primero.Value.Id, segundo.Value.Id);
        Assert.Equal(1, ctx.Caps.VecesEn("POST", "/v1/holds"));
        Assert.Equal(2, ctx.Caps.VecesEn("POST", "/v1/payments"));   // los dos de la PRIMERA vez
    }

    // ── Lo que se rechaza antes de tocar una capacidad ─────────────────────

    /// <summary>Un total de cero se rechaza; no se acepta como «gratis».</summary>
    /// <remarks>
    /// Cero es un precio VÁLIDO que no falla en ninguna parte hasta que alguien mira la factura
    /// (<c>a_failed_tryparse_is_not_a_value</c>). Y se rechaza <b>antes</b> de apartar nada: se
    /// comprueba que no hubo ni una llamada.
    /// </remarks>
    [Fact]
    public async Task Un_alquiler_de_cero_se_rechaza_sin_tocar_ninguna_capacidad()
    {
        var ctx = Nuevo(FelizEstricto());

        var r = await ctx.Flow.ReserveAsync(
            Arrendatario, "andamio-6m", 1, Semana, Cop(0m), Cop(400000m), "alq-1", CancellationToken.None);

        Assert.False(r.IsOk);
        Assert.Equal("alquiler.bad_total", r.Rejection!.Code);
        Assert.Empty(ctx.Caps.Llamadas);
    }

    /// <summary>Más unidades de las que el flujo admite se rechaza.</summary>
    [Fact]
    public async Task Mas_unidades_de_las_que_caben_se_rechaza()
    {
        var ctx = Nuevo(FelizEstricto());

        var r = await Reservar(ctx.Flow, unidades: RentalFlow.MaxUnits + 1);

        Assert.False(r.IsOk);
        Assert.Equal("alquiler.bad_quantity", r.Rejection!.Code);
        Assert.Empty(ctx.Caps.Llamadas);
    }

    /// <summary>
    /// N unidades son N apartados, y ninguno es un pozo contable.
    /// </summary>
    /// <remarks>
    /// Un hold de <c>Api.Booking</c> es UNA unidad de capacidad —<c>CreateHoldRequest</c> no lleva
    /// cantidad— así que alquilar dos andamios son dos apartados. Y se mira POR DÓNDE pregunta: si
    /// alguno se hubiera ido a <c>Api.Inventory</c>, aparecería un <c>/v1/items</c>.
    /// </remarks>
    [Fact]
    public async Task Dos_unidades_son_dos_apartados_sobre_el_mismo_recurso()
    {
        var ctx = Nuevo(FelizEstricto());

        var r = await Reservar(ctx.Flow, unidades: 2);

        Assert.True(r.IsOk);
        Assert.Equal(2, ctx.Caps.VecesEn("POST", "/v1/holds"));
        Assert.Equal(2, r.Value.Holds.Count);
        Assert.All(r.Value.Holds, h => Assert.Equal("rec-andamio", h.ResourceId));
        Assert.Equal(0, ctx.Caps.Veces("GET", "/v1/items"));
        Assert.Equal(0, ctx.Caps.Veces("POST", "/v1/items"));

        // Y sólo DOS cobros, no uno por unidad: se alquila un lote, no dos alquileres.
        Assert.Equal(2, ctx.Caps.VecesEn("POST", "/v1/payments"));
    }
}
