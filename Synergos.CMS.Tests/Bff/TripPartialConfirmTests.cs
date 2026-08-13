using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Synergos.Bff.Core;
using Synergos.Bff.Viajes.Clients;
using Synergos.Bff.Viajes.Domain;
using Synergos.Core;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// Un ítem que no se pudo confirmar no tumba el viaje entero, si se pidió así (#40).
/// </summary>
/// <remarks>
/// <para><b>La decisión de fondo, y por qué es del que vende.</b> Todo-o-nada es correcto para un
/// paquete que no sirve partido; conservar lo que salió es correcto para tres compras que
/// coinciden en un carrito. La misma máquina sirve a los dos y no puede adivinar cuál es cuál, así
/// que <b>el modo llega en la petición</b> — exactamente como la penalidad de <c>CancelAsync</c>
/// llega calculada de fuera.</para>
///
/// <para><b>Y acá NO se devuelve plata</b>, que es lo que costó pensar. El viaje se cotiza UNA vez
/// («el precio de un paquete no es necesariamente la suma de sus partes»), así que el orquestador
/// no sabe cuánto vale el ítem caído. Repartir el total sería inventarse una política comercial.
/// Se reporta qué no se cumplió y quien vendió ordena la devolución.</para>
/// </remarks>
public sealed class TripPartialConfirmTests
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

        public List<string> Llamadas { get; } = new();

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
            => Llamadas.Count(l => l.EndsWith(sufijo, StringComparison.Ordinal));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            var clave = $"{req.Method.Method} {req.RequestUri!.AbsolutePath}";
            Llamadas.Add(clave);

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

    private static DateTimeOffset F(int dia) => new(2026, 9, dia, 12, 0, 0, TimeSpan.Zero);

    private static IReadOnlyList<TripItem> Itinerario => new[]
    {
        new TripItem("vuelo-1", "Vuelo BOG→MDE", F(10), F(11)),
        new TripItem("hotel-1", "Hotel 2 noches", F(11), F(13)),
        new TripItem("auto-1", "Auto compacto", F(11), F(13)),
    };

    /// <summary>Todo bien menos la confirmación del tercer apartado.</summary>
    private static CapacidadesFalsas ConElTercerItemCaido()
    {
        var caps = new CapacidadesFalsas()
            .Ok("GET /v1/resources", """{"items":[{"id":"rec-x","capacity":1}],"total":1,"offset":0,"hasMore":false}""")
            .Ok("POST /v1/quotes", """{"total":{"amount":900000,"currency":"COP"},"lines":[]}""")
            .Ok("POST /v1/payments", """{"id":"pay-1","status":"Authorized","captured":{"amount":0,"currency":"COP"},"refundable":{"amount":0,"currency":"COP"}}""")
            .Ok("POST /v1/payments/pay-1/capture", """{"id":"pay-1","status":"Captured","captured":{"amount":900000,"currency":"COP"},"refundable":{"amount":900000,"currency":"COP"}}""");

        var apartados = 0;
        caps.Cuando("POST /v1/holds", _ =>
        {
            apartados++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""{"id":"h-{{apartados}}","resourceId":"rec-x","status":"Held"}""",
                    System.Text.Encoding.UTF8, "application/json"),
            };
        });

        // h-1 y h-2 confirman; h-3 no.
        caps.Ok("POST /v1/holds/h-1/confirm", """{"id":"res-1","status":"Confirmed"}""");
        caps.Ok("POST /v1/holds/h-2/confirm", """{"id":"res-2","status":"Confirmed"}""");
        caps.Falla("POST /v1/holds/h-3/confirm", HttpStatusCode.Conflict,
            "booking.hold_expired", "El apartado venció.");
        caps.Ok("POST /v1/holds/h-3/release", """{"id":"h-3","status":"Released"}""");

        // Lo que hace falta para que una compensación pueda CERRAR: si no está guionado, la saga
        // se queda en Compensating y el test mediría otra cosa.
        caps.Ok("GET /v1/payments/pay-1", """{"id":"pay-1","status":"Captured","captured":{"amount":900000,"currency":"COP"},"refundable":{"amount":900000,"currency":"COP"}}""");
        caps.Ok("POST /v1/payments/pay-1/refund", """{"id":"pay-1","status":"Refunded","captured":{"amount":900000,"currency":"COP"},"refundable":{"amount":0,"currency":"COP"}}""");
        caps.Ok("POST /v1/payments/pay-1/void", """{"id":"pay-1","status":"Voided","captured":{"amount":0,"currency":"COP"},"refundable":{"amount":0,"currency":"COP"}}""");
        caps.Ok("POST /v1/reservations/res-1/cancel", """{"id":"res-1","status":"Cancelled"}""");
        caps.Ok("POST /v1/reservations/res-2/cancel", """{"id":"res-2","status":"Cancelled"}""");
        caps.Ok("POST /v1/holds/h-1/release", """{"id":"h-1","status":"Released"}""");
        caps.Ok("POST /v1/holds/h-2/release", """{"id":"h-2","status":"Released"}""");

        return caps;
    }

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

    // ── El default NO cambia ────────────────────────────────────────────────

    /// <summary>
    /// Sin pedirlo, un ítem caído sigue tumbando el viaje entero.
    /// </summary>
    /// <remarks>
    /// Es la mitad que protege a quien ya usaba esto: la vía hotel (#36) reserva UNA habitación y
    /// para ella todo-o-nada es correcto. Cambiar el default habría cambiado su producto sin que
    /// nadie lo pidiera.
    /// </remarks>
    [Fact]
    public async Task Sin_pedir_parcial_un_item_caido_tumba_el_viaje()
    {
        var (flow, _, sagas) = Nuevo(ConElTercerItemCaido());
        await flow.BookAsync(Viajero, Itinerario, "v-1", CancellationToken.None);

        var r = await flow.ConfirmAsync("v-1", CancellationToken.None);

        Assert.False(r.IsOk);
        Assert.Equal(SagaStatus.Compensated, sagas.Find("v-1")!.Status);
    }

    // ── El modo parcial ─────────────────────────────────────────────────────

    /// <summary>
    /// Con parcial, lo que sí salió se queda y lo caído se marca.
    /// </summary>
    [Fact]
    public async Task Con_parcial_se_conserva_lo_que_si_salio()
    {
        var (flow, _, sagas) = Nuevo(ConElTercerItemCaido());
        await flow.BookAsync(Viajero, Itinerario, "v-1", CancellationToken.None, partialConfirm: true);

        var r = await flow.ConfirmAsync("v-1", CancellationToken.None);

        Assert.True(r.IsOk);
        Assert.Equal(SagaStatus.Completed, r.Value.Status);

        var holds = r.Value.Holds;
        Assert.Equal(2, holds.Count(h => h.ReservationId is not null));
        Assert.Single(holds.Where(h => h.Unfulfilled));
        Assert.Equal("auto-1", holds.Single(h => h.Unfulfilled).ProductRef);

        Assert.Equal(SagaStatus.Completed, sagas.Find("v-1")!.Status);
    }

    /// <summary>
    /// El apartado del ítem caído se SUELTA, y en el momento — no al final.
    /// </summary>
    /// <remarks>
    /// El viaje termina en <c>Completed</c>, y completar marca las compensaciones armadas como no
    /// aplicables. Dejarlo para entonces convertiría ese apartado en cupo retenido que ya no
    /// suelta nadie — un fallo que no rompe nada y se descubre cuando el hotel está lleno de
    /// reservas que nadie hizo.
    /// </remarks>
    [Fact]
    public async Task El_apartado_del_item_caido_se_suelta()
    {
        var (flow, caps, _) = Nuevo(ConElTercerItemCaido());
        await flow.BookAsync(Viajero, Itinerario, "v-1", CancellationToken.None, partialConfirm: true);

        await flow.ConfirmAsync("v-1", CancellationToken.None);

        Assert.Equal(1, caps.Veces("/v1/holds/h-3/release"));
        // Y NO se soltaron los que sí se confirmaron.
        Assert.Equal(0, caps.Veces("/v1/holds/h-1/release"));
        Assert.Equal(0, caps.Veces("/v1/holds/h-2/release"));
    }

    /// <summary>
    /// NO se devuelve plata sola: el orquestador no sabe cuánto vale el ítem caído.
    /// </summary>
    /// <remarks>
    /// El viaje se cotiza entero a propósito, así que repartir el total entre los ítems sería
    /// inventarse una política comercial. Se reporta qué no se cumplió; quien vendió ordena la
    /// devolución con el monto que él sí sabe calcular.
    /// </remarks>
    [Fact]
    public async Task No_se_devuelve_plata_sola()
    {
        var (flow, caps, _) = Nuevo(ConElTercerItemCaido());
        await flow.BookAsync(Viajero, Itinerario, "v-1", CancellationToken.None, partialConfirm: true);

        await flow.ConfirmAsync("v-1", CancellationToken.None);

        Assert.Equal(0, caps.Veces("/refund"));
    }

    /// <summary>
    /// Si NO se cumplió nada, no es un viaje parcial: es un viaje fallido.
    /// </summary>
    /// <remarks>
    /// Dejarlo <c>Completed</c> diría que se entregó algo, y quien vendió leería «parcial» donde
    /// no hubo nada — el peor sitio para una media verdad, porque decide si se devuelve todo.
    /// </remarks>
    [Fact]
    public async Task Si_no_se_cumplio_nada_el_viaje_es_fallido()
    {
        var caps = ConElTercerItemCaido();
        // Ahora tampoco confirman los dos primeros.
        caps.Falla("POST /v1/holds/h-1/confirm", HttpStatusCode.Conflict, "booking.hold_expired", "venció");
        caps.Falla("POST /v1/holds/h-2/confirm", HttpStatusCode.Conflict, "booking.hold_expired", "venció");
        caps.Ok("POST /v1/holds/h-1/release", """{"id":"h-1","status":"Released"}""");
        caps.Ok("POST /v1/holds/h-2/release", """{"id":"h-2","status":"Released"}""");

        var (flow, _, sagas) = Nuevo(caps);
        await flow.BookAsync(Viajero, Itinerario, "v-1", CancellationToken.None, partialConfirm: true);

        var r = await flow.ConfirmAsync("v-1", CancellationToken.None);

        Assert.False(r.IsOk);
        Assert.Equal("viajes.nothing_fulfilled", r.Rejection!.Code);
        Assert.Equal(SagaStatus.Compensated, sagas.Find("v-1")!.Status);
    }

    /// <summary>
    /// Si soltar el apartado caído falla, su compensación queda PENDIENTE para el barrido.
    /// </summary>
    /// <remarks>
    /// Marcarla hecha porque «el viaje siguió» perdería el cupo en silencio. Que quede pendiente
    /// es lo que hace que alguien vuelva por él.
    /// </remarks>
    [Fact]
    public async Task Si_soltar_falla_la_compensacion_queda_pendiente()
    {
        var caps = ConElTercerItemCaido();
        caps.Falla("POST /v1/holds/h-3/release", HttpStatusCode.ServiceUnavailable,
            "booking.unavailable", "no hay quien atienda");

        var (flow, _, sagas) = Nuevo(caps);
        await flow.BookAsync(Viajero, Itinerario, "v-1", CancellationToken.None, partialConfirm: true);

        var r = await flow.ConfirmAsync("v-1", CancellationToken.None);

        Assert.True(r.IsOk);
        var suelta = sagas.Find("v-1")!.Compensations
            .Single(c => c.Kind == ViajesCompensations.ReleaseBookingHold && c.TargetId == "h-3");
        Assert.True(suelta.IsPending, "La compensación del apartado que no se pudo soltar tiene que quedar pendiente.");
    }

    [Fact] // idempotente: re-confirmar un viaje parcial no vuelve a intentar el caído.
    public async Task Reconfirmar_un_viaje_parcial_no_reintenta_lo_caido()
    {
        var (flow, caps, _) = Nuevo(ConElTercerItemCaido());
        await flow.BookAsync(Viajero, Itinerario, "v-1", CancellationToken.None, partialConfirm: true);
        await flow.ConfirmAsync("v-1", CancellationToken.None);

        var antes = caps.Veces("/v1/holds/h-3/confirm");
        await flow.ConfirmAsync("v-1", CancellationToken.None);

        Assert.Equal(antes, caps.Veces("/v1/holds/h-3/confirm"));
    }
}
