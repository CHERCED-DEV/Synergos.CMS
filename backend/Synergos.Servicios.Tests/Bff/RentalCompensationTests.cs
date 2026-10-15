using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Synergos.Bff.Alquiler.Clients;
using Synergos.Bff.Alquiler.Contracts;
using Synergos.Bff.Alquiler.Domain;
using Synergos.Bff.Core;
using Synergos.Core;
using static Synergos.CMS.Tests.Bff.DobleDeAlquiler;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// El alquiler que se deshace, y el que se cierra días después (HU #147), por el flujo IMPERATIVO
/// (<c>RentalFlow</c>, las rutas <c>/v1/rentals*</c> que usa la ruta directa del CMS).
/// </summary>
/// <remarks>
/// <para>Los casos de la reserva y de lo que pasa después viven en <see cref="RentalLiquidacionBase"/> y los
/// corre también el flujo declarado, sobre el mismo doble (línea 005, research R12). Aquí quedan los que sólo
/// tienen sentido en este motor: la proyección que lee el CMS por la ruta directa, los sujetos de cada cobro y
/// el total que llega calculado.</para>
/// </remarks>
public sealed class RentalCompensationTests : RentalLiquidacionBase
{
    protected override ConductorDeRenta Nuevo(CapacidadesFalsas caps) => new ConductorImperativo(caps);

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

    /// <summary>El conductor de <c>RentalFlow</c>, construido a mano como siempre.</summary>
    private sealed class ConductorImperativo : ConductorDeRenta
    {
        public ConductorImperativo(CapacidadesFalsas caps) : base(caps)
        {
            Sagas = new MemoriaSagas();
            Reloj = new RelojFalso(Ahora);
            var fabrica = new FabricaFalsa(caps);
            var api = new AlquilerCapabilities(fabrica);
            var vocabulario = new SagaVocabulary("alquiler", "el alquiler de equipos");
            var comp = new Compensator<RentalSaga>(
                new AlquilerCompensationExecutor(api), Reloj, NullLogger<Compensator<RentalSaga>>.Instance);
            var aviso = new CompensationAlert(fabrica, vocabulario, Options.Create(new AlertOptions
            {
                ToKind = "alquiler.guardia",
                ToId = "operaciones",
                Address = "guardia@ejemplo.co",
                TemplateKey = "alquiler.compensacion.colgada",
            }));
            var motor = new SagaEngine<RentalSaga>(Sagas, comp, aviso, ArriendoDePrueba.Nuevo(), vocabulario, Reloj,
                NullLogger<SagaEngine<RentalSaga>>.Instance);

            Flow = new RentalFlow(api, motor, Reloj, NullLogger<RentalFlow>.Instance);
        }

        public RentalFlow Flow { get; }
        public MemoriaSagas Sagas { get; }
        public RelojFalso Reloj { get; }

        public override async Task<VistaDeRenta> ReservarAsync(string id = "alq-1", int unidades = 1, decimal garantia = 400000m)
            => Vista(await Flow.ReserveAsync(
                Arrendatario, "andamio-6m", unidades, Semana, Cop(350000m), Cop(garantia), id, CancellationToken.None));

        public override async Task<VistaDeRenta> DevolverAsync(string id, decimal danio)
            => Vista(await Flow.ReturnAsync(id, Cop(danio), CancellationToken.None));

        public override async Task<VistaDeRenta> CancelarAsync(string id, decimal penalidad)
            => Vista(await Flow.CancelAsync(id, Cop(penalidad), CancellationToken.None));

        public override SagaStatus? Estado(string id) => Sagas.Find(id)?.Status;

        public override int Pendientes(string id) => Sagas.Find(id)?.Pending().Count ?? 0;

        public override void Avanzar(TimeSpan cuanto) => Reloj.Avanzar(cuanto);

        public override bool HayCandidatasAlBarrido()
            => Sagas.StartedBefore(Reloj.GetUtcNow()).Count > 0 || Sagas.WithPendingCompensations().Count > 0;

        private static VistaDeRenta Vista(Result<RentalSaga> r)
        {
            if (!r.IsOk)
            {
                return new VistaDeRenta(false, r.Rejection!.Code, string.Empty, null, null, [], null, null, 0m);
            }

            var s = r.Value;
            var proyectada = RentalResponse.From(s);
            return new VistaDeRenta(true, null, s.Id, s.DepositPaymentId, s.RentalPaymentId,
                s.Holds.Select(h => (h.HoldId, h.ResourceId)).ToList(), proyectada.State, proyectada.DepositHeld,
                s.DamageCharged.Amount);
        }
    }

    private static ConductorImperativo Imperativo(CapacidadesFalsas caps) => new(caps);

    /// <summary>La garantía se retiene ANTES de cobrar el alquiler.</summary>
    /// <remarks>
    /// <para>Al revés se cobraría por un equipo que no va a salir, y deshacerlo costaría una devolución
    /// en vez de una anulación. El orden se lee en qué sujeto nombra cada autorización.</para>
    ///
    /// <para>Sólo en este motor: el declarado autoriza los dos cobros con el origen de la saga, y se
    /// distinguen por la llave (NN-13 de la línea 005). Su orden lo fija la base, por los identificadores.</para>
    /// </remarks>
    [Fact]
    public async Task La_garantia_va_antes_que_el_cobro()
    {
        var c = Imperativo(FelizEstricto());

        Assert.True((await c.ReservarAsync()).Ok);

        var autorizaciones = c.Caps.Llamadas
            .Where(l => l.Method == "POST" && l.Path.EndsWith("/v1/payments", StringComparison.Ordinal))
            .Select(l => l.Body!)
            .ToList();

        Assert.Equal(2, autorizaciones.Count);
        Assert.Contains("alquiler.garantia", autorizaciones[0], StringComparison.Ordinal);
        Assert.Contains("alquiler.alquiler", autorizaciones[1], StringComparison.Ordinal);
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
        var c = Imperativo(caps);

        Assert.False((await c.ReservarAsync()).Ok);

        var muerta = c.Sagas.Find("alq-1")!;
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
        var c = Imperativo(FelizEstricto());
        Assert.True((await c.ReservarAsync()).Ok);

        var colgada = c.Sagas.Find("alq-1")! with { Status = SagaStatus.CompensationFailed };

        var proyectada = RentalResponse.From(colgada);
        Assert.Equal("failed", proyectada.State);
        Assert.Null(proyectada.DepositHeld);
    }

    /// <summary>Un total de cero se rechaza; no se acepta como «gratis».</summary>
    /// <remarks>
    /// <para>Cero es un precio VÁLIDO que no falla en ninguna parte hasta que alguien mira la factura
    /// (<c>a_failed_tryparse_is_not_a_value</c>). Y se rechaza <b>antes</b> de apartar nada: se
    /// comprueba que no hubo ni una llamada.</para>
    ///
    /// <para>Sólo en este motor: aquí el total LLEGA calculado. En el declarado lo cotiza el servidor con
    /// la oferta publicada, y la oferta no admite un valor del día de cero (<c>OfertaDeAlquilerTests</c>).</para>
    /// </remarks>
    [Fact]
    public async Task Un_alquiler_de_cero_se_rechaza_sin_tocar_ninguna_capacidad()
    {
        var c = Imperativo(FelizEstricto());

        var r = await c.Flow.ReserveAsync(
            Arrendatario, "andamio-6m", 1, Semana, Cop(0m), Cop(400000m), "alq-1", CancellationToken.None);

        Assert.False(r.IsOk);
        Assert.Equal("alquiler.bad_total", r.Rejection!.Code);
        Assert.Empty(c.Caps.Llamadas);
    }
}
