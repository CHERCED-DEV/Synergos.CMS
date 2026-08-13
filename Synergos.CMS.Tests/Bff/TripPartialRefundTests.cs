using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Synergos.Bff.Core;
using Synergos.Bff.Viajes.Clients;
using Synergos.Bff.Viajes.Contracts;
using Synergos.Bff.Viajes.Domain;
using Synergos.Core;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// Quien vendió ORDENA la devolución de lo que no se cumplió (#40, rebanada 3).
/// </summary>
/// <remarks>
/// <para><b>Es la otra mitad de la confirmación parcial, y sin ella la primera era un
/// retroceso.</b> Con parcial, el ítem caído se suelta y el viaje sigue en pie — pero quien compró
/// ya pagó el carrito entero. Sin una puerta para devolverle esa parte, cablear el carrito
/// multi-producto contra el orquestador habría dejado esa plata acá, <b>sin que nada
/// fallara</b>.</para>
///
/// <para><b>El monto llega de fuera a propósito.</b> Este orquestador cotiza el viaje entero de
/// una vez —«el precio de un paquete no es necesariamente la suma de sus partes»— así que no sabe
/// cuánto vale el ítem caído. Repartir el total sería inventarse una política comercial. Es la
/// misma forma que la penalidad de cancelar.</para>
///
/// <para><b>Y lo devuelto se ESPEJA de <c>Api.Payments</c>, no se acumula acá.</b> Repetir la
/// orden con la misma llave devuelve la misma operación; sumarla de este lado diría que el viaje
/// devolvió el doble de lo que devolvió.</para>
/// </remarks>
public sealed class TripPartialRefundTests
{
    private sealed class RelojFalso : TimeProvider
    {
        private readonly DateTimeOffset _ahora;
        public RelojFalso(DateTimeOffset ahora) => _ahora = ahora;
        public override DateTimeOffset GetUtcNow() => _ahora;
    }

    private sealed class CapacidadesFalsas : HttpMessageHandler
    {
        private readonly List<(string Patron, Func<HttpRequestMessage, HttpResponseMessage> Responde)> _rutas = new();

        public List<(string Clave, string? Llave, string? Cuerpo)> Llamadas { get; } = new();

        public CapacidadesFalsas Cuando(string patron, Func<HttpRequestMessage, HttpResponseMessage> responde)
        {
            _rutas.Insert(0, (patron, responde));
            return this;
        }

        public CapacidadesFalsas Ok(string patron, string json)
            => Cuando(patron, _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            });

        public CapacidadesFalsas Falla(string patron, HttpStatusCode codigo, string code, string detail)
            => Cuando(patron, _ => new HttpResponseMessage(codigo)
            {
                Content = new StringContent(
                    $$"""{"title":"x","status":{{(int)codigo}},"detail":"{{detail}}","code":"{{code}}"}""",
                    System.Text.Encoding.UTF8, "application/problem+json"),
            });

        public int Veces(string sufijo)
            => Llamadas.Count(l => l.Clave.EndsWith(sufijo, StringComparison.Ordinal));

        public (string Clave, string? Llave, string? Cuerpo) Ultima(string sufijo)
            => Llamadas.Last(l => l.Clave.EndsWith(sufijo, StringComparison.Ordinal));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            var clave = $"{req.Method.Method} {req.RequestUri!.AbsolutePath}";
            Llamadas.Add((clave,
                req.Headers.TryGetValues("Idempotency-Key", out var k) ? k.FirstOrDefault() : null,
                req.Content?.ReadAsStringAsync(ct).GetAwaiter().GetResult()));

            foreach (var (patron, responde) in _rutas)
            {
                if (clave.StartsWith(patron, StringComparison.Ordinal)) return Task.FromResult(responde(req));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class FabricaFalsa : IHttpClientFactory
    {
        private readonly HttpMessageHandler _h;
        public FabricaFalsa(HttpMessageHandler h) => _h = h;
        public HttpClient CreateClient(string name)
            => new(_h, disposeHandler: false) { BaseAddress = new Uri("http://caps.local/") };
    }

    private sealed class MemoriaSagas : ISagaStore<TripSaga>
    {
        private readonly Dictionary<string, TripSaga> _todas = new(StringComparer.Ordinal);
        public TripSaga? Find(string id) => _todas.TryGetValue(id, out var s) ? s : null;
        public void Put(TripSaga saga) => _todas[saga.Id] = saga;
        public IReadOnlyList<TripSaga> All() => _todas.Values.ToList();
        public IReadOnlyList<TripSaga> WithPendingCompensations()
            => _todas.Values.Where(s => s.Compensations.Any(c => c.IsPending)).ToList();
        public IReadOnlyList<TripSaga> StartedBefore(DateTimeOffset corte)
            => _todas.Values.Where(s => s.StartedAtUtc < corte).ToList();
    }

    private static readonly DateTimeOffset Ahora = new(2026, 8, 12, 10, 0, 0, TimeSpan.Zero);
    private static readonly Ref Viajero = Ref.Create("viajes.viajero", "u-1");
    private static readonly IdempotencyKey Llave = IdempotencyKey.Of("orden-1:refund:300000");

    private static IReadOnlyList<TripItem> Carrito => new[]
    {
        new TripItem("vuelo-1", "Vuelo BOG→MDE",
            new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 10, 14, 0, 0, TimeSpan.Zero)),
    };

    /// <summary>Un viaje que sale entero, cobrado en 900.000.</summary>
    private static CapacidadesFalsas ViajeQueSale()
        => new CapacidadesFalsas()
            .Ok("GET /v1/resources", """{"items":[{"id":"rec-x","capacity":1}],"total":1,"offset":0,"hasMore":false}""")
            .Ok("POST /v1/quotes", """{"total":{"amount":900000,"currency":"COP"},"lines":[]}""")
            .Ok("POST /v1/payments", """{"id":"pay-1","status":"Authorized","refundable":{"amount":0,"currency":"COP"}}""")
            .Ok("POST /v1/payments/pay-1/capture", """{"id":"pay-1","status":"Captured","amount":{"amount":900000,"currency":"COP"},"refundable":{"amount":900000,"currency":"COP"}}""")
            .Ok("POST /v1/holds", """{"id":"h-1","resourceId":"rec-x","status":"Held"}""")
            .Ok("POST /v1/holds/h-1/confirm", """{"id":"res-1","status":"Confirmed"}""")
            .Ok("GET /v1/payments/pay-1", """{"id":"pay-1","status":"Captured","amount":{"amount":900000,"currency":"COP"},"refundable":{"amount":900000,"currency":"COP"}}""")
            // Devuelve 300.000 e informa el ACUMULADO, que es lo que la saga espeja.
            .Ok("POST /v1/payments/pay-1/refund", """{"id":"pay-1","status":"Captured","amount":{"amount":900000,"currency":"COP"},"refunded":{"amount":300000,"currency":"COP"},"refundable":{"amount":600000,"currency":"COP"}}""");

    private static (TripFlow Flow, CapacidadesFalsas Caps, MemoriaSagas Sagas) Nuevo(CapacidadesFalsas caps)
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
            ToKind = "viajes.guardia", ToId = "operaciones", Address = "guardia@ejemplo.co",
            TemplateKey = "viajes.compensacion.colgada",
        }));
        var motor = new SagaEngine<TripSaga>(sagas, comp, aviso, vocabulario, reloj,
            NullLogger<SagaEngine<TripSaga>>.Instance);

        return (new TripFlow(api, motor, reloj, NullLogger<TripFlow>.Instance), caps, sagas);
    }

    private static async Task<(TripFlow Flow, CapacidadesFalsas Caps, MemoriaSagas Sagas)> ViajeConfirmado(
        CapacidadesFalsas? caps = null)
    {
        var todo = Nuevo(caps ?? ViajeQueSale());
        await todo.Flow.BookAsync(Viajero, Carrito, "v-1", CancellationToken.None, partialConfirm: true);
        var r = await todo.Flow.ConfirmAsync("v-1", CancellationToken.None);
        Assert.True(r.IsOk);
        return todo;
    }

    // ── Lo que se devuelve ──────────────────────────────────────────────────

    /// <summary>Se devuelve exactamente lo que ordenó quien vendió, ni más ni menos.</summary>
    [Fact]
    public async Task Se_devuelve_lo_que_ordena_quien_vendio()
    {
        var (flow, caps, sagas) = await ViajeConfirmado();

        var r = await flow.RefundAsync("v-1", Money.Of(300000m, "COP"), "auto no cumplido", Llave,
            CancellationToken.None);

        Assert.True(r.IsOk);
        Assert.Equal(1, caps.Veces("/refund"));

        var pedido = caps.Ultima("/refund");
        Assert.Contains("300000", pedido.Cuerpo, StringComparison.Ordinal);
        Assert.Equal(Llave.Value, pedido.Llave);

        // Y el viaje sigue en pie: devolver una parte no lo deshace.
        Assert.Equal(SagaStatus.Completed, sagas.Find("v-1")!.Status);
        Assert.Equal(300000m, sagas.Find("v-1")!.Refunded!.Value.Amount);
    }

    /// <summary>
    /// Repetir la orden no devuelve dos veces, ni dice que devolvió el doble.
    /// </summary>
    /// <remarks>
    /// Las dos mitades importan y la segunda es la sutil: la llave protege el movimiento de plata
    /// en <c>Api.Payments</c>, pero si la saga <i>sumara</i> lo pedido en vez de espejar lo que la
    /// capacidad dice haber devuelto, el segundo intento —que no movió nada— habría dejado escrito
    /// que el viaje devolvió 600.000. Nadie habría perdido plata y el registro mentiría.
    /// </remarks>
    [Fact]
    public async Task Repetir_la_orden_no_devuelve_dos_veces()
    {
        var (flow, caps, sagas) = await ViajeConfirmado();

        await flow.RefundAsync("v-1", Money.Of(300000m, "COP"), null, Llave, CancellationToken.None);
        var otra = await flow.RefundAsync("v-1", Money.Of(300000m, "COP"), null, Llave, CancellationToken.None);

        Assert.True(otra.IsOk);
        Assert.Equal(300000m, sagas.Find("v-1")!.Refunded!.Value.Amount);
        Assert.All(caps.Llamadas.Where(l => l.Clave.EndsWith("/refund", StringComparison.Ordinal)),
            l => Assert.Equal(Llave.Value, l.Llave));
    }

    /// <summary>Lo devuelto sale en la respuesta, para que quien vendió selle su orden.</summary>
    /// <remarks>
    /// Sin esto, el CMS tendría que deducirlo del total — y deducir es adivinar: puede haberse
    /// devuelto algo antes por otro ítem. Preguntarle a <c>Api.Payments</c> no es opción: no puede
    /// hablarle.
    /// </remarks>
    [Fact]
    public async Task Lo_devuelto_sale_en_la_respuesta()
    {
        var (flow, _, _) = await ViajeConfirmado();

        var r = await flow.RefundAsync("v-1", Money.Of(300000m, "COP"), null, Llave, CancellationToken.None);
        var salida = TripResponse.From(r.Value);

        Assert.NotNull(salida.Refunded);
        Assert.Equal(300000m, salida.Refunded!.Amount);
        Assert.Equal("COP", salida.Refunded.Currency);
    }

    // ── Lo que se rechaza ───────────────────────────────────────────────────

    /// <summary>Sobre un viaje que todavía no salió no se devuelve una parte.</summary>
    /// <remarks>
    /// Antes de confirmar no se movió plata: lo que hay es una autorización, y deshacerla entera
    /// es cancelar. Aceptar una devolución parcial acá sería ordenarle a <c>Api.Payments</c> algo
    /// que no puede hacer, y descubrirlo allá cuesta un viaje de ida y vuelta por algo que se sabe
    /// de este lado.
    /// </remarks>
    [Fact]
    public async Task Un_viaje_sin_confirmar_no_admite_devolucion_parcial()
    {
        var (flow, caps, _) = Nuevo(ViajeQueSale());
        await flow.BookAsync(Viajero, Carrito, "v-1", CancellationToken.None, partialConfirm: true);

        var r = await flow.RefundAsync("v-1", Money.Of(300000m, "COP"), null, Llave, CancellationToken.None);

        Assert.False(r.IsOk);
        Assert.Equal("viajes.not_refundable", r.Rejection!.Code);
        Assert.Equal(0, caps.Veces("/refund"));
    }

    /// <summary>No se devuelve más de lo que costó el viaje.</summary>
    [Fact]
    public async Task No_se_devuelve_mas_de_lo_que_costo()
    {
        var (flow, caps, _) = await ViajeConfirmado();

        var r = await flow.RefundAsync("v-1", Money.Of(2000000m, "COP"), null, Llave, CancellationToken.None);

        Assert.False(r.IsOk);
        Assert.Equal("viajes.bad_refund", r.Rejection!.Code);
        Assert.Equal(0, caps.Veces("/refund"));
    }

    /// <summary>Y no se devuelve en otra moneda que la del viaje.</summary>
    /// <remarks>
    /// <c>Money</c> lanza al combinar monedas distintas, y con razón. Acá el dato viene de fuera,
    /// así que se contesta con un rechazo en vez de con una excepción — que es la diferencia entre
    /// «lo pediste mal» y «se nos rompió».
    /// </remarks>
    [Fact]
    public async Task No_se_devuelve_en_otra_moneda()
    {
        var (flow, caps, _) = await ViajeConfirmado();

        var r = await flow.RefundAsync("v-1", Money.Of(100m, "USD"), null, Llave, CancellationToken.None);

        Assert.False(r.IsOk);
        Assert.Equal("viajes.bad_refund", r.Rejection!.Code);
        Assert.Equal(0, caps.Veces("/refund"));
    }

    /// <summary>
    /// Si la devolución falla, NO se arma una compensación.
    /// </summary>
    /// <remarks>
    /// <para><b>Es el punto más fino de esta rebanada.</b> El reflejo sería anotarla como
    /// pendiente para que el barrido la reintente — y sería un defecto grave: el motor sólo sabe
    /// devolver <i>todo lo devolvible</i> (<c>RefundPayment</c>), así que el barrido acabaría
    /// devolviendo el viaje entero. Quien compró un vuelo, un hotel y un auto se quedaría sin
    /// pagar el vuelo y el hotel que <b>sí</b> recibió.</para>
    ///
    /// <para>El rechazo sale hacia quien vendió, que puede repetir la orden con la misma llave sin
    /// duplicar nada. Es la excepción a «una devolución no se pierde en un log» y está escrita
    /// para que nadie la corrija por instinto.</para>
    /// </remarks>
    [Fact]
    public async Task Si_la_devolucion_falla_no_se_arma_una_compensacion()
    {
        var caps = ViajeQueSale();
        var (flow, _, sagas) = await ViajeConfirmado(caps);
        caps.Falla("POST /v1/payments/pay-1/refund", HttpStatusCode.ServiceUnavailable,
            "payments.provider_down", "El proveedor no responde.");

        var r = await flow.RefundAsync("v-1", Money.Of(300000m, "COP"), null, Llave, CancellationToken.None);

        Assert.False(r.IsOk);

        var saga = sagas.Find("v-1")!;
        Assert.DoesNotContain(saga.Compensations, c => c.IsPending);
        Assert.Null(saga.Refunded);
        Assert.Equal(SagaStatus.Completed, saga.Status);
    }
}
