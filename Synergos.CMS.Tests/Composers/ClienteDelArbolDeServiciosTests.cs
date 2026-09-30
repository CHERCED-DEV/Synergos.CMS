using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Composers;
using Synergos.CMS.Web.Middlewares;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Composers;

/// <summary>
/// La pieza con la que el CMS habla con el árbol de servicios (#178), ejercida por la cadena
/// REAL de <c>IHttpClientFactory</c>: sólo el último eslabón —el que sale a la red— es un doble.
/// </summary>
/// <remarks>
/// <para><b>Por qué la cadena real y no los handlers sueltos.</b> Lo que se vigila es el ORDEN y
/// la COMBINACIÓN: que la llave y la correlación salgan, que la telemetría vea una llamada y no
/// cada intento, que el reintento lea la bandera y deje el cuerpo legible para el cliente. Cada
/// handler probado por separado puede estar bien y la cadena, mal armada.</para>
/// </remarks>
public sealed class ClienteDelArbolDeServiciosTests
{
    private const string Nombre = "capacidad-de-prueba";
    private const string Llave = "llave-de-prueba";

    /// <summary>El último eslabón: contesta lo que diga el guion, en orden, y anota lo que llega.</summary>
    private sealed class Guion : HttpMessageHandler
    {
        private readonly Queue<Func<CancellationToken, Task<HttpResponseMessage>>> _pasos = new();

        public List<HttpRequestMessage> Recibidas { get; } = new();

        public Guion Responde(HttpStatusCode codigo, string? cuerpo = null)
        {
            _pasos.Enqueue(_ => Task.FromResult(new HttpResponseMessage(codigo)
            {
                Content = cuerpo is null
                    ? new ByteArrayContent(Array.Empty<byte>())
                    : new StringContent(cuerpo, Encoding.UTF8, "application/problem+json"),
            }));
            return this;
        }

        public Guion NoContesta()
        {
            _pasos.Enqueue(_ => throw new HttpRequestException("Connection refused (capacidad.local:80)"));
            return this;
        }

        public Guion SeCuelga()
        {
            _pasos.Enqueue(async ct =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                throw new InvalidOperationException("inalcanzable");
            });
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            Recibidas.Add(req);
            if (_pasos.Count == 0)
            {
                throw new InvalidOperationException($"El guion se acabó en el intento {Recibidas.Count}.");
            }
            return _pasos.Dequeue()(ct);
        }
    }

    private sealed record Montaje(HttpClient Cliente, IWebhookTelemetryStore Telemetria, IServiceProvider Proveedor);

    private static IConfiguration Config(int reintentos) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Synergos:Reintento:MaxRetryAttempts"] = reintentos.ToString(System.Globalization.CultureInfo.InvariantCulture),
            // Un milisegundo: lo que se prueba es SI se repite, no cuánto espera.
            ["Synergos:Reintento:RetryBaseDelayMs"] = "1",
        })
        .Build();

    private static Montaje Montar(
        Guion guion,
        int reintentos = 2,
        string? llave = Llave,
        TimeSpan? techo = null,
        string? correlacion = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions<ReintentoSettings>().Bind(Config(reintentos).GetSection("Synergos:Reintento"));

        services
            .AddClienteDelArbolDeServicios(
                Nombre,
                new DestinoDelArbol(new Uri("http://capacidad.local/"), llave, techo ?? TimeSpan.FromSeconds(10)))
            .ConfigurePrimaryHttpMessageHandler(() => guion);

        var sp = services.BuildServiceProvider();

        if (correlacion is not null)
        {
            var http = new DefaultHttpContext();
            http.Items[CorrelationIdMiddleware.ContextKey] = correlacion;
            sp.GetRequiredService<IHttpContextAccessor>().HttpContext = http;
        }

        return new Montaje(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(Nombre),
            sp.GetRequiredService<IWebhookTelemetryStore>(),
            sp);
    }

    private static HttpRequestMessage Escritura(string? llaveDeIdempotencia)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "v1/cosas")
        {
            Content = JsonContent.Create(new { que = "algo" }),
        };
        if (llaveDeIdempotencia is not null)
        {
            req.Headers.TryAddWithoutValidation(ClienteDelArbolDeServicios.CabeceraDeIdempotencia, llaveDeIdempotencia);
        }
        return req;
    }

    private static string Rechazo(string code, bool? transient)
        => JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["title"] = "x",
            ["detail"] = $"motivo de {code}",
            ["code"] = code,
            ["transient"] = transient,
        });

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    // ── Lo que viaja ────────────────────────────────────────────────────────

    [Fact]
    public async Task Manda_la_llave_compartida_contra_la_URL_base()
    {
        var guion = new Guion().Responde(HttpStatusCode.OK, "{}");
        var m = Montar(guion);

        using var res = await m.Cliente.GetAsync("v1/cosas/1");

        var llegada = Assert.Single(guion.Recibidas);
        Assert.Equal("http://capacidad.local/v1/cosas/1", llegada.RequestUri!.ToString());
        Assert.True(llegada.Headers.TryGetValues(ClienteDelArbolDeServicios.CabeceraDeLlave, out var llaves));
        Assert.Equal(Llave, Assert.Single(llaves!));
    }

    [Fact]
    public async Task Sin_llave_configurada_no_manda_la_cabecera()
    {
        // El clon limpio: sin llave la capacidad contesta 401, que es la verdad sobre ese
        // despliegue. Mandar una cabecera vacía sería inventar una credencial.
        var guion = new Guion().Responde(HttpStatusCode.OK, "{}");
        var m = Montar(guion, llave: null);

        using var res = await m.Cliente.GetAsync("v1/cosas/1");

        Assert.False(Assert.Single(guion.Recibidas).Headers.Contains(ClienteDelArbolDeServicios.CabeceraDeLlave));
    }

    [Fact]
    public async Task Lleva_la_correlacion_de_la_peticion_en_curso()
    {
        var guion = new Guion().Responde(HttpStatusCode.OK, "{}");
        var m = Montar(guion, correlacion: "f178c0ffee000001");

        using var res = await m.Cliente.GetAsync("v1/cosas/1");

        Assert.True(Assert.Single(guion.Recibidas).Headers
            .TryGetValues(CorrelationIdMiddleware.HeaderName, out var ids));
        Assert.Equal("f178c0ffee000001", Assert.Single(ids!));
    }

    // ── El reintento: repetible Y pasajero ───────────────────────────────────

    [Fact]
    public async Task Una_escritura_con_llave_se_repite_si_la_capacidad_dice_que_es_pasajero()
    {
        var guion = new Guion()
            .Responde(HttpStatusCode.ServiceUnavailable, Rechazo("cosas.store_busy", transient: true))
            .Responde(HttpStatusCode.OK, "{}");
        var m = Montar(guion);

        using var res = await m.Cliente.SendAsync(Escritura("llave-1"));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(2, guion.Recibidas.Count);
        Assert.All(guion.Recibidas, r =>
            Assert.Equal("llave-1", r.Headers.GetValues(ClienteDelArbolDeServicios.CabeceraDeIdempotencia).Single()));
    }

    [Fact]
    public async Task Un_rechazo_firme_no_se_repite_y_el_cliente_lo_sigue_pudiendo_leer()
    {
        // La cadena LEE la bandera antes que el cliente. Si esa lectura consumiera el cuerpo, el
        // cliente leería «no consta» y un «no» del medio de pago se contaría como una caída.
        var guion = new Guion()
            .Responde(HttpStatusCode.Conflict, Rechazo("payments.payment_declined", transient: false));
        var m = Montar(guion);

        using var res = await m.Cliente.SendAsync(Escritura("llave-1"));
        var rechazo = await RechazoDelArbolDeServicios.LeerAsync(res, Json, CancellationToken.None);

        Assert.Single(guion.Recibidas);
        Assert.NotNull(rechazo);
        Assert.True(rechazo!.EsFirme);
        Assert.Equal("payments.payment_declined", rechazo.Codigo);
        Assert.Equal("motivo de payments.payment_declined", rechazo.Detalle);
    }

    [Fact]
    public async Task Un_5xx_sin_bandera_no_consta_y_no_se_repite()
    {
        // Un proxy que devuelve HTML, una capacidad anterior a la bandera: la ausencia NO es
        // «pasajero». Repetir ahí sería deducirlo del código de estado (#129).
        var guion = new Guion().Responde(HttpStatusCode.ServiceUnavailable, "<html>bad gateway</html>");
        var m = Montar(guion);

        using var res = await m.Cliente.SendAsync(Escritura("llave-1"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
        Assert.Single(guion.Recibidas);
    }

    [Fact]
    public async Task Una_escritura_SIN_llave_no_se_repite_aunque_la_capacidad_diga_que_es_pasajero()
    {
        // Es la que puede crear dos veces lo mismo — una línea más en la canasta, un sello más.
        var guion = new Guion()
            .Responde(HttpStatusCode.ServiceUnavailable, Rechazo("cart.store_busy", transient: true));
        var m = Montar(guion);

        using var res = await m.Cliente.SendAsync(Escritura(llaveDeIdempotencia: null));

        Assert.Single(guion.Recibidas);
    }

    [Fact]
    public async Task Una_peticion_VETADA_no_se_repite_aunque_lleve_llave_y_sea_pasajero()
    {
        // El cliente sabe algo que la pieza no: que la capacidad cierra la llave aunque diga
        // pasajero (Api.Payments al autorizar, medido vivo). El veto manda sobre las dos llaves.
        var guion = new Guion()
            .Responde(HttpStatusCode.ServiceUnavailable, Rechazo("payments.transport_not_configured", transient: true));
        var m = Montar(guion);

        using var res = await m.Cliente.SendAsync(Escritura("llave-1").NoSeRepite());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
        Assert.Single(guion.Recibidas);
    }

    [Fact]
    public async Task Sin_respuesta_una_lectura_se_repite_hasta_que_contesta()
    {
        var guion = new Guion().NoContesta().NoContesta().Responde(HttpStatusCode.OK, "{}");
        var m = Montar(guion);

        using var res = await m.Cliente.GetAsync("v1/cosas/1");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(3, guion.Recibidas.Count);
    }

    [Fact]
    public async Task Sin_respuesta_se_rinde_tras_los_reintentos_con_la_excepcion_de_siempre()
    {
        // Lo que ven los clientes al rendirse es la MISMA HttpRequestException de antes de la
        // pieza, que es lo que cada uno ya sabe traducir. Un tipo nuevo de Polly habría sido un
        // 500 sin traducir en catorce clientes.
        var guion = new Guion().NoContesta().NoContesta().NoContesta();
        var m = Montar(guion);

        await Assert.ThrowsAsync<HttpRequestException>(() => m.Cliente.GetAsync("v1/cosas/1"));
        Assert.Equal(3, guion.Recibidas.Count);
    }

    [Fact]
    public async Task Una_escritura_sin_llave_que_no_obtuvo_respuesta_no_se_repite()
    {
        // Pudo haber llegado y cortarse la vuelta: sin llave no hay forma de saber si ya se hizo.
        var guion = new Guion().NoContesta();
        var m = Montar(guion);

        await Assert.ThrowsAsync<HttpRequestException>(() => m.Cliente.SendAsync(Escritura(llaveDeIdempotencia: null)));
        Assert.Single(guion.Recibidas);
    }

    [Fact]
    public async Task El_techo_es_el_de_la_llamada_entera_y_sale_como_siempre()
    {
        // Sin timeout por intento: el techo es el TimeoutSeconds del despliegue, envuelve la
        // cadena entera y sale como la TaskCanceledException que los clientes ya traducen.
        var guion = new Guion().SeCuelga();
        var m = Montar(guion, techo: TimeSpan.FromMilliseconds(300));

        var ex = await Assert.ThrowsAsync<TaskCanceledException>(() => m.Cliente.GetAsync("v1/cosas/1"));

        Assert.IsType<TimeoutException>(ex.InnerException);
        Assert.Single(guion.Recibidas);
    }

    [Fact]
    public async Task Con_cero_reintentos_no_se_repite_nada()
    {
        var guion = new Guion().NoContesta();
        var m = Montar(guion, reintentos: 0);

        await Assert.ThrowsAsync<HttpRequestException>(() => m.Cliente.GetAsync("v1/cosas/1"));
        Assert.Single(guion.Recibidas);
    }

    // ── La telemetría: antes del reintento ──────────────────────────────────

    [Fact]
    public async Task La_telemetria_ve_UNA_llamada_con_sus_reintentos_dentro()
    {
        // ADR 0072: lo que el operador necesita es lo que esperó quien llamó. Si la telemetría
        // quedara DENTRO del reintento, contaría tres llamadas —dos fallidas— donde hubo una buena.
        var guion = new Guion().NoContesta().NoContesta().Responde(HttpStatusCode.OK, "{}");
        var m = Montar(guion);

        using var res = await m.Cliente.GetAsync("v1/cosas/1");

        var canal = Assert.Single(m.Telemetria.GetChannelStats(), c => c.ChannelName == Nombre);
        Assert.Equal(1, canal.TotalCalls);
        Assert.Equal(1, canal.SuccessCount);
        Assert.Equal(3, guion.Recibidas.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict, true)]
    [InlineData(HttpStatusCode.NotFound, true)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.InternalServerError, false)]
    public async Task Para_la_telemetria_un_4xx_de_la_capacidad_es_una_respuesta_y_no_una_caida(
        HttpStatusCode codigo, bool cuentaComoExito)
    {
        // Api.Identity contesta 409 a cada alta a partir de la segunda: contarlo como fallo
        // pintaría en rojo el caso normal. El 401 sí es caída: la llave está mal para todos.
        var guion = new Guion().Responde(codigo);
        var m = Montar(guion);

        using var res = await m.Cliente.GetAsync("v1/cosas/1");

        var canal = Assert.Single(m.Telemetria.GetChannelStats(), c => c.ChannelName == Nombre);
        Assert.Equal(cuentaComoExito ? 1 : 0, canal.SuccessCount);
        Assert.Equal(cuentaComoExito ? 0 : 1, canal.FailureCount);
    }

    // ── Un tercero: la tabla de la librería, pero la misma llave de repetible ──

    private static (HttpClient Cliente, Guion Guion) Tercero(Guion guion)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions<ReintentoSettings>().Bind(Config(2).GetSection("Synergos:Reintento"));
        services.AddHttpClient("tercero", h => h.BaseAddress = new Uri("https://pasarela.local/"))
            .AddResilienciaDeTercero()
            .ConfigurePrimaryHttpMessageHandler(() => guion);

        return (services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>().CreateClient("tercero"), guion);
    }

    [Fact]
    public async Task Un_tercero_que_contesta_5xx_a_una_lectura_se_repite()
    {
        // Una pasarela no emite `transient`: ahí sí manda la tabla de códigos, la de la librería.
        var (cliente, guion) = Tercero(new Guion()
            .Responde(HttpStatusCode.BadGateway)
            .Responde(HttpStatusCode.OK, "{}"));

        using var res = await cliente.GetAsync("transactions?reference=r-1");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(2, guion.Recibidas.Count);
    }

    [Fact]
    public async Task Una_devolucion_a_un_tercero_no_se_repite_jamas()
    {
        // POST /refunds sin llave: repetirlo es devolver dos veces.
        var (cliente, guion) = Tercero(new Guion().Responde(HttpStatusCode.BadGateway));

        using var res = await cliente.PostAsJsonAsync("refunds", new { transaction_id = "t-1" });

        Assert.Equal(HttpStatusCode.BadGateway, res.StatusCode);
        Assert.Single(guion.Recibidas);
    }

    // ── El destino, leído de la sección del molde ───────────────────────────

    [Theory]
    [InlineData(null, null, "http://127.0.0.1:5215/", 10)]
    [InlineData("http://workflow:8080", "20", "http://workflow:8080/", 20)]
    [InlineData("http://workflow:8080/", "0", "http://workflow:8080/", 10)]
    [InlineData("  ", "abc", "http://127.0.0.1:5215/", 10)]
    public void El_destino_se_lee_de_la_seccion_con_sus_defaults(
        string? url, string? segundos, string esperada, int segundosEsperados)
    {
        var seccion = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Synergos:Gob:BaseUrl"] = url,
                ["Synergos:Gob:TimeoutSeconds"] = segundos,
                ["Synergos:Gob:ApiKey"] = "k",
            })
            .Build()
            .GetSection("Synergos:Gob");

        var destino = DestinoDelArbol.De(seccion, "http://127.0.0.1:5215/", 10);

        Assert.Equal(esperada, destino.BaseUrl.ToString());
        Assert.Equal(TimeSpan.FromSeconds(segundosEsperados), destino.Timeout);
        Assert.Equal("k", destino.Llave);
    }

    [Fact]
    public void La_URL_puede_ir_en_otra_clave_de_la_misma_seccion()
    {
        // Tienda: dos servicios bajo la MISMA llave y el mismo techo.
        var seccion = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Synergos:Tienda:BaseUrl"] = "http://bff-tienda:8080/",
                ["Synergos:Tienda:CartBaseUrl"] = "http://api-cart:8080",
            })
            .Build()
            .GetSection("Synergos:Tienda");

        var destino = DestinoDelArbol.De(seccion, "http://127.0.0.1:5210/", 30, claveDeLaUrl: "CartBaseUrl");

        Assert.Equal("http://api-cart:8080/", destino.BaseUrl.ToString());
    }
}
