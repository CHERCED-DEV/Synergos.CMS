using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Synergos.Bff.Core;
using Synergos.Bff.Viajes.Clients;
using Synergos.Bff.Viajes.Domain;
using Synergos.Core;
using Compensation = Synergos.Bff.Core.Compensation;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// El viaje que se deshace (HU #36).
/// </summary>
/// <remarks>
/// <para><b>Lo que este dominio aporta y los tres anteriores no:</b> los pasos reversibles son
/// varios y heterogéneos, y —lo que de verdad cambia— <b>pueden estar en estados distintos
/// cuando llega el fallo</b>. Al confirmar el tercer ítem, los dos primeros ya son reservas: lo
/// que hay que deshacer no es lo mismo para todos.</para>
///
/// <para>Por eso el doble de <c>Api.Booking</c> es estricto: <b>soltar un apartado ya confirmado
/// se rechaza</b>. Un doble más permisivo que la cosa real no prueba nada — ya mordió en este
/// repo con otros disfraces, y aquí sería peor: el flujo pasaría en verde y en producción los
/// ítems ya confirmados no se cancelarían nunca.</para>
/// </remarks>
public sealed class TripCompensationTests
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
    /// El doble se comporta como <c>Api.Booking</c>: <b>soltar un apartado ya confirmado se
    /// rechaza</b>, porque al confirmarlo dejó de existir y se convirtió en una reserva.
    /// </summary>
    /// <remarks>
    /// Sin esto, el test codifica la misma suposición equivocada que el código: un flujo que
    /// nunca reescribiera la compensación seguiría en verde, y en producción los ítems ya
    /// confirmados no se cancelarían jamás — el viajero se quedaría con un vuelo que nadie pagó.
    /// </remarks>
    private static CapacidadesFalsas ComoBookingDeVerdad(CapacidadesFalsas caps, params (string Hold, string Reserva)[] items)
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
                    Content = new StringContent($$"""{"id":"{{h}}","resourceId":"rec","expiresAt":"2026-08-05T10:15:00+00:00"}""",
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

    private sealed class MemoriaSagas : ISagaStore<TripSaga>
    {
        private readonly Dictionary<string, TripSaga> _s = new(StringComparer.Ordinal);
        public TripSaga? Find(string id) => _s.GetValueOrDefault(id);
        public IReadOnlyList<TripSaga> WithPendingCompensations()
            => _s.Values.Where(x => x.IsUnwinding() && x.Pending().Count > 0).ToList();
        public IReadOnlyList<TripSaga> StartedBefore(DateTimeOffset limite)
            => _s.Values.Where(x => x.Status == SagaStatus.Running && x.StartedAtUtc < limite).ToList();
        public void Put(TripSaga saga) => _s[saga.Id] = saga;
    }

    private static readonly DateTimeOffset Ahora = new(2026, 8, 5, 10, 0, 0, TimeSpan.Zero);
    private static readonly Ref Viajero = Ref.Create("viajes.viajero", "u-1");

    private sealed record Contexto(TripFlow Flow, CapacidadesFalsas Caps, MemoriaSagas Sagas, RelojFalso Reloj);

    /// <summary>
    /// Un vuelo, dos noches de hotel y un auto: cuatro productos heterogéneos, cuatro ventanas.
    /// </summary>
    /// <remarks>
    /// Probarlo mezclado no es cosmética. Si las cuatro formas no cupieran en la misma
    /// transacción, el vertical tendría que partir el viaje y el viajero vería cuatro cobros —
    /// y cuatro cancelaciones parciales cuando algo fallara.
    /// </remarks>
    private static IReadOnlyList<TripItem> Itinerario => new[]
    {
        new TripItem("vuelo/AV-8020/2026-09-01", "Bogotá → Cartagena", Fecha(9, 1, 8), Fecha(9, 1, 10)),
        new TripItem("hotel/caribe/DBL", "Hotel Caribe — doble", Fecha(9, 1, 15), Fecha(9, 3, 11)),
        new TripItem("auto/economico/CTG", "Auto económico", Fecha(9, 1, 12), Fecha(9, 3, 9)),
    };

    private static DateTimeOffset Fecha(int mes, int dia, int hora)
        => new(2026, mes, dia, hora, 0, 0, TimeSpan.Zero);

    private static CapacidadesFalsas Feliz() => new CapacidadesFalsas()
        .Ok("POST /v1/quotes", """{"subtotal":{"amount":1200000,"currency":"COP"},"tax":{"amount":0,"currency":"COP"},"total":{"amount":1200000,"currency":"COP"}}""")
        // Tres recursos, uno por producto. La capacidad los devuelve por sujeto.
        .Secuencia("GET /v1/resources",
            """{"id":"rec-vuelo","subjectKind":"viajes.producto","subjectId":"vuelo/AV-8020/2026-09-01","capacity":180}""",
            """{"id":"rec-hotel","subjectKind":"viajes.producto","subjectId":"hotel/caribe/DBL","capacity":40}""",
            """{"id":"rec-auto","subjectKind":"viajes.producto","subjectId":"auto/economico/CTG","capacity":12}""")
        .Secuencia("POST /v1/holds",
            """{"id":"h-1","resourceId":"rec-vuelo","expiresAt":"2026-08-05T10:15:00+00:00"}""",
            """{"id":"h-2","resourceId":"rec-hotel","expiresAt":"2026-08-05T10:15:00+00:00"}""",
            """{"id":"h-3","resourceId":"rec-auto","expiresAt":"2026-08-05T10:15:00+00:00"}""")
        .Ok("POST /v1/payments", """{"id":"pg1","status":"Authorized","amount":{"amount":1200000,"currency":"COP"},"refundable":{"amount":0,"currency":"COP"}}""")
        .Ok("POST /v1/payments/pg1/capture", """{"id":"pg1","status":"Captured","amount":{"amount":1200000,"currency":"COP"},"refundable":{"amount":1200000,"currency":"COP"}}""")
        .Ok("POST /v1/payments/pg1/void", """{"id":"pg1","status":"Voided","amount":{"amount":1200000,"currency":"COP"},"refundable":{"amount":0,"currency":"COP"}}""")
        .Ok("POST /v1/payments/pg1/refund", """{"id":"pg1","status":"Captured","amount":{"amount":1200000,"currency":"COP"},"refundable":{"amount":0,"currency":"COP"}}""")
        .Ok("GET /v1/payments/pg1", """{"id":"pg1","status":"Captured","amount":{"amount":1200000,"currency":"COP"},"refundable":{"amount":1200000,"currency":"COP"}}""")
        .Ok("POST /v1/deliveries", """{"id":"d1","status":"Sent"}""");

    /// <summary>El guion feliz, con <c>Api.Booking</c> comportándose como la de verdad.</summary>
    private static CapacidadesFalsas FelizEstricto()
        => ComoBookingDeVerdad(Feliz(), ("h-1", "res-1"), ("h-2", "res-2"), ("h-3", "res-3"));

    private static Contexto Nuevo(CapacidadesFalsas caps)
    {
        var sagas = new MemoriaSagas();
        var reloj = new RelojFalso(Ahora);
        var fabrica = new FabricaFalsa(caps);
        var api = new ViajesCapabilities(fabrica);
        var vocabulario = new SagaVocabulary("viajes", "la reserva de viaje");
        var comp = new Compensator<TripSaga>(
            new ViajesCompensationExecutor(api), reloj, NullLogger<Compensator<TripSaga>>.Instance);
        var aviso = new CompensationAlert(fabrica, vocabulario, Options.Create(new AlertOptions
        {
            ToKind = "viajes.guardia",
            ToId = "operaciones",
            Address = "guardia@ejemplo.co",
            TemplateKey = "viajes.compensacion.colgada",
        }));
        var motor = new SagaEngine<TripSaga>(sagas, comp, aviso, vocabulario, reloj,
            NullLogger<SagaEngine<TripSaga>>.Instance);

        return new Contexto(new TripFlow(api, motor, reloj, NullLogger<TripFlow>.Instance), caps, sagas, reloj);
    }

    private static Task<Result<TripSaga>> Reservar(TripFlow flow, string id = "viaje-1")
        => flow.BookAsync(Viajero, Itinerario, id, CancellationToken.None);

    // ── Los cuatro productos van a la MISMA capacidad ───────────────────────

    /// <summary>
    /// La decisión que traía la HU #36: vuelo, hotel y auto van todos a <c>Api.Booking</c>.
    /// </summary>
    /// <remarks>
    /// Y se comprueba mirando POR DÓNDE pregunta, no lo que devuelve: si un producto se fuera a
    /// <c>Api.Inventory</c>, aparecería un <c>GET /v1/items</c> en las llamadas.
    /// </remarks>
    [Fact]
    public async Task Vuelo_hotel_y_auto_son_el_MISMO_recurso_reservable()
    {
        var ctx = Nuevo(FelizEstricto());

        var r = await Reservar(ctx.Flow);

        Assert.True(r.IsOk);
        Assert.Equal(3, ctx.Caps.Veces("GET", "/v1/resources"));
        Assert.Equal(3, ctx.Caps.Veces("POST", "/v1/holds"));

        // Ni un solo pozo contable: si alguno se hubiera ido a Api.Inventory, esto sería > 0.
        Assert.Equal(0, ctx.Caps.Veces("GET", "/v1/items"));
        Assert.Equal(0, ctx.Caps.Veces("POST", "/v1/items"));

        // Y cada uno preguntó por SU sujeto, sin adivinar el identificador del recurso.
        var sujetos = ctx.Caps.Llamadas
            .Where(l => l.Method == "GET" && l.Path.EndsWith("/v1/resources", StringComparison.Ordinal))
            .Select(l => Uri.UnescapeDataString(l.Query ?? string.Empty))
            .ToList();
        Assert.Contains(sujetos, q => q.Contains("subjectId=vuelo/AV-8020/2026-09-01", StringComparison.Ordinal));
        Assert.Contains(sujetos, q => q.Contains("subjectId=hotel/caribe/DBL", StringComparison.Ordinal));
        Assert.All(sujetos, q => Assert.Contains("subjectKind=viajes.producto", q, StringComparison.Ordinal));
    }

    [Fact] // el precio sale de la capacidad, no del llamador: si no, se compra la suite al precio de la estándar.
    public async Task El_precio_NO_lo_pone_quien_pide()
    {
        var ctx = Nuevo(FelizEstricto());

        var r = await Reservar(ctx.Flow);

        Assert.True(r.IsOk);
        Assert.Equal(1200000m, r.Value.Total.Amount);
        Assert.Equal(1, ctx.Caps.Veces("POST", "/v1/quotes"));

        // Y el cobro se autorizó por lo cotizado, no por otra cosa.
        var pago = ctx.Caps.Llamadas.First(l => l.Path.EndsWith("/v1/payments", StringComparison.Ordinal));
        Assert.Contains("1200000", pago.Body!, StringComparison.Ordinal);
    }

    // ── Lo que se aparta se anota como deshacible EN EL ACTO ────────────────

    /// <summary>
    /// Cada apartado queda armado en el instante en que existe, no al final.
    /// </summary>
    /// <remarks>
    /// Si se anotaran todas al terminar, una caída después del segundo apartado dejaría dos
    /// recursos retenidos sin nada que los suelte (<c>feedback_compensation_is_data</c>).
    /// </remarks>
    [Fact]
    public async Task Cada_apartado_queda_armado_en_el_acto()
    {
        var ctx = Nuevo(FelizEstricto());
        await Reservar(ctx.Flow);

        var saga = ctx.Sagas.Find("viaje-1")!;
        Assert.Equal(4, saga.Compensations.Count);   // tres apartados + la autorización
        Assert.Equal(3, saga.Compensations.Count(c => c.Kind == ViajesCompensations.ReleaseBookingHold));
        Assert.Single(saga.Compensations.Where(c => c.Kind == ViajesCompensations.VoidPayment));

        // ARMADA no es PENDIENTE: nada se ejecutó, la saga va perfectamente.
        Assert.Equal(SagaStatus.Running, saga.Status);
        Assert.Equal(0, ctx.Caps.Veces("POST", "/release"));
        Assert.Equal(0, ctx.Caps.Veces("POST", "/void"));
    }

    [Fact] // un fallo en el TERCER ítem deshace los dos primeros.
    public async Task Si_falla_el_tercer_item_se_sueltan_los_dos_primeros()
    {
        var caps = FelizEstricto();
        // El TERCER apartado no sale; los dos anteriores sí. Va en un solo guion con contador y
        // no en dos registros: el segundo pisaría al primero —son la misma ruta— y fallarían los
        // tres, que es otro escenario y además el que NO se quiere probar acá.
        var apartados = 0;
        caps.Cuando("POST /v1/holds", _ =>
        {
            apartados++;
            return apartados <= 2
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $$"""{"id":"h-{{apartados}}","resourceId":"rec","expiresAt":"2026-08-05T10:15:00+00:00"}""",
                        Encoding.UTF8, "application/json"),
                }
                : new HttpResponseMessage(HttpStatusCode.Conflict)
                {
                    Content = new StringContent(
                        """{"code":"booking.insufficient_capacity","detail":"no quedan autos"}""",
                        Encoding.UTF8, "application/problem+json"),
                };
        });

        var ctx = Nuevo(caps);
        var r = await Reservar(ctx.Flow);

        Assert.False(r.IsOk);
        // El motivo que sale es el de la capacidad, no uno del orquestador: «no quedan autos» es
        // accionable y «no se pudo reservar» no lo es.
        Assert.Equal("booking.insufficient_capacity", r.Rejection!.Code);

        var saga = ctx.Sagas.Find("viaje-1")!;
        Assert.Equal(SagaStatus.Compensated, saga.Status);
        Assert.Empty(saga.Pending());
        Assert.Equal(2, ctx.Caps.Veces("POST", "/release"));   // los dos que sí se apartaron
        Assert.Equal(0, ctx.Caps.Veces("POST", "/v1/payments"));   // nunca se llegó a la plata
    }

    // ── La compensación que cambia de carácter ──────────────────────────────

    /// <summary>
    /// <b>El corazón de este dominio.</b> Al fallar el último ítem, los ya confirmados se
    /// CANCELAN y el que seguía apartado se SUELTA — dos formas distintas en la misma saga.
    /// </summary>
    /// <remarks>
    /// <para>El doble rechaza soltar un apartado ya confirmado, igual que <c>Api.Booking</c>. Sin
    /// la reescritura, esas compensaciones fallarían para siempre y el viajero se quedaría con
    /// dos reservas que nadie va a usar y con la plata cobrada.</para>
    ///
    /// <para>Y se comprueba el RESULTADO, no las llamadas: contar <c>cancel</c> diría que se
    /// intentó, no que quedó deshecho.</para>
    /// </remarks>
    [Fact]
    public async Task Al_fallar_el_ultimo_los_confirmados_se_CANCELAN_y_el_apartado_se_SUELTA()
    {
        var caps = FelizEstricto();
        // El tercer confirm no sale. Los dos primeros ya son reservas.
        caps.Falla("POST /v1/holds/h-3/confirm", HttpStatusCode.Conflict, "booking.hold_expired");

        var ctx = Nuevo(caps);
        await Reservar(ctx.Flow);

        var r = await ctx.Flow.ConfirmAsync("viaje-1", CancellationToken.None);
        Assert.False(r.IsOk);

        var saga = ctx.Sagas.Find("viaje-1")!;
        Assert.Equal(SagaStatus.Compensated, saga.Status);
        Assert.Empty(saga.Pending());   // TODO quedó deshecho, no solo intentado

        // Dos cancelaciones de reserva y una liberación de apartado: las dos formas conviven.
        Assert.Equal(1, ctx.Caps.Veces("POST", "/v1/reservations/res-1/cancel"));
        Assert.Equal(1, ctx.Caps.Veces("POST", "/v1/reservations/res-2/cancel"));
        Assert.Equal(1, ctx.Caps.Veces("POST", "/v1/holds/h-3/release"));

        // Y la plata se devolvió, porque ya se había capturado.
        Assert.Equal(1, ctx.Caps.Veces("POST", "/v1/payments/pg1/refund"));
        Assert.Equal(0, ctx.Caps.Veces("POST", "/v1/payments/pg1/void"));
    }

    /// <summary>
    /// La compensación del pago cambia de carácter al capturar: void antes, refund después.
    /// </summary>
    [Fact]
    public async Task Antes_de_capturar_se_LIBERA_la_autorizacion_y_despues_se_DEVUELVE()
    {
        // Antes: la captura falla, así que nunca se movió plata → void.
        var antes = FelizEstricto();
        antes.Falla("POST /v1/payments/pg1/capture", HttpStatusCode.Conflict, "payments.declined");
        var ctxAntes = Nuevo(antes);
        await Reservar(ctxAntes.Flow);
        await ctxAntes.Flow.ConfirmAsync("viaje-1", CancellationToken.None);

        Assert.Equal(1, ctxAntes.Caps.Veces("POST", "/v1/payments/pg1/void"));
        Assert.Equal(0, ctxAntes.Caps.Veces("POST", "/v1/payments/pg1/refund"));
        Assert.Empty(ctxAntes.Sagas.Find("viaje-1")!.Pending());

        // Después: la captura sale y falla lo siguiente → refund.
        var despues = FelizEstricto();
        despues.Falla("POST /v1/holds/h-1/confirm", HttpStatusCode.Conflict, "booking.hold_expired");
        var ctxDespues = Nuevo(despues);
        await Reservar(ctxDespues.Flow);
        await ctxDespues.Flow.ConfirmAsync("viaje-1", CancellationToken.None);

        Assert.Equal(0, ctxDespues.Caps.Veces("POST", "/v1/payments/pg1/void"));
        Assert.Equal(1, ctxDespues.Caps.Veces("POST", "/v1/payments/pg1/refund"));
        Assert.Empty(ctxDespues.Sagas.Find("viaje-1")!.Pending());
    }

    // ── El camino feliz, y la idempotencia ──────────────────────────────────

    [Fact] // happy: confirmar cierra la puerta al final, cuando ya no queda nada que pueda fallar.
    public async Task Confirmar_captura_primero_y_reserva_despues()
    {
        var ctx = Nuevo(FelizEstricto());
        await Reservar(ctx.Flow);

        var r = await ctx.Flow.ConfirmAsync("viaje-1", CancellationToken.None);

        Assert.True(r.IsOk);
        Assert.Equal(SagaStatus.Completed, r.Value.Status);
        Assert.All(r.Value.Holds, h => Assert.NotNull(h.ReservationId));
        Assert.Empty(r.Value.Pending());

        // El orden importa: capturar va ANTES de confirmar. Al revés, un fallo del cobro dejaría
        // un viaje confirmado que nadie pagó, y eso solo se arregla llamando al viajero.
        var orden = ctx.Caps.Llamadas.Select(l => l.Path).ToList();
        Assert.True(
            orden.IndexOf("/v1/payments/pg1/capture") < orden.IndexOf("/v1/holds/h-1/confirm"),
            "Se confirmó el cupo antes de capturar: un fallo del cobro dejaría el viaje sin pagar.");
    }

    [Fact] // idempotent: pedir el mismo viaje dos veces no aparta ni cobra dos veces.
    public async Task Reservar_dos_veces_con_la_misma_llave_no_duplica_nada()
    {
        var ctx = Nuevo(FelizEstricto());

        var primera = await Reservar(ctx.Flow);
        var segunda = await Reservar(ctx.Flow);

        Assert.True(segunda.IsOk);
        Assert.Equal(primera.Value.Id, segunda.Value.Id);
        Assert.Equal(3, ctx.Caps.Veces("POST", "/v1/holds"));
        Assert.Equal(1, ctx.Caps.Veces("POST", "/v1/payments"));
    }

    [Fact] // idempotent: re-confirmar no vuelve a capturar ni a confirmar.
    public async Task Re_confirmar_no_repite_nada()
    {
        var ctx = Nuevo(FelizEstricto());
        await Reservar(ctx.Flow);
        await ctx.Flow.ConfirmAsync("viaje-1", CancellationToken.None);

        var otra = await ctx.Flow.ConfirmAsync("viaje-1", CancellationToken.None);

        Assert.True(otra.IsOk);
        Assert.Equal(1, ctx.Caps.Veces("POST", "/capture"));
        Assert.Equal(3, ctx.Caps.Veces("POST", "/confirm"));
    }

    /// <summary>
    /// Las llaves de idempotencia salen de la saga, y son DISTINTAS por ítem.
    /// </summary>
    /// <remarks>
    /// Si los tres apartados compartieran llave, <c>Api.Booking</c> devolvería el primero tres
    /// veces y el viaje quedaría con un vuelo y sin hotel — creyendo que tiene los tres.
    /// </remarks>
    [Fact]
    public void Cada_item_lleva_su_propia_llave()
    {
        var ctx = Nuevo(FelizEstricto());
        Reservar(ctx.Flow).GetAwaiter().GetResult();

        var llaves = ctx.Caps.Llamadas
            .Where(l => l.Path.EndsWith("/v1/holds", StringComparison.Ordinal))
            .Select(l => l.Key)
            .ToList();

        Assert.Equal(3, llaves.Count);
        Assert.All(llaves, k => Assert.False(string.IsNullOrWhiteSpace(k)));
        Assert.Equal(3, llaves.Distinct(StringComparer.Ordinal).Count());
    }

    // ── Lo que se rechaza antes de tocar nada ───────────────────────────────

    [Fact] // filter: lo inválido se rechaza sin apartar nada — no deja nada que deshacer.
    public async Task Lo_invalido_se_rechaza_ANTES_de_tocar_una_capacidad()
    {
        var ctx = Nuevo(FelizEstricto());

        var sinItems = await ctx.Flow.BookAsync(Viajero, Array.Empty<TripItem>(), "v-a", CancellationToken.None);
        Assert.Equal("viajes.no_items", sinItems.Rejection!.Code);

        var ventanaMala = await ctx.Flow.BookAsync(Viajero,
            new[] { new TripItem("hotel/x", "x", Fecha(9, 3, 10), Fecha(9, 1, 10)) }, "v-b", CancellationToken.None);
        Assert.Equal("viajes.bad_window", ventanaMala.Rejection!.Code);

        var duplicado = await ctx.Flow.BookAsync(Viajero,
            new[] { Itinerario[0], Itinerario[0] }, "v-c", CancellationToken.None);
        Assert.Equal("viajes.duplicate_item", duplicado.Rejection!.Code);

        var demasiados = await ctx.Flow.BookAsync(Viajero,
            Enumerable.Range(0, TripFlow.MaxItems + 1)
                .Select(i => new TripItem($"p/{i}", $"p{i}", Fecha(9, 1, 8), Fecha(9, 1, 10))).ToList(),
            "v-d", CancellationToken.None);
        Assert.Equal("viajes.too_many_items", demasiados.Rejection!.Code);

        // Ni una sola llamada: rechazar tarde habría dejado apartados que soltar.
        Assert.Empty(ctx.Caps.Llamadas);
    }

    /// <summary>
    /// El mismo producto en DOS periodos distintos sí vale — dos tramos de un vuelo.
    /// </summary>
    /// <remarks>
    /// Es el borde de la regla anterior, y hay que fijarlo: prohibir el producto repetido a secas
    /// haría imposible reservar la ida y la vuelta.
    /// </remarks>
    [Fact]
    public async Task El_mismo_producto_en_otro_periodo_NO_es_un_duplicado()
    {
        var caps = ComoBookingDeVerdad(Feliz(), ("h-1", "res-1"), ("h-2", "res-2"));
        var ctx = Nuevo(caps);

        var r = await ctx.Flow.BookAsync(Viajero, new[]
        {
            new TripItem("vuelo/AV-8020", "ida", Fecha(9, 1, 8), Fecha(9, 1, 10)),
            new TripItem("vuelo/AV-8020", "vuelta", Fecha(9, 5, 18), Fecha(9, 5, 20)),
        }, "viaje-ida-vuelta", CancellationToken.None);

        Assert.True(r.IsOk);
        Assert.Equal(2, r.Value.Holds.Count);
    }

    // ── El viajero se arrepiente ────────────────────────────────────────────

    [Fact] // cancelar antes de confirmar suelta los apartados y libera la autorización.
    public async Task Cancelar_antes_de_confirmar_no_cuesta_una_devolucion()
    {
        var ctx = Nuevo(FelizEstricto());
        await Reservar(ctx.Flow);

        var r = await ctx.Flow.CancelAsync("viaje-1", CancellationToken.None);

        Assert.True(r.IsOk);
        Assert.Equal(SagaStatus.Compensated, r.Value.Status);
        Assert.Empty(r.Value.Pending());
        Assert.Equal(3, ctx.Caps.Veces("POST", "/release"));
        Assert.Equal(1, ctx.Caps.Veces("POST", "/v1/payments/pg1/void"));
        Assert.Equal(0, ctx.Caps.Veces("POST", "/refund"));
    }

    [Fact] // empty: un viaje que no existe no se confirma ni se cancela.
    public async Task Un_viaje_que_no_existe_no_se_confirma()
    {
        var ctx = Nuevo(FelizEstricto());

        Assert.Equal("viajes.trip_not_found",
            (await ctx.Flow.ConfirmAsync("no-existe", CancellationToken.None)).Rejection!.Code);
        Assert.Equal("viajes.trip_not_found",
            (await ctx.Flow.CancelAsync("no-existe", CancellationToken.None)).Rejection!.Code);
        Assert.Equal("viajes.trip_not_found", ctx.Flow.Get("no-existe").Rejection!.Code);
    }

    [Fact] // un viaje ya deshecho no se puede confirmar: sus apartados ya no existen.
    public async Task Un_viaje_deshecho_ya_no_se_confirma()
    {
        var ctx = Nuevo(FelizEstricto());
        await Reservar(ctx.Flow);
        await ctx.Flow.CancelAsync("viaje-1", CancellationToken.None);

        var r = await ctx.Flow.ConfirmAsync("viaje-1", CancellationToken.None);

        Assert.False(r.IsOk);
        Assert.Equal("viajes.not_confirmable", r.Rejection!.Code);
    }

    // ── Lo que el orquestador NO le manda a las capacidades ─────────────────

    /// <summary>
    /// Los sustantivos de viajes no cruzan: la capacidad recibe un recurso y una ventana.
    /// </summary>
    /// <remarks>
    /// <c>Reservation</c> del CMS lleva <c>RoomTypeCode</c>, <c>RatePlanCode</c> y
    /// <c>GuestName</c>, y ninguna capacidad puede guardarlos —lo dice la propia HU #36—. Se
    /// quedan del lado del contenido; acá solo cruza un <c>Ref</c> opaco.
    /// </remarks>
    [Fact]
    public async Task Ningun_sustantivo_de_viajes_cruza_a_las_capacidades()
    {
        var ctx = Nuevo(FelizEstricto());
        await Reservar(ctx.Flow);
        await ctx.Flow.ConfirmAsync("viaje-1", CancellationToken.None);

        var cuerpos = ctx.Caps.Llamadas.Select(l => l.Body).Where(b => b is not null).ToList();
        Assert.NotEmpty(cuerpos);

        foreach (var prohibido in new[] { "roomType", "ratePlan", "guestName", "Hotel Caribe", "Bogotá" })
        {
            Assert.All(cuerpos, b =>
                Assert.False(b!.Contains(prohibido, StringComparison.OrdinalIgnoreCase),
                    $"'{prohibido}' viajó a una capacidad: es un sustantivo de viajes y allá no significa nada."));
        }
    }
}
