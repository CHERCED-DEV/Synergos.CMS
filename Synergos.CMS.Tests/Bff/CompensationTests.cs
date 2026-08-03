using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Synergos.Bff.Salud.Clients;
using Synergos.Bff.Salud.Domain;
using Synergos.Bff.Salud.Storage;
using Synergos.Core;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// Cubre la compensación cruzada de <see cref="AppointmentFlow"/>.
/// </summary>
/// <remarks>
/// <para>Es lo único que este orquestador aporta y que ninguna capacidad puede aportar. Los
/// caminos felices están probados en cada capacidad; lo que <b>solo</b> se puede probar acá es
/// qué pasa cuando el paso tres falla después de que el dos movió plata.</para>
///
/// <para>Las capacidades se simulan con un <see cref="HttpMessageHandler"/> guionado: es la
/// única forma de que un test provoque <i>a voluntad</i> que Booking se caiga justo entre el
/// cobro y la confirmación. Con procesos reales ese instante no se puede elegir.</para>
/// </remarks>
public sealed class CompensationTests
{
    private sealed class RelojFalso : TimeProvider
    {
        private DateTimeOffset _now;
        public RelojFalso(DateTimeOffset inicio) => _now = inicio;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Avanzar(TimeSpan d) => _now += d;
    }

    /// <summary>Capacidades guionadas: cada ruta responde lo que se le diga, y cuenta las llamadas.</summary>
    private sealed class CapacidadesFalsas : HttpMessageHandler
    {
        private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _rutas = new(StringComparer.Ordinal);

        public List<(string Method, string Path, string? Key)> Llamadas { get; } = new();

        /// <summary>Guiona una ruta. La clave es <c>METHOD /ruta</c> con comodín <c>*</c> al final.</summary>
        public CapacidadesFalsas Cuando(string patron, Func<HttpRequestMessage, HttpResponseMessage> responde)
        {
            _rutas[patron] = responde;
            return this;
        }

        public CapacidadesFalsas Ok(string patron, string json)
            => Cuando(patron, _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            });

        public CapacidadesFalsas Falla(string patron, HttpStatusCode codigo, string code)
            => Cuando(patron, _ => new HttpResponseMessage(codigo)
            {
                Content = new StringContent($$"""{"code":"{{code}}","detail":"guionado"}""",
                    System.Text.Encoding.UTF8, "application/problem+json"),
            });

        public CapacidadesFalsas Caida(string patron)
            => Cuando(patron, _ => throw new HttpRequestException("guionado: caída"));

        public int Veces(string method, string pathContiene)
            => Llamadas.Count(l => l.Method == method && l.Path.Contains(pathContiene, StringComparison.Ordinal));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var clave = $"{request.Method.Method} {path}";
            lock (Llamadas)
            {
                Llamadas.Add((request.Method.Method, path,
                    request.Headers.TryGetValues("Idempotency-Key", out var v) ? v.FirstOrDefault() : null));
            }

            foreach (var (patron, responde) in _rutas)
            {
                if (Coincide(patron, clave)) return Task.FromResult(responde(request));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("""{"code":"stub.no_route","detail":"sin guion"}""",
                    System.Text.Encoding.UTF8, "application/problem+json"),
            });
        }

        private static bool Coincide(string patron, string clave)
            => patron.EndsWith('*')
                ? clave.StartsWith(patron[..^1], StringComparison.Ordinal)
                : string.Equals(patron, clave, StringComparison.Ordinal);
    }

    private sealed class FabricaFalsa : IHttpClientFactory
    {
        private readonly CapacidadesFalsas _handler;
        public FabricaFalsa(CapacidadesFalsas h) => _handler = h;
        public HttpClient CreateClient(string name)
            => new(_handler, disposeHandler: false) { BaseAddress = new Uri("http://capacidad.local/") };
    }

    private sealed class MemoriaSagas : ISagaStore
    {
        private readonly Dictionary<string, AppointmentSaga> _s = new(StringComparer.Ordinal);
        public AppointmentSaga? Find(string id) => _s.GetValueOrDefault(id);
        public IReadOnlyList<AppointmentSaga> WithPendingCompensations() => _s.Values.Where(x => x.Pending.Count > 0).ToList();
        public IReadOnlyList<AppointmentSaga> ForPatient(Ref patient) => _s.Values.Where(x => x.Patient == patient).ToList();
        public void Put(AppointmentSaga saga) => _s[saga.Id] = saga;
    }

    private static readonly DateTimeOffset Ahora = new(2026, 3, 2, 10, 0, 0, TimeSpan.Zero);
    private static readonly Ref Paciente = Ref.Create("salud.paciente", "p-1");
    private static readonly Ref Profesional = Ref.Create("salud.profesional", "dr-1");
    private static readonly Ref Servicio = Ref.Create("salud.servicio", "consulta");
    private static readonly TimeWindow Cita = TimeWindow.Of(Ahora.AddDays(1), Ahora.AddDays(1).AddMinutes(30));

    private sealed record Contexto(AppointmentFlow Flow, CapacidadesFalsas Caps, MemoriaSagas Sagas, RelojFalso Reloj);

    /// <summary>Guion del camino feliz: todo responde bien.</summary>
    private static CapacidadesFalsas Feliz() => new CapacidadesFalsas()
        .Ok("POST /v1/grants/check", """{"id":"g1","active":true}""")
        .Ok("POST /v1/quotes", """{"total":{"amount":50000,"currency":"COP"}}""")
        .Ok("POST /v1/holds", """{"id":"h1","resourceId":"r1","expiresAt":"2026-03-03T10:10:00+00:00"}""")
        .Ok("POST /v1/payments/pg1/capture", """{"id":"pg1","status":"Captured","amount":{"amount":50000,"currency":"COP"},"refundable":{"amount":50000,"currency":"COP"}}""")
        .Ok("POST /v1/payments/pg1/void", """{"id":"pg1","status":"Voided","amount":{"amount":50000,"currency":"COP"},"refundable":{"amount":0,"currency":"COP"}}""")
        .Ok("POST /v1/payments/pg1/refund", """{"id":"pg1","status":"Captured","amount":{"amount":50000,"currency":"COP"},"refundable":{"amount":0,"currency":"COP"}}""")
        .Ok("GET /v1/payments/pg1", """{"id":"pg1","status":"Captured","amount":{"amount":50000,"currency":"COP"},"refundable":{"amount":50000,"currency":"COP"}}""")
        .Ok("POST /v1/payments", """{"id":"pg1","status":"Authorized","amount":{"amount":50000,"currency":"COP"},"refundable":{"amount":0,"currency":"COP"}}""")
        .Ok("POST /v1/holds/h1/release", """{"id":"h1","resourceId":"r1","expiresAt":"2026-03-03T10:10:00+00:00"}""")
        .Ok("POST /v1/holds/h1/confirm", """{"id":"res1","status":"Confirmed"}""");

    private static Contexto Nuevo(CapacidadesFalsas caps)
    {
        var sagas = new MemoriaSagas();
        var reloj = new RelojFalso(Ahora);
        var api = new SaludCapabilities(new FabricaFalsa(caps));
        var comp = new Compensator(api, reloj, NullLogger<Compensator>.Instance);
        return new Contexto(new AppointmentFlow(api, sagas, comp, reloj, NullLogger<AppointmentFlow>.Instance), caps, sagas, reloj);
    }

    private static Task<Result<AppointmentSaga>> Agendar(AppointmentFlow flow, string id = "saga-1")
        => flow.ScheduleAsync(Paciente, Profesional, "r1", Servicio, Cita, id, CancellationToken.None);

    // ── El camino feliz, para tener contra qué contrastar ────────────────────

    [Fact]
    public async Task El_camino_feliz_deja_la_cita_confirmada_y_nada_pendiente()
    {
        var (flow, _, _, _) = Nuevo(Feliz());

        var agendada = await Agendar(flow);
        var confirmada = await flow.ConfirmAsync(agendada.Value.Id, CancellationToken.None);

        Assert.Equal(SagaStatus.Confirmed, confirmada.Value.Status);
        Assert.Equal("res1", confirmada.Value.ReservationId);
        Assert.Empty(confirmada.Value.Pending);
    }

    // ── El caso que justifica todo esto ──────────────────────────────────────

    [Fact]
    public async Task Si_la_confirmacion_falla_DESPUES_de_capturar_se_DEVUELVE_la_plata()
    {
        // Es el escenario entero: se cobró y la cita no existe. Sin compensación, el paciente
        // paga por nada y el sistema no sabe que le debe.
        var caps = Feliz().Falla("POST /v1/holds/h1/confirm", HttpStatusCode.Gone, "booking.hold_expired");
        var (flow, _, _, _) = Nuevo(caps);

        var agendada = await Agendar(flow);
        var confirmada = await flow.ConfirmAsync(agendada.Value.Id, CancellationToken.None);

        Assert.False(confirmada.IsOk);
        Assert.Equal("booking.hold_expired", confirmada.Rejection!.Code);
        Assert.Equal(1, caps.Veces("POST", "/refund"));

        var saga = flow.Get(agendada.Value.Id).Value;
        Assert.Equal(SagaStatus.Compensated, saga.Status);
        Assert.Empty(saga.Pending);
    }

    [Fact]
    public async Task Tras_capturar_la_compensacion_pasa_de_LIBERAR_a_DEVOLVER()
    {
        // Si siguiera siendo "liberar la autorización", Payments la rechazaría con
        // already_captured en cada intento y la compensación quedaría colgada para siempre.
        var caps = Feliz().Falla("POST /v1/holds/h1/confirm", HttpStatusCode.Gone, "booking.hold_expired");
        var (flow, _, _, _) = Nuevo(caps);

        var agendada = await Agendar(flow);
        await flow.ConfirmAsync(agendada.Value.Id, CancellationToken.None);

        Assert.Equal(0, caps.Veces("POST", "/void"));
        Assert.Equal(1, caps.Veces("POST", "/refund"));
    }

    [Fact]
    public async Task Si_falla_ANTES_de_capturar_se_LIBERA_la_autorizacion_y_no_se_devuelve()
    {
        // Acá compensar es barato porque no se movió plata. Es exactamente para lo que existen
        // las dos fases.
        var caps = Feliz().Falla("POST /v1/payments/pg1/capture", HttpStatusCode.Conflict, "payments.declined");
        var (flow, _, _, _) = Nuevo(caps);

        var agendada = await Agendar(flow);
        await flow.ConfirmAsync(agendada.Value.Id, CancellationToken.None);

        Assert.Equal(1, caps.Veces("POST", "/void"));
        Assert.Equal(0, caps.Veces("POST", "/refund"));
        Assert.Equal(SagaStatus.Compensated, flow.Get(agendada.Value.Id).Value.Status);
    }

    // ── Cuando la compensación TAMBIÉN falla ─────────────────────────────────

    [Fact]
    public async Task Una_compensacion_que_falla_queda_PENDIENTE_y_no_se_da_por_hecha()
    {
        // Darla por buena sin ejecutarla es plata cobrada sin servicio, y nadie se entera.
        var caps = Feliz()
            .Falla("POST /v1/holds/h1/confirm", HttpStatusCode.Gone, "booking.hold_expired")
            .Caida("GET /v1/payments/pg1");
        var (flow, _, _, _) = Nuevo(caps);

        var agendada = await Agendar(flow);
        await flow.ConfirmAsync(agendada.Value.Id, CancellationToken.None);

        var saga = flow.Get(agendada.Value.Id).Value;
        Assert.Equal(SagaStatus.Compensating, saga.Status);
        Assert.Contains(saga.Pending, c => c.Kind == CompensationKind.RefundPayment);
        Assert.Single(flow.PendingCompensations());
    }

    [Fact]
    public async Task El_reintento_RESPETA_el_retroceso_y_no_martillea()
    {
        // La causa habitual de que una compensación falle es que la capacidad está caída, y
        // martillearla no la levanta: solo alarga la caída.
        var caps = Feliz()
            .Falla("POST /v1/holds/h1/confirm", HttpStatusCode.Gone, "booking.hold_expired")
            .Caida("GET /v1/payments/pg1");
        var (flow, _, _, reloj) = Nuevo(caps);

        var agendada = await Agendar(flow);
        await flow.ConfirmAsync(agendada.Value.Id, CancellationToken.None);
        var trasElPrimero = caps.Veces("GET", "/v1/payments/pg1");

        await flow.CompensateAsync(agendada.Value.Id, "barrido", CancellationToken.None);
        var sinEsperar = caps.Veces("GET", "/v1/payments/pg1");

        reloj.Avanzar(Compensator.Backoff(1) + TimeSpan.FromSeconds(1));
        await flow.CompensateAsync(agendada.Value.Id, "barrido", CancellationToken.None);
        var trasEsperar = caps.Veces("GET", "/v1/payments/pg1");

        Assert.Equal(trasElPrimero, sinEsperar);
        Assert.True(trasEsperar > sinEsperar, "pasado el retroceso, el barrido tenía que reintentar");
    }

    [Fact]
    public async Task El_barrido_COMPLETA_la_compensacion_cuando_la_capacidad_vuelve()
    {
        // Es la razón de ser del barrido: compensar en línea falla justo cuando la capacidad
        // está caída, que es la causa habitual de que el flujo fallara.
        var caps = Feliz()
            .Falla("POST /v1/holds/h1/confirm", HttpStatusCode.Gone, "booking.hold_expired")
            .Caida("GET /v1/payments/pg1");
        var (flow, _, _, reloj) = Nuevo(caps);

        var agendada = await Agendar(flow);
        await flow.ConfirmAsync(agendada.Value.Id, CancellationToken.None);
        Assert.Equal(SagaStatus.Compensating, flow.Get(agendada.Value.Id).Value.Status);

        // Payments vuelve.
        caps.Ok("GET /v1/payments/pg1", """{"id":"pg1","status":"Captured","amount":{"amount":50000,"currency":"COP"},"refundable":{"amount":50000,"currency":"COP"}}""");
        reloj.Avanzar(Compensator.Backoff(1) + TimeSpan.FromSeconds(1));
        await flow.CompensateAsync(agendada.Value.Id, "barrido", CancellationToken.None);

        var saga = flow.Get(agendada.Value.Id).Value;
        Assert.Equal(SagaStatus.Compensated, saga.Status);
        Assert.Empty(flow.PendingCompensations());
    }

    [Fact]
    public async Task Tras_agotar_los_intentos_la_saga_queda_marcada_para_UNA_PERSONA()
    {
        // Rendirse en silencio sería lo peor: la plata sigue cobrada y el rastro dice
        // "compensado".
        var caps = Feliz()
            .Falla("POST /v1/holds/h1/confirm", HttpStatusCode.Gone, "booking.hold_expired")
            .Caida("GET /v1/payments/pg1");
        var (flow, _, _, reloj) = Nuevo(caps);

        var agendada = await Agendar(flow);
        await flow.ConfirmAsync(agendada.Value.Id, CancellationToken.None);

        for (var i = 1; i < Compensator.MaxAttempts + 2; i++)
        {
            reloj.Avanzar(Compensator.Backoff(i) + TimeSpan.FromSeconds(1));
            await flow.CompensateAsync(agendada.Value.Id, "barrido", CancellationToken.None);
        }

        var saga = flow.Get(agendada.Value.Id).Value;
        Assert.Equal(SagaStatus.CompensationFailed, saga.Status);
        Assert.Contains(saga.Pending, c => c.Attempts >= Compensator.MaxAttempts);
    }

    [Fact]
    public async Task Una_devolucion_YA_HECHA_se_da_por_cumplida_y_no_devuelve_dos_veces()
    {
        // El caso del reintento donde la devolución sí había salido pero no llegó a anotarse.
        // Sin esto, el barrido devolvería otra vez — y Payments lo rechazaría, dejando la
        // compensación colgada sobre algo que ya estaba resuelto.
        var caps = Feliz()
            .Falla("POST /v1/holds/h1/confirm", HttpStatusCode.Gone, "booking.hold_expired")
            .Ok("GET /v1/payments/pg1", """{"id":"pg1","status":"Captured","amount":{"amount":50000,"currency":"COP"},"refundable":{"amount":0,"currency":"COP"}}""");
        var (flow, _, _, _) = Nuevo(caps);

        var agendada = await Agendar(flow);
        await flow.ConfirmAsync(agendada.Value.Id, CancellationToken.None);

        Assert.Equal(0, caps.Veces("POST", "/refund"));
        Assert.Equal(SagaStatus.Compensated, flow.Get(agendada.Value.Id).Value.Status);
    }

    // ── Idempotencia y orden ─────────────────────────────────────────────────

    [Fact]
    public async Task Las_llaves_son_DETERMINISTAS_y_derivan_de_la_saga()
    {
        // Es lo que hace que reintentar un paso SEA la recuperación: la capacidad reconoce la
        // llave y devuelve lo que ya hizo. Sin eso, tras una caída entre "cobré" y "lo anoté" no
        // habría manera de averiguar si el cobro salió.
        var (flow, caps, _, _) = Nuevo(Feliz());

        var agendada = await Agendar(flow, "saga-fija");
        await flow.ConfirmAsync(agendada.Value.Id, CancellationToken.None);

        var llaves = caps.Llamadas.Where(l => l.Key is not null).Select(l => l.Key!).ToList();

        Assert.Contains(llaves, k => k == IdempotencyKey.From("saga-fija", "hold").Value);
        Assert.Contains(llaves, k => k == IdempotencyKey.From("saga-fija", "authorize").Value);
        Assert.Contains(llaves, k => k == IdempotencyKey.From("saga-fija", "capture").Value);
        Assert.Contains(llaves, k => k == IdempotencyKey.From("saga-fija", "confirm").Value);
    }

    [Fact]
    public async Task Repetir_AGENDAR_con_el_mismo_id_no_toma_un_segundo_cupo()
    {
        var (flow, caps, _, _) = Nuevo(Feliz());

        await Agendar(flow, "misma");
        await Agendar(flow, "misma");

        Assert.Equal(1, caps.Veces("POST", "/v1/holds"));
    }

    [Fact]
    public async Task Confirmar_dos_veces_no_captura_dos_veces()
    {
        var (flow, caps, _, _) = Nuevo(Feliz());
        var agendada = await Agendar(flow);

        await flow.ConfirmAsync(agendada.Value.Id, CancellationToken.None);
        var segunda = await flow.ConfirmAsync(agendada.Value.Id, CancellationToken.None);

        Assert.True(segunda.IsOk);
        Assert.Equal(1, caps.Veces("POST", "/capture"));
    }

    [Fact]
    public async Task El_CONSENTIMIENTO_se_comprueba_antes_de_tocar_nada()
    {
        // Comprobarlo después de apartar un cupo obligaría a soltarlo — una compensación que no
        // hacía falta.
        var caps = Feliz().Falla("POST /v1/grants/check", HttpStatusCode.Forbidden, "consent.revoked");
        var (flow, _, _, _) = Nuevo(caps);

        var r = await Agendar(flow);

        Assert.Equal("consent.revoked", r.Rejection!.Code);
        Assert.Equal(0, caps.Veces("POST", "/v1/holds"));
        Assert.Equal(0, caps.Veces("POST", "/v1/payments"));
    }

    [Fact]
    public async Task Una_cita_YA_CONFIRMADA_no_se_compensa()
    {
        // Deshacerla es una cancelación con su política de plazo, no una compensación — y
        // tratarla como compensación devolvería plata saltándose esa política.
        var (flow, _, _, _) = Nuevo(Feliz());
        var agendada = await Agendar(flow);
        await flow.ConfirmAsync(agendada.Value.Id, CancellationToken.None);

        var r = await flow.CancelAsync(agendada.Value.Id, CancellationToken.None);

        Assert.Equal("salud.already_confirmed", r.Rejection!.Code);
    }

    [Fact]
    public async Task Sin_copago_no_se_autoriza_ningun_cobro()
    {
        // Un pago de cero lo rechazaría Payments, y registrarlo ensuciaría la conciliación con
        // filas que no corresponden a ningún movimiento.
        var caps = Feliz().Ok("POST /v1/quotes", """{"total":{"amount":0,"currency":"COP"}}""");
        var (flow, _, _, _) = Nuevo(caps);

        var agendada = await Agendar(flow);

        Assert.True(agendada.IsOk);
        Assert.Equal(0, caps.Veces("POST", "/v1/payments"));
    }
}
