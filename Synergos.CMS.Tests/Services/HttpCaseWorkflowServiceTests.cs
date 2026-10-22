using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// El expediente avanza contra <c>Api.Workflow</c> (HU #44).
/// </summary>
/// <remarks>
/// <para>Lo que se prueba no es «decidir». Es lo que este camino tiene de distinto: que
/// <b>la tabla de transiciones ya no está de este lado</b> —el destino se lee de la definición— y
/// que las dos cosas que el motor en proceso hacía bien se conservan: la idempotencia sobre el
/// estado destino, y que un expediente decidido quede anotado, asentado y avisado acá.</para>
/// </remarks>
public sealed class HttpCaseWorkflowServiceTests
{
    private sealed class CapacidadFalsa : HttpMessageHandler
    {
        private readonly Dictionary<string, Func<HttpResponseMessage>> _rutas = new(StringComparer.OrdinalIgnoreCase);

        public List<(string Method, string Path, string? Query, string? Key, string? Body)> Llamadas { get; } = new();

        public CapacidadFalsa Ok(string ruta, string json)
        {
            _rutas[ruta] = () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            return this;
        }

        public CapacidadFalsa Falla(string ruta, HttpStatusCode codigo, string code, string detail)
        {
            _rutas[ruta] = () => new HttpResponseMessage(codigo)
            {
                Content = new StringContent(
                    $$"""{"title":"x","status":{{(int)codigo}},"detail":"{{detail}}","code":"{{code}}"}""",
                    Encoding.UTF8, "application/problem+json"),
            };
            return this;
        }

        public int Veces(string sufijo)
            => Llamadas.Count(l => l.Path.EndsWith(sufijo, StringComparison.Ordinal));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            var path = req.RequestUri!.AbsolutePath;
            Llamadas.Add((req.Method.Method, path, req.RequestUri.Query,
                req.Headers.TryGetValues("Idempotency-Key", out var k) ? k.FirstOrDefault() : null,
                req.Content?.ReadAsStringAsync(ct).GetAwaiter().GetResult()));

            return Task.FromResult(_rutas.TryGetValue($"{req.Method.Method} {path}", out var f)
                ? f()
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class FabricaFalsa : IHttpClientFactory
    {
        private readonly CapacidadFalsa _h;
        public FabricaFalsa(CapacidadFalsa h) => _h = h;
        public HttpClient CreateClient(string name)
            => new(_h, disposeHandler: false) { BaseAddress = new Uri("http://workflow.local/") };
    }

    // La definición tal como la publica el despliegue: los estados SON los slugs públicos del
    // expediente, que es lo que hace que la traducción de vuelta sea una lectura y no una tabla.
    // Es la v1: la de los expedientes que nacieron antes de 009 y siguen con ella.
    private const string DefinicionV1 = """
        {"key":"gov.tramite","initialState":"submitted",
         "finalStates":["approved","rejected"],
         "transitions":[
           {"name":"approve","from":"submitted","to":"approved"},
           {"name":"approve","from":"in-review","to":"approved"},
           {"name":"reject","from":"submitted","to":"rejected"},
           {"name":"request-info","from":"submitted","to":"info-requested"}]}
        """;

    // La v2 (tools/provisionar.procesos.json): se toma antes de decidir y la subsanación vuelve.
    private const string DefinicionV2 = """
        {"key":"gov.tramite.v2","initialState":"submitted",
         "finalStates":["approved","rejected"],
         "transitions":[
           {"name":"take","from":"submitted","to":"in-review"},
           {"name":"approve","from":"in-review","to":"approved"},
           {"name":"reject","from":"in-review","to":"rejected"},
           {"name":"request-info","from":"in-review","to":"info-requested"},
           {"name":"resubmit","from":"info-requested","to":"in-review"}]}
        """;

    private static readonly CaseActor Ana = new(
        CaseRoles.KindFuncionario, "f-ana", "Ana Funcionaria", "ana@entidad.gov.co");

    private static string Instancia(string estado, string clave = "gov.tramite")
        => $$"""{"id":"wf-1","definitionKey":"{{clave}}","state":"{{estado}}"}""";

    private static string Pagina(params string[] items)
        => $$"""{"items":[{{string.Join(",", items)}}],"total":{{items.Length}},"offset":0,"hasMore":false}""";

    private static CapacidadFalsa Feliz() => new CapacidadFalsa()
        .Ok("GET /v1/definitions/gov.tramite", DefinicionV1)
        .Ok("GET /v1/definitions/gov.tramite.v2", DefinicionV2)
        .Ok("GET /v1/instances", Pagina(Instancia("submitted")))
        .Ok("POST /v1/instances/wf-1/fire", Instancia("approved"));

    private static async Task<(HttpCaseWorkflowService Svc, StubApplicationService Casos, string CaseId)>
        ArmarAsync(CapacidadFalsa capacidad)
    {
        var casos = new StubApplicationService(
            new StubTramiteCatalogProvider(), new StubGovFeeCalculator(), new StubPaymentProvider());

        // Un expediente recién radicado: es el que la capacidad puede arrancar sin inventar nada.
        var abierto = casos.ListCases().First(c => c.Status == CaseStatus.Radicado);

        var svc = new HttpCaseWorkflowService(
            new FabricaFalsa(capacidad),
            casos,
            Options.Create(new GobSettings { Mode = "Api", ApiKey = "k" }));

        return await Task.FromResult((svc, casos, abierto.CaseId));
    }

    // ── El camino feliz ─────────────────────────────────────────────────────

    [Fact] // happy: la capacidad dice que es legal y el expediente queda decidido de este lado.
    public async Task Una_decision_legal_mueve_el_expediente_y_queda_anotada()
    {
        var capacidad = Feliz();
        var (svc, casos, caseId) = await ArmarAsync(capacidad);

        var resultado = await svc.DecideAsync(caseId, "approve", "Todo en orden.", Ana);

        Assert.Equal(CaseStatus.Resuelto, resultado.Status);
        Assert.Equal(CaseStatus.Resuelto, casos.FindCase(caseId)!.Status);
        Assert.Equal("Todo en orden.", resultado.Decision!.Note);
        Assert.Equal(1, capacidad.Veces("/fire"));
    }

    /// <summary>
    /// El estado que queda es el que dijo la CAPACIDAD, no el que suponía el CMS.
    /// </summary>
    /// <remarks>
    /// Es la mitad que hace real la mudanza. Si el CMS mandara su propia conclusión, una
    /// definición que llevara a otro sitio dejaría al expediente y a la instancia diciendo cosas
    /// distintas — y la siguiente decisión se tomaría sobre una realidad que no es.
    /// </remarks>
    [Fact]
    public async Task El_estado_que_queda_lo_dice_la_capacidad()
    {
        var capacidad = Feliz();
        // La definición dice que `approve` desde `submitted` va a `approved`; la instancia vuelve
        // en `info-requested`. Manda la instancia.
        capacidad.Ok("POST /v1/instances/wf-1/fire", Instancia("info-requested"));

        var (svc, casos, caseId) = await ArmarAsync(capacidad);

        var resultado = await svc.DecideAsync(caseId, "approve", "", Ana);

        Assert.Equal(CaseStatus.Subsanacion, resultado.Status);
        Assert.Equal(CaseStatus.Subsanacion, casos.FindCase(caseId)!.Status);
    }

    // ── Lo que se conserva del motor en proceso ─────────────────────────────

    /// <summary>
    /// Decidir dos veces lo mismo NO vuelve a llamar, y sigue sin ser un error.
    /// </summary>
    /// <remarks>
    /// <para>Es el punto más fino de la HU. El motor en proceso es idempotente sobre el estado
    /// destino; <c>Api.Workflow</c> contesta otra cosa a lo mismo (<c>instance_closed</c>, que es
    /// un conflicto) porque responde «¿es legal esta transición?» y no «¿hace falta hacer algo?».
    /// Las dos son correctas.</para>
    ///
    /// <para>Dejar subir la suya convertiría el doble clic del funcionario, que hoy no hace nada,
    /// en un error en pantalla — y nadie lo habría decidido.</para>
    /// </remarks>
    [Fact]
    public async Task Decidir_dos_veces_lo_mismo_no_llama_ni_falla()
    {
        var capacidad = Feliz();
        var (svc, _, caseId) = await ArmarAsync(capacidad);

        await svc.DecideAsync(caseId, "approve", "Va.", Ana);
        var segunda = await svc.DecideAsync(caseId, "approve", "Va otra vez.", Ana);

        Assert.Equal(CaseStatus.Resuelto, segunda.Status);
        // Y la nota de la primera es la que queda: la segunda no re-decide nada.
        Assert.Equal("Va.", segunda.Decision!.Note);
        Assert.Equal(1, capacidad.Veces("/fire"));
    }

    [Fact] // la definición se lee UNA vez: no puede cambiar mientras la clave sea la misma.
    public async Task La_definicion_se_lee_una_sola_vez()
    {
        var capacidad = Feliz();
        var (svc, _, caseId) = await ArmarAsync(capacidad);

        await svc.DecideAsync(caseId, "approve", "", Ana);
        await svc.DecideAsync(caseId, "approve", "", Ana);

        Assert.Equal(1, capacidad.Veces("/v1/definitions/gov.tramite"));
    }

    // ── Lo que rechaza ──────────────────────────────────────────────────────

    /// <summary>
    /// Una transición ilegal sube con el MOTIVO de la capacidad — que trae las que sí se pueden.
    /// </summary>
    /// <remarks>
    /// Es la mejora concreta sobre la tabla local: el mensaje del motor en proceso decía qué no
    /// se podía, nunca qué sí.
    /// </remarks>
    [Fact]
    public async Task Una_transicion_ilegal_sube_con_la_lista_de_las_que_si()
    {
        var capacidad = Feliz();
        capacidad.Falla("POST /v1/instances/wf-1/fire", HttpStatusCode.Conflict,
            "workflow.transition_not_allowed", "Desde 'submitted' no se puede 'approve'. Sí se puede: reject.");

        var (svc, casos, caseId) = await ArmarAsync(capacidad);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.DecideAsync(caseId, "approve", "", Ana));

        Assert.Contains("Sí se puede: reject", ex.Message, StringComparison.Ordinal);
        // Y el expediente NO se movió: la decisión se aplica DESPUÉS de que la capacidad la acepta.
        Assert.Equal(CaseStatus.Radicado, casos.FindCase(caseId)!.Status);
    }

    [Fact] // un outcome que la definición no nombra es error de quien llama, no del expediente.
    public async Task Un_outcome_que_el_proceso_no_tiene_se_rechaza_sin_salir_a_la_red()
    {
        var capacidad = Feliz();
        var (svc, _, caseId) = await ArmarAsync(capacidad);

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => svc.DecideAsync(caseId, "archivar", "", Ana));

        Assert.Contains("approve", ex.Message, StringComparison.Ordinal);
        Assert.Equal(0, capacidad.Veces("/fire"));
    }

    /// <summary>
    /// Sin definición publicada NO se decide, y el mensaje dice que es un paso de despliegue.
    /// </summary>
    /// <remarks>
    /// Adivinar un proceso acá sería volver a tener la tabla de este lado, que es lo que la HU
    /// existe para quitar.
    /// </remarks>
    [Fact]
    public async Task Sin_definicion_publicada_no_se_decide_y_lo_dice()
    {
        // El expediente aún no tiene proceso y /definitions da 404: la de los nuevos no está publicada.
        var capacidad = new CapacidadFalsa().Ok("GET /v1/instances", Pagina());
        var (svc, casos, caseId) = await ArmarAsync(capacidad);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.DecideAsync(caseId, "approve", "", Ana));

        Assert.Contains("despliegue", ex.Message, StringComparison.Ordinal);
        Assert.Contains("gov.tramite.v2", ex.Message, StringComparison.Ordinal);
        Assert.Equal(CaseStatus.Radicado, casos.FindCase(caseId)!.Status);
    }

    /// <summary>
    /// Con la capacidad caída NO se cae a la tabla local: se falla con el motivo.
    /// </summary>
    /// <remarks>
    /// Caer al stub en silencio convertiría una caída en decisiones tomadas con un proceso que
    /// quizá ya no es el vigente, y nadie se enteraría. Mismo criterio que la HU #27 con los cobros.
    /// </remarks>
    [Fact]
    public async Task Con_la_capacidad_caida_NO_se_decide_con_la_tabla_local()
    {
        var capacidad = Feliz();
        capacidad.Falla("GET /v1/instances", HttpStatusCode.ServiceUnavailable,
            "workflow.unavailable", "no hay quien atienda");

        var (svc, casos, caseId) = await ArmarAsync(capacidad);

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.DecideAsync(caseId, "approve", "", Ana));

        Assert.Equal(CaseStatus.Radicado, casos.FindCase(caseId)!.Status);
    }

    /// <summary>
    /// Una capacidad que no CONTESTA degrada con un motivo, no con un 500 sin explicación.
    /// </summary>
    /// <remarks>
    /// <para><b>Lo destapó levantar los procesos, no un test.</b> La <c>HttpRequestException</c>
    /// subía cruda hasta el borde, que sólo traduce <c>ArgumentException</c> e
    /// <c>InvalidOperationException</c> — así que <c>Api.Workflow</c> apagada daba un 500 mudo
    /// donde <c>GobSettings</c> promete «degrada y lo dice». Y el mensaje llevaba la dirección
    /// interna, que no es asunto de quien usa el portal.</para>
    /// </remarks>
    [Fact]
    public async Task Una_capacidad_que_no_contesta_degrada_con_motivo_y_sin_direcciones()
    {
        var capacidad = new CaidaTotal();
        var casos = new StubApplicationService(
            new StubTramiteCatalogProvider(), new StubGovFeeCalculator(), new StubPaymentProvider());
        var abierto = casos.ListCases().First(c => c.Status == CaseStatus.Radicado);

        var svc = new HttpCaseWorkflowService(
            new FabricaCaida(capacidad), casos, Options.Create(new GobSettings { Mode = "Api" }));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.DecideAsync(abierto.CaseId, "approve", "", Ana));

        Assert.Contains("no responde", ex.Message, StringComparison.Ordinal);
        Assert.Contains("NO se aplicó", ex.Message, StringComparison.Ordinal);
        // La dirección interna no sale al borde.
        Assert.DoesNotContain("127.0.0.1", ex.Message, StringComparison.Ordinal);
        Assert.Equal(CaseStatus.Radicado, casos.FindCase(abierto.CaseId)!.Status);
    }

    private sealed class CaidaTotal : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
            => throw new HttpRequestException("Connection refused (127.0.0.1:5215)");
    }

    /// <summary>La capacidad acepta la conexión y no contesta nunca.</summary>
    private sealed class Colgada : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("inalcanzable");
        }
    }

    private sealed class FabricaDe : IHttpClientFactory
    {
        private readonly HttpMessageHandler _h;
        private readonly TimeSpan _techo;
        public FabricaDe(HttpMessageHandler h, TimeSpan techo) { _h = h; _techo = techo; }
        public HttpClient CreateClient(string name)
            => new(_h, disposeHandler: false) { BaseAddress = new Uri("http://workflow.local/"), Timeout = _techo };
    }

    private static (HttpCaseWorkflowService Svc, StubApplicationService Casos, string CaseId) Colgado(TimeSpan techo)
    {
        var casos = new StubApplicationService(
            new StubTramiteCatalogProvider(), new StubGovFeeCalculator(), new StubPaymentProvider());
        var abierto = casos.ListCases().First(c => c.Status == CaseStatus.Radicado);

        return (new HttpCaseWorkflowService(
            new FabricaDe(new Colgada(), techo), casos, Options.Create(new GobSettings { Mode = "Api" })),
            casos, abierto.CaseId);
    }

    /// <summary>
    /// Si quien decide se va, eso NO se cuenta como que la capacidad tardó (#178).
    /// </summary>
    /// <remarks>
    /// <c>EnRed</c> traducía TODA <see cref="TaskCanceledException"/> a «Api.Workflow tardó
    /// demasiado», también la del llamador que cerró la pestaña: un fallo de la capacidad que no
    /// ocurrió, anotado a su nombre. La cancelación de quien llama se re-lanza tal cual.
    /// </remarks>
    [Fact]
    public async Task Si_quien_decide_se_va_no_se_cuenta_como_que_la_capacidad_tardo()
    {
        var (svc, casos, caseId) = Colgado(TimeSpan.FromSeconds(30));
        using var seFue = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => svc.DecideAsync(caseId, "approve", "", Ana, seFue.Token));

        Assert.Equal(CaseStatus.Radicado, casos.FindCase(caseId)!.Status);
    }

    [Fact] // el control del de arriba: el techo SÍ se sigue contando como que la capacidad tardó.
    public async Task Si_la_capacidad_agota_el_techo_se_dice_que_tardo()
    {
        var (svc, casos, caseId) = Colgado(TimeSpan.FromMilliseconds(100));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.DecideAsync(caseId, "approve", "", Ana));

        Assert.Contains("tardó demasiado", ex.Message, StringComparison.Ordinal);
        Assert.Equal(CaseStatus.Radicado, casos.FindCase(caseId)!.Status);
    }

    private sealed class FabricaCaida : IHttpClientFactory
    {
        private readonly CaidaTotal _h;
        public FabricaCaida(CaidaTotal h) => _h = h;
        public HttpClient CreateClient(string name)
            => new(_h, disposeHandler: false) { BaseAddress = new Uri("http://workflow.local/") };
    }

    // ── La migración, que es lo que no se puede tratar en silencio ──────────

    /// <summary>
    /// Un expediente YA movido y sin proceso no se arranca: se rechaza diciendo por qué.
    /// </summary>
    /// <remarks>
    /// <para>Arrancarle una instancia ahora la pondría en el estado inicial, y la capacidad diría
    /// que un expediente casi resuelto acaba de empezar. Adelantarlo a golpe de transiciones
    /// escribiría historia falsa —fechas y actores que no ocurrieron— y <i>parecería</i> que
    /// funciona, que es lo peor que puede hacer una migración.</para>
    /// </remarks>
    [Fact]
    public async Task Un_expediente_anterior_al_cableado_no_se_adivina()
    {
        var capacidad = Feliz();
        capacidad.Ok("GET /v1/instances", Pagina());       // no tiene proceso

        var (svc, casos, _) = await ArmarAsync(capacidad);
        var enMarcha = casos.ListCases().First(c => c.Status == CaseStatus.EnRevision);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.DecideAsync(enMarcha.CaseId, "approve", "", Ana));

        Assert.Contains("migrarlos", ex.Message, StringComparison.Ordinal);
        Assert.Equal(0, capacidad.Veces("/fire"));
    }

    /// <summary>
    /// Uno recién radicado SÍ se arranca —no hay historia que inventar—, y con la v2.
    /// </summary>
    /// <remarks>
    /// La definición con que arranca es la de los ajustes (<c>gov.tramite.v2</c> por defecto,
    /// 009-FR-008), y su primer paso es TOMARLO (P4): en la v2 un expediente radicado no se decide.
    /// </remarks>
    [Fact]
    public async Task Un_expediente_recien_radicado_arranca_su_proceso_con_la_v2_y_se_toma()
    {
        var capacidad = Feliz();
        capacidad.Ok("GET /v1/instances", Pagina());
        capacidad.Ok("POST /v1/instances", Instancia("submitted", "gov.tramite.v2"));
        capacidad.Ok("POST /v1/instances/wf-1/fire", Instancia("in-review", "gov.tramite.v2"));

        var (svc, casos, caseId) = await ArmarAsync(capacidad);

        var resultado = await svc.TakeAsync(caseId, Ana, null);

        Assert.Equal(CaseStatus.EnRevision, resultado.Status);
        Assert.Equal(CaseStatus.EnRevision, casos.FindCase(caseId)!.Status);
        Assert.Null(resultado.Decision);

        // Y la llave del arranque es el expediente: un reintento encuentra la que él mismo creó.
        var arranque = capacidad.Llamadas.First(l => l.Path == "/v1/instances" && l.Method == "POST");
        Assert.Equal($"gov-case-{caseId}", arranque.Key);
        Assert.Contains("\"definitionKey\":\"gov.tramite.v2\"", arranque.Body, StringComparison.Ordinal);

        var disparo = capacidad.Llamadas.Single(l => l.Path.EndsWith("/fire", StringComparison.Ordinal));
        Assert.Contains("\"transition\":\"take\"", disparo.Body, StringComparison.Ordinal);
    }

    /// <summary>
    /// Un expediente sigue toda su vida con la definición con que nació (009-FR-008).
    /// </summary>
    /// <remarks>
    /// <para>Los nuevos arrancan con la v2, pero uno cuya instancia es de la v1 se decide con la
    /// v1 —que no tiene «tomar»—. Leer la definición de los ajustes en vez de la de su instancia le
    /// cambiaría las reglas a mitad de camino, y la capacidad, que sí sigue la de la instancia,
    /// rechazaría lo que el CMS acababa de ofrecer.</para>
    /// </remarks>
    [Fact]
    public async Task Un_expediente_que_nacio_con_la_v1_sigue_con_la_v1()
    {
        var capacidad = Feliz();                   // la instancia es de la v1 (gov.tramite)
        var (svc, casos, caseId) = await ArmarAsync(capacidad);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => svc.TakeAsync(caseId, Ana, null));

        Assert.Contains("'gov.tramite'", ex.Message, StringComparison.Ordinal);
        Assert.Equal(0, capacidad.Veces("/fire"));
        Assert.Equal(0, capacidad.Veces("/v1/definitions/gov.tramite.v2"));
        Assert.Equal(CaseStatus.Radicado, casos.FindCase(caseId)!.Status);

        // Y con la v1 se decide como antes: directo desde radicado.
        var decidido = await svc.DecideAsync(caseId, "approve", "", Ana);
        Assert.Equal(CaseStatus.Resuelto, decidido.Status);
    }

    /// <summary>
    /// Quien actúa viaja en el disparo con SU identidad, y es quien firma lo anotado (D-5 de 009).
    /// </summary>
    /// <remarks>
    /// Antes el disparo decía siempre <c>funcionario@entidad.gov.co</c>: la capacidad registraba
    /// la misma persona para toda decisión y el CMS anotaba lo mismo.
    /// </remarks>
    [Fact]
    public async Task Quien_actua_viaja_en_el_disparo_y_firma_lo_anotado()
    {
        var capacidad = Feliz();
        var (svc, _, caseId) = await ArmarAsync(capacidad);

        var resultado = await svc.DecideAsync(caseId, "approve", "Va.", Ana);

        var disparo = capacidad.Llamadas.Single(l => l.Path.EndsWith("/fire", StringComparison.Ordinal));
        Assert.Contains("\"actorKind\":\"gov.funcionario\"", disparo.Body, StringComparison.Ordinal);
        Assert.Contains("\"actorId\":\"f-ana\"", disparo.Body, StringComparison.Ordinal);
        Assert.Contains("\"actorRoles\":[\"funcionario\"]", disparo.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(GovCaseDecisionRecorder.OfficerActor, disparo.Body, StringComparison.Ordinal);

        Assert.Equal("ana@entidad.gov.co", resultado.Decision!.DecidedBy);
    }

    [Fact] // el expediente viaja como Ref opaco, con el Kind configurado.
    public async Task El_expediente_viaja_como_sujeto_opaco()
    {
        var capacidad = Feliz();
        var (svc, _, caseId) = await ArmarAsync(capacidad);

        await svc.DecideAsync(caseId, "approve", "", Ana);

        var busqueda = capacidad.Llamadas.First(l => l.Path == "/v1/instances" && l.Method == "GET");
        Assert.Contains("subjectKind=gov.expediente", busqueda.Query, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString(caseId), busqueda.Query!, StringComparison.Ordinal);
    }
}
