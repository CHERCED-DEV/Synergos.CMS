using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Synergos.Bff.Core;
using Synergos.Bff.Viajes.Clients;
using Synergos.Bff.Viajes.Domain;
using Synergos.CMS.Tests.Bff.Viajes;
using Synergos.Core;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// El oráculo del viaje (<see cref="OraculoDelViaje"/>) por el motor IMPERATIVO: <see cref="TripFlow"/> como siempre,
/// el que sirve <c>/v1/trips</c> al CMS.
/// </summary>
/// <remarks>
/// <para>Además de lo común, lo que sólo hace este motor: <b>cancelar un viaje ya confirmado con la penalidad</b> que pone
/// quien vende. Por la puerta eso es la operación posterior <c>viajes.cancelacion</c>, con acceso de personal (línea 006,
/// T027); hasta entonces estos casos viven aquí y no en la base.</para>
/// </remarks>
public sealed class TripCompensationTests : OraculoDelViaje
{
    private sealed class MemoriaSagas : ISagaStore<TripSaga>
    {
        private readonly Dictionary<string, TripSaga> _s = new(StringComparer.Ordinal);
        public TripSaga? Find(string id) => _s.GetValueOrDefault(id);
        public IReadOnlyList<TripSaga> WithPendingCompensations()
            => _s.Values.Where(x => x.IsUnwinding() && x.Pending().Count > 0).ToList();
        public IReadOnlyList<TripSaga> StartedBefore(DateTimeOffset limite)
            => _s.Values.Where(x => x.Status == SagaStatus.Running && x.StartedAtUtc < limite).ToList();
        public void Put(TripSaga saga) => _s[saga.Id] = saga;

        // Un fake en memoria no tiene caché que vaciar: la verdad ES el diccionario (#34).
        public void Invalidate() { }
    }

    /// <summary>El conductor imperativo: <see cref="TripFlow"/> tal cual, y la vista sale de su <see cref="TripSaga"/>.</summary>
    private sealed class ConductorImperativo(TripFlow flujo, MemoriaSagas sagas) : IConductorDelViaje
    {
        public string Soltar => ViajesCompensations.ReleaseBookingHold;
        public string Anular => ViajesCompensations.VoidPayment;
        public string Devolver => ViajesCompensations.RefundPayment;
        public string NoExiste => "viajes.trip_not_found";
        public string NoConfirmable => "viajes.not_confirmable";
        public string VentanaMala => "viajes.bad_window";

        public async Task<Result<VistaDelViaje>> BookAsync(Ref viajero, IReadOnlyList<TripItem> items, string id, CancellationToken ct)
            => (await flujo.BookAsync(viajero, items, id, ct)).Map(VistaDelViaje.De);

        public async Task<Result<VistaDelViaje>> ConfirmAsync(string id, CancellationToken ct)
            => (await flujo.ConfirmAsync(id, ct)).Map(VistaDelViaje.De);

        public async Task<Result<VistaDelViaje>> CancelAsync(string id, Money? retener, CancellationToken ct)
            => (await flujo.CancelAsync(id, retener, ct)).Map(VistaDelViaje.De);

        public Result<VistaDelViaje> Get(string id) => flujo.Get(id).Map(VistaDelViaje.De);

        public VistaDelViaje? Find(string id) => sagas.Find(id) is { } s ? VistaDelViaje.De(s) : null;
    }

    private protected override Contexto Nuevo(CapacidadesFalsas caps)
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
        var motor = new SagaEngine<TripSaga>(sagas, comp, aviso, ArriendoDePrueba.Nuevo(), vocabulario, reloj,
            NullLogger<SagaEngine<TripSaga>>.Instance);

        return new Contexto(
            new ConductorImperativo(new TripFlow(api, motor, reloj, NullLogger<TripFlow>.Instance), sagas), caps, reloj);
    }

    // ── Lo que sólo hace este motor: cancelar lo confirmado con la penalidad de quien vende ──

    /// <summary>
    /// Cancelar tras confirmar devuelve el TOTAL MENOS la penalidad que retenga la política.
    /// </summary>
    /// <remarks>
    /// <para>Es lo que el cableado destapó (HU #36, rebanada 2b): el hotel retiene una penalidad y este orquestador
    /// devolvía todo. Encender el modo <c>Bff</c> sin esto habría cambiado el monto devuelto en silencio.</para>
    ///
    /// <para><b>La penalidad llega calculada</b>, no se calcula acá: depende de la tarifa y de cuántos días falten, y eso
    /// lo sabe quien vendió. <c>Api.Booking</c> lo dice de frente — «qué se devuelve y a quién lo decide Api.Payments y
    /// lo ORDENA el BFF».</para>
    /// </remarks>
    [Fact]
    public async Task Cancelar_tras_confirmar_devuelve_el_TOTAL_MENOS_la_penalidad()
    {
        var ctx = Nuevo(FelizEstricto());
        await Reservar(ctx.Flow);
        await ctx.Flow.ConfirmAsync("viaje-1", CancellationToken.None);

        var r = await ctx.Flow.CancelAsync(
            "viaje-1", Money.Of(200000m, Money.Cop), CancellationToken.None);

        Assert.True(r.IsOk);
        Assert.Equal(SagaStatus.Compensated, r.Value.Status);
        Assert.Empty(r.Value.Pending());

        // 1.200.000 devolvibles menos 200.000 de penalidad.
        var devolucion = ctx.Caps.Llamadas.Single(l => l.Path.EndsWith("/refund", StringComparison.Ordinal));
        Assert.Contains("1000000", devolucion.Body!, StringComparison.Ordinal);
        Assert.DoesNotContain("1200000", devolucion.Body!, StringComparison.Ordinal);
    }

    /// <summary>
    /// Si la devolución falla al cancelar, queda ANOTADA para el barrido, no en un log.
    /// </summary>
    /// <remarks>
    /// Es el único trozo de la cancelación que necesita la máquina: soltar una reserva que no se pudo soltar se ve y se
    /// arregla; una devolución perdida se la queda alguien.
    /// </remarks>
    [Fact]
    public async Task Si_la_devolucion_falla_al_cancelar_queda_pendiente()
    {
        var caps = FelizEstricto();
        var ctx = Nuevo(caps);
        await Reservar(ctx.Flow);
        await ctx.Flow.ConfirmAsync("viaje-1", CancellationToken.None);

        caps.Falla("POST /v1/payments/pg1/refund", HttpStatusCode.ServiceUnavailable, "payments.unreachable");

        var r = await ctx.Flow.CancelAsync("viaje-1", null, CancellationToken.None);

        Assert.True(r.IsOk);
        // Las reservas SÍ se soltaron: no se corta el resto por una devolución caída.
        Assert.Equal(1, ctx.Caps.Veces("POST", "/v1/reservations/res-1/cancel"));
        Assert.Equal(1, ctx.Caps.Veces("POST", "/v1/reservations/res-3/cancel"));

        // Y la plata quedó anotada, que es lo que hace que el barrido vuelva por ella.
        var pendientes = r.Value.Pending();
        Assert.Single(pendientes);
        Assert.Equal(ViajesCompensations.RefundPayment, pendientes[0].Kind);
        Assert.Contains("devolución", r.Value.LastError!, StringComparison.Ordinal);
    }

    [Fact] // una penalidad que no cabe en el viaje es un error de quien la calculó.
    public async Task Una_penalidad_mayor_que_el_viaje_se_rechaza()
    {
        var ctx = Nuevo(FelizEstricto());
        await Reservar(ctx.Flow);

        var r = await ctx.Flow.CancelAsync(
            "viaje-1", Money.Of(9_000_000m, Money.Cop), CancellationToken.None);

        Assert.False(r.IsOk);
        Assert.Equal("viajes.bad_penalty", r.Rejection!.Code);
        Assert.Equal(0, ctx.Caps.Veces("POST", "/release"));   // no se deshizo nada
    }
}
