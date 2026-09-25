using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Synergos.Bff.Core;
using Synergos.Bff.Salud.Clients;
using Synergos.Bff.Salud.Domain;
using Synergos.Bff.Viajes.Clients;
using Synergos.Bff.Viajes.Domain;
using Synergos.Core;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// Que CADA FLUJO pase por <see cref="SagaEngine{TSaga}.Abrir"/> (defecto #166).
/// </summary>
/// <remarks>
/// <para><b>Por qué hacía falta un test por flujo teniendo ocho sobre la regla.</b>
/// <c>LlaveDeIdempotenciaTests</c> cubre el defecto #41 a fondo —qué desbloquea y qué no, el
/// reintento del reintento, el lazo de las cien— y lo hace contra el MOTOR. Su propio
/// <c>&lt;remarks&gt;</c> dice por qué: «la regla es del motor, y los dos flujos la comparten
/// justamente para que no haya dos».</para>
///
/// <para>Esa frase era cierta al escribirse —<c>Bff.Tienda</c> y <c>Bff.Eventos</c>— y dejó de
/// serlo sin que nada lo cruzara: <c>Bff.Salud</c> y <c>Bff.Viajes</c> se escribieron después y
/// hacían <c>Find(sagaId)</c>, devolviendo lo que hubiera <b>incluida una saga
/// <see cref="SagaStatus.Compensated"/></b>. O sea el defecto #41 vivo en dos de los cuatro,
/// mientras el motor que lo arregla era de lo mejor probado del árbol.</para>
///
/// <para>Es la regla 5 del repo hermano: <b>un test que llama al MÉTODO no ve que falte el
/// llamador</b>. Y la cobertura excelente del motor es justo lo que daba sensación de estar
/// cubierto.</para>
///
/// <para><b>El encierro era alcanzable, y eso es lo que lo vuelve un defecto y no una fealdad:</b>
/// el <c>sagaId</c> ES la llave de idempotencia —lo dice el endpoint— y la llave se deriva de lo
/// que se agenda o se reserva, así que nunca cambia. A quien se le caía el cobro le quedaba esa
/// cita —o ese viaje— encerrada para siempre, con el borde contestando <c>201</c>.</para>
///
/// <para><b>El discriminador, y por qué es éste:</b> se afirma que el resultado <b>nunca es una
/// saga <c>Compensated</c></b>. Con el defecto lo es siempre; con el arreglo, nunca — y no depende
/// de cuán lejos llegue el flujo después de <c>Abrir</c>, que es lo que permite probarlo con las
/// capacidades caídas en vez de con un guion feliz de veinte respuestas. Afirmar «el id es
/// <c>K#2</c>» habría atado el test a dónde cada flujo escribe su primer <c>Put</c>, que es
/// distinto en los dos y no es la propiedad.</para>
///
/// <para><b>Y va el espejo</b>, porque sin él «no devuelve la muerta» lo pasaría también un flujo
/// que hubiera perdido la idempotencia entera: sobre una saga VIVA la misma llave sigue
/// devolviendo ESA.</para>
/// </remarks>
public sealed class ReintentoTrasDeshacerTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    /// <summary>La llave, con la forma de la de verdad: derivada de lo que se pide.</summary>
    private const string Llave = "salud:p1:d1:2026-10-01T09:00:00Z";

    // ── El árbol mínimo ─────────────────────────────────────────────────────

    private sealed class RelojFalso(DateTimeOffset ahora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ahora;
    }

    /// <summary>Un almacén en memoria. Acá no se prueba durabilidad: se prueba la decisión.</summary>
    private sealed class MemoriaSagas<TSaga> : ISagaStore<TSaga> where TSaga : class, ISaga<TSaga>
    {
        private readonly Dictionary<string, TSaga> _sagas = new(StringComparer.Ordinal);

        public TSaga? Find(string id) => _sagas.TryGetValue(id, out var s) ? s : null;
        public void Put(TSaga saga) => _sagas[saga.Id] = saga;
        public void Invalidate() { }
        public IReadOnlyList<TSaga> WithPendingCompensations() => Array.Empty<TSaga>();
        public IReadOnlyList<TSaga> StartedBefore(DateTimeOffset l) => Array.Empty<TSaga>();
    }

    /// <summary>
    /// Todas las capacidades caídas.
    /// </summary>
    /// <remarks>
    /// A propósito: lo que se prueba es lo que pasa ANTES de tocar la red. Con las capacidades
    /// caídas, un flujo que pasó por <c>Abrir</c> devuelve un rechazo y uno que no pasó devuelve
    /// la saga muerta en <c>Ok</c> — la diferencia se lee sin montar un guion feliz.
    /// </remarks>
    private sealed class TodoCaido : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("""{"code":"caido","message":"la capacidad no responde"}"""),
            });
    }

    /// <remarks>
    /// El handler se RECIBE y no se crea acá: una fábrica dueña de un <c>IDisposable</c> tendría
    /// que ser disposable ella (CA1001), y con `TreatWarningsAsErrors` eso no compila. Es la misma
    /// forma que usan los tests de al lado.
    /// </remarks>
    private sealed class FabricaFalsa(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => new(handler, disposeHandler: false) { BaseAddress = new Uri("http://capacidad.local/") };
    }

    private static SagaEngine<TSaga> Motor<TSaga>(
        ISagaStore<TSaga> store, ICompensationExecutor<TSaga> ejecutor, IHttpClientFactory fabrica,
        string prefijo, string sujeto, TimeProvider reloj)
        where TSaga : class, ISaga<TSaga>
    {
        var vocabulario = new SagaVocabulary(prefijo, sujeto);
        return new SagaEngine<TSaga>(
            store,
            new Compensator<TSaga>(ejecutor, reloj, NullLogger<Compensator<TSaga>>.Instance),
            new CompensationAlert(fabrica, vocabulario, Options.Create(new AlertOptions())),
            ArriendoDePrueba.Nuevo(),
            vocabulario,
            reloj,
            NullLogger<SagaEngine<TSaga>>.Instance);
    }

    // ── Salud ───────────────────────────────────────────────────────────────

    private static (AppointmentFlow Flujo, MemoriaSagas<AppointmentSaga> Sagas) Salud()
    {
        var sagas = new MemoriaSagas<AppointmentSaga>();
        var reloj = new RelojFalso(Ahora);
        var fabrica = new FabricaFalsa(new TodoCaido());
        var api = new SaludCapabilities(fabrica);
        var motor = Motor(sagas, new SaludCompensationExecutor(api), fabrica, "salud", "la cita", reloj);
        return (new AppointmentFlow(api, motor, reloj, NullLogger<AppointmentFlow>.Instance), sagas);
    }

    private static AppointmentSaga Cita(string id, SagaStatus estado) => new(
        id,
        Ref.Create("salud.paciente", "p1"),
        Ref.Create("salud.profesional", "d1"),
        TimeWindow.Of(Ahora.AddDays(6), Ahora.AddDays(6).AddMinutes(30)),
        estado,
        null, null, null,
        Money.Zero(Money.Cop),
        Array.Empty<Compensation>(),
        null,
        Ahora.AddDays(-1));

    private static Task<Result<AppointmentSaga>> Agendar(AppointmentFlow flujo, string llave) =>
        flujo.ScheduleAsync(
            Ref.Create("salud.paciente", "p1"),
            Ref.Create("salud.profesional", "d1"),
            "rec-1",
            Ref.Create("salud.servicio", "consulta"),
            TimeWindow.Of(Ahora.AddDays(6), Ahora.AddDays(6).AddMinutes(30)),
            llave,
            CancellationToken.None);

    [Fact]
    public async Task Salud_una_cita_DESHECHA_no_encierra_a_quien_la_pidio()
    {
        var (flujo, sagas) = Salud();
        sagas.Put(Cita(Llave, SagaStatus.Compensated));

        var r = await Agendar(flujo, Llave);

        // Con el defecto: Ok con la saga muerta, y el borde contestando 201 sobre una cita que
        // no existe. La cita quedaba inalcanzable para siempre, porque la llave nunca cambia.
        Assert.False(r.IsOk && r.Value.Status == SagaStatus.Compensated);
    }

    [Theory]
    [InlineData(SagaStatus.Running)]
    [InlineData(SagaStatus.Completed)]
    [InlineData(SagaStatus.Compensating)]
    [InlineData(SagaStatus.CompensationFailed)]
    public async Task Salud_sobre_una_cita_VIVA_la_misma_llave_sigue_devolviendo_ESA(SagaStatus estado)
    {
        // El espejo. Sin esto, «no devuelve la muerta» también lo pasaría un flujo que hubiera
        // perdido la idempotencia entera — y ahí un reintento por timeout tomaría un segundo cupo.
        var (flujo, sagas) = Salud();
        sagas.Put(Cita(Llave, estado));

        var r = await Agendar(flujo, Llave);

        Assert.True(r.IsOk);
        Assert.Equal(Llave, r.Value.Id);
        Assert.Equal(estado, r.Value.Status);
    }

    // ── Viajes ──────────────────────────────────────────────────────────────

    private static (TripFlow Flujo, MemoriaSagas<TripSaga> Sagas) Viajes()
    {
        var sagas = new MemoriaSagas<TripSaga>();
        var reloj = new RelojFalso(Ahora);
        var fabrica = new FabricaFalsa(new TodoCaido());
        var api = new ViajesCapabilities(fabrica);
        var motor = Motor(sagas, new ViajesCompensationExecutor(api), fabrica,
            "viajes", "la reserva de viaje", reloj);
        return (new TripFlow(api, motor, reloj, NullLogger<TripFlow>.Instance), sagas);
    }

    private static TripSaga Viaje(string id, SagaStatus estado) => new(
        id,
        Ref.Create("viajes.viajero", "v1"),
        estado,
        Array.Empty<ItemHold>(),
        null,
        Money.Zero(Money.Cop),
        Money.Zero(Money.Cop),
        Array.Empty<Compensation>(),
        null,
        Ahora.AddDays(-1));

    private static Task<Result<TripSaga>> Reservar(TripFlow flujo, string llave) =>
        flujo.BookAsync(
            Ref.Create("viajes.viajero", "v1"),
            new[]
            {
                new TripItem("hotel/caribe/DBL", "Hotel Caribe, doble",
                    Ahora.AddDays(30), Ahora.AddDays(32)),
            },
            llave,
            CancellationToken.None);

    [Fact]
    public async Task Viajes_un_viaje_DESHECHO_no_encierra_a_quien_lo_pidio()
    {
        var (flujo, sagas) = Viajes();
        sagas.Put(Viaje(Llave, SagaStatus.Compensated));

        var r = await Reservar(flujo, Llave);

        Assert.False(r.IsOk && r.Value.Status == SagaStatus.Compensated);
    }

    [Theory]
    [InlineData(SagaStatus.Running)]
    [InlineData(SagaStatus.Completed)]
    [InlineData(SagaStatus.Compensating)]
    [InlineData(SagaStatus.CompensationFailed)]
    public async Task Viajes_sobre_un_viaje_VIVO_la_misma_llave_sigue_devolviendo_ESE(SagaStatus estado)
    {
        var (flujo, sagas) = Viajes();
        sagas.Put(Viaje(Llave, estado));

        var r = await Reservar(flujo, Llave);

        Assert.True(r.IsOk);
        Assert.Equal(Llave, r.Value.Id);
        Assert.Equal(estado, r.Value.Status);
    }
}
