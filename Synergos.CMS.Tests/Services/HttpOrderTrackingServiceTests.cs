using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// El pedido avanza contra <c>Api.Workflow</c> (HU #46).
/// </summary>
/// <remarks>
/// <para>Lo que se prueba es lo que este camino tiene de distinto: que <b>leer no sale a la
/// red</b>, que <b>cada dominio usa su definición</b>, que saltar etapas se dispara en secuencia,
/// y que las fechas las sigue sellando el CMS.</para>
/// </remarks>
public sealed class HttpOrderTrackingServiceTests
{
    private sealed class CapacidadFalsa : HttpMessageHandler
    {
        private readonly Dictionary<string, Func<HttpResponseMessage>> _rutas = new(StringComparer.OrdinalIgnoreCase);
        private string _estado = "paid";

        public List<(string Method, string Path, string? Query, string? Key, string? Body)> Llamadas { get; } = new();
        public bool SinInstancia { get; set; } = true;

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

        /// <summary>Las transiciones disparadas, en orden.</summary>
        public List<string> Disparadas { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            var path = req.RequestUri!.AbsolutePath;
            var cuerpo = req.Content?.ReadAsStringAsync(ct).GetAwaiter().GetResult();
            Llamadas.Add((req.Method.Method, path, req.RequestUri.Query,
                req.Headers.TryGetValues("Idempotency-Key", out var k) ? k.FirstOrDefault() : null, cuerpo));

            if (_rutas.TryGetValue($"{req.Method.Method} {path}", out var guionado)) return Task.FromResult(guionado());

            // Buscar instancia.
            if (req.Method == HttpMethod.Get && path == "/v1/instances")
            {
                var items = SinInstancia ? "" : Instancia();
                return Ok($$"""{"items":[{{items}}],"total":0,"offset":0,"hasMore":false}""");
            }

            // Arrancar instancia.
            if (req.Method == HttpMethod.Post && path == "/v1/instances")
            {
                SinInstancia = false;
                return Ok(Instancia());
            }

            // Disparar transición: el estado pasa a ser la transición pedida.
            if (req.Method == HttpMethod.Post && path.EndsWith("/fire", StringComparison.Ordinal))
            {
                var nombre = System.Text.Json.JsonDocument.Parse(cuerpo!).RootElement
                    .GetProperty("transition").GetString()!;
                Disparadas.Add(nombre);
                _estado = nombre;
                return Ok(Instancia());
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private string Instancia()
            => $$"""{"id":"wf-1","definitionKey":"tracking.shop","state":"{{_estado}}"}""";

        private static Task<HttpResponseMessage> Ok(string json)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
    }

    private sealed class FabricaFalsa : IHttpClientFactory
    {
        private readonly HttpMessageHandler _h;
        public FabricaFalsa(HttpMessageHandler h) => _h = h;
        public HttpClient CreateClient(string name)
            => new(_h, disposeHandler: false) { BaseAddress = new Uri("http://workflow.local/") };
    }

    private static (HttpOrderTrackingService Svc, IOrderTrackingService Local) Armar(
        HttpMessageHandler capacidad, string domain = "shop",
        IReadOnlyList<OrderTrackingStageDefinition>? pipeline = null)
    {
        var local = new StubOrderTrackingService(
            pipeline ?? StubOrderTrackingService.ShopPipeline, null, new InMemoryJsonEntityStore(), "t");

        return (new HttpOrderTrackingService(
            new FabricaFalsa(capacidad), local, Options.Create(new TrackingSettings { Mode = "Api" }),
            pipeline ?? StubOrderTrackingService.ShopPipeline, domain), local);
    }

    // ── El camino feliz ─────────────────────────────────────────────────────

    [Fact] // happy: la capacidad valida y el CMS sella la fecha.
    public async Task Un_avance_valido_queda_sellado_de_este_lado()
    {
        var capacidad = new CapacidadFalsa();
        var (svc, local) = Armar(capacidad);

        var timeline = await svc.AdvanceAsync("ord-1", "paid", "Pago ok");

        Assert.Equal("paid", timeline.CurrentStage);
        // La fecha vive en el almacén del CMS, no en la capacidad.
        Assert.NotNull((await local.GetTimelineAsync("ord-1"))!.Stages.First(s => s.Stage == "paid").ReachedAt);
    }

    /// <summary>
    /// Saltar etapas dispara las intermedias EN SECUENCIA.
    /// </summary>
    /// <remarks>
    /// El pedido pasó por cada una —el pipeline es secuencial por construcción— y declarar
    /// transiciones de salto perdería las intermedias en la historia de la capacidad, que es
    /// justo lo que un timeline necesita.
    /// </remarks>
    [Fact]
    public async Task Saltar_etapas_dispara_las_intermedias_en_orden()
    {
        var capacidad = new CapacidadFalsa();
        var (svc, _) = Armar(capacidad);

        var timeline = await svc.AdvanceAsync("ord-1", "shipped");

        // `paid` es el estado inicial: la instancia nace ahí y no se dispara hacia él.
        Assert.Equal(new[] { "preparing", "shipped" }, capacidad.Disparadas);
        Assert.Equal("shipped", timeline.CurrentStage);
        Assert.True(timeline.Stages.First(s => s.Stage == "preparing").Reached);
    }

    /// <summary>
    /// La nota se sella SOLO en la etapa destino, no en las intermedias del salto.
    /// </summary>
    /// <remarks>
    /// <b>Lo destapó una verificación en vivo</b>, no un doble: mirando la historia real de la
    /// capacidad, «Entregado» aparecía también en «Enviado» — una nota que nadie escribió sobre
    /// ese paso. El motor local siempre la selló sólo en la destino; el camino HTTP tiene que
    /// contar lo mismo.
    /// </remarks>
    [Fact]
    public async Task La_nota_va_solo_en_la_etapa_destino()
    {
        var capacidad = new CapacidadFalsa();
        var (svc, _) = Armar(capacidad);

        // Sin tildes a proposito: System.Text.Json las escapa y la asercion miraria \u00f3.
        await svc.AdvanceAsync("ord-1", "shipped", "Salio el despacho");

        var conNota = capacidad.Llamadas
            .Where(l => l.Path.EndsWith("/fire", StringComparison.Ordinal)
                        && l.Body!.Contains("Salio el despacho", StringComparison.Ordinal))
            .ToList();

        Assert.Single(conNota);
        Assert.Contains("\"transition\":\"shipped\"", conNota[0].Body, StringComparison.Ordinal);
    }

    /// <summary>Quién avanza es el sistema, no el pedido.</summary>
    /// <remarks>
    /// También de la verificación en vivo: usando el <c>Kind</c> del sujeto como actor, la
    /// historia quedaba diciendo que «tracking.shop» despachó algo.
    /// </remarks>
    [Fact]
    public async Task El_actor_no_es_el_sujeto()
    {
        var capacidad = new CapacidadFalsa();
        var (svc, _) = Armar(capacidad);

        await svc.AdvanceAsync("ord-1", "preparing");

        var disparo = capacidad.Llamadas.First(l => l.Path.EndsWith("/fire", StringComparison.Ordinal));
        Assert.Contains($"\"actorKind\":\"{HttpOrderTrackingService.ActorKind}\"", disparo.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"actorKind\":\"tracking.", disparo.Body, StringComparison.Ordinal);
    }

    // ── Lo que NO sale a la red ─────────────────────────────────────────────

    /// <summary>
    /// Leer el timeline no llama a nadie, y es la diferencia deliberada con la HU #44.
    /// </summary>
    /// <remarks>
    /// Se pinta en cada vista de pedido. Con la capacidad caída, quien compró sigue viendo dónde
    /// va lo suyo — mostrar lo que ya pasó no decide nada.
    /// </remarks>
    [Fact]
    public async Task Leer_el_timeline_NO_sale_a_la_red()
    {
        var capacidad = new CapacidadFalsa();
        var (svc, _) = Armar(capacidad);
        await svc.AdvanceAsync("ord-1", "paid");
        capacidad.Llamadas.Clear();

        var timeline = await svc.GetTimelineAsync("ord-1");

        Assert.NotNull(timeline);
        Assert.Empty(capacidad.Llamadas);
    }

    [Fact] // idempotente/monotónico: ni llama ni falla, igual que el motor en proceso.
    public async Task Avanzar_a_una_etapa_ya_alcanzada_no_llama_ni_falla()
    {
        var capacidad = new CapacidadFalsa();
        var (svc, _) = Armar(capacidad);
        await svc.AdvanceAsync("ord-1", "shipped");
        capacidad.Llamadas.Clear();

        var otra = await svc.AdvanceAsync("ord-1", "preparing");

        Assert.Equal("shipped", otra.CurrentStage);
        Assert.Empty(capacidad.Llamadas);
    }

    // ── Una definición por dominio ──────────────────────────────────────────

    /// <summary>
    /// Cada dominio pide SU definición y nombra su sujeto con SU kind.
    /// </summary>
    /// <remarks>
    /// Los nombres de estado se repiten entre pipelines —<c>paid</c> está en tres,
    /// <c>completed</c> en dos— así que una definición compartida leería la etapa de un dominio
    /// contra el pipeline de otro: «enviado» convertido en «matriculado» sin que nada falle.
    /// </remarks>
    [Theory]
    [InlineData("shop", "tracking.shop")]
    [InlineData("travel", "tracking.travel")]
    [InlineData("events", "tracking.events")]
    [InlineData("academy", "tracking.academy")]
    public async Task Cada_dominio_usa_su_propia_definicion(string dominio, string esperada)
    {
        var capacidad = new CapacidadFalsa();
        var (svc, _) = Armar(capacidad, dominio);

        Assert.Equal(esperada, svc.DefinitionKey);
        Assert.Equal(esperada, svc.SubjectKind);

        await svc.AdvanceAsync("ord-1", "paid");

        var arranque = capacidad.Llamadas.First(l => l.Path == "/v1/instances" && l.Method == "POST");
        Assert.Contains($"\"definitionKey\":\"{esperada}\"", arranque.Body, StringComparison.Ordinal);

        var busqueda = capacidad.Llamadas.First(l => l.Path == "/v1/instances" && l.Method == "GET");
        Assert.Contains($"subjectKind={Uri.EscapeDataString(esperada)}", busqueda.Query, StringComparison.Ordinal);
    }

    [Fact] // sin dominio no hay definición que pedir, y caer a una compartida es EL defecto.
    public void Sin_dominio_no_se_construye()
    {
        Assert.Throws<ArgumentException>(() => new HttpOrderTrackingService(
            new FabricaFalsa(new CapacidadFalsa()),
            new StubOrderTrackingService(null, null, null, "t"),
            Options.Create(new TrackingSettings()),
            StubOrderTrackingService.ShopPipeline,
            "  "));
    }

    // ── Lo que rechaza ──────────────────────────────────────────────────────

    [Fact] // una etapa que no sigue a la actual sube con el motivo de la capacidad.
    public async Task Una_transicion_ilegal_sube_con_el_motivo_de_la_capacidad()
    {
        var capacidad = new CapacidadFalsa();
        capacidad.Falla("POST /v1/instances/wf-1/fire", HttpStatusCode.Conflict,
            "workflow.transition_not_allowed", "Desde 'paid' no se puede 'delivered'. Sí se puede: preparing.");

        var (svc, local) = Armar(capacidad);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => svc.AdvanceAsync("ord-1", "delivered"));

        Assert.Contains("Sí se puede: preparing", ex.Message, StringComparison.Ordinal);
        // Y el timeline NO se movió: el CMS sella DESPUÉS de que la capacidad acepta.
        Assert.Null(await local.GetTimelineAsync("ord-1"));
    }

    /// <summary>
    /// Con la capacidad caída no se avanza, pero el timeline se sigue viendo.
    /// </summary>
    /// <remarks>
    /// Es la promesa entera de este cableado: la degradación cae sobre avanzar, no sobre mirar.
    /// </remarks>
    [Fact]
    public async Task Con_la_capacidad_caida_no_se_avanza_pero_SI_se_consulta()
    {
        var viva = new CapacidadFalsa();
        var (svcVivo, local) = Armar(viva);
        await svcVivo.AdvanceAsync("ord-1", "preparing");

        var muerta = new HttpOrderTrackingService(
            new FabricaFalsa(new CaidaTotal()), local, Options.Create(new TrackingSettings { Mode = "Api" }),
            StubOrderTrackingService.ShopPipeline, "shop");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => muerta.AdvanceAsync("ord-1", "shipped"));

        Assert.Contains("no responde", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("127.0.0.1", ex.Message, StringComparison.Ordinal);

        // Y lo que importa: mirar sigue funcionando.
        var timeline = await muerta.GetTimelineAsync("ord-1");
        Assert.Equal("preparing", timeline!.CurrentStage);
    }

    private sealed class CaidaTotal : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
            => throw new HttpRequestException("Connection refused (127.0.0.1:5215)");
    }

    // ── La puesta al día de un pedido en vuelo ──────────────────────────────

    /// <summary>
    /// Un pedido que ya venía en marcha se pone al día hasta donde el CMS dice que está.
    /// </summary>
    /// <remarks>
    /// <b>Acá SÍ se puede, al revés que en la HU #44</b>, y la diferencia es la que importa: en
    /// Gobierno la historia de la capacidad ES el registro legal, así que fabricarla habría
    /// escrito fechas y actores que no ocurrieron. Acá la capacidad es un motor de reglas y las
    /// fechas siguen siendo las del CMS, intactas: se reconstruye DÓNDE va el pedido, no CUÁNDO
    /// pasó.
    /// </remarks>
    [Fact]
    public async Task Un_pedido_en_vuelo_se_pone_al_dia_sin_tocar_sus_fechas()
    {
        // El pedido ya iba por `preparing` en el almacén del CMS, sin instancia en la capacidad.
        var local = new StubOrderTrackingService(
            StubOrderTrackingService.ShopPipeline, null, new InMemoryJsonEntityStore(), "t");
        await local.AdvanceAsync("ord-viejo", "preparing", "antes del cableado");
        var antes = await local.GetTimelineAsync("ord-viejo");

        var capacidad = new CapacidadFalsa();
        var svc = new HttpOrderTrackingService(
            new FabricaFalsa(capacidad), local, Options.Create(new TrackingSettings { Mode = "Api" }),
            StubOrderTrackingService.ShopPipeline, "shop");

        var despues = await svc.AdvanceAsync("ord-viejo", "shipped");

        // Se puso al día hasta `preparing` y de ahí siguió.
        Assert.Contains("preparing", capacidad.Disparadas);
        Assert.Equal("shipped", despues.CurrentStage);

        // Y las fechas que ya existían no se tocaron.
        Assert.Equal(
            antes!.Stages.First(s => s.Stage == "paid").ReachedAt,
            despues.Stages.First(s => s.Stage == "paid").ReachedAt);
    }
}
