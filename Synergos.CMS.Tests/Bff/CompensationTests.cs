using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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

        public List<(string Method, string Path, string? Key, string? Body)> Llamadas { get; } = new();

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

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var clave = $"{request.Method.Method} {path}";
            var cuerpo = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            lock (Llamadas)
            {
                Llamadas.Add((request.Method.Method, path,
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
                    System.Text.Encoding.UTF8, "application/problem+json"),
            };
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
        public IReadOnlyList<AppointmentSaga> WithPendingCompensations()
            => _s.Values.Where(x => x.IsUnwinding && x.Pending.Count > 0).ToList();
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
        .Ok("POST /v1/holds/h1/confirm", """{"id":"res1","status":"Confirmed"}""")
        .Ok("POST /v1/deliveries", """{"id":"d1","status":"Sent"}""");

    /// <summary>Una guardia configurada, que es lo que un despliegue de verdad tiene.</summary>
    private static AlertOptions Guardia() => new()
    {
        ToKind = "salud.guardia",
        ToId = "operaciones",
        Address = "guardia@ejemplo.co",
    };

    private static Contexto Nuevo(CapacidadesFalsas caps, AlertOptions? alertas = null)
    {
        var sagas = new MemoriaSagas();
        var reloj = new RelojFalso(Ahora);
        var api = new SaludCapabilities(new FabricaFalsa(caps));
        var comp = new Compensator(api, reloj, NullLogger<Compensator>.Instance);
        var aviso = new CompensationAlert(api, Options.Create(alertas ?? Guardia()));
        return new Contexto(
            new AppointmentFlow(api, sagas, comp, aviso, reloj, NullLogger<AppointmentFlow>.Instance),
            caps, sagas, reloj);
    }

    /// <summary>Lleva la saga hasta rendirse: agotar los intentos con la capacidad caída.</summary>
    private static async Task<AppointmentSaga> HastaRendirse(Contexto ctx, string id = "saga-1")
    {
        var agendada = await Agendar(ctx.Flow, id);
        await ctx.Flow.ConfirmAsync(id, CancellationToken.None);

        for (var i = 1; i <= Compensator.MaxAttempts; i++)
        {
            ctx.Reloj.Avanzar(Compensator.Backoff(i) + TimeSpan.FromSeconds(1));
            await ctx.Flow.CompensateAsync(id, "barrido", CancellationToken.None);
        }

        return ctx.Flow.Get(agendada.Value.Id).Value;
    }

    /// <summary>Guion donde la devolución nunca sale: Payments no contesta.</summary>
    private static CapacidadesFalsas ImposibleDeCompensar() => Feliz()
        .Falla("POST /v1/holds/h1/confirm", HttpStatusCode.Gone, "booking.hold_expired")
        .Caida("GET /v1/payments/pg1");

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

    // ── Lo que el barrido puede tocar ────────────────────────────────────────
    //
    // Esta sección existe por un defecto que ningún test veía y que un proceso real destapó en
    // seis segundos: todos los demás llaman a CompensateAsync directo, y por eso ninguno
    // ejercitaba la SELECCIÓN — qué sagas elige el barrido. La respuesta era "todas las que
    // tengan una compensación pendiente", y eso describe a toda cita sana esperando confirmación.

    [Fact]
    public void Una_cita_recien_agendada_NO_es_trabajo_para_el_barrido()
    {
        // Sus compensaciones están ARMADAS, no pendientes: son el seguro, no la tarea. Ejecutarlas
        // suelta el cupo y libera el cobro de una cita que no tiene ningún problema.
        var ctx = Nuevo(Feliz());
        var agendada = Agendar(ctx.Flow).GetAwaiter().GetResult();

        Assert.Equal(SagaStatus.AwaitingConfirmation, agendada.Value.Status);
        Assert.Equal(2, agendada.Value.Pending.Count);       // armadas
        Assert.False(agendada.Value.IsUnwinding);
        Assert.False(agendada.Value.NeedsSweep);             // pero NO es trabajo
    }

    [Fact]
    public async Task El_barrido_no_ve_las_citas_sanas_esperando_confirmacion()
    {
        // La misma regla en la otra cara: si la vista de operación las listara, /v1/compensations
        // sería un listado de todas las citas del día en vez de la lista de lo que va mal.
        var ctx = Nuevo(Feliz());
        await Agendar(ctx.Flow, "sana-1");
        await Agendar(ctx.Flow, "sana-2");

        Assert.Empty(ctx.Flow.PendingCompensations());
    }

    [Fact]
    public async Task Una_cita_que_SI_fallo_vuelve_a_entrar_en_el_barrido()
    {
        // El contrapeso del test anterior: filtrar de más dejaría sin reintento justo lo que el
        // barrido existe para reintentar.
        var ctx = Nuevo(ImposibleDeCompensar());
        var agendada = await Agendar(ctx.Flow);
        await ctx.Flow.ConfirmAsync(agendada.Value.Id, CancellationToken.None);

        var saga = ctx.Flow.Get(agendada.Value.Id).Value;
        Assert.True(saga.IsUnwinding);
        Assert.True(saga.NeedsSweep);
        Assert.Single(ctx.Flow.PendingCompensations());
    }

    // ── Rendirse tiene que SIGNIFICAR algo ───────────────────────────────────

    [Fact]
    public async Task Una_compensacion_RENDIDA_deja_de_reintentarse()
    {
        // Sin esto, "tras ocho intentos se rinde" es mentira: el barrido la sigue intentando cada
        // minuto para siempre, gritando el mismo error y tapando en el log los que sí se pueden
        // atender.
        var ctx = Nuevo(ImposibleDeCompensar());
        await HastaRendirse(ctx);

        var intentosAlRendirse = ctx.Flow.Get("saga-1").Value.Pending.Single().Attempts;
        var llamadasAlRendirse = ctx.Caps.Veces("GET", "/v1/payments/pg1");

        for (var i = 0; i < 5; i++)
        {
            ctx.Reloj.Avanzar(TimeSpan.FromHours(2));
            await ctx.Flow.CompensateAsync("saga-1", "barrido", CancellationToken.None);
        }

        Assert.Equal(Compensator.MaxAttempts, intentosAlRendirse);
        Assert.Equal(intentosAlRendirse, ctx.Flow.Get("saga-1").Value.Pending.Single().Attempts);
        Assert.Equal(llamadasAlRendirse, ctx.Caps.Veces("GET", "/v1/payments/pg1"));
    }

    [Fact]
    public async Task Una_saga_rendida_y_avisada_ya_no_necesita_barrido_pero_SIGUE_en_la_lista()
    {
        // Las dos mitades importan: no hay nada que el barrido pueda hacer, y aun así tiene que
        // seguir viéndose en /v1/compensations. Desaparecer de la lista sería peor que fallar.
        var ctx = Nuevo(ImposibleDeCompensar());
        var saga = await HastaRendirse(ctx);

        Assert.False(saga.NeedsSweep);
        Assert.Single(ctx.Flow.PendingCompensations());
    }

    // ── El aviso a una persona ───────────────────────────────────────────────

    [Fact]
    public async Task Rendirse_AVISA_a_una_persona()
    {
        // Es lo que cierra el lazo. Sin el aviso, CompensationFailed es una fila que nadie mira
        // y un log en rojo que solo sirve si alguien estaba mirando ese minuto.
        var ctx = Nuevo(ImposibleDeCompensar());
        var saga = await HastaRendirse(ctx);

        Assert.Equal(SagaStatus.CompensationFailed, saga.Status);
        Assert.Equal(1, ctx.Caps.Veces("POST", "/v1/deliveries"));
        Assert.NotNull(saga.AlertedAtUtc);
        Assert.Equal(1, saga.AlertsSent);
    }

    [Fact]
    public async Task El_aviso_sale_UNA_vez_y_no_una_por_vuelta_del_barrido()
    {
        // Un aviso por minuto no es vigilancia: Api.Notifications lo cortaría por tope de
        // frecuencia, y ese corte se llevaría por delante los avisos de las demás citas.
        var ctx = Nuevo(ImposibleDeCompensar());
        await HastaRendirse(ctx);

        for (var i = 0; i < 10; i++)
        {
            ctx.Reloj.Avanzar(TimeSpan.FromHours(2));
            await ctx.Flow.CompensateAsync("saga-1", "barrido", CancellationToken.None);
        }

        Assert.Equal(1, ctx.Caps.Veces("POST", "/v1/deliveries"));
    }

    [Fact]
    public async Task Mientras_solo_REINTENTA_no_se_avisa_a_nadie()
    {
        // Avisar en el primer fallo entrenaría a la guardia a ignorar el aviso, que es la única
        // forma de que un aviso deje de servir.
        var ctx = Nuevo(ImposibleDeCompensar());
        var agendada = await Agendar(ctx.Flow);
        await ctx.Flow.ConfirmAsync(agendada.Value.Id, CancellationToken.None);

        ctx.Reloj.Avanzar(Compensator.Backoff(1) + TimeSpan.FromSeconds(1));
        await ctx.Flow.CompensateAsync(agendada.Value.Id, "barrido", CancellationToken.None);

        Assert.Equal(SagaStatus.Compensating, ctx.Flow.Get(agendada.Value.Id).Value.Status);
        Assert.Equal(0, ctx.Caps.Veces("POST", "/v1/deliveries"));
    }

    [Fact]
    public async Task Si_NOTIFICATIONS_esta_caida_el_aviso_se_reintenta_en_el_barrido()
    {
        // Notifications caída es una capacidad caída como cualquier otra: transitoria. Darla por
        // avisada dejaría a la guardia sin enterarse de una plata colgada.
        var caps = ImposibleDeCompensar().Caida("POST /v1/deliveries");
        var ctx = Nuevo(caps);
        var saga = await HastaRendirse(ctx);

        Assert.Null(saga.AlertedAtUtc);
        Assert.True(saga.NeedsSweep, "un aviso que no salió es trabajo pendiente para el barrido");

        // Vuelve Notifications.
        caps.Ok("POST /v1/deliveries", """{"id":"d1","status":"Sent"}""");
        ctx.Reloj.Avanzar(TimeSpan.FromMinutes(1));
        await ctx.Flow.CompensateAsync("saga-1", "barrido", CancellationToken.None);

        Assert.NotNull(ctx.Flow.Get("saga-1").Value.AlertedAtUtc);
    }

    [Fact]
    public async Task Si_falta_la_PLANTILLA_no_se_reintenta_para_siempre()
    {
        // No lo arregla reintentar: lo arregla una persona autorando la plantilla. Repetirlo cada
        // minuto solo llenaría el log del mismo error, tapando el que importa.
        var caps = ImposibleDeCompensar()
            .Falla("POST /v1/deliveries", HttpStatusCode.NotFound, "notifications.template_not_found");
        var ctx = Nuevo(caps);
        var saga = await HastaRendirse(ctx);

        Assert.NotNull(saga.AlertedAtUtc);   // se dio por dicho: se gritó una vez
        Assert.Equal(0, saga.AlertsSent);    // pero NO salió ningún aviso, y eso queda anotado

        ctx.Reloj.Avanzar(TimeSpan.FromHours(2));
        await ctx.Flow.CompensateAsync("saga-1", "barrido", CancellationToken.None);
        Assert.Equal(1, ctx.Caps.Veces("POST", "/v1/deliveries"));
    }

    [Fact]
    public async Task Sin_guardia_configurada_no_se_inventa_un_destinatario()
    {
        // Cablear una dirección sería la primera grieta de un BFF que después hay que desplegar
        // en otra clínica con otra guardia.
        var ctx = Nuevo(ImposibleDeCompensar(), new AlertOptions());
        var saga = await HastaRendirse(ctx);

        Assert.Equal(0, ctx.Caps.Veces("POST", "/v1/deliveries"));
        Assert.Equal(0, saga.AlertsSent);
        Assert.Equal(SagaStatus.CompensationFailed, saga.Status);
    }

    [Fact]
    public async Task El_aviso_NO_lleva_datos_del_paciente()
    {
        // Sale por correo o SMS a una guardia operativa. El identificador de la cita basta para
        // que quien lo atienda entre al sistema y mire; el paciente en el cuerpo del correo
        // sacaría un dato clínico a un canal que no está pensado para eso.
        var ctx = Nuevo(ImposibleDeCompensar());
        await HastaRendirse(ctx);

        var aviso = ctx.Caps.Llamadas.Single(l => l.Path == "/v1/deliveries").Body!;

        Assert.Contains("saga-1", aviso, StringComparison.Ordinal);
        Assert.DoesNotContain(Paciente.Id, aviso, StringComparison.Ordinal);
        Assert.DoesNotContain(Paciente.Kind, aviso, StringComparison.Ordinal);
    }

    [Fact]
    public async Task El_aviso_solo_usa_los_marcadores_que_la_plantilla_puede_esperar()
    {
        // Api.Notifications RECHAZA un marcador sin valor en vez de mandar un hueco. Si el aviso
        // dejara de mandar uno de estos, la plantilla autorada contra ellos empezaría a fallar
        // con missing_placeholder — y el fallo aparecería justo el día que hay que avisar.
        var ctx = Nuevo(ImposibleDeCompensar());
        await HastaRendirse(ctx);

        var aviso = ctx.Caps.Llamadas.Single(l => l.Path == "/v1/deliveries").Body!;

        foreach (var marcador in CompensationAlert.Placeholders)
        {
            Assert.Contains($"\"{marcador}\":", aviso, StringComparison.Ordinal);
        }
    }

    // ── La puerta de la persona: el reintento manual ─────────────────────────

    [Fact]
    public async Task El_reintento_manual_REARMA_los_intentos_y_completa()
    {
        // Sin esta puerta, "se rinde" sería "se abandona", y la única salida a una devolución
        // colgada sería tocarla a mano en Payments, por fuera del rastro de la saga.
        var caps = ImposibleDeCompensar();
        var ctx = Nuevo(caps);
        await HastaRendirse(ctx);

        // Alguien arregla la causa.
        caps.Ok("GET /v1/payments/pg1", """{"id":"pg1","status":"Captured","amount":{"amount":50000,"currency":"COP"},"refundable":{"amount":50000,"currency":"COP"}}""");

        var r = await ctx.Flow.RetryStuckAsync("saga-1", CancellationToken.None);

        Assert.True(r.IsOk);
        Assert.Equal(SagaStatus.Compensated, r.Value.Status);
        Assert.Equal(1, caps.Veces("POST", "/refund"));
    }

    [Fact]
    public async Task Si_vuelve_a_colgarse_el_SEGUNDO_aviso_lleva_otra_llave()
    {
        // Con la misma llave, Api.Notifications reconocería el aviso viejo y devolvería aquel
        // envío sin mandar nada: la guardia no se enteraría de la segunda vez.
        var ctx = Nuevo(ImposibleDeCompensar());
        await HastaRendirse(ctx);

        await ctx.Flow.RetryStuckAsync("saga-1", CancellationToken.None);
        for (var i = 1; i <= Compensator.MaxAttempts; i++)
        {
            ctx.Reloj.Avanzar(Compensator.Backoff(i) + TimeSpan.FromSeconds(1));
            await ctx.Flow.CompensateAsync("saga-1", "barrido", CancellationToken.None);
        }

        var llaves = ctx.Caps.Llamadas.Where(l => l.Path == "/v1/deliveries").Select(l => l.Key).ToList();

        Assert.Equal(2, llaves.Count);
        Assert.Equal(IdempotencyKey.From("saga-1", "alert:0").Value, llaves[0]);
        Assert.Equal(IdempotencyKey.From("saga-1", "alert:1").Value, llaves[1]);
    }

    [Fact]
    public async Task El_reintento_manual_sobre_algo_que_NO_se_rindio_se_rechaza()
    {
        // Lo que está en curso lo reintenta el barrido solo, y forzarlo saltaría el retroceso
        // justo cuando la capacidad está caída.
        var ctx = Nuevo(ImposibleDeCompensar());
        var agendada = await Agendar(ctx.Flow);
        await ctx.Flow.ConfirmAsync(agendada.Value.Id, CancellationToken.None);

        var r = await ctx.Flow.RetryStuckAsync(agendada.Value.Id, CancellationToken.None);

        Assert.Equal("salud.nothing_stuck", r.Rejection!.Code);
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
